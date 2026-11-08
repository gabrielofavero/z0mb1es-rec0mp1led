using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;

/// <summary>
/// Developer diagnostic that drives original game methods through one mode cycle.
/// It bypasses input and therefore cannot certify player-facing menu navigation.
/// </summary>
internal sealed class ModeProbe
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Game game;
    private readonly Type gameType;
    private readonly Type stateType;
    private readonly Type menuType;
    private readonly string mode;
    private readonly int menuState;
    private readonly int playState;
    private int step;
    private int stepAt;
    private int mainMenuReturnedAt = -1;
    private bool progressionChecked;
    private bool frameCaptured;
    private bool pauseCaptured;
    private bool splashCaptured;
    private bool menuCaptured;

    public bool Verified { get; private set; }
    public string Mode => mode;
    public int PlayStartedAt => step >= 2 ? playStartedAt : -1;
    private int playStartedAt = -1;
    public bool ShouldCapture(int updates) => step == 2 && !frameCaptured && updates - stepAt >= 120;
    public void MarkCaptured() => frameCaptured = true;
    public bool ShouldCapturePause(int updates) => step == 3 && !pauseCaptured && updates - stepAt >= 20;
    public void MarkPauseCaptured() => pauseCaptured = true;
    public bool ShouldCaptureSplash(int updates) => step == 1 && !splashCaptured && updates - stepAt >= 1;
    public void MarkSplashCaptured() => splashCaptured = true;
    public bool ShouldCaptureModeMenu(int updates) => step == 1 && !menuCaptured && updates - stepAt >= 25;
    public void MarkModeMenuCaptured() => menuCaptured = true;

    public ModeProbe(Game game, Type gameType, string mode)
    {
        this.game = game;
        this.gameType = gameType;
        this.mode = mode;
        stateType = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.director.GameState", true)!;
        menuType = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!;
        (menuState, playState) = mode switch
        {
            "normal" => (2, 3),
            "viking" => (4, 5),
            "endless" => (6, 7),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    public void Update(int updates, bool mainMenuReady)
    {
        if (step == 0 && mainMenuReady && updates >= 120)
        {
            var target = GameObject();
            if (mode == "endless") Invoke(target, "Init", true);
            else Invoke(target, "Init");
            State = menuState;
            Advance(1, updates, "init");
        }
        else if (step == 1 && updates - stepAt >= 35 && (bool)(GameObject().GetType().GetField(
                     mode == "viking" ? "loadComplete" : "loadcomplete", Instance)?.GetValue(GameObject()) ?? false))
        {
            if (mode != "viking")
            {
                var players = (Array)menuType.GetField("playerState", Static)!.GetValue(null)!;
                players.SetValue(Enum.ToObject(players.GetType().GetElementType()!, 2), 0);
            }
            var target = GameObject();
            if (mode == "endless") Invoke(target, "Play", true);
            else Invoke(target, "Play");
            Require(State == playState, "play state was not entered");
            playStartedAt = updates;
            Advance(2, updates, "playing");
        }
        else if (step == 2 && mode == "endless" && !progressionChecked && updates - stepAt >= 180)
        {
            var endless = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.ZombieGame", true)!
                .GetField("endlessUpdate", Static)!.GetValue(null)!;
            var roundField = endless.GetType().GetField("round", Instance)!;
            var initialRound = (int)roundField.GetValue(endless)!;
            for (var i = 0; i < 6 && (int)roundField.GetValue(endless)! <= 2; i++)
                Invoke(endless, "NextRound");
            var round = (int)roundField.GetValue(endless)!;
            var profile = gameType.GetField("profile", Static)!.GetValue(null)!;
            var earned = (bool[])profile.GetType().GetField("achievables", Instance)!.GetValue(profile)!;
            Console.WriteLine($"MODE_PROBE mode=endless event=round-check update={updates} initialRound={initialRound} round={round} oneRound={earned[15]} twoRounds={earned[16]}");
            Require(round >= 3 && earned[15] && earned[16], "endless round awards did not persist in the profile");
            progressionChecked = true;
            Console.WriteLine($"MODE_PROBE mode=endless event=round-awards update={updates} round={round} oneRound={earned[15]} twoRounds={earned[16]}");
        }
        else if (step == 2 && updates - stepAt >= 300)
        {
            Require(State == playState, "mode stopped before the play interval ended");
            Pause();
            Advance(3, updates, "paused");
        }
        else if (step == 3 && updates - stepAt >= 30)
        {
            ClickLevel("Pause", "Resum3");
            Require(!LevelActive("Pause") && State == playState && CurrentPlayMode() == 1,
                "resume failed");
            Advance(4, updates, "resumed");
        }
        else if (step == 4 && updates - stepAt >= 120)
        {
            Require(State == playState, "mode stopped after resume");
            Pause();
            ClickLevel("Pause", "End Gam3");
            Require(LevelActive("EndGameYouSure"), "end-game confirmation did not open");
            Advance(5, updates, "end-confirmation");
        }
        else if (step == 5 && updates - stepAt >= 30)
        {
            ClickLevel("EndGameYouSure", "End Gam3");
            Require(State == menuState, "end-game did not return to mode menu");
            menuType.GetField("needsQuit", Static)!.SetValue(null, true);
            Advance(6, updates, "mode-menu");
        }
        else if (step == 6 && State == 1 && mainMenuReady)
        {
            // Game1 enters state 1 after TouchMgr.Update, so the input hook restores
            // the new menu on the following update.
            if (mainMenuReturnedAt < 0) { mainMenuReturnedAt = updates; return; }
            var mainMenu = gameType.GetField("mainMenu", Static)!.GetValue(null)!;
            var scene = mainMenu.GetType().GetField("sceneMgr", Instance)!.GetValue(mainMenu)!;
            var video = scene.GetType().GetField("video", Instance)!.GetValue(scene)!;
            var position = (float)video.GetType().GetField("time", Instance)!.GetValue(video)!;
            var expected = mode switch { "viking" => 1f, "endless" => 2f, _ => 0f };
            Require(MathF.Abs(position - expected) < 0.01f,
                $"main carousel returned to {position} instead of {expected}");
            Verified = true;
            Advance(7, updates, "main-menu");
            game.Exit();
        }
    }

    private object GameObject() => gameType.GetField(mode == "viking" ? "vgame" : "zgame", Static)!
        .GetValue(null) ?? throw new InvalidOperationException("Original mode object is missing.");

    private int State
    {
        get => (int)stateType.GetField("state", Static)!.GetValue(null)!;
        set => stateType.GetField("state", Static)!.SetValue(null, value);
    }

    private void Pause()
    {
        var manager = CurrentTimeManager();
        Invoke(manager, "Pause", 0);
        menuType.GetMethod("SetLevel", Static)!.Invoke(null, [2]);
        Require(LevelActive("Pause") && CurrentPlayMode() == 2, "pause menu or time state did not activate");
    }

    private object CurrentTimeManager()
    {
        var timeMgr = gameType.Assembly.GetType("Viking_x86.director.TimeMgr", true)!;
        return timeMgr.GetMethod("CurTMgr", Static)!.Invoke(null, null)!;
    }

    private int CurrentPlayMode() => Convert.ToInt32(CurrentTimeManager().GetType()
        .GetField("playMode", Instance)!.GetValue(CurrentTimeManager()));

    private object Level(string name)
    {
        var levels = (IDictionary)menuType.GetField("menuLevel", Static)!.GetValue(null)!;
        foreach (DictionaryEntry entry in levels)
            if (entry.Value?.GetType().Name == name) return entry.Value;
        throw new InvalidOperationException($"Original menu level is missing: {name}");
    }

    private bool LevelActive(string name) => (bool)Level(name).GetType().BaseType!
        .GetField("active", Instance)!.GetValue(Level(name))!;

    private void ClickLevel(string name, string item) => Invoke(Level(name), "Click", item);

    private static void Invoke(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethods(Instance).Single(m => m.Name == name &&
            m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => p.ParameterType.IsInstanceOfType(args[i])).All(x => x));
        method.Invoke(target, args);
    }

    private void Advance(int next, int updates, string eventName)
    {
        step = next;
        stepAt = updates;
        Console.WriteLine($"MODE_PROBE mode={mode} event={eventName} update={updates} state={State}");
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException($"Mode probe: {failure}.");
    }
}
