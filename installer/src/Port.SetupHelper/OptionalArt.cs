using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Port.Prepare;

namespace Port.SetupHelper;

internal sealed record ArtOptions(string Normal, string? NormalSource, string Viking, string? VikingSource)
{
    public void Validate()
    {
        if (Normal is not ("keep" or "none" or "steam" or "360") ||
            Viking is not ("keep" or "none" or "360") ||
            (Normal is "steam" or "360") != (NormalSource is not null) ||
            (Viking == "360") != (VikingSource is not null))
            throw new ArgumentException("Invalid optional art selection.");
    }
}

internal static class OptionalArt
{
    public static string ValidateSource(string kind, string source)
    {
        if (kind == "i-maed-steam")
        {
            using var pin = Embedded("supported-steam-versions.json");
            var root = Path.GetFullPath(source);
            Check(Path.Combine(root, pin.RootElement.GetProperty("executable").GetProperty("path").GetString()!),
                pin.RootElement.GetProperty("executable"), "Steam I MAED 1.0.0.0");
            foreach (var row in pin.RootElement.GetProperty("requiredTextures").EnumerateArray())
            {
                var relative = row.GetProperty("path").GetString()!;
                if (!Safe(relative)) throw new InvalidDataException("Unsafe Steam art path.");
                Check(Path.Combine(root, "Content", relative.Replace('/', Path.DirectorySeparatorChar)), row,
                    "Steam I MAED 1.0.0.0");
            }
            return "I MAED Steam 1.0.0.0; I MAED and Endless artwork";
        }
        if (kind is "i-maed-360" or "time-viking-360")
        {
            using var pin = Embedded("supported-360-versions.json");
            var id = kind == "i-maed-360" ? "zombies-360" : "viking-360";
            var row = pin.RootElement.GetProperty("titles").EnumerateArray()
                .Single(item => item.GetProperty("sourceId").GetString() == id).GetProperty("package");
            Check(source, row, kind == "i-maed-360" ? "I MAED 360 LIVE" : "Time Viking 360 LIVE");
            return kind == "i-maed-360" ? "I MAED 360 pinned LIVE; 121 mapped artworks" :
                "Time Viking 360 pinned LIVE; 270 mapped artworks";
        }
        throw new ArgumentException("Unsupported optional art source.");
    }

