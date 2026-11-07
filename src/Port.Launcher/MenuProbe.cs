using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Port.Hooks;

/// <summary>Developer diagnostic for original menu construction and drawing.</summary>
internal sealed class MenuProbe
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Game game;
    private readonly Type menuType;
    private IDictionary? levels;
    private int current = -1;
    private int openedAt;
    private bool captured;
    private bool touchOpened;
    private bool touchCaptured;
    private bool displayOpened;
    private bool displayCaptured;
    private bool customOpened;
    private bool customCaptured;
    private bool assetOpened;
    private bool assetCaptured;

    public bool Verified { get; private set; }
    public int CurrentLevel => current;
    public bool ShouldCapture(int updates) => current >= 0 && current < 17 &&
        current is not (11 or 16) && !captured && updates - openedAt >= 25;
    public void MarkCaptured() => captured = true;
    public bool ShouldCaptureTouch(int updates) => current == 5 && touchOpened &&
        !touchCaptured && updates - openedAt >= 2;
    public void MarkTouchCaptured() => touchCaptured = true;
    public bool ShouldCaptureDisplay(int updates) => current == 5 && displayOpened &&
        !displayCaptured && updates - openedAt >= 2;
    public void MarkDisplayCaptured() => displayCaptured = true;
    public bool ShouldCaptureCustom(int updates) => current == 5 && customOpened &&
        !customCaptured && updates - openedAt >= 2;
    public void MarkCustomCaptured() => customCaptured = true;
    public bool ShouldCaptureAsset(int updates) => current == 5 && assetOpened &&
        !assetCaptured && updates - openedAt >= 2;
    public void MarkAssetCaptured() => assetCaptured = true;

    public MenuProbe(Game game, Type gameType)
    {
        this.game = game;
        menuType = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!;
    }

    public void Update(int updates, bool mainMenuReady)
    {
        if (!mainMenuReady) return;
        levels ??= (IDictionary?)menuType.GetField("menuLevel", Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Original menu has not been initialized.");
        if (levels.Count != 17) throw new InvalidOperationException("Original menu level count changed.");
        if (current == -1)
        {
            Open(0, updates);
            return;
        }
        if (current >= 17 || updates - openedAt < 32) return;
        var level = levels![current] ?? throw new InvalidOperationException($"Missing original menu level {current}.");
        var baseType = level.GetType().BaseType!;
        var active = (bool)baseType.GetField("active", Instance)!.GetValue(level)!;
        var alpha = (float)baseType.GetField("alpha", Instance)!.GetValue(level)!;
        if (current is 11 or 16)
        {
            var thanks = levels[12]!;
            var thanksActive = (bool)thanks.GetType().BaseType!
                .GetField("active", Instance)!.GetValue(thanks)!;
            if (active || !thanksActive)
                throw new InvalidOperationException($"Menu probe level {current} did not follow the original full-game redirect.");
            Console.WriteLine($"MENU_PROBE level={current} type={level.GetType().Name} redirected=ThanksUpsell trial=False");
            if (current == 16)
            {
                menuType.GetMethod("SetLevel", Static)!.Invoke(null, [-1]);
                Verified = true;
                game.Exit();
                return;
            }
            Open(current + 1, updates);
            return;
        }
        if (!active || alpha < 0.9f || !captured)
            throw new InvalidOperationException($"Menu probe level {current} did not remain visible.");
        if (current == 5 && !touchOpened)
        {
            var settingsItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var textField = settingsItems.GetValue(5)!.GetType().GetField("text", Instance)!;
            if (settingsItems.Length != 9 ||
                (string?)textField.GetValue(settingsItems.GetValue(5)) != "GRAPH1CS" ||
                (string?)textField.GetValue(settingsItems.GetValue(6)) != "AUD10" ||
                (string?)textField.GetValue(settingsItems.GetValue(7)) != "1NPUT" ||
                (string?)textField.GetValue(settingsItems.GetValue(8)) != "ASSETS")
                throw new InvalidOperationException("Settings category order differs.");
            var location = (Vector2)settingsItems.GetValue(5)!.GetType()
                .GetField("loc", Instance)!.GetValue(settingsItems.GetValue(5))!;
            var menuScale = (float)game.GetType().GetField("MENUSCALE", Static)!.GetValue(null)!;
            var centeredX = 250 + (DesktopDisplay.SceneSize.X - DesktopDisplay.LogicalWidth) / (2f * menuScale);
            if (MathF.Abs(location.X - centeredX) > 0.01f)
                throw new InvalidOperationException($"Settings items are not centered: {location.X} versus {centeredX}.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["1NPUT"]);
            if (!DesktopDisplay.IsTouchPage)
                throw new InvalidOperationException("Settings T0UCH entry did not open.");
            touchOpened = true;
            openedAt = updates;
            return;
        }
        if (current == 5 && !displayOpened)
        {
            if (!touchCaptured || !DesktopDisplay.IsTouchPage)
                throw new InvalidOperationException("Native Touch settings page was not rendered.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (DesktopDisplay.IsTouchPage)
                throw new InvalidOperationException("Touch settings page did not return to Settings.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["AUD10"]);
            var audioItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            if (audioItems.Length != 6 ||
                (string?)audioItems.GetValue(3)!.GetType().GetField("text", Instance)!.GetValue(audioItems.GetValue(3)) != "SFX" ||
                (string?)audioItems.GetValue(4)!.GetType().GetField("text", Instance)!.GetValue(audioItems.GetValue(4)) != "BGM")
                throw new InvalidOperationException("Audio settings page differs.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["GRAPH1CS"]);
            if (!DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Settings DISPLAY entry did not open.");
            var graphics = (GraphicsDeviceManager)game.Services.GetService(typeof(IGraphicsDeviceManager))!;
            var initialVSync = graphics.SynchronizeWithVerticalRetrace;
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, [initialVSync ? "VSYNC: ON" : "VSYNC: OFF"]);
            if (graphics.SynchronizeWithVerticalRetrace == initialVSync)
                throw new InvalidOperationException("Settings VSync click did not apply.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, [initialVSync ? "VSYNC: OFF" : "VSYNC: ON"]);
            if (graphics.SynchronizeWithVerticalRetrace != initialVSync)
                throw new InvalidOperationException("Settings VSync did not restore.");
            var initialFlash = DesktopDisplay.ReduceFlashing;
            level.GetType().GetMethod("Click", Instance)!.Invoke(level,
                [initialFlash ? "R3DUCE FLASH1NG: ON" : "R3DUCE FLASH1NG: OFF"]);
            if (DesktopDisplay.ReduceFlashing == initialFlash)
                throw new InvalidOperationException("Reduce Flashing did not toggle.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level,
                [initialFlash ? "R3DUCE FLASH1NG: OFF" : "R3DUCE FLASH1NG: ON"]);
            if (DesktopDisplay.ReduceFlashing != initialFlash)
                throw new InvalidOperationException("Reduce Flashing did not restore.");
            var desktopMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            var presets = DesktopDisplay.AvailablePresets(desktopMode.Width, desktopMode.Height);
            var start = Array.FindIndex(presets, preset =>
                preset.Width == game.GraphicsDevice.PresentationParameters.BackBufferWidth &&
                preset.Height == game.GraphicsDevice.PresentationParameters.BackBufferHeight);
            for (var i = 1; i <= presets.Length; i++)
            {
                level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["PRES3T:"]);
                var expected = presets[start < 0 ? (i - 1) % presets.Length : (start + i) % presets.Length];
                var actual = game.GraphicsDevice.PresentationParameters;
                if (actual.BackBufferWidth != expected.Width || actual.BackBufferHeight != expected.Height)
                    throw new InvalidOperationException($"Settings preset cycle expected {expected.Width}x{expected.Height}, got {actual.BackBufferWidth}x{actual.BackBufferHeight}.");
            }
            if (!DesktopDisplay.ApplyForProbe("Windowed", 1280, 720))
                throw new InvalidOperationException("Live display resize did not apply.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["M0DE: WINDOWED"]);
            if (!game.Window.IsBorderlessEXT || game.GraphicsDevice.PresentationParameters.IsFullScreen)
                throw new InvalidOperationException("Settings mode click did not apply borderless mode.");
            if (!DesktopDisplay.ApplyForProbe("Windowed", 1280, 720))
                throw new InvalidOperationException("Display mode did not restore after the click.");
            displayOpened = true;
            openedAt = updates;
            return;
        }
        if (current == 5 && !customOpened)
        {
            if (!displayCaptured || !DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Native Display settings page was not rendered.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["CUST0M RES0LUTI0N"]);
            if (!DesktopDisplay.IsCustomPage)
                throw new InvalidOperationException("Custom Resolution entry did not open.");
            customOpened = true;
            openedAt = updates;
            return;
        }
        if (current == 5 && !assetOpened)
        {
            if (!customCaptured || !DesktopDisplay.IsCustomPage)
                throw new InvalidOperationException("Native Custom Resolution page was not rendered.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["APPLY CUST0M"]);
            if (game.GraphicsDevice.PresentationParameters.BackBufferWidth != 1280 ||
                game.GraphicsDevice.PresentationParameters.BackBufferHeight != 720)
                throw new InvalidOperationException("Custom resolution did not apply.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (!DesktopDisplay.IsDisplayPage || DesktopDisplay.IsCustomPage)
                throw new InvalidOperationException("Custom Resolution page did not return to Display.");
            if (!DesktopDisplay.ApplyForProbe("Windowed", 600, 360))
                throw new InvalidOperationException("Display size did not restore.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Display settings page did not return to Settings.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["ASSETS"]);
            if (!DesktopDisplay.IsAssetPage)
                throw new InvalidOperationException("Assets entry did not open.");
            assetOpened = true;
            openedAt = updates;
            return;
        }
        if (current == 5)
        {
            if (!assetCaptured || !DesktopDisplay.IsAssetPage)
                throw new InvalidOperationException("Assets page was not rendered.");
            var assetItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            if (assetItems.Length != 4 ||
                !((string)assetItems.GetValue(0)!.GetType().GetField("text", Instance)!
                    .GetValue(assetItems.GetValue(0))!).StartsWith("I MAED:", StringComparison.Ordinal))
                throw new InvalidOperationException("Assets rows differ.");
            var before = AssetChoices.NormalImported;
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["I MAED:"]);
            if (AssetChoices.NormalAvailable && AssetChoices.NormalImported == before)
                throw new InvalidOperationException("I MAED selection did not toggle.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (DesktopDisplay.IsAssetPage)
                throw new InvalidOperationException("Assets Back did not return to Settings.");
        }
        Console.WriteLine($"MENU_PROBE level={current} type={level.GetType().Name} active={active} alpha={alpha} captured={captured}");
        if (current == 16)
        {
            menuType.GetMethod("SetLevel", Static)!.Invoke(null, [-1]);
            Verified = true;
            game.Exit();
            return;
        }
        Open(current + 1, updates);
    }

    private void Open(int key, int updates)
    {
        var level = levels![key] ?? throw new InvalidOperationException($"Missing original menu level {key}.");
        var type = level.GetType();
        var prepare = type.GetMethod(key switch
        {
            5 or 10 => "SetFromPlayer",
            7 or 8 or 9 => "Init",
            _ => "__none__"
        }, Instance);
        prepare?.Invoke(level, null);
        menuType.GetMethod("SetLevel", Static)!.Invoke(null, [key]);
        current = key;
        openedAt = updates;
        captured = false;
        Console.WriteLine($"MENU_PROBE_OPEN level={key} type={type.Name} update={updates}");
    }
}
