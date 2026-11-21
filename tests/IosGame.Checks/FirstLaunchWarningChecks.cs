using System.Reflection;
using System.Text.Json.Nodes;
using Port.Hooks;
using Port.Prepare;

internal static class FirstLaunchWarningChecks
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "z0mb1es-warning-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cancelled = Path.Combine(root, "cancelled");
            if (await FirstLaunchWarning.EnsureAsync(cancelled, initial =>
                {
                    if (initial) throw new Exception("Fresh checkbox was checked.");
                    return Task.FromResult<bool?>(null);
                }) || Directory.Exists(cancelled))
                throw new Exception("Cancelling acknowledged the warning or wrote player data.");

            foreach (var enabled in new[] { false, true })
            {
                var saves = Path.Combine(root, enabled ? "reduced" : "original");
                if (!await FirstLaunchWarning.EnsureAsync(saves, _ => Task.FromResult<bool?>(enabled)) ||
                    !FirstLaunchWarning.IsAcknowledged(saves))
                    throw new Exception("Continuing did not acknowledge the warning.");
                AssertGamePreference(saves, enabled);

                // Settings can change later without showing the warning or reapplying its choice.
                var displayPath = Path.Combine(saves, "display.json");
                var display = JsonNode.Parse(File.ReadAllText(displayPath))!.AsObject();
                display["ReduceFlashing"] = !enabled;
                File.WriteAllText(displayPath, display.ToJsonString());
                var before = File.ReadAllText(displayPath);
                if (!await FirstLaunchWarning.EnsureAsync(saves, _ =>
                    throw new Exception("Acknowledged warning appeared on a later launch.")))
                    throw new Exception("Acknowledged launch was cancelled.");
                FirstLaunchWarning.Acknowledge(saves, enabled);
                if (File.ReadAllText(displayPath) != before)
                    throw new Exception("Later launch reset the player's graphics choice.");
                AssertGamePreference(saves, !enabled);
            }

            var existing = Path.Combine(root, "existing");
            Directory.CreateDirectory(existing);
            const string previous = """
                {"Mode":"Windowed","Width":1280,"Height":720,"Auto":false,"VSync":false,
                 "ReduceFlashing":true,"SafeArea":false,"TouchpadSize":"Small","FutureSetting":"keep"}
                """;
            File.WriteAllText(Path.Combine(existing, "display.json"), previous);
            File.WriteAllText(Path.Combine(existing, "savegame"), "saved progress");
            await FirstLaunchWarning.EnsureAsync(existing, initial =>
            {
                if (!initial) throw new Exception("Existing reduced-flashing choice was not reflected in the checkbox.");
                return Task.FromResult<bool?>(false);
            });
            var expected = JsonNode.Parse(previous)!;
            expected["ReduceFlashing"] = false;
            if (!JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(Path.Combine(existing, "display.json")))) ||
                File.ReadAllText(Path.Combine(existing, "savegame")) != "saved progress")
                throw new Exception("Warning changed unrelated settings or saved progress.");
            AssertGamePreference(existing, false);

            var failed = Path.Combine(root, "failed");
            Directory.CreateDirectory(Path.Combine(failed, "display.json"));
            try
            {
                await FirstLaunchWarning.EnsureAsync(failed, _ => Task.FromResult<bool?>(true));
                throw new Exception("Failed preference write allowed acknowledgment.");
            }
            catch (IOException) { }
            if (FirstLaunchWarning.IsAcknowledged(failed) || Directory.GetFiles(failed).Length != 0)
                throw new Exception("Failed preference write committed acknowledgment or left scratch files.");

            // An interrupted acknowledgment is retried even if the preference was already saved.
            var interrupted = Path.Combine(root, "interrupted");
            var marker = Path.Combine(interrupted, "flashing-warning-acknowledged");
            Directory.CreateDirectory(marker);
            try { FirstLaunchWarning.Acknowledge(interrupted, true); throw new Exception("Marker failure ignored."); }
            catch (IOException) { }
            if (FirstLaunchWarning.IsAcknowledged(interrupted)) throw new Exception("Failed marker looked acknowledged.");
            Directory.Delete(marker);
            await FirstLaunchWarning.EnsureAsync(interrupted, initial =>
            {
                if (!initial) throw new Exception("Retry lost the previously saved checkbox choice.");
                return Task.FromResult<bool?>(initial);
            });
            AssertGamePreference(interrupted, true);

            Console.WriteLine("FIRST_LAUNCH_WARNING_OK cancel/both-choices/game-preference/restart/settings-preserved/write-failure/retry");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void AssertGamePreference(string saves, bool enabled)
    {
        // Exercise the actual graphics preference reader without starting an animated game.
        DesktopStorage.Configure(saves);
        var read = typeof(DesktopDisplay).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
        var preferences = read.Invoke(null, null)!;
        if ((bool)preferences.GetType().GetProperty("ReduceFlashing")!.GetValue(preferences)! != enabled)
            throw new Exception("The game did not load the warning's flashing preference.");
    }
}
