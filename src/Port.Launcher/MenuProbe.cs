using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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
    private bool touchpadOpened;
    private bool touchpadCaptured;
    private bool gameplayOpened;
    private bool gameplayCaptured;
    private bool displayOpened;
    private bool displayCaptured;
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
    public bool ShouldCaptureTouchpad(int updates) => current == 5 && touchpadOpened &&
        !touchpadCaptured && updates - openedAt >= 2;
    public void MarkTouchpadCaptured() => touchpadCaptured = true;
    public bool ShouldCaptureGameplay(int updates) => current == 5 && gameplayOpened &&
        !gameplayCaptured && updates - openedAt >= 2;
    public void MarkGameplayCaptured() => gameplayCaptured = true;
    public bool ShouldCaptureDisplay(int updates) => current == 5 && displayOpened &&
        !displayCaptured && updates - openedAt >= 2;
    public void MarkDisplayCaptured() => displayCaptured = true;
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
        if (current is 2 or 3)
        {
            var rows = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var menuScale = (float)game.GetType().GetField("MENUSCALE", Static)!.GetValue(null)!;
            var centeredX = DesktopDisplay.SceneSize.X / (2f * menuScale);
            var locations = rows.Cast<object>().Select(row =>
                (Vector2)row.GetType().GetField("loc", Instance)!.GetValue(row)!).ToArray();
            var minX = locations.Min(position => position.X);
            var maxX = locations.Max(position => position.X);
            if (locations.Length != 3 || MathF.Abs((minX + maxX) / 2f - centeredX) > 0.01f ||
                MathF.Abs(locations[1].X - locations[0].X - 100f) > 0.01f ||
                MathF.Abs(locations[2].X - locations[1].X - (current == 2 ? 100f : 110f)) > 0.01f)
                throw new InvalidOperationException($"Menu {current} lost its staggered, centered layout.");
        }
        if (current == 5 && !gameplayOpened)
        {
            var settingsItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var textField = settingsItems.GetValue(5)!.GetType().GetField("text", Instance)!;
            if (settingsItems.Length != 8 ||
                (string?)textField.GetValue(settingsItems.GetValue(5)) != "GRAPH1CS" ||
                (string?)textField.GetValue(settingsItems.GetValue(6)) != "AUD10" ||
                (string?)textField.GetValue(settingsItems.GetValue(7)) != "GAMEPLAY")
                throw new InvalidOperationException("Settings category order differs.");
            var positions = Enumerable.Range(5, 3).Select(index =>
                (Vector2)settingsItems.GetValue(index)!.GetType().GetField("loc", Instance)!
                    .GetValue(settingsItems.GetValue(index))!).ToArray();
            var menuScale = (float)game.GetType().GetField("MENUSCALE", Static)!.GetValue(null)!;
            var centeredX = DesktopDisplay.SceneSize.X / (2f * menuScale);
            if (MathF.Abs(positions[0].X - (centeredX - 85f)) > 0.01f ||
                MathF.Abs(positions[1].X - (centeredX + 115f)) > 0.01f ||
                MathF.Abs(positions[2].X - (centeredX + 15f)) > 0.01f)
                throw new InvalidOperationException("Settings categories lost their staggered layout.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["GAMEPLAY"]);
            if (!DesktopDisplay.IsGameplayPage)
                throw new InvalidOperationException("Settings Gameplay entry did not open.");
            var gameplayItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            if (gameplayItems.Length != 4 ||
                (string?)textField.GetValue(gameplayItems.GetValue(0)) != "PLAYER NAME" ||
                (string?)textField.GetValue(gameplayItems.GetValue(1)) != "  " + Port.Bridge.PlayerIdentity.Name ||
                (string?)textField.GetValue(gameplayItems.GetValue(2)) != "1NPUT")
                throw new InvalidOperationException("Gameplay settings rows differ.");
            var gameplayOffsets = new[] { -45f, 40f, -30f, 25f };
            for (var i = 0; i < gameplayItems.Length; i++)
            {
                var position = (Vector2)gameplayItems.GetValue(i)!.GetType()
                    .GetField("loc", Instance)!.GetValue(gameplayItems.GetValue(i))!;
                if (MathF.Abs(position.X - (centeredX + gameplayOffsets[i])) > 0.01f)
                    throw new InvalidOperationException("Gameplay rows lost their staggered layout.");
            }
            if (!DesktopDisplay.IsStaticPlayerNameLabel(gameplayItems.GetValue(0)!) ||
                DesktopDisplay.IsStaticPlayerNameLabel(gameplayItems.GetValue(1)!))
                throw new InvalidOperationException("Player Name label selection differs.");
            var label = gameplayItems.GetValue(0)!;
            var labelPosition = (Vector2)label.GetType().GetField("loc", Instance)!.GetValue(label)!;
            if ((bool)label.GetType().GetMethod("TryClick", Instance)!.Invoke(label,
                    [labelPosition * menuScale, level])!)
                throw new InvalidOperationException("Player Name label still accepts clicks.");
            gameplayOpened = true;
            openedAt = updates;
            return;
        }
        if (current == 5 && !touchOpened)
        {
            if (!gameplayCaptured || !DesktopDisplay.IsGameplayPage)
                throw new InvalidOperationException("Gameplay settings page was not rendered.");
            var editingItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var nameValue = (string)editingItems.GetValue(1)!.GetType().GetField("text", Instance)!
                .GetValue(editingItems.GetValue(1))!;
            level.GetType().GetMethod("Click", Instance)!.Invoke(level,
                [nameValue]);
            if (!DesktopDisplay.CapturesInput)
                throw new InvalidOperationException("Player Name did not capture text input.");
            var textInput = typeof(TextInputEXT).GetMethod("OnTextInput", Static)!;
            for (var i = 0; i < 17; i++) textInput.Invoke(null, ['A']);
            var editingText = (string?)editingItems.GetValue(1)!.GetType().GetField("text", Instance)!
                .GetValue(editingItems.GetValue(1));
            if (editingText != "  " + new string('A', 16) + "_")
                throw new InvalidOperationException("Player Name did not enforce the 16-character limit.");
            level.GetType().GetMethod("Cancel", Instance)!.Invoke(level, null);
            if (DesktopDisplay.IsGameplayPage)
                throw new InvalidOperationException("Player Name cancel did not return to Settings.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["GAMEPLAY"]);
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["1NPUT"]);
            if (!DesktopDisplay.IsTouchPage)
                throw new InvalidOperationException("Gameplay Input entry did not open.");
            touchOpened = true;
            openedAt = updates;
            return;
        }
        if (current == 5 && !displayOpened)
        {
            if (!touchpadOpened)
            {
                if (!touchCaptured || !DesktopDisplay.IsTouchPage)
                    throw new InvalidOperationException("Input settings page was not rendered.");
                level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["T0UCHPADS"]);
                if (!DesktopDisplay.IsTouchpadPage)
                    throw new InvalidOperationException("Touchpads category did not open.");
                touchpadOpened = true;
                openedAt = updates;
                return;
            }
            if (!touchpadCaptured || !DesktopDisplay.IsTouchpadPage)
                throw new InvalidOperationException("Touchpads settings page was not rendered.");
            var touchpadItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var touchpadText = touchpadItems.GetValue(0)!.GetType().GetField("text", Instance)!;
            if (touchpadItems.Length != 4 ||
                (string?)touchpadText.GetValue(touchpadItems.GetValue(0)) != "RELATIVE CONTR0LS" ||
                (string?)touchpadText.GetValue(touchpadItems.GetValue(1)) != "SH0W T0UCHPADS")
                throw new InvalidOperationException("Touchpad settings rows differ.");
            var initialSize = (string)touchpadText.GetValue(touchpadItems.GetValue(2))!;
            for (var i = 0; i < 3; i++)
            {
                var currentSize = (string)touchpadText.GetValue(touchpadItems.GetValue(2))!;
                level.GetType().GetMethod("Click", Instance)!.Invoke(level, [currentSize]);
            }
            if ((string)touchpadText.GetValue(touchpadItems.GetValue(2))! != initialSize)
                throw new InvalidOperationException("Touchpad size did not complete its three-choice cycle.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (!DesktopDisplay.IsTouchPage || DesktopDisplay.IsTouchpadPage)
                throw new InvalidOperationException("Touchpad settings did not return to Input.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (DesktopDisplay.IsTouchPage || !DesktopDisplay.IsGameplayPage)
                throw new InvalidOperationException("Input settings page did not return to Gameplay.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (DesktopDisplay.IsGameplayPage)
                throw new InvalidOperationException("Gameplay settings page did not return to Settings.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["AUD10"]);
            var audioItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            if (audioItems.Length != 6 ||
                (string?)audioItems.GetValue(3)!.GetType().GetField("text", Instance)!.GetValue(audioItems.GetValue(3)) != "SFX" ||
                (string?)audioItems.GetValue(4)!.GetType().GetField("text", Instance)!.GetValue(audioItems.GetValue(4)) != "BGM")
                throw new InvalidOperationException("Audio settings page differs.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["GRAPH1CS"]);
            if (!DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Settings Graphics entry did not open.");
            var graphicsItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var graphicsText = graphicsItems.GetValue(1)!.GetType().GetField("text", Instance)!;
            if (graphicsItems.Length != 6 ||
                !((string?)graphicsText.GetValue(graphicsItems.GetValue(1)))!.StartsWith("RES0LUTI0N:", StringComparison.Ordinal) ||
                (string?)graphicsText.GetValue(graphicsItems.GetValue(4)) != "ASSETS")
                throw new InvalidOperationException("Graphics category rows differ.");
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
        if (current == 5 && !assetOpened)
        {
            if (!displayCaptured || !DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Graphics settings page was not rendered.");
            var graphicsItems = (Array)baseType.GetField("item", Instance)!.GetValue(level)!;
            var resolutionItem = graphicsItems.GetValue(1)!;
            var resolutionText = resolutionItem.GetType().GetField("text", Instance)!;
            var click = level.GetType().GetMethod("Click", Instance)!;
            var desktopMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            var presets = DesktopDisplay.AvailablePresets(desktopMode.Width, desktopMode.Height);
            var start = Array.FindIndex(presets, preset => preset == DesktopDisplay.RenderResolution);
            for (var i = 1; i <= presets.Length + 1; i++)
            {
                click.Invoke(level, [(string)resolutionText.GetValue(resolutionItem)!]);
                var choice = (start + i) % (presets.Length + 1);
                var expected = choice == presets.Length
                    ? DesktopDisplay.ResolveResolution(true, 600, 360, desktopMode.Width, desktopMode.Height)
                    : presets[choice];
                var actual = game.GraphicsDevice.PresentationParameters;
                var expectedLabel = choice == presets.Length ? "RES0LUTI0N: AUTO" :
                    $"RES0LUTI0N: {expected.Width} X {expected.Height}";
                if (!DesktopDisplay.IsDisplayPage ||
                    (string?)resolutionText.GetValue(resolutionItem) != expectedLabel ||
                    !ReferenceEquals(baseType.GetField("item", Instance)!.GetValue(level), graphicsItems) ||
                    DesktopDisplay.RenderResolution != expected ||
                    actual.BackBufferWidth != expected.Width || actual.BackBufferHeight != expected.Height)
                    throw new InvalidOperationException($"Settings preset cycle expected {expected.Width}x{expected.Height}, got {actual.BackBufferWidth}x{actual.BackBufferHeight}.");
            }
            if (presets.Length > 1)
            {
                if (!DesktopDisplay.ApplyForProbe("Borderless", presets[0].Width, presets[0].Height))
                    throw new InvalidOperationException("Borderless probe setup failed.");
                click.Invoke(level, [(string)resolutionText.GetValue(resolutionItem)!]);
                var actual = game.GraphicsDevice.PresentationParameters;
                if (DesktopDisplay.RenderResolution != presets[1] ||
                    actual.BackBufferWidth != desktopMode.Width || actual.BackBufferHeight != desktopMode.Height)
                    throw new InvalidOperationException("Borderless preset did not change the render resolution within the desktop backbuffer.");
            }
            if (!DesktopDisplay.ApplyForProbe("Windowed", 600, 360))
                throw new InvalidOperationException("Display size did not restore.");
            var textType = game.GetType().Assembly.GetType("IMAK3Z0MB1EGAEM.Text", true)!;
            var textWidth = textType.GetMethod("GetStringWidth", Static, [typeof(string)])!;
            var menuScale = (float)game.GetType().GetField("MENUSCALE", Static)!.GetValue(null)!;
            var position = (Vector2)resolutionItem.GetType().GetField("loc", Instance)!.GetValue(resolutionItem)!;
            var layer = (float)resolutionItem.GetType().GetField("layer", Instance)!.GetValue(resolutionItem)!;
            if (MathF.Abs(position.X - (DesktopDisplay.SceneSize.X / (2f * menuScale) + 40f)) > 0.01f)
                throw new InvalidOperationException("Graphics Resolution row lost its staggered position.");
            var labels = new[] { "RES0LUTI0N: AUTO" }.Concat(
                DesktopDisplay.AvailablePresets(3840, 2160).Select(preset =>
                    $"RES0LUTI0N: {preset.Width} X {preset.Height}"));
            foreach (var label in labels)
            {
                var halfWidth = (float)textWidth.Invoke(null, [label])! * layer * menuScale / 2f;
                var center = position.X * menuScale;
                if (center - halfWidth < 0 || center + halfWidth > DesktopDisplay.SceneSize.X)
                    throw new InvalidOperationException($"Graphics Resolution label does not fit at 600x360: {label} (center={center}, halfWidth={halfWidth}, scale={menuScale}).");
            }
            if (!DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Resolution selection left Graphics.");
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
            if (!DesktopDisplay.IsDisplayPage || DesktopDisplay.IsAssetPage)
                throw new InvalidOperationException("Assets Back did not return to Graphics.");
            level.GetType().GetMethod("Click", Instance)!.Invoke(level, ["BACK"]);
            if (DesktopDisplay.IsDisplayPage)
                throw new InvalidOperationException("Graphics Back did not return to Settings.");
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
