using Microsoft.Xna.Framework.Content;

namespace Port.IosGame;

// XNA asset names are case-insensitive. Preserve the verified bundle's exact
// filenames while resolving names from the phone game on iOS's filesystem.
internal sealed class CaseMappedContentManager : ContentManager
{
    private readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? log;

    internal CaseMappedContentManager(IServiceProvider services, string root, Action<string>? log = null)
        : base(services, Path.GetFullPath(root))
    {
        this.log = log;
        foreach (var path in Directory.EnumerateFiles(RootDirectory, "*", SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetExtension(path), ".xnb", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = Path.GetRelativePath(RootDirectory, path).Replace('\\', '/');
            if (!paths.TryAdd(relative, path)) throw new InvalidDataException("Ambiguous XNB filename casing: " + relative);
        }
        log?.Invoke("content-name-index=ready xnb=" + paths.Count);
    }

    protected override Stream OpenStream(string assetName)
    {
        var relative = assetName.Replace('\\', '/');
        if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.Split('/').Any(part =>
                part.Length == 0 || part is "." or ".." || part.Contains(':')))
            throw new ContentLoadException("Invalid relative content name: " + assetName);
        var requested = relative + ".xnb";
        if (!paths.TryGetValue(requested, out var exact))
            throw new ContentLoadException("Asset is absent from the verified content index: " + requested);
        var canonical = Path.GetRelativePath(RootDirectory, exact).Replace('\\', '/');
        if (requested != canonical) log?.Invoke("content-case=" + requested + " -> " + canonical);
        return File.OpenRead(exact);
    }
}
