using System.Text.Json;

namespace Port.Hooks;

/// <summary>Persistent visual-art choices, separate from the phone save and game content.</summary>
public static class AssetChoices
{
    private sealed record Preferences(int Format, string? Normal, string? Viking, string? Endless);
    private static Preferences preferences = new(1, null, null, null);
    private static string? normalOverride;
    private static string? vikingOverride;
    private static string? endlessOverride;
    private static string normalKind = "original";
    private static bool normalAvailable;
    private static bool vikingAvailable;
    private static bool endlessAvailable;
    private static bool normalConfigured;
    private static bool vikingConfigured;
    private static bool endlessConfigured;
    private static string normalReason = "";
    private static string vikingReason = "";
    private static string endlessReason = "";

    public static string Store { get; private set; } = "";
    public static bool NormalAvailable => normalAvailable;
    public static bool VikingAvailable => vikingAvailable;
    public static bool EndlessAvailable => endlessAvailable;
    public static bool NormalImported => Wants(preferences.Normal, normalOverride, normalConfigured) && normalAvailable;
    public static bool VikingImported => Wants(preferences.Viking, vikingOverride, vikingConfigured) && vikingAvailable;
    public static bool EndlessImported => Wants(preferences.Endless, endlessOverride, endlessConfigured) && endlessAvailable;
    public static string NormalLabel => NormalImported ? "ENHANCED" :
        Wants(preferences.Normal, normalOverride, normalConfigured) ? "UNAVAILABLE" : "ORIGINAL";
    public static string VikingLabel => VikingImported ? "ENHANCED" :
        Wants(preferences.Viking, vikingOverride, vikingConfigured) ? "UNAVAILABLE" : "ORIGINAL";
    public static string EndlessLabel => EndlessImported ? "ENHANCED" :
        Wants(preferences.Endless, endlessOverride, endlessConfigured) ? "UNAVAILABLE" : "ORIGINAL";

    public static void Initialize(string? store, string? normal, string? viking, string? endless)
    {
        Store = Path.GetFullPath(store ?? Path.Combine(AppContext.BaseDirectory, "optional-art"));
        normalOverride = normal;
        vikingOverride = viking;
        endlessOverride = endless;
        preferences = Load();
        Refresh();
    }

    public static void Refresh()
    {
        normalKind = SelectNormalSource();
        normalConfigured = normalKind != "original" ||
            File.Exists(Path.Combine(Store, "normal-source.json")) ||
            File.Exists(Path.Combine(Store, "i-maed-360", "current.json")) ||
            File.Exists(Path.Combine(Store, "i-maed-steam", "current.json"));
        vikingConfigured = File.Exists(Path.Combine(Store, "time-viking-360", "current.json"));
        endlessConfigured = File.Exists(Path.Combine(Store, "endless-360", "current.json"));
        var normal = normalKind == "steam" ? "i-maed-steam" : "i-maed-360";
        (normalAvailable, normalReason) = normalKind == "original" ? (false, "No I MAED import is installed.") :
            AssetDraw.CheckSource(Store, normal);
        (vikingAvailable, vikingReason) = AssetDraw.CheckSource(Store, "time-viking-360");
        (endlessAvailable, endlessReason) = AssetDraw.CheckSource(Store, "endless-360");
        Apply();
    }

    private static string SelectNormalSource()
    {
        if (normalOverride is "steam" or "360") return normalOverride;
        var choice = Path.Combine(Store, "normal-source.json");
        if (File.Exists(choice))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(choice));
                return document.RootElement.GetProperty("kind").GetString() switch
                {
                    "i-maed-steam" => "steam",
                    "i-maed-360" => "360",
                    _ => "original"
                };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException)
            {
                Console.Error.WriteLine($"ART_SOURCE_CHOICE_UNAVAILABLE {error.Message}");
                return "original";
            }
        }
        if (File.Exists(Path.Combine(Store, "i-maed-360", "current.json"))) return "360";
        if (File.Exists(Path.Combine(Store, "i-maed-steam", "current.json"))) return "steam";
        return "original";
    }

    private static bool Wants(string? saved, string? temporary, bool available) =>
        temporary is not null ? temporary != "original" : saved switch
        {
            "original" => false,
            "imported" => true,
            _ => available
        };

    private static void Apply()
    {
        var normal = NormalImported ? normalKind : "original";
        AssetDraw.Configure(Store, normal, VikingImported ? "360" : "original",
            EndlessImported ? "360" : "original");
        Console.WriteLine($"ART_CHOICES normal={NormalLabel} viking={VikingLabel} endless={EndlessLabel}");
    }

    public static string Toggle(string mode)
    {
        Refresh();
        var available = mode switch
        {
            "normal" => normalAvailable,
            "viking" => vikingAvailable,
            "endless" => endlessAvailable,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        if (!available)
        {
            var reason = mode switch
            {
                "normal" => normalReason,
                "viking" => vikingReason,
                _ => endlessReason
            };
            Console.Error.WriteLine($"ART_UNAVAILABLE mode={mode} action=repair reason={reason}");
            return "IMPORT UNAVAILABLE. REPAIR IN SETUP.";
        }
        var prior = preferences;
        preferences = mode switch
        {
            "normal" => preferences with { Normal = NormalImported ? "original" : "imported" },
            "viking" => preferences with { Viking = VikingImported ? "original" : "imported" },
            _ => preferences with { Endless = EndlessImported ? "original" : "imported" }
        };
        normalOverride = vikingOverride = endlessOverride = null;
        try
        {
            var path = Path.Combine(DesktopStorage.SaveDirectory, "assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences) + "\n");
            File.Move(temporary, path, overwrite: true);
            Apply();
            return "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            preferences = prior;
            Apply();
            Console.Error.WriteLine($"ART_PREFERENCES_SAVE_FAILED {error.Message}");
            return "COULD NOT SAVE ASSET CHOICE.";
        }
    }

    private static Preferences Load()
    {
        try
        {
            var path = Path.Combine(DesktopStorage.SaveDirectory, "assets.json");
            if (!File.Exists(path)) return new(1, null, null, null);
            var loaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));
            if (loaded is null || loaded.Format != 1 ||
                !Valid(loaded.Normal) || !Valid(loaded.Viking) || !Valid(loaded.Endless))
                throw new InvalidDataException("Asset preference format differs.");
            return loaded;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Console.Error.WriteLine($"ART_PREFERENCES_UNAVAILABLE {error.Message}");
            return new(1, null, null, null);
        }
    }

    private static bool Valid(string? value) => value is null or "original" or "imported";
}
