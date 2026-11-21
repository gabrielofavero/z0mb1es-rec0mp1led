using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Port.Content;
using Port.Prepare;

if ((args.Length != 8 && args.Length != 10) || args[0] != "--xap" ||
    args[2] != "--preparation" || args[4] != "--converter" ||
    args[6] != "--output" || (args.Length == 10 && args[8] != "--converter-sha256"))
{
    Console.Error.WriteLine("Usage: content-prepare --xap <supported.xap> --preparation <verified-extraction> --converter <local-converter> --output <new-private-directory> [--converter-sha256 <sha256>]");
    return 2;
}

try
{
    var identity = PreparationEngine.ValidateXap(args[1]);
    var output = Path.GetFullPath(args[7]);
    var privateRoot = Path.GetFullPath("private") + Path.DirectorySeparatorChar;
    if (!output.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Developer content output must be under ignored private/.");
    var preparation = Path.GetFullPath(args[3]);
    using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(preparation, "provenance.json")));
    var document = provenance.RootElement;
    if (document.GetProperty("phase").GetString() != "original-extracted" ||
        document.GetProperty("input").GetProperty("xapSha256").GetString() != identity.XapSha256)
        throw new InvalidDataException("Extraction provenance does not match the supported XAP.");
    var source = Path.Combine(preparation, "original-xap");
    var assets = ContentInventory.Read(source);
    var records = document.GetProperty("files").EnumerateArray().ToDictionary(
        row => row.GetProperty("path").GetString()!, row => row,
        StringComparer.Ordinal);
    foreach (var asset in assets)
    {
        if (!records.TryGetValue("original-xap/" + asset.Path, out var record) ||
            record.GetProperty("bytes").GetInt64() != asset.Bytes ||
            record.GetProperty("sha256").GetString() != asset.Sha256)
            throw new InvalidDataException($"Extracted content does not match provenance: {asset.Path}");
    }
    using var configStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("content-tools.json")
        ?? throw new InvalidDataException("Pinned conversion tool configuration is missing.");
    using var config = JsonDocument.Parse(configStream);
    var windowsSha = config.RootElement.GetProperty("audioConverter").GetProperty("executableSha256").GetString()!;
    var converterSha = args.Length == 10 ? args[9].ToLowerInvariant() :
        OperatingSystem.IsWindows() ? windowsSha : throw new InvalidOperationException(
            "This host has no repository-pinned audio converter; pass --converter-sha256 for a verified local executable.");
    if (converterSha.Length != 64 || !converterSha.All(Uri.IsHexDigit))
        throw new ArgumentException("Audio converter SHA-256 must be 64 hexadecimal characters.");
    if (OperatingSystem.IsWindows() && converterSha != windowsSha)
        throw new InvalidDataException("Windows audio converter hash differs from the pinned converter.");
    var manifestSha = ContentPreparer.Prepare(source, output, args[5], converterSha, identity.XapSha256,
        step => Console.WriteLine($"CONTENT_PROGRESS {step.Stage} {step.Completed}/{step.Total}"));
    Console.WriteLine($"CONTENT_PREPARED assets={assets.Count} manifestSha256={manifestSha}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"CONTENT_PREPARE_FAIL {error.GetType().Name}: {error.Message}");
    return 1;
}
