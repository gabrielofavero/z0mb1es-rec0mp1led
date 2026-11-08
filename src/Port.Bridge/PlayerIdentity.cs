using System.Text.Json;

namespace Port.Bridge;

/// <summary>The offline gamertag shared by the phone service bridge and desktop settings.</summary>
public static class PlayerIdentity
{
    public const int MaxLength = 16;
    public const string DefaultName = "Local Player";
    private static string name = DefaultName;
    private static string? path;

    public static string Name => name;

    public static void Configure(string saveDirectory)
    {
        path = Path.Combine(saveDirectory, "player-name.json");
        name = DefaultName;
        try
        {
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<PlayerNameFile>(File.ReadAllText(path));
            if (saved is not null && IsValid(saved.Name)) name = saved.Name;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"PLAYER_NAME_LOAD_SKIPPED {error.GetType().Name}");
        }
    }

    public static bool IsValid(string? value) => value is { Length: > 0 and <= MaxLength } &&
        value[0] != ' ' && value[^1] != ' ' &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '\'');

    public static bool TrySetName(string value)
    {
        if (!IsValid(value)) return false;
        if (path is null) throw new InvalidOperationException("Player identity has not been configured.");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new PlayerNameFile(value)) + "\n");
            File.Move(temp, path, overwrite: true);
            name = value;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"PLAYER_NAME_SAVE_FAILED {error.GetType().Name}");
            return false;
        }
    }

    private sealed record PlayerNameFile(string Name);
}
