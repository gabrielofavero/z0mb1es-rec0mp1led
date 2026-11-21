using System.Security.Cryptography;
using System.Text;
using Port.Analysis;
using Port.Content;

namespace Port.Prepare;

/// <summary>Plan 1 preparation with only in-process metadata/IL/content operations.</summary>
public static class IosGamePreparation
{
    public const string RuntimeIdentity = ".NET 10.0.10 ios-arm64 (metadata catalog)";
    private const string RuntimeCatalogHash = "9a86961dec1b4e8f7ec92c8b2804ed506793975230d1c1fdd955d61cf2f236bc";

    public static GamePreparationResult Prepare(string xap, string workspace, string inputs,
        IContentAudioConverter converter, Action<string>? stage = null,
        CancellationToken cancellationToken = default)
    {
        var identity = PreparationEngine.ValidateXap(xap, cancellationToken);
        if (Path.Exists(workspace)) throw new IOException("Preparation workspace must be new.");
        var runtime = Path.Combine(inputs, "abi-runtime");
        var files = Directory.GetFiles(runtime, "*.dll").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        var rows = string.Concat(files.Select(path => Path.GetFileName(path) + " " + Hash(path) + "\n"));
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rows))).ToLowerInvariant() != RuntimeCatalogHash)
            throw new PreparationException("The iOS runtime metadata catalog differs from the reviewed input.");
        var preparation = Path.Combine(inputs, "preparation");
        var fna = Path.Combine(preparation, "FNA.dll");
        var bridge = Path.Combine(preparation, "Port.Bridge.dll");
        var hooks = Path.Combine(preparation, "Port.Hooks.dll");
        var versions = Path.Combine(preparation, "supported-versions.json");
        stage?.Invoke("extract");
        var extracted = PreparationEngine.Prepare(new PreparationRequest(xap, Path.Combine(workspace, "extraction")),
            cancellationToken: cancellationToken);
        var originalRoot = Path.Combine(extracted.OutputDirectory, "original-xap");
        var original = Path.Combine(originalRoot, "ZombiesWP7.dll");
        var inventory = Path.Combine(workspace, "evidence.json");
        var report = Path.Combine(workspace, "ios-abi-report.json");
        stage?.Invoke("inspect-in-process");
        GameAnalysis.WriteInventory(xap, versions, inventory);
        cancellationToken.ThrowIfCancellationRequested();
        stage?.Invoke("abi-in-process");
        GameAnalysis.WriteAbiReport(inventory, original, fna, bridge, report, versions, files, RuntimeIdentity);
        stage?.Invoke("ios-abi-report-sha256=" + Hash(report));
        var plan = IlRewriter.ReadPinnedPlan(report, fna, bridge, runtime);
        var privateRoot = Path.Combine(workspace, "private/prepared");
        stage?.Invoke("rewrite");
        var rewritten = IlRewriter.Rewrite(original, Path.Combine(privateRoot, "rewrite"), plan, privateRoot);
        cancellationToken.ThrowIfCancellationRequested();
        stage?.Invoke("hook");
        var hooked = GameplayPatcher.Apply(original, Path.Combine(rewritten.OutputDirectory, "ZombiesWP7.dll"),
            hooks, Path.Combine(privateRoot, "hook"), privateRoot);
        stage?.Invoke("content-in-process");
        var content = Path.Combine(workspace, "content");
        ContentPreparer.Prepare(originalRoot, content, converter, identity.XapSha256);
        cancellationToken.ThrowIfCancellationRequested();
        PreparedGameVerifier.Verify(Path.Combine(hooked.OutputDirectory, "ZombiesWP7.dll"), content);
        stage?.Invoke("verified");
        return new(identity, originalRoot, hooked.OutputDirectory, content);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
