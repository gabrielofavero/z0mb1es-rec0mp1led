using System.Diagnostics;
using Port.Content;

namespace Port.Prepare;

/// <summary>Tool locations are supplied by the host, never inferred from its executable or CWD.</summary>
public sealed record GamePreparationTools(
    string Inspector, string AbiChecker, string AbiRulesDirectory, string SupportedVersions,
    string FnaAssembly, string BridgeAssembly, string HooksAssembly,
    string AudioConverter, string AudioConverterSha256);

public sealed record GamePreparationResult(
    PackageIdentity Identity, string ExtractedRoot, string PreparedDllDirectory,
    string ContentDirectory);

/// <summary>Shared guarded preparation used by installers and future first-launch hosts.</summary>
public static class GamePreparationPipeline
{
    public static GamePreparationResult Prepare(string xapPath, string workspace,
        GamePreparationTools tools, Action<string>? stage = null,
        Action<PreparationProgress>? extractionProgress = null,
        Action<ContentProgress>? contentProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var xap = Path.GetFullPath(xapPath);
        var work = Path.GetFullPath(workspace);
        if (Path.Exists(work)) throw new IOException("Preparation workspace must be new.");
        // Reject unsupported input before creating any workspace files.
        var identity = PreparationEngine.ValidateXap(xap, cancellationToken);
        var inspector = Path.GetFullPath(tools.Inspector);
        var checker = Path.GetFullPath(tools.AbiChecker);
        var rules = Path.GetFullPath(tools.AbiRulesDirectory);
        var versions = Path.GetFullPath(tools.SupportedVersions);
        var fna = Path.GetFullPath(tools.FnaAssembly);
        var bridge = Path.GetFullPath(tools.BridgeAssembly);
        var hooks = Path.GetFullPath(tools.HooksAssembly);
        var converter = Path.GetFullPath(tools.AudioConverter);
        Directory.CreateDirectory(work);
        var extraction = Path.Combine(work, "extraction");
        var evidence = Path.Combine(work, "evidence.json");
        var report = Path.Combine(work, "abi-report.json");
        stage?.Invoke("extract");
        var extracted = PreparationEngine.Prepare(new PreparationRequest(xap, extraction),
            extractionProgress, cancellationToken);
        var originalRoot = Path.Combine(extracted.OutputDirectory, "original-xap");
        var original = Path.Combine(originalRoot, "ZombiesWP7.dll");
        stage?.Invoke("inspect");
        Run(inspector, work, ["--json", "--xap", xap, "--output", evidence,
            "--versions", versions], cancellationToken);
        stage?.Invoke("abi");
        Run(checker, work, ["--inventory", evidence, "--dll", original, "--fna", fna,
            "--bridge", bridge, "--output", report, "--versions", versions],
            cancellationToken, allowReportFailure: true, reportPath: report);
        cancellationToken.ThrowIfCancellationRequested();
        stage?.Invoke("rewrite");
        var plan = IlRewriter.ReadPinnedPlan(report, fna, bridge, rules);
        var privateRoot = Path.Combine(work, "private", "prepared");
        var rewritten = IlRewriter.Rewrite(original,
            Path.Combine(privateRoot, "rewrite"), plan, privateRoot);
        cancellationToken.ThrowIfCancellationRequested();
        stage?.Invoke("hook");
        var hooked = GameplayPatcher.Apply(original,
            Path.Combine(rewritten.OutputDirectory, "ZombiesWP7.dll"), hooks,
            Path.Combine(privateRoot, "hook"), privateRoot);
        cancellationToken.ThrowIfCancellationRequested();
        stage?.Invoke("content");
        var content = Path.Combine(work, "content");
        ContentPreparer.Prepare(originalRoot, content, converter,
            tools.AudioConverterSha256, identity.XapSha256, contentProgress);
        cancellationToken.ThrowIfCancellationRequested();
        return new GamePreparationResult(identity, originalRoot, hooked.OutputDirectory, content);
    }

    private static void Run(string executable, string work, string[] arguments,
        CancellationToken cancellationToken,
        bool allowReportFailure = false, string? reportPath = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = work, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {executable}");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0 && !(allowReportFailure && process.ExitCode == 1 &&
            reportPath is not null && File.Exists(reportPath)))
            throw new InvalidDataException($"{Path.GetFileName(executable)} failed: {error.Trim()}");
        if (output.Length != 0) Console.Write(output);
    }
}
