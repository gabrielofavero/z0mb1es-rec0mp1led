using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
using Port.Bridge;

namespace Microsoft.Xna.Framework.GamerServices;

public sealed class GamerServicesComponent : GameComponent
{
    public GamerServicesComponent(Game game) : base(game) => BridgeRuntime.Attach(game);
}

public sealed class GameUpdateRequiredException : Exception
{
    public GameUpdateRequiredException() : base("The original phone service requested an update.") { }
}

public enum MessageBoxIcon { }

public static class Guide
{
    private static bool screenSaverEnabled = true;

    // These two properties reflect state owned by this desktop process.
    public static bool IsVisible => false;
    public static bool IsScreenSaverEnabled
    {
        get => screenSaverEnabled;
        set => screenSaverEnabled = value;
    }

    // Offline desktop policy: the user-supplied supported XAP runs without a phone license service.
    public static bool IsTrialMode => false;
    public static bool SimulateTrialMode => false;
    public static IAsyncResult BeginShowMessageBox(string title, string text,
        IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon,
        AsyncCallback callback, object state)
    {
        var choices = buttons.ToArray();
        System.Diagnostics.Trace.TraceInformation($"Guide.BeginShowMessageBox title={title} choices={choices.Length}");
        var mainMenuQuit = title == "Quit game?" && text == "Exit to phone?" &&
            choices.SequenceEqual(["Quit", "Cancel"]);
        if (mainMenuQuit)
            System.Diagnostics.Trace.TraceInformation("Main menu Back requested quit; exiting without a desktop dialog.");
        var result = Task.Factory.StartNew(_ => mainMenuQuit ? (int?)0 :
            DesktopPhoneFallback.ShowChoice(title, text, choices, focusButton),
            state, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        if (callback is not null)
            _ = result.ContinueWith(completed => callback(completed), TaskScheduler.Default);
        return result;
    }
    public static int? EndShowMessageBox(IAsyncResult result) => result is Task<int?> pending
        ? pending.GetAwaiter().GetResult()
        : throw new ArgumentException("Unexpected message box result.", nameof(result));
    public static void ShowMarketplace(PlayerIndex player) =>
        DesktopPhoneFallback.MarketplaceUnavailable("Guide.ShowMarketplace(PlayerIndex)");
}

public class Gamer
{
    public static SignedInGamerCollection SignedInGamers => SignedInGamerCollection.LocalGamers;
    public string Gamertag => "Local Player";
    public LeaderboardWriter LeaderboardWriter =>
        Unavailable.Api<LeaderboardWriter>("Gamer.LeaderboardWriter");
}

public sealed class SignedInGamer : Gamer
{
    public bool IsSignedInToLive => false;
    public IAsyncResult BeginGetAchievements(AsyncCallback callback, object state) =>
        CompletedResult<AchievementCollection>.Create(new AchievementCollection(BridgeRuntime.AchievementFlags()), callback, state);
    public AchievementCollection EndGetAchievements(IAsyncResult result) =>
        CompletedResult<AchievementCollection>.End(result);
    public IAsyncResult BeginAwardAchievement(string key, AsyncCallback callback, object state) =>
        CompletedResult<bool>.Create(true, callback, state);
    public void EndAwardAchievement(IAsyncResult result) =>
        _ = CompletedResult<bool>.End(result);
}

public sealed class SignedInGamerCollection : ReadOnlyCollection<SignedInGamer>
{
    internal static SignedInGamerCollection LocalGamers { get; } = new(new List<SignedInGamer> { new() });
    internal SignedInGamerCollection(IList<SignedInGamer> gamers) : base(gamers) { }
    public SignedInGamer this[PlayerIndex player] => player == PlayerIndex.One ? base[0] :
        throw new ArgumentOutOfRangeException(nameof(player));
}

public enum LeaderboardKey { }

public readonly struct LeaderboardIdentity
{
    public static LeaderboardIdentity Create(LeaderboardKey key, int gameMode) =>
        new();
}

public sealed class LeaderboardReader
{
    public static IAsyncResult BeginRead(LeaderboardIdentity identity, IEnumerable<Gamer> gamers,
        Gamer pivot, int pageSize, AsyncCallback callback, object state) =>
        CompletedResult<LeaderboardReader>.Create(new LeaderboardReader(), callback, state);
    public static LeaderboardReader EndRead(IAsyncResult result) =>
        CompletedResult<LeaderboardReader>.End(result);
    public ReadOnlyCollection<LeaderboardEntry> Entries =>
        new(Array.Empty<LeaderboardEntry>());
}

public sealed class LeaderboardEntry
{
    public Gamer Gamer => Unavailable.Api<Gamer>("LeaderboardEntry.Gamer");
    public long Rating
    {
        get => Unavailable.Api<long>("LeaderboardEntry.Rating.get");
        set => Unavailable.Api("LeaderboardEntry.Rating.set(Int64)");
    }
}

public sealed class LeaderboardWriter
{
    public LeaderboardEntry GetLeaderboard(LeaderboardIdentity identity) =>
        Unavailable.Api<LeaderboardEntry>("LeaderboardWriter.GetLeaderboard(LeaderboardIdentity)");
}

public sealed class AchievementCollection
{
    private static readonly string[] Keys =
    [
        "im_100k", "im_million", "im_win", "im_invincible", "im_asteroids", "im_nomove", "im_laser",
        "tv_100k", "tv_million", "tv_win", "tv_tower", "tv_invincible", "ez_100k", "ez_million",
        "ez_oneroom", "ez_oneround", "ez_tworounds", "kill1000", "kill10000", "mega"
    ];
    private readonly Achievement[] achievements;
    internal AchievementCollection(bool[] flags) => achievements = Keys.Select((key, index) =>
        new Achievement(key, index < flags.Length && flags[index])).ToArray();
    public int Count => achievements.Length;
    public Achievement this[int index] => achievements[index];
}

public sealed class Achievement
{
    private readonly string key;
    private readonly bool earned;
    internal Achievement(string key, bool earned) { this.key = key; this.earned = earned; }
    public string Name => key.Replace('_', ' ');
    public int GamerScore => 0;
    public string Key => key;
    public bool IsEarned => earned;
    public string HowToEarn => "Earn during play";
    public string Description => "Earned locally";
}

internal sealed class CompletedResult<T> : IAsyncResult
{
    private static readonly ManualResetEvent CompletedEvent = new(true);
    private readonly T value;
    private CompletedResult(T value, object? state) { this.value = value; AsyncState = state; }
    public object? AsyncState { get; }
    public WaitHandle AsyncWaitHandle => CompletedEvent;
    public bool CompletedSynchronously => true;
    public bool IsCompleted => true;
    internal static IAsyncResult Create(T value, AsyncCallback? callback, object? state)
    {
        var result = new CompletedResult<T>(value, state);
        callback?.Invoke(result);
        return result;
    }
    internal static T End(IAsyncResult result) => result is CompletedResult<T> completed
        ? completed.value : throw new ArgumentException("Unexpected service result.", nameof(result));
}
