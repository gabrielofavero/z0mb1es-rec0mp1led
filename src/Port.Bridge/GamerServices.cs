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
    public virtual string Gamertag => PlayerIdentity.Name;
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
    internal int GameMode { get; }
    private LeaderboardIdentity(int gameMode) => GameMode = gameMode;
    public static LeaderboardIdentity Create(LeaderboardKey key, int gameMode) =>
        new(gameMode);
}

public sealed class LeaderboardReader
{
    private readonly ReadOnlyCollection<LeaderboardEntry> entries;
    private LeaderboardReader(LeaderboardIdentity identity, int pageSize) => entries =
        new(LocalScores.Top(identity.GameMode, pageSize)
            .Select(row => new LeaderboardEntry(row.Label, row.Score)).ToList());
    public static IAsyncResult BeginRead(LeaderboardIdentity identity, IEnumerable<Gamer> gamers,
        Gamer pivot, int pageSize, AsyncCallback callback, object state) =>
        CompletedResult<LeaderboardReader>.Create(new LeaderboardReader(identity, pageSize), callback, state);
    public static LeaderboardReader EndRead(IAsyncResult result) =>
        CompletedResult<LeaderboardReader>.End(result);
    public ReadOnlyCollection<LeaderboardEntry> Entries => entries;
}

public sealed class LeaderboardEntry
{
    private readonly Gamer gamer;
    private long rating;
    internal LeaderboardEntry(string label, long score) { gamer = new LocalRunGamer(label); rating = score; }
    public Gamer Gamer => gamer;
    public long Rating
    {
        get => rating;
        set => rating = value;
    }
}

internal sealed class LocalRunGamer(string label) : Gamer
{
    public override string Gamertag => label;
}

public sealed class LeaderboardWriter
{
    public LeaderboardEntry GetLeaderboard(LeaderboardIdentity identity) =>
        Unavailable.Api<LeaderboardEntry>("LeaderboardWriter.GetLeaderboard(LeaderboardIdentity)");
}

public sealed class AchievementCollection
{
    private readonly Achievement[] achievements;
    internal AchievementCollection(bool[] flags) => achievements = AchievementCatalog.Keys.Select((key, index) =>
        new Achievement(index, key, index < flags.Length && flags[index])).ToArray();
    public int Count => achievements.Length;
    public Achievement this[int index] => achievements[index];
}

public sealed class Achievement
{
    private readonly int index;
    private readonly string key;
    private readonly bool earned;
    internal Achievement(int index, string key, bool earned) { this.index = index; this.key = key; this.earned = earned; }
    public string Name => AchievementCatalog.Names[index];
    public int GamerScore => 0;
    public string Key => key;
    public bool IsEarned => earned;
    public string HowToEarn => AchievementCatalog.Conditions[index];
    public string Description => AchievementCatalog.Conditions[index];
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
