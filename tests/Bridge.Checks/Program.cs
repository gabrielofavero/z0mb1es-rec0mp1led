using System.Diagnostics;
using System.Collections.ObjectModel;
using Microsoft.Devices;
using Microsoft.Devices.Sensors;
using Microsoft.Phone.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Port.Bridge;

if (typeof(GamerServicesComponent).BaseType != typeof(GameComponent) ||
    typeof(SignedInGamer).BaseType != typeof(Gamer) ||
    typeof(SignedInGamerCollection).BaseType != typeof(ReadOnlyCollection<SignedInGamer>) ||
    !typeof(LeaderboardKey).IsEnum || !typeof(MessageBoxIcon).IsEnum ||
    !typeof(MarketplaceContentType).IsEnum)
    throw new Exception("A bridge inheritance or enum shape changed.");

using var game = new Game();
if (BridgeRuntime.IsAttached(game)) throw new Exception("Bridge was attached before component construction.");
using var component = new GamerServicesComponent(game);
if (!BridgeRuntime.IsAttached(game) || !ReferenceEquals(component.Game, game))
    throw new Exception("Gamer services component did not attach to its game.");
if (Guide.IsVisible) throw new Exception("An unshown desktop guide claims to be visible.");
Guide.IsScreenSaverEnabled = false;
if (Guide.IsScreenSaverEnabled) throw new Exception("Screen saver state did not persist.");
Guide.IsScreenSaverEnabled = true;

using var captured = new StringWriter();
using var listener = new TextWriterTraceListener(captured);
Trace.Listeners.Add(listener);
Trace.AutoFlush = true;
AppContext.SetSwitch("Port.Bridge.SuppressPhoneDialogs", true);
try
{
    if (Guide.IsTrialMode || Guide.SimulateTrialMode)
        throw new Exception("Offline desktop policy unexpectedly enabled trial mode.");
    if (Gamer.SignedInGamers.Count != 1 || Gamer.SignedInGamers[PlayerIndex.One].IsSignedInToLive)
        throw new Exception("Local offline profile is unavailable or claims Xbox Live sign-in.");
    var local = Gamer.SignedInGamers[PlayerIndex.One];
    var pending = local.BeginGetAchievements(null!, null!);
    if (!pending.IsCompleted || local.EndGetAchievements(pending).Count != 20)
        throw new Exception("Local achievement list did not complete.");
    var award = local.BeginAwardAchievement("im_win", null!, null!);
    local.EndAwardAchievement(award);
    if (local.EndGetAchievements(local.BeginGetAchievements(null!, null!))[2].IsEarned)
        throw new Exception("Service call awarded an achievement without an original profile trigger.");
    var board = LeaderboardReader.BeginRead(LeaderboardIdentity.Create(default, 0), [local], local,
        10, null!, null!);
    if (!board.IsCompleted || LeaderboardReader.EndRead(board).Entries.Count != 0)
        throw new Exception("Offline leaderboard did not return an empty result.");
    var callbackDone = new ManualResetEventSlim();
    IAsyncResult? callbackResult = null;
    var dialogState = new object();
    var dialog = Guide.BeginShowMessageBox("Quit?", "Leave the game?", ["Yes", "No"], 1,
        default, result => { callbackResult = result; callbackDone.Set(); }, dialogState);
    if (!callbackDone.Wait(TimeSpan.FromSeconds(5)) || !ReferenceEquals(dialog, callbackResult) ||
        !ReferenceEquals(dialog.AsyncState, dialogState) || Guide.EndShowMessageBox(dialog) is not null)
        throw new Exception("Desktop message box did not complete safely when dialogs were suppressed.");
    var quit = Guide.BeginShowMessageBox("Quit game?", "Exit to phone?", ["Quit", "Cancel"], 1,
        default, null!, null!);
    if (!quit.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)) || Guide.EndShowMessageBox(quit) != 0)
        throw new Exception("Main menu quit did not complete without a desktop dialog.");
    var accelerometer = new Accelerometer();
    accelerometer.Start();
    accelerometer.Start();
    new MarketplaceDetailTask().Show();
    Guide.ShowMarketplace(PlayerIndex.One);
    VibrateController.Default.Start(TimeSpan.FromMilliseconds(100));
    VibrateController.Default.Start(TimeSpan.FromMilliseconds(100));
}
finally
{
    listener.Flush();
    Trace.Listeners.Remove(listener);
}
foreach (var api in new[] { "Accelerometer.Start()", "MarketplaceDetailTask.Show()",
             "Guide.ShowMarketplace(PlayerIndex)", "VibrateController.Start(TimeSpan)" })
    if (!captured.ToString().Contains(api, StringComparison.Ordinal))
        throw new Exception($"Missing diagnostic log for {api}.");

Console.WriteLine("BRIDGE_CHECKS_OK component attached; menu startup policies and unsupported API diagnostics verified.");
