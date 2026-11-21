using Port.Prepare;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Port.Prepare.Checks <supported.xap> <ignored-test-directory>");
    return 2;
}

var identity = PreparationEngine.ValidateXap(args[0]);
if (identity.ArchiveEntries != 127 || identity.DllBytes != 750136)
    throw new Exception("Supported package metadata changed unexpectedly.");

var root = Path.GetFullPath(args[1]);
Directory.CreateDirectory(root);
var badXap = Path.Combine(root, "unknown.xap");
File.WriteAllBytes(badXap, [0x50, 0x4B, 0x03, 0x04, 0x00]);
var unknownOutput = Path.Combine(root, "unknown-output");
try
{
    PreparationEngine.Prepare(new PreparationRequest(badXap, unknownOutput));
    throw new Exception("Unknown XAP was accepted.");
}
catch (PreparationException error) when (error.Message.StartsWith("Unsupported XAP", StringComparison.Ordinal)) { }
if (Path.Exists(unknownOutput)) throw new Exception("Unknown XAP created output.");

var unknownWorkspace = Path.Combine(root, "unknown-pipeline");
try
{
    GamePreparationPipeline.Prepare(badXap, unknownWorkspace,
        new GamePreparationTools("missing-inspector", "missing-abi", "missing-rules",
            "missing-versions", "missing-fna", "missing-bridge", "missing-hooks",
            "missing-converter", new string('0', 64)));
    throw new Exception("Unknown XAP entered the shared pipeline.");
}
catch (PreparationException error) when (error.Message.StartsWith("Unsupported XAP", StringComparison.Ordinal)) { }
if (Path.Exists(unknownWorkspace)) throw new Exception("Unknown XAP created a pipeline workspace.");

var final = Path.Combine(root, "injected-failure");
try
{
    PreparationEngine.Prepare(new PreparationRequest(args[0], final), progress =>
    {
        if (progress.Stage == "extracting" && progress.CompletedFiles == 1)
            throw new InjectedFailure();
    });
    throw new Exception("Injected extraction failure did not stop preparation.");
}
catch (InjectedFailure) { }
if (Path.Exists(final) || Directory.EnumerateDirectories(root, ".injected-failure.stage-*").Any())
    throw new Exception("Failed preparation left a final or staging directory.");

var cancelled = Path.Combine(root, "cancelled-output");
using (var cancellation = new CancellationTokenSource())
{
    try
    {
        PreparationEngine.Prepare(new PreparationRequest(args[0], cancelled), progress =>
        {
            if (progress.Stage == "extracting" && progress.CompletedFiles == 1)
                cancellation.Cancel();
        }, cancellation.Token);
        throw new Exception("Cancelled preparation did not stop.");
    }
    catch (OperationCanceledException) { }
}
if (Path.Exists(cancelled) || Directory.EnumerateDirectories(root, ".cancelled-output.stage-*").Any())
    throw new Exception("Cancelled preparation left a final or staging directory.");

var existing = Path.Combine(root, "existing-output");
Directory.CreateDirectory(existing);
var sentinel = Path.Combine(existing, "keep.txt");
File.WriteAllText(sentinel, "preserve me");
try
{
    PreparationEngine.Prepare(new PreparationRequest(args[0], existing));
    throw new Exception("Existing output was overwritten.");
}
catch (PreparationException error) when (error.Message.StartsWith("Output already exists", StringComparison.Ordinal)) { }
if (File.ReadAllText(sentinel) != "preserve me") throw new Exception("Existing output changed.");

Console.WriteLine("CHECKS_OK: unknown input rejected before extraction or pipeline mutation, failure/cancellation cleaned staging, existing output preserved.");
return 0;

sealed class InjectedFailure : Exception;
