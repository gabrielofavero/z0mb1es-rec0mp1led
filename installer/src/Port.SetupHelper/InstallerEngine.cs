using System.Security.Cryptography;
using System.Text.Json;
using Port.Prepare;

namespace Port.SetupHelper;

internal static class InstallerEngine
{
    private const string XapHash = "6b88c7fbd95444d2560b4b11e6f35d40a6c0f7abc30706155dcbccd18bc333fe";
    private const string DllHash = "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823";
    private const string FfmpegHash = "e2e148a514b188a08102ec71c03f970ad1273316fab430e4c95065b0650fb306";
    private sealed record FileRow(string Path, long Bytes, string Sha256);
    private sealed record InstallManifest(int Format, string SourceXapSha256, FileRow[] Files);

    public static void SetInitialFlashing(bool enabled, string saveDirectory)
    {
        if (!enabled) return;
        var directory = Path.GetFullPath(saveDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "display.json");
        if (File.Exists(path)) return; // Never overwrite a player's existing display settings.
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file);
        writer.Write("{\"Mode\":\"Fullscreen\",\"Width\":600,\"Height\":360,\"VSync\":true,\"ReduceFlashing\":true}\n");
    }

    public static void Verify(string directory, Action<int, int>? progress = null)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(Path.Combine(root, "install-manifest.json")))
            ?? throw new InvalidDataException("Install manifest is empty.");
        if (manifest.Format != 1 || manifest.SourceXapSha256 != XapHash || manifest.Files.Length < 100)
            throw new InvalidDataException("Unsupported install manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        progress?.Invoke(0, manifest.Files.Length + 1);
        var checkedFiles = 0;
        foreach (var row in manifest.Files)
        {
            if (!SafeRelative(row.Path) || !seen.Add(row.Path))
                throw new InvalidDataException($"Invalid manifest path: {row.Path}");
            var path = Path.GetFullPath(Path.Combine(root, row.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || new FileInfo(path).Length != row.Bytes || Hash(path) != row.Sha256)
                throw new InvalidDataException($"Installed file failed verification: {row.Path}");
            progress?.Invoke(++checkedFiles, manifest.Files.Length + 1);
        }
        foreach (var name in new[] { "Z0MB1ES.exe", "ZombiesWP7.dll", "patch-manifest.json",
                     "content-manifest.json", "Content/Arial.xnb" })
            if (!seen.Contains(name)) throw new InvalidDataException($"Install is missing {name}.");
        using var patch = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "patch-manifest.json")));
        if (patch.RootElement.GetProperty("sourceSha256").GetString() != DllHash ||
            patch.RootElement.GetProperty("preparedSha256").GetString() != Hash(Path.Combine(root, "ZombiesWP7.dll")))
            throw new InvalidDataException("Prepared DLL differs from its patch manifest.");
        using var content = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "content-manifest.json")));
        if (content.RootElement.GetProperty("SourceXapSha256").GetString() != XapHash)
            throw new InvalidDataException("Content manifest has the wrong source.");
        var installed = manifest.Files.ToDictionary(row => row.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var output in content.RootElement.GetProperty("Outputs").EnumerateArray())
        {
            var path = output.GetProperty("Path").GetString()!;
            if (!SafeRelative(path) || !installed.TryGetValue(path, out var row) ||
                row.Bytes != output.GetProperty("Bytes").GetInt64() ||
                row.Sha256 != output.GetProperty("Sha256").GetString())
                throw new InvalidDataException($"Installed content differs from its preparation manifest: {path}");
        }
        PreparedGameVerifier.Verify(Path.Combine(root, "ZombiesWP7.dll"), root);
        progress?.Invoke(manifest.Files.Length + 1, manifest.Files.Length + 1);
    }

    public static void Install(string destination, string payload, string xapPath, ArtOptions art,
        string? cancelFile = null)
    {
        var progressLock = new object();
        var lastProgress = -1;
        void Report(int value)
        {
            lock (progressLock)
            {
                value = Math.Clamp(value, 0, 1000);
                if (value <= lastProgress) return;
                lastProgress = value;
                Console.WriteLine($"PROGRESS {value}");
            }
        }

        void CheckCancelled()
        {
            if (cancelFile is not null && File.Exists(cancelFile))
                throw new OperationCanceledException("Installation cancelled.");
        }

        Report(0);
        CheckCancelled();
        art.Validate();
        art = art with
        {
            NormalSource = art.NormalSource is null ? null : Path.GetFullPath(art.NormalSource),
            VikingSource = art.VikingSource is null ? null : Path.GetFullPath(art.VikingSource)
        };
        var target = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(target) ?? throw new ArgumentException("Destination cannot be a drive root.");
        if (target == Path.GetPathRoot(target)) throw new ArgumentException("Unsafe destination.");
        var source = Path.GetFullPath(payload);
        var xap = Path.GetFullPath(xapPath);
        foreach (var selected in new[] { art.NormalSource, art.VikingSource }.Where(path => path is not null))
            if (Path.GetFullPath(selected!).StartsWith(target + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Move optional source art outside the installation directory.");
        if (art.Normal is "steam" or "360")
            OptionalArt.ValidateSource(art.Normal == "steam" ? "i-maed-steam" : "i-maed-360",
                art.NormalSource!);
        if (art.Viking == "360") OptionalArt.ValidateSource("time-viking-360", art.VikingSource!);
        CheckCancelled();
        if (xap.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            xap.StartsWith(target + ".previous" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Move the source XAP outside the installation directory before setup.");
        var identity = PreparationEngine.ValidateXap(xap);
        CheckCancelled();
        if (identity.XapSha256 != XapHash) throw new InvalidDataException("Unsupported XAP.");
        Report(30);
        Directory.CreateDirectory(parent);
        var work = Path.Combine(parent, ".port-work-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(parent, ".port-stage-" + Guid.NewGuid().ToString("N"));
        var backup = target + ".previous";
        if (Directory.Exists(backup))
        {
            Verify(backup);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(backup, target);
        }
        try
        {
            Directory.CreateDirectory(work);
            Console.WriteLine("STAGE runtime");
            CopyTree(Path.Combine(source, "runtime"), staged,
                (done, total) => Report(30 + 70 * done / total));
            Report(100);
            CheckCancelled();
            var reuse = Directory.Exists(target);
            if (reuse)
            {
                Console.WriteLine("STAGE reuse-check");
                try { Verify(target, (done, total) => Report(100 + 80 * done / total)); }
                catch { reuse = false; }
                if (reuse && (Hash(Path.Combine(target, "Port.Hooks.dll")) != Hash(Path.Combine(staged, "Port.Hooks.dll")) ||
                              Hash(Path.Combine(target, "Port.Bridge.dll")) != Hash(Path.Combine(staged, "Port.Bridge.dll"))))
                    reuse = false;
            }
            string? originalRoot = null;
            if (reuse)
            {
                Console.WriteLine("STAGE reuse");
                using var content = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "content-manifest.json")));
                var outputs = content.RootElement.GetProperty("Outputs").EnumerateArray().ToArray();
                for (var i = 0; i < outputs.Length; i++)
                {
                    var output = outputs[i];
                    var relative = output.GetProperty("Path").GetString()!;
                    if (!SafeRelative(relative)) throw new InvalidDataException("Unsafe prepared content path.");
                    var to = Path.Combine(staged, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)), to);
                    Report(180 + 570 * (i + 1) / outputs.Length);
                }
                foreach (var name in new[] { "ZombiesWP7.dll", "patch-manifest.json", "content-manifest.json" })
                    File.Copy(Path.Combine(target, name), Path.Combine(staged, name));
                Report(850);
                CheckCancelled();
            }
            else
            {
                Console.WriteLine("STAGE extract");
                var prepared = GamePreparationPipeline.Prepare(xap, Path.Combine(work, "base"),
                    new GamePreparationTools(
                        Path.Combine(source, "tools", "inspect_assembly.exe"),
                        Path.Combine(source, "tools", "abi", "abi_check.exe"),
                        Path.Combine(source, "tools", "abi"),
                        Path.Combine(source, "config", "supported-versions.json"),
                        Path.Combine(staged, "FNA.dll"), Path.Combine(staged, "Port.Bridge.dll"),
                        Path.Combine(staged, "Port.Hooks.dll"),
                        Path.Combine(source, "tools", "ffmpeg.exe"), FfmpegHash),
                    step =>
                    {
                        Console.WriteLine($"STAGE {step}");
                        Report(step switch
                        {
                            "extract" => 100, "inspect" => 330, "abi" => 370,
                            "rewrite" => 410, "hook" => 470, _ => 500
                        });
                        CheckCancelled();
                    },
                    step =>
                    {
                        Report(100 + 230 * step.CompletedFiles / Math.Max(1, step.TotalFiles));
                        CheckCancelled();
                    },
                    step =>
                    {
                        var fraction = (int)(step.Completed * 1000 / Math.Max(1, step.Total));
                        Report(step.Stage switch
                        {
                            "copy" => 500 + 45 * fraction / 1000,
                            "music" => 545 + 245 * fraction / 1000,
                            _ => 790 + 20 * fraction / 1000
                        });
                        CheckCancelled();
                    });
                CheckCancelled();
                originalRoot = prepared.ExtractedRoot;
                File.Copy(Path.Combine(prepared.PreparedDllDirectory, "ZombiesWP7.dll"), Path.Combine(staged, "ZombiesWP7.dll"));
                File.Copy(Path.Combine(prepared.PreparedDllDirectory, "patch-manifest.json"), Path.Combine(staged, "patch-manifest.json"));
                CopyTree(prepared.ContentDirectory, staged, (done, total) => Report(810 + 40 * done / total));
                Report(850);
            }
            Console.WriteLine("STAGE art");
            OptionalArt.Stage(art, xap, source, staged, target, work, originalRoot);
            Report(910);
            CheckCancelled();
            Console.WriteLine("STAGE verify");
            WriteManifest(staged, identity.XapSha256,
                (done, total) => Report(910 + 45 * done / total));
            Verify(staged, (done, total) => Report(955 + 30 * done / total));
            CheckCancelled();
            Console.WriteLine("STAGE commit");
            Report(990);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(staged, target); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            // Inno finalizes this backup only after its own registration succeeds.
        }
        finally
        {
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
        }
        Report(1000);
    }

    public static void Rollback(string destination)
    {
        var target = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        var backup = target + ".previous";
        if (!Directory.Exists(backup))
        {
            if (Directory.Exists(target))
            {
                Verify(target);
                Directory.Delete(target, recursive: true);
            }
            return;
        }
        Verify(backup);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(backup, target);
        Verify(target);
    }

    public static void FinalizeInstall(string destination)
    {
        var target = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
        Verify(target);
        var backup = target + ".previous";
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
    }

    private static void WriteManifest(string root, string sourceHash, Action<int, int>? progress = null)
    {
        var paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        var rows = new List<FileRow>(paths.Length);
        progress?.Invoke(0, paths.Length + 1);
        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            rows.Add(new FileRow(Path.GetRelativePath(root, path).Replace('\\', '/'),
                new FileInfo(path).Length, Hash(path)));
            progress?.Invoke(i + 1, paths.Length + 1);
        }
        var files = rows.OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
        File.WriteAllText(Path.Combine(root, "install-manifest.json"),
            JsonSerializer.Serialize(new InstallManifest(1, sourceHash, files),
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.WriteAllText(Path.Combine(root, "install-report.txt"),
            $"Supported XAP SHA-256: {sourceHash}\nVerified files: {files.Length}\n");
        progress?.Invoke(paths.Length + 1, paths.Length + 1);
    }

    private static void CopyTree(string source, string destination, Action<int, int>? progress = null)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        Directory.CreateDirectory(destination);
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        progress?.Invoke(0, Math.Max(1, files.Length));
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            progress?.Invoke(i + 1, files.Length);
        }
        if (files.Length == 0) progress?.Invoke(1, 1);
    }

    private static bool SafeRelative(string path) => path.Length != 0 && !Path.IsPathRooted(path) &&
        path.Split('/').All(part => part.Length != 0 && part != "." && part != ".." && !part.Contains('\\'));

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
