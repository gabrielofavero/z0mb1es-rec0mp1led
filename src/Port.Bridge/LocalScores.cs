using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Port.Bridge;

public static class LocalScores
{
    private sealed record Run(int Mode, long Score, DateTimeOffset PlayedAt);
    private static string? path;
    private static readonly object Gate = new();
    private static List<Run> runs = [];

    public static void Configure(string directory)
    {
        lock (Gate)
        {
            path = Path.Combine(directory, "leaderboard-runs.json");
            runs = [];
            try
            {
                if (File.Exists(path)) runs = JsonSerializer.Deserialize<List<Run>>(File.ReadAllText(path)) ?? [];
                runs = runs.Where(run => run.Mode is >= 0 and <= 2 && run.Score >= 0).TakeLast(1000).ToList();
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"LOCAL_SCORES_LOAD_SKIPPED {error.GetType().Name}");
            }
        }
    }

    public static void Record(int mode, long score)
    {
        if (mode is < 0 or > 2 || score < 0) return;
        lock (Gate)
        {
            if (path is null) return;
            runs.Add(new Run(mode, score, DateTimeOffset.Now));
            runs = runs.TakeLast(1000).ToList();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(runs));
                File.Move(temp, path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"LOCAL_SCORES_SAVE_FAILED {error.GetType().Name}");
            }
        }
    }

    internal static ReadOnlyCollection<(string Label, long Score)> Top(int mode, int count)
    {
        lock (Gate)
            return runs.Where(run => run.Mode == mode).OrderByDescending(run => run.Score)
                .Take(Math.Max(0, count))
                .Select(run => (run.PlayedAt.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture), run.Score))
                .ToList().AsReadOnly();
    }
}
