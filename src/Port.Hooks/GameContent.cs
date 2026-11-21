namespace Port.Hooks;

/// <summary>Explicit root for the phone game's custom non-XNB files.</summary>
public static class GameContent
{
    private static string? root;

    public static void Configure(string contentRoot) => root =
        Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar);

    public static Stream OpenStream(string name)
    {
        if (root is null) throw new InvalidOperationException("Game content root is not configured.");
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name))
            throw new InvalidDataException("Invalid game content path.");
        var relative = name.Replace('\\', '/');
        if (relative.Split('/').Any(segment => segment.Length == 0 ||
            segment is "." or ".." || segment.Contains(':')))
            throw new InvalidDataException("Invalid game content path.");
        var path = Path.GetFullPath(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Game content path escapes its root.");
        return File.OpenRead(path);
    }
}
