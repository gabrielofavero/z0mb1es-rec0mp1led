using System.Text.Json;
using System.Text.Json.Nodes;

namespace Port.Prepare;

/// <summary>Player acknowledgment and initial graphics choice, independent of imported assets.</summary>
public static class FirstLaunchWarning
{
    public const string Title = "Flashing lights warning";
    public const string Text =
        "This game was designed with flashing visuals as part of its original art style. " +
        "Reduce Flashing reduces some menu and gameplay effects, but flashing visuals " +
        "can still occur with that setting enabled.\n\n" +
        "This game is not recommended for people with photosensitive epilepsy or " +
        "other sensitivity to flashing lights. Continue only if you understand this warning.\n\n" +
        "You can change Reduce Flashing later in Settings → Graphics.";
    public const string CheckboxLabel = "Enable Reduce Flashing";

    public static bool IsAcknowledged(string saveDirectory) =>
        File.Exists(Path.Combine(saveDirectory, "flashing-warning-acknowledged"));

    public static bool InitialReduceFlashing(string saveDirectory) =>
        ReadDisplay(saveDirectory)["ReduceFlashing"] is JsonValue value &&
        value.TryGetValue<bool>(out var enabled) && enabled;

    /// <summary>Cancellation leaves onboarding pending. A later launch never reapplies the initial choice.</summary>
    public static async Task<bool> EnsureAsync(string saveDirectory, Func<bool, Task<bool?>> prompt)
    {
        if (IsAcknowledged(saveDirectory)) return true;
        var choice = await prompt(InitialReduceFlashing(saveDirectory));
        if (choice is null) return false;
        Acknowledge(saveDirectory, choice.Value);
        return true;
    }

    public static void Acknowledge(string saveDirectory, bool reduceFlashing)
    {
        if (IsAcknowledged(saveDirectory)) return;
        var display = ReadDisplay(saveDirectory);
        display["ReduceFlashing"] = reduceFlashing;
        Directory.CreateDirectory(saveDirectory);
        WriteAtomic(Path.Combine(saveDirectory, "display.json"), display.ToJsonString() + "\n");
        // Commit acknowledgment only after the graphics preference is safely saved.
        WriteAtomic(Path.Combine(saveDirectory, "flashing-warning-acknowledged"), "acknowledged\n");
    }

    private static JsonObject ReadDisplay(string saveDirectory)
    {
        var path = Path.Combine(saveDirectory, "display.json");
        if (File.Exists(path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject display) return display;
            }
            catch (JsonException) { } // Match the game's fallback for unreadable display preferences.
        }
        return new JsonObject { ["Mode"] = "Fullscreen", ["Width"] = 600, ["Height"] = 360,
            ["VSync"] = true };
    }

    private static void WriteAtomic(string path, string text)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
