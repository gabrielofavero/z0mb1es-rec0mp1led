using System.Diagnostics;
using System.Reflection;
using Port.Bridge;

namespace Port.Hooks;

/// <summary>Persists newly earned original achievements and presents a local unlock notice.</summary>
public static class DesktopAchievements
{
    private static readonly Queue<int> pending = new();
    private static int visible = -1;
    private static long visibleUntil;

    public static void Configure()
    {
        pending.Clear();
        visible = -1;
        visibleUntil = 0;
    }

    public static void AddAchievable(object profile, int index)
    {
        var flags = (bool[]?)profile.GetType().GetField("achievables")?.GetValue(profile)
            ?? throw new MissingFieldException("PlayerProfile.achievables");
        if ((uint)index >= flags.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var wasEarned = flags[index];
        profile.GetType().GetMethod("AddAchievable", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(profile, [index]);
        if (wasEarned || !flags[index]) return;

        pending.Enqueue(index);
        Console.WriteLine($"ACHIEVEMENT_UNLOCKED index={index} key={AchievementCatalog.Keys[index]}");
        var gameType = profile.GetType().Assembly.GetType("ZombiesWP7.Game1", true)!;
        const BindingFlags fields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var player = gameType.GetField("player", fields)?.GetValue(null);
        var custom = gameType.GetField("custom", fields)?.GetValue(null);
        if (player is not null && custom is not null)
        {
            try { DesktopStorage.Write(profile, player, custom); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                TargetInvocationException && error.GetBaseException() is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"ACHIEVEMENT_SAVE_FAILED index={index} reason={error.GetType().Name}");
            }
        }
    }

    public static string? CurrentToast()
    {
        var now = Stopwatch.GetTimestamp();
        if (visible >= 0 && now >= visibleUntil) visible = -1;
        if (visible < 0 && pending.Count > 0)
        {
            visible = pending.Dequeue();
            visibleUntil = now + 4 * Stopwatch.Frequency;
        }
        return visible < 0 ? null : AchievementCatalog.Names[visible];
    }
}
