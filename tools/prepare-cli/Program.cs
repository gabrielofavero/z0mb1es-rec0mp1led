using Port.Prepare;

if (args.Length > 0 && args[0] == "hook")
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i + 1 < args.Length; i += 2)
        if (!options.TryAdd(args[i], args[i + 1])) { Usage(); return 2; }
    var names = new[] { "--xap", "--dll", "--prepared", "--hooks", "--output" };
    if (args.Length != 11 || names.Any(name => !options.ContainsKey(name)) ||
        options.Keys.Except(names).Any())
    { Usage(); return 2; }
    try
    {
        var input = PreparationEngine.ValidateXap(options["--xap"]);
        if (input.DllSha256 != "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823")
            throw new PreparationException("Hook input XAP differs from the supported DLL.");
        var result = GameplayPatcher.Apply(options["--dll"], options["--prepared"],
            options["--hooks"], options["--output"]);
        Console.WriteLine($"Hooked DLL SHA-256: {result.PreparedSha256}");
        Console.WriteLine($"Patch manifest: {Path.Combine(result.OutputDirectory, "patch-manifest.json")}");
        return 0;
    }
    catch (Exception error) when (error is PreparationException or IOException or InvalidDataException or
        UnauthorizedAccessException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"Hook failed: {error.Message}");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "rewrite")
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i + 1 < args.Length; i += 2)
    {
        if (!options.TryAdd(args[i], args[i + 1])) { Usage(); return 2; }
    }
    if (args.Length % 2 != 1 ||
        !new[] { "--xap", "--dll", "--abi-report", "--fna", "--bridge", "--output" }.All(options.ContainsKey) ||
        options.Keys.Except(new[] { "--xap", "--dll", "--abi-report", "--fna", "--bridge", "--output" }).Any())
    { Usage(); return 2; }
    var root = Path.GetFullPath(Path.Combine("private", "prepared"));
    var rewriteOutput = Path.GetFullPath(options["--output"]);
    if (rewriteOutput != root && !rewriteOutput.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Rewrite output must be within private/prepared/.");
        return 2;
    }
    try
    {
        var plan = IlRewriter.ReadPinnedPlan(options["--abi-report"], options["--fna"], options["--bridge"]);
        var input = PreparationEngine.ValidateXap(options["--xap"]);
        if (input.DllSha256 != plan.SourceSha256)
            throw new PreparationException("XAP and guarded DLL version differ.");
        var result = IlRewriter.Rewrite(options["--dll"], rewriteOutput, plan);
        Console.WriteLine($"Prepared DLL SHA-256: {result.PreparedSha256}");
        Console.WriteLine($"Patch manifest: {Path.Combine(result.OutputDirectory, "patch-manifest.json")}");
        return 0;
    }
    catch (Exception error) when (error is PreparationException or IOException or InvalidDataException or
        UnauthorizedAccessException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"Rewrite failed: {error.Message}");
        return 1;
    }
}

if (args.Length < 2)
{
    Usage();
    return 2;
}

string? xap = null;
var output = Path.Combine("private", "preparation");
var outputProvided = false;
for (var i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length) { Usage(); return 2; }
    switch (args[i])
    {
        case "--xap" when xap is null:
            xap = args[i + 1];
            break;
        case "--output" when !outputProvided:
            output = args[i + 1];
            outputProvided = true;
            break;
        default:
            Usage();
            return 2;
    }
}
if (string.IsNullOrWhiteSpace(xap) || string.IsNullOrWhiteSpace(output))
{
    Usage();
    return 2;
}

try
{
    var result = PreparationEngine.Prepare(new PreparationRequest(xap, output));
    Console.WriteLine($"Extracted {result.ExtractedFiles} original files to {result.OutputDirectory}");
    Console.WriteLine($"Version: {result.Input.SupportedVersion}; provenance SHA-256: {result.ProvenanceSha256}");
    Console.WriteLine("Extraction only: no rewritten DLL or playable game was produced.");
    return 0;
}
catch (Exception error) when (error is PreparationException or IOException or InvalidDataException or
    UnauthorizedAccessException or OperationCanceledException or System.Xml.XmlException)
{
    Console.Error.WriteLine($"Preparation failed: {error.Message}");
    return 1;
}

static void Usage() => Console.Error.WriteLine(
    "Usage: prepare --xap <supported.xap> [--output <new-private-directory>]\n" +
    "   or: prepare rewrite --xap <supported.xap> --dll <original.dll> --abi-report <report.json> --fna <FNA.dll> --bridge <Port.Bridge.dll> --output <private/prepared/new-directory>\n" +
    "   or: prepare hook --xap <supported.xap> --dll <original.dll> --prepared <rewritten.dll> --hooks <Port.Hooks.dll> --output <private/prepared/new-directory>\n" +
    "Default output: private/preparation (must not already exist).");