    public static void Stage(ArtOptions options, string xap, string payload, string staged,
        string target, string work, string? originalRoot)
    {
        options.Validate();
        if (options.Normal is "steam" or "360")
            Console.WriteLine("ART_VALID " + ValidateSource(options.Normal == "steam" ? "i-maed-steam" : "i-maed-360",
                options.NormalSource!));
        if (options.Viking == "360")
            Console.WriteLine("ART_VALID " + ValidateSource("time-viking-360", options.VikingSource!));

        var prior = Path.Combine(target, "optional-art");
        var store = Path.Combine(work, "private", "asset-swap", "prepared");
        var selectedNormal = "";
        if (options.Normal == "keep" && Directory.Exists(prior))
        {
            var choice = Path.Combine(prior, "normal-source.json");
            if (File.Exists(choice))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(choice));
                selectedNormal = document.RootElement.GetProperty("kind").GetString() ?? "";
            }
            else if (Directory.Exists(Path.Combine(prior, "i-maed-360"))) selectedNormal = "i-maed-360";
            else if (Directory.Exists(Path.Combine(prior, "i-maed-steam"))) selectedNormal = "i-maed-steam";
            if (selectedNormal is "i-maed-360" or "i-maed-steam")
                CopyTree(Path.Combine(prior, selectedNormal), Path.Combine(store, selectedNormal));
        }
        if (options.Viking == "keep" && Directory.Exists(Path.Combine(prior, "time-viking-360")))
            CopyTree(Path.Combine(prior, "time-viking-360"), Path.Combine(store, "time-viking-360"));
        if (options.Normal == "steam") selectedNormal = "i-maed-steam";
        else if (options.Normal == "360") selectedNormal = "i-maed-360";
        var hasArt = selectedNormal.Length != 0 || Directory.Exists(Path.Combine(store, "time-viking-360")) ||
            options.Viking == "360";
        if (!hasArt) return;

        if (originalRoot is null)
        {
            Console.WriteLine("STAGE art-phone-source");
            originalRoot = Path.Combine(PreparationEngine.Prepare(
                new PreparationRequest(xap, Path.Combine(work, "art-extraction"))).OutputDirectory, "original-xap");
        }
        if (options.Normal == "keep" && selectedNormal.Length != 0)
        {
            try
            {
                RunImporter(payload, staged, work, "probe", "--kind", selectedNormal,
                    "--store", store, "--phone-root", originalRoot);
            }
            catch (InvalidDataException error)
            {
                Console.Error.WriteLine($"ART_KEEP_UNAVAILABLE {selectedNormal}: {error.Message}");
                Directory.Delete(Path.Combine(store, selectedNormal), recursive: true);
                selectedNormal = "";
            }
        }
        if (options.Viking == "keep" && Directory.Exists(Path.Combine(store, "time-viking-360")))
        {
            try
            {
                RunImporter(payload, staged, work, "probe", "--kind", "time-viking-360",
                    "--store", store, "--phone-root", originalRoot);
            }
            catch (InvalidDataException error)
            {
                Console.Error.WriteLine($"ART_KEEP_UNAVAILABLE time-viking-360: {error.Message}");
                Directory.Delete(Path.Combine(store, "time-viking-360"), recursive: true);
            }
        }
        if (options.Normal is "steam" or "360")
            RunImporter(payload, staged, work, "prepare", "--kind", selectedNormal, "--source",
                options.NormalSource!, "--store", store, "--phone-root", originalRoot);
        if (options.Viking == "360")
            RunImporter(payload, staged, work, "prepare", "--kind", "time-viking-360", "--source",
                options.VikingSource!, "--store", store, "--phone-root", originalRoot);
        if (selectedNormal.Length == 0 && !Directory.Exists(Path.Combine(store, "time-viking-360")))
            return;
        if (selectedNormal.Length != 0 || Directory.Exists(Path.Combine(store, "time-viking-360")))
            RunImporter(payload, staged, work, "compose-endless", "--store", store,
                "--phone-root", originalRoot);
        Console.WriteLine("STAGE art-verify");
        RunImporter(payload, staged, work, "probe", "--store", store, "--phone-root", originalRoot);
        if (selectedNormal.Length != 0)
            File.WriteAllText(Path.Combine(store, "normal-source.json"),
                JsonSerializer.Serialize(new { kind = selectedNormal }) + "\n");
        CopyTree(store, Path.Combine(staged, "optional-art"));
    }

    private static void RunImporter(string payload, string staged, string work, params string[] arguments)
    {
        var executable = Path.Combine(payload, "tools", "Port.AssetImporter.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Optional art importer is absent.", executable);
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.Environment["PORT_ASSET_PRIVATE"] = Path.Combine(work, "private");
        info.Environment["PORT_ASSET_DECODER"] = Path.Combine(payload, "tools", "decode_360_xnb.exe");
        info.Environment["PORT_ASSET_FNA"] = Path.Combine(staged, "FNA.dll");
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start optional art importer.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Optional art import failed: {error.Trim()}");
        if (output.Length != 0) Console.Write(output);
    }

    private static JsonDocument Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing embedded art pin: {name}");
        return JsonDocument.Parse(stream);
    }

    private static bool Safe(string relative) => relative.Length != 0 && !Path.IsPathRooted(relative) &&
        relative.Split('/').All(part => part.Length != 0 && part is not ("." or "..") &&
                                      !part.Contains('\\') && !part.Contains(':'));

    private static void Check(string path, JsonElement pin, string label)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException($"{label}: required source is missing or linked: {file.Name}");
        if (file.Length != pin.GetProperty("bytes").GetInt64() ||
            Hash(file.FullName) != pin.GetProperty("sha256").GetString())
            throw new InvalidDataException($"{label}: unsupported or damaged source: {file.Name}");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
