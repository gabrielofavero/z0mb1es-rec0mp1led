using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Port.Content;
using Port.Prepare;

namespace Port.SetupHelper;

internal static class InstallerEngine
{
    private const string XapHash = "6b88c7fbd95444d2560b4b11e6f35d40a6c0f7abc30706155dcbccd18bc333fe";
    private const string DllHash = "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823";
    private const string FfmpegHash = "e2e148a514b188a08102ec71c03f970ad1273316fab430e4c95065b0650fb306";
    private sealed record FileRow(string Path, long Bytes, string Sha256);
    private sealed record InstallManifest(int Format, string SourceXapSha256, FileRow[] Files);

    public static void Verify(string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(Path.Combine(root, "install-manifest.json")))
            ?? throw new InvalidDataException("Install manifest is empty.");
        if (manifest.Format != 1 || manifest.SourceXapSha256 != XapHash || manifest.Files.Length < 100)
            throw new InvalidDataException("Unsupported install manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in manifest.Files)
        {
            if (!SafeRelative(row.Path) || !seen.Add(row.Path))
                throw new InvalidDataException($"Invalid manifest path: {row.Path}");
            var path = Path.GetFullPath(Path.Combine(root, row.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) || new FileInfo(path).Length != row.Bytes || Hash(path) != row.Sha256)
                throw new InvalidDataException($"Installed file failed verification: {row.Path}");
        }
        foreach (var name in new[] { "Port.Launcher.exe", "ZombiesWP7.dll", "patch-manifest.json",
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
    }

    public static void Install(string destination, string payload, string xapPath, ArtOptions art)
    {
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
        if (xap.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            xap.StartsWith(target + ".previous" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Move the source XAP outside the installation directory before setup.");
        var identity = PreparationEngine.ValidateXap(xap);
        if (identity.XapSha256 != XapHash) throw new InvalidDataException("Unsupported XAP.");
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
        var previousCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.CreateDirectory(work);
            Directory.SetCurrentDirectory(work);
            Directory.CreateDirectory(Path.Combine(work, "config"));
            File.Copy(Path.Combine(source, "config", "supported-versions.json"),
                Path.Combine(work, "config", "supported-versions.json"));
            Console.WriteLine("STAGE runtime");
            CopyTree(Path.Combine(source, "runtime"), staged);
            var reuse = Directory.Exists(target);
            if (reuse) try { Verify(target); } catch { reuse = false; }
            string? originalRoot = null;
            if (reuse)
            {
                Console.WriteLine("STAGE reuse");
                using var content = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "content-manifest.json")));
                foreach (var output in content.RootElement.GetProperty("Outputs").EnumerateArray())
                {
                    var relative = output.GetProperty("Path").GetString()!;
                    if (!SafeRelative(relative)) throw new InvalidDataException("Unsafe prepared content path.");
                    var to = Path.Combine(staged, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)), to);
                }
                foreach (var name in new[] { "ZombiesWP7.dll", "patch-manifest.json", "content-manifest.json" })
                    File.Copy(Path.Combine(target, name), Path.Combine(staged, name));
            }
            else
            {
                Console.WriteLine("STAGE extract");
                var extracted = PreparationEngine.Prepare(new PreparationRequest(xap, Path.Combine(work, "extraction")));
                originalRoot = Path.Combine(extracted.OutputDirectory, "original-xap");
                var original = Path.Combine(originalRoot, "ZombiesWP7.dll");
                var evidence = Path.Combine(work, "evidence.json");
                var report = Path.Combine(work, "abi-report.json");
                Run(Path.Combine(source, "tools", "inspect_assembly.exe"), work,
                    ["--json", "--xap", xap, "--output", evidence]);
                Run(Path.Combine(source, "tools", "abi", "abi_check.exe"), work,
                    ["--inventory", evidence, "--dll", original, "--fna", Path.Combine(staged, "FNA.dll"),
                     "--bridge", Path.Combine(staged, "Port.Bridge.dll"), "--output", report], allowReportFailure: true);
                Console.WriteLine("STAGE rewrite");
                var plan = IlRewriter.ReadPinnedPlan(report, Path.Combine(staged, "FNA.dll"),
                    Path.Combine(staged, "Port.Bridge.dll"), Path.Combine(source, "tools", "abi"));
                var rewritten = IlRewriter.Rewrite(original, Path.Combine(work, "private", "prepared", "rewrite"), plan);
                var hooked = GameplayPatcher.Apply(original, Path.Combine(rewritten.OutputDirectory, "ZombiesWP7.dll"),
                    Path.Combine(staged, "Port.Hooks.dll"), Path.Combine(work, "private", "prepared", "hook"));
                Console.WriteLine("STAGE content");
                var contentDir = Path.Combine(work, "content");
                ContentPreparer.Prepare(originalRoot, contentDir, Path.Combine(source, "tools", "ffmpeg.exe"),
                    FfmpegHash, identity.XapSha256);
                File.Copy(Path.Combine(hooked.OutputDirectory, "ZombiesWP7.dll"), Path.Combine(staged, "ZombiesWP7.dll"));
                File.Copy(Path.Combine(hooked.OutputDirectory, "patch-manifest.json"), Path.Combine(staged, "patch-manifest.json"));
                CopyTree(contentDir, staged);
            }
            OptionalArt.Stage(art, xap, source, staged, target, work, originalRoot);
            Console.WriteLine("STAGE verify");
            WriteManifest(staged, identity.XapSha256);
            Verify(staged);
            Console.WriteLine("STAGE commit");
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
            Directory.SetCurrentDirectory(previousCwd);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);
        }
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

    private static void WriteManifest(string root, string sourceHash)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new FileRow(Path.GetRelativePath(root, path).Replace('\\', '/'),
                new FileInfo(path).Length, Hash(path)))
            .OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
        File.WriteAllText(Path.Combine(root, "install-manifest.json"),
            JsonSerializer.Serialize(new InstallManifest(1, sourceHash, files),
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.WriteAllText(Path.Combine(root, "install-report.txt"),
            $"Supported XAP SHA-256: {sourceHash}\nVerified files: {files.Length}\n");
    }

    private static void CopyTree(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void Run(string executable, string cwd, string[] arguments, bool allowReportFailure = false)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {executable}");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !(allowReportFailure && process.ExitCode == 1 && File.Exists(arguments[^1])))
            throw new InvalidDataException($"{Path.GetFileName(executable)} failed: {error.Trim()}");
        if (output.Length != 0) Console.Write(output);
    }

    private static bool SafeRelative(string path) => path.Length != 0 && !Path.IsPathRooted(path) &&
        path.Split('/').All(part => part.Length != 0 && part != "." && part != ".." && !part.Contains('\\'));

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
