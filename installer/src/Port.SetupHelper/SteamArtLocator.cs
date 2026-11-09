using System.Text.RegularExpressions;
using System.Security;
using Microsoft.Win32;

namespace Port.SetupHelper;

internal static class SteamArtLocator
{
    private const string AppId = "1800730";
    private static readonly Regex LibraryPath = new("\"path\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase);
    private static readonly Regex LegacyLibraryPath = new("\"\\d+\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.IgnoreCase);
    private static readonly Regex ManifestAppId = new("\"appid\"\\s*\"1800730\"", RegexOptions.IgnoreCase);
    private static readonly Regex InstallDirectory = new("\"installdir\"\\s*\"(?<value>[^\"]+)\"", RegexOptions.IgnoreCase);

    public static string? Find(string? steamRoot = null)
    {
        IEnumerable<string> roots = steamRoot is null ? RegisteredRoots() : [steamRoot];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var library in Libraries(root))
            {
                try
                {
                    var fullLibrary = Path.GetFullPath(library);
                    if (!seen.Add(fullLibrary)) continue;
                    var manifest = Path.Combine(fullLibrary, "steamapps", $"appmanifest_{AppId}.acf");
                    if (!File.Exists(manifest)) continue;
                    var contents = File.ReadAllText(manifest);
                    if (!ManifestAppId.IsMatch(contents)) continue;
                    var match = InstallDirectory.Match(contents);
                    if (!match.Success) continue;
                    var name = match.Groups["value"].Value;
                    if (name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0) continue;
                    var candidate = Path.Combine(fullLibrary, "steamapps", "common", name);
                    if (!Directory.Exists(candidate)) continue;
                    try
                    {
                        OptionalArt.ValidateSource("i-maed-steam", candidate);
                        return candidate;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        // Steam metadata can outlive an incomplete or unsupported installation.
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A missing or unreadable library must not prevent manual selection.
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> RegisteredRoots()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var (key, value) in new[]
        {
            (@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath")
        })
        {
            string? path;
            try { path = Registry.GetValue(key, value, null) as string; }
            catch (Exception error) when (error is SecurityException or UnauthorizedAccessException)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(path)) yield return path;
        }
    }

    private static IEnumerable<string> Libraries(string root)
    {
        yield return root;
        var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) yield break;
        string contents;
        try { contents = File.ReadAllText(file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { yield break; }
        foreach (Match match in LibraryPath.Matches(contents).Cast<Match>()
                     .Concat(LegacyLibraryPath.Matches(contents).Cast<Match>()))
            yield return match.Groups["value"].Value.Replace(@"\\", @"\");
    }
}
