using Port.Content;

namespace Port.Prepare;

/// <summary>Uses the reviewed metadata plan and iOS in-process import stages on Android.</summary>
public static class AndroidGamePreparation
{
    public static GamePreparationResult Prepare(string xap, string workspace, string inputs,
        IContentAudioConverter converter, Action<string>? stage = null, CancellationToken cancellationToken = default)
    {
        var identity = PreparationEngine.ValidateXap(xap, cancellationToken);
        if (Path.Exists(workspace)) throw new IOException("Preparation workspace must be new.");
        var metadata = Path.Combine(inputs, "metadata");
        var runtime = Path.Combine(inputs, "runtime");
        var plan = IlRewriter.ReadPinnedPlan(Path.Combine(inputs, "android-rewrite-abi.json"),
            Path.Combine(metadata, "FNA.dll"), Path.Combine(metadata, "Port.Bridge.dll"), runtime);
        stage?.Invoke("extract");
        var extracted = PreparationEngine.Prepare(new(xap, Path.Combine(workspace, "extraction")),
            cancellationToken: cancellationToken);
        var originalRoot = Path.Combine(extracted.OutputDirectory, "original-xap");
        var original = Path.Combine(originalRoot, "ZombiesWP7.dll");
        var privateRoot = Path.Combine(workspace, "private/prepared");
        stage?.Invoke("rewrite");
        var rewritten = IlRewriter.Rewrite(original, Path.Combine(privateRoot, "rewrite"), plan, privateRoot);
        cancellationToken.ThrowIfCancellationRequested();
        stage?.Invoke("hook");
        var hooked = GameplayPatcher.Apply(original, Path.Combine(rewritten.OutputDirectory, "ZombiesWP7.dll"),
            Path.Combine(metadata, "Port.Hooks.dll"), Path.Combine(privateRoot, "hook"), privateRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var android = Path.Combine(inputs, "android");
        var game = Path.Combine(privateRoot, "android");
        AndroidFrameworkAdapter.Adapt(Path.Combine(hooked.OutputDirectory, "ZombiesWP7.dll"),
            Path.Combine(game, "ZombiesWP7.dll"), Path.Combine(android, "FNA.NET.dll"),
            Path.Combine(android, "Port.Bridge.dll"), Path.Combine(android, "Port.Hooks.dll"), metadata, runtime);
        stage?.Invoke("content-in-process");
        var content = Path.Combine(workspace, "content");
        ContentPreparer.Prepare(originalRoot, content, converter, identity.XapSha256);
        cancellationToken.ThrowIfCancellationRequested();
        PreparedGameVerifier.Verify(Path.Combine(game, "ZombiesWP7.dll"), content);
        stage?.Invoke("verified");
        return new(identity, originalRoot, game, content);
    }
}
