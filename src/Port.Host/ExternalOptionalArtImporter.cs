using System.Diagnostics;
using Port.Prepare;

namespace Port.Host;

/// <summary>Runs a pinned host-supplied optional art tool after shared source validation.</summary>
public sealed class ExternalOptionalArtImporter(string executable, string decoder,
    string fnaAssembly, string privateWorkspace) : IOptionalArtImporter
{
    public (string Version, long SourceBytes) Preview(string kind, string path) =>
        PinnedOptionalArtSource.Preview(kind, path);

    public void Prepare(string kind, string path, string phoneRoot, string artRoot,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        Preview(kind, path);
        progress?.Invoke("prepare-" + kind);
        Run(artRoot, cancellationToken, "prepare", "--kind", kind, "--source", Path.GetFullPath(path),
            "--store", artRoot, "--phone-root", phoneRoot);
        Run(artRoot, cancellationToken, "probe", "--kind", kind, "--store", artRoot,
            "--phone-root", phoneRoot);
    }

    public void Compose(string phoneRoot, string artRoot, Action<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Invoke("compose-art");
        Run(artRoot, cancellationToken, "compose-endless", "--store", artRoot,
            "--phone-root", phoneRoot);
        Run(artRoot, cancellationToken, "probe", "--store", artRoot,
            "--phone-root", phoneRoot);
    }

    private void Run(string artRoot, CancellationToken token, params string[] arguments)
    {
        token.ThrowIfCancellationRequested();
        var tool = Path.GetFullPath(executable);
        if (!File.Exists(tool)) throw new FileNotFoundException("Optional art tool is absent.", tool);
        var info = new ProcessStartInfo(tool)
        {
            WorkingDirectory = Path.GetFullPath(privateWorkspace),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        Directory.CreateDirectory(info.WorkingDirectory);
        // Art is committed from the host's staging tree, outside its scratch directory.
        info.Environment["PORT_ASSET_PRIVATE"] = Path.GetDirectoryName(Path.GetFullPath(artRoot))
            ?? throw new ArgumentException("Optional art must have a staging directory.", nameof(artRoot));
        info.Environment["PORT_ASSET_DECODER"] = Path.GetFullPath(decoder);
        info.Environment["PORT_ASSET_FNA"] = Path.GetFullPath(fnaAssembly);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start optional art tool.");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        process.WaitForExitAsync(token).GetAwaiter().GetResult();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Optional art tool failed: {error.Trim()}");
        if (output.Length != 0) Console.Write(output);
    }
}
