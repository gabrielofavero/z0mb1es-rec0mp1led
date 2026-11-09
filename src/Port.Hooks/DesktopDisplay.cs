using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Port.Bridge;

namespace Port.Hooks;

/// <summary>Expands the phone scene to the display aspect ratio and adds Display settings.</summary>
public static class DesktopDisplay
{
    public const int LogicalWidth = 600;
    public const int LogicalHeight = 360;
    private static readonly (int Width, int Height)[] Presets =
    [
        (600, 360), (1280, 720), (1920, 1080), (2560, 1080),
        (2560, 1440), (3440, 1440), (3840, 1600), (3840, 2160)
    ];
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Game? game;
    private static GraphicsDeviceManager? graphics;
    private static RenderTarget2D? scene;
    private static SpriteBatch? batch;
    private static Texture2D? overlayPixel;
    private static MethodInfo? drawArrowText;
    private static object? arrowCenter;
    private static FieldInfo? viewportField;
    private static FieldInfo? gameFontField;
    private static FieldInfo? zombieScreenField;
    private static FieldInfo? vikingScreenField;
    private static (int Width, int Height) sceneSize = (LogicalWidth, LogicalHeight);
    private static (int Width, int Height) desktopSize;
    private static DisplayPreferences preferences = new("Fullscreen", 600, 360);
    private static object? settingsMenu;
    private static FieldInfo? itemsField;
    private static FieldInfo? titleField;
    private static FieldInfo? infosField;
    private static FieldInfo? itemTextField;
    private static FieldInfo? itemFlagField;
    private static FieldInfo? itemLocationField;
    private static FieldInfo? itemTiltField;
    private static FieldInfo? menuScaleField;
    private static IDictionary? menuLevels;
    private static float menuOffsetApplied;
    private static Array? originalItems;
    private static Array? settingsItems;
    private static Array? audioItems;
    private static Array? touchItems;
    private static Array? touchpadItems;
    private static Array? gameplayItems;
    private static Array? displayItems;
    private static Array? assetItems;
    private static object? previousInfos;
    private static bool displayPage;
    private static bool touchPage;
    private static bool touchpadPage;
    private static bool gameplayPage;
    private static bool audioPage;
    private static bool assetPage;
    private static int editing = -1;
    private static bool captureInput;
    private static KeyboardState previousKeyboard;
    private static GamePadState previousPad;
    private static string nameText = PlayerIdentity.DefaultName;

    public static bool CapturesInput => captureInput;
    public static Vector2 SceneSize => new(sceneSize.Width, sceneSize.Height);
    // Endless darkens unrevealed rooms after drawing the map. Its original
    // right edge was fixed at 800, which leaves a gap on wider desktop scenes.
    public static float EndlessMaskRightEdge() => sceneSize.Width;
    public static bool IsDisplayPage => displayPage;
    public static (int Width, int Height) RenderResolution => ActiveResolution;
    public static bool IsTouchPage => touchPage;
    public static bool IsTouchpadPage => touchpadPage;
    public static bool IsGameplayPage => gameplayPage;
    public static bool IsStaticPlayerNameLabel(object item) =>
        gameplayPage && gameplayItems is not null && ReferenceEquals(gameplayItems.GetValue(0), item);
    public static bool IsAssetPage => assetPage;
    internal static IntPtr GameWindowHandle => game?.Window.Handle ?? IntPtr.Zero;

    public static void Configure(Game runningGame)
    {
        game = runningGame;
        graphics = (GraphicsDeviceManager?)runningGame.Services.GetService(typeof(IGraphicsDeviceManager))
            ?? throw new InvalidOperationException("Game has no graphics manager.");
        scene?.Dispose();
        scene = null;
        batch?.Dispose();
        batch = null;
        overlayPixel?.Dispose();
        overlayPixel = null;
        var assembly = runningGame.GetType().Assembly;
        const BindingFlags staticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var textType = assembly.GetType("IMAK3Z0MB1EGAEM.Text", true)!;
        drawArrowText = textType.GetMethods(staticFields).Single(method => method.Name == "DrawString" &&
            method.GetParameters().Length == 6 &&
            method.GetParameters()[1].ParameterType == typeof(string) &&
            method.GetParameters()[5].ParameterType.IsEnum);
        arrowCenter = Enum.Parse(drawArrowText.GetParameters()[5].ParameterType, "Center");
        viewportField = runningGame.GetType().GetField("VIEWPORT", staticFields)
            ?? throw new MissingFieldException("Game1.VIEWPORT");
        gameFontField = runningGame.GetType().GetField("arial", staticFields)
            ?? throw new MissingFieldException("Game1.arial");
        menuScaleField = runningGame.GetType().GetField("MENUSCALE", staticFields)
            ?? throw new MissingFieldException("Game1.MENUSCALE");
        zombieScreenField = assembly.GetType("IMAK3Z0MB1EGAEM.ScrollMan", true)!
            .GetField("screenSize", staticFields) ?? throw new MissingFieldException("ScrollMan.screenSize");
        vikingScreenField = assembly.GetType("Viking_x86.VScroll", true)!
            .GetField("screenSize", staticFields) ?? throw new MissingFieldException("VScroll.screenSize");
        sceneSize = (LogicalWidth, LogicalHeight);
        desktopSize = default;
        settingsMenu = null;
        itemsField = titleField = infosField = itemTextField = null;
        itemFlagField = itemLocationField = itemTiltField = null;
        menuLevels = null;
        menuOffsetApplied = 0;
        originalItems = settingsItems = audioItems = touchItems = touchpadItems = gameplayItems = displayItems = assetItems = null;
        previousInfos = null;
        displayPage = touchPage = touchpadPage = gameplayPage = audioPage = assetPage = captureInput = false;
        editing = -1;
        preferences = Load();
        nameText = PlayerIdentity.Name;
    }

    public static void ApplyStartup(bool windowed)
    {
        var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        desktopSize = (display.Width, display.Height);
        Apply(windowed ? preferences with { Mode = "Windowed" } : preferences, save: false);
    }

    public static (int Width, int Height)[] AvailablePresets(int displayWidth, int displayHeight) =>
        Presets.Where(preset => preset.Width <= displayWidth && preset.Height <= displayHeight).ToArray();

    public static (int Width, int Height) ResolveResolution(bool auto, int width, int height,
        int displayWidth, int displayHeight) => auto
        ? (Math.Max(LogicalWidth, displayWidth), Math.Max(LogicalHeight, displayHeight))
        : (width, height);

    public static StringBuilder[] BuildVersionInfo()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly?.GetName().Version?.ToString(3)
            ?? "unknown";
        return [new StringBuilder($"Version {version}")];
    }

    public static void AttachSettingsMenu()
    {
        if (game is null) return;
        var assembly = game.GetType().Assembly;
        var menu = assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!;
        if (menu.GetField("menuLevel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(null) is not IDictionary levels || levels.Count != 17 ||
            levels[5] is not { } settings) return;
        if (ReferenceEquals(settingsMenu, settings)) return;
        var baseType = settings.GetType().BaseType!;
        itemsField = baseType.GetField("item", Fields)!;
        titleField = baseType.GetField("title", Fields)!;
        infosField = baseType.GetField("infos", Fields)!;
        if (itemsField.GetValue(settings) is not Array oldItems || oldItems.Length != 5)
            throw new InvalidOperationException("Original Settings menu item layout differs.");
        originalItems = oldItems;
        var itemType = assembly.GetType("ZombiesWP7.menu.MenuItem", true)!;
        itemTextField = itemType.GetField("text", Fields)!;
        itemFlagField = itemType.GetField("flag", Fields)!;
        itemLocationField = itemType.GetField("loc", Fields)!;
        itemTiltField = itemType.GetField("tiltLoc", Fields)!;
        var layerField = itemType.GetField("layer", Fields)!;
        layerField.SetValue(oldItems.GetValue(0), 4.5f);
        layerField.SetValue(oldItems.GetValue(1), 3.5f);
        layerField.SetValue(oldItems.GetValue(2), 3.5f);
        menuLevels = levels;
        menuOffsetApplied = 0;
        CenterMenuItems();
        CenterGameMenus();
        var constructor = itemType.GetConstructor([typeof(string), typeof(Vector2), typeof(float)])
            ?? throw new MissingMethodException("MenuItem(string, Vector2, float)");
        object Item(string text, float y, float xOffset = 0, float scale = 0.9f) =>
            constructor.Invoke([text, new Vector2(CenteredMenuX + xOffset, y), scale]);
        settingsItems = Array.CreateInstance(itemType, 8);
        Array.Copy(oldItems, settingsItems, 5);
        settingsItems.SetValue(Item("GRAPH1CS", 150, -85), 5);
        settingsItems.SetValue(Item("AUD10", 200, 115), 6);
        settingsItems.SetValue(Item("GAMEPLAY", 250, 15), 7);
        gameplayItems = Array.CreateInstance(itemType, 4);
        gameplayItems.SetValue(Item("PLAYER NAME", 105, -45, 0.7f), 0);
        gameplayItems.SetValue(Item("  " + nameText, 145, 40, 0.7f), 1);
        gameplayItems.SetValue(Item("1NPUT", 195, -30), 2);
        gameplayItems.SetValue(Item("BACK", 245, 25), 3);
        audioItems = Array.CreateInstance(itemType, 6);
        Array.Copy(oldItems, audioItems, 5);
        audioItems.SetValue(Item("BACK", 250), 5);
        touchItems = Array.CreateInstance(itemType, 3);
        touchItems.SetValue(oldItems.GetValue(0), 0);
        touchItems.SetValue(Item("T0UCHPADS", 180, -20), 1);
        touchItems.SetValue(Item("BACK", 240), 2);
        touchpadItems = Array.CreateInstance(itemType, 4);
        touchpadItems.SetValue(oldItems.GetValue(1), 0);
        touchpadItems.SetValue(oldItems.GetValue(2), 1);
        touchpadItems.SetValue(Item("TOUCHPAD SIZE: ORIGINAL", 190, -65, 0.7f), 2);
        touchpadItems.SetValue(Item("BACK", 235), 3);
        displayItems = Array.CreateInstance(itemType, 6);
        var displayLabels = new[] { "M0DE", "RES0LUTI0N", "VSYNC", "R3DUCE FLASH1NG", "ASSETS", "BACK" };
        var displayOffsets = new[] { -60f, 40f, -45f, 50f, -55f, 10f };
        for (var i = 0; i < displayLabels.Length; i++)
            displayItems.SetValue(Item(displayLabels[i], 95 + i * 34, displayOffsets[i], 0.7f), i);
        assetItems = Array.CreateInstance(itemType, 4);
        var assetOffsets = new[] { -35f, 35f, -25f, 0f };
        for (var i = 0; i < 4; i++)
            assetItems.SetValue(Item("BACK", 95 + i * 50, assetOffsets[i], 0.7f), i);
        settingsMenu = settings;
        previousInfos = null;
        editing = -1;
        captureInput = false;
        ShowSettingsPage();
        RefreshLabels();
    }

    private static void CenterGameMenus()
    {
        foreach (var key in new[] { 2, 3 })
        {
            if (menuLevels![key] is not { } level || itemsField!.GetValue(level) is not Array items ||
                items.Length != 3)
                throw new InvalidOperationException($"Original menu {key} layout differs.");
            var positions = items.Cast<object>().Select(item =>
                (Vector2)itemLocationField!.GetValue(item)!).ToArray();
            var minX = positions.Min(position => position.X);
            var maxX = positions.Max(position => position.X);
            var shift = new Vector2(CenteredMenuX - (minX + maxX) / 2f, 0);
            foreach (var item in items)
            {
                itemLocationField!.SetValue(item, (Vector2)itemLocationField.GetValue(item)! + shift);
                itemTiltField!.SetValue(item, (Vector2)itemTiltField.GetValue(item)! + shift);
            }
        }
    }

    private static void PlaceOriginal(int index, bool visible, Vector2 position)
    {
        var item = originalItems!.GetValue(index)!;
        var location = visible ? position + new Vector2(TextMenuOffset, 0) : new Vector2(-1000, -1000);
        itemLocationField!.SetValue(item, location);
        itemTiltField!.SetValue(item, location);
    }

    private static void ShowSettingsPage()
    {
        touchPage = touchpadPage = gameplayPage = displayPage = audioPage = assetPage = false;
        for (var i = 0; i < 3; i++) PlaceOriginal(i, false, Vector2.Zero);
        PlaceOriginal(3, false, Vector2.Zero);
        PlaceOriginal(4, false, Vector2.Zero);
        itemsField!.SetValue(settingsMenu, settingsItems);
        titleField!.SetValue(settingsMenu, "s3tt1ngs");
    }

    private static void ShowTouchPage()
    {
        touchpadPage = gameplayPage = displayPage = audioPage = assetPage = false;
        touchPage = true;
        PlaceOriginal(0, true, new Vector2(LogicalWidth / (2f * MenuScale) - 27.5f, 120));
        PlaceOriginal(1, false, Vector2.Zero);
        PlaceOriginal(2, false, Vector2.Zero);
        PlaceOriginal(3, false, Vector2.Zero);
        PlaceOriginal(4, false, Vector2.Zero);
        itemsField!.SetValue(settingsMenu, touchItems);
        titleField!.SetValue(settingsMenu, "1nput");
    }

    private static void ShowTouchpadPage()
    {
        touchPage = false;
        touchpadPage = true;
        PlaceOriginal(0, false, Vector2.Zero);
        PlaceOriginal(1, true, new Vector2(LogicalWidth / (2f * MenuScale) + 27.5f, 100));
        PlaceOriginal(2, true, new Vector2(LogicalWidth / (2f * MenuScale) + 7.5f, 145));
        itemsField!.SetValue(settingsMenu, touchpadItems);
        titleField!.SetValue(settingsMenu, "t0uchpads");
        RefreshLabels();
    }

    private static void ShowGameplayPage()
    {
        touchPage = touchpadPage = displayPage = audioPage = assetPage = false;
        gameplayPage = true;
        itemsField!.SetValue(settingsMenu, gameplayItems);
        titleField!.SetValue(settingsMenu, "gameplay");
        RefreshLabels();
    }

    private static void ShowAudioPage()
    {
        displayPage = touchPage = touchpadPage = gameplayPage = assetPage = false;
        audioPage = true;
        for (var i = 0; i < 3; i++) PlaceOriginal(i, false, Vector2.Zero);
        PlaceOriginal(3, true, new Vector2(LogicalWidth / (2f * MenuScale) - 72.5f, 135));
        PlaceOriginal(4, true, new Vector2(LogicalWidth / (2f * MenuScale) + 72.5f, 190));
        itemsField!.SetValue(settingsMenu, audioItems);
        titleField!.SetValue(settingsMenu, "aud10");
    }

    private static void ShowDisplayPage()
    {
        touchPage = touchpadPage = gameplayPage = audioPage = assetPage = false;
        displayPage = true;
        titleField!.SetValue(settingsMenu, "graph1cs");
        itemsField!.SetValue(settingsMenu, displayItems);
        RefreshLabels();
    }

    private static void ShowAssetPage()
    {
        AssetChoices.Refresh();
        displayPage = touchPage = touchpadPage = gameplayPage = audioPage = false;
        assetPage = true;
        titleField!.SetValue(settingsMenu, "assets");
        itemsField!.SetValue(settingsMenu, assetItems);
        RefreshLabels();
    }

    public static bool HandleSettingsClick(object settings, string text)
    {
        if (!ReferenceEquals(settings, settingsMenu)) return false;
        if (!displayPage && !touchPage && !touchpadPage && !gameplayPage && !audioPage && !assetPage)
        {
            if (text is not ("GRAPH1CS" or "AUD10" or "GAMEPLAY")) return false;
            previousInfos = infosField!.GetValue(settings);
            infosField.SetValue(settings, null);
            if (text == "GAMEPLAY")
            {
                ShowGameplayPage();
                return true;
            }
            if (text == "AUD10") ShowAudioPage();
            else ShowDisplayPage();
            return true;
        }
        if (audioPage)
        {
            if (text != "BACK") return false;
            CloseDisplayPage();
            return true;
        }
        if (touchPage)
        {
            if (text == "T0UCHPADS") ShowTouchpadPage();
            else if (text == "BACK") ReturnToGameplayPage();
            else return false;
            return true;
        }
        if (touchpadPage)
        {
            if (text.StartsWith("TOUCHPAD SIZE:", StringComparison.Ordinal))
            {
                var sizes = new[] { "Original", "Small", "Smallest" };
                var index = Array.IndexOf(sizes, preferences.TouchpadSize);
                preferences = preferences with { TouchpadSize = sizes[(index + 1) % sizes.Length] };
                Save();
                RefreshLabels();
            }
            else if (text == "BACK") ReturnToTouchPage();
            else return false;
            return true;
        }
        if (gameplayPage)
        {
            if (text.StartsWith("  ", StringComparison.Ordinal)) StartEditingName();
            else if (text == "1NPUT") ShowTouchPage();
            else if (text == "BACK") CloseDisplayPage();
            else return false;
            return true;
        }
        if (assetPage)
        {
            if (text == "BACK") ReturnToGraphicsPage();
            else
            {
                var mode = text.StartsWith("I MAED:", StringComparison.Ordinal) ? "normal" :
                    text.StartsWith("TIME VIKING:", StringComparison.Ordinal) ? "viking" :
                    text.StartsWith("ENDLESS:", StringComparison.Ordinal) ? "endless" : null;
                if (mode is null) return false;
                var result = AssetChoices.Toggle(mode);
                if (result.Length != 0) SetInfo(result);
            }
            RefreshLabels();
            return true;
        }
        if (text.StartsWith("M0DE:", StringComparison.Ordinal))
        {
            var modes = new[] { "Windowed", "Borderless", "Fullscreen" };
            var index = Array.IndexOf(modes, preferences.Mode);
            Apply(preferences with { Mode = modes[(index + 1) % modes.Length] }, save: true);
        }
        else if (text.StartsWith("RES0LUTI0N:", StringComparison.Ordinal))
        {
            var available = AvailablePresets(desktopSize.Width, desktopSize.Height);
            var index = preferences.Auto ? -1 : Array.FindIndex(available, preset =>
                preset.Width == preferences.Width && preset.Height == preferences.Height);
            if (index == available.Length - 1)
                Apply(preferences with { Auto = true }, save: true);
            else if (available.Length > 0)
            {
                var preset = available[index + 1];
                Apply(preferences with { Auto = false, Width = preset.Width, Height = preset.Height }, save: true);
            }
        }
        else if (text.StartsWith("VSYNC:", StringComparison.Ordinal))
            Apply(preferences with { VSync = !preferences.VSync }, save: true);
        else if (text == "ASSETS") ShowAssetPage();
        else if (text.StartsWith("R3DUCE FLASH1NG:", StringComparison.Ordinal))
        {
            preferences = preferences with { ReduceFlashing = !preferences.ReduceFlashing };
            Save();
        }
        else if (text == "BACK") CloseDisplayPage();
        else return false;
        RefreshLabels();
        return true;
    }

    public static bool HandleSettingsCancel(object settings)
    {
        if (!ReferenceEquals(settings, settingsMenu) || (!displayPage && !touchPage && !touchpadPage && !gameplayPage && !audioPage && !assetPage)) return false;
        if (assetPage)
        {
            ReturnToGraphicsPage();
            return true;
        }
        if (touchPage)
        {
            ReturnToGameplayPage();
            return true;
        }
        if (touchpadPage)
        {
            ReturnToTouchPage();
            return true;
        }
        CloseDisplayPage();
        return true;
    }

    private static void ReturnToGameplayPage()
    {
        captureInput = true;
        infosField!.SetValue(settingsMenu, null);
        ShowGameplayPage();
    }

    private static void ReturnToTouchPage()
    {
        captureInput = true;
        infosField!.SetValue(settingsMenu, null);
        ShowTouchPage();
    }

    private static void ReturnToGraphicsPage()
    {
        editing = -1;
        captureInput = true;
        infosField!.SetValue(settingsMenu, null);
        ShowDisplayPage();
    }

    private static void CloseDisplayPage()
    {
        StopEditingName();
        editing = -1;
        captureInput = true;
        ShowSettingsPage();
        infosField!.SetValue(settingsMenu, previousInfos);
    }

    private static void StartEditingName()
    {
        editing = 1;
        captureInput = true;
        nameText = "";
        previousKeyboard = Keyboard.GetState();
        previousPad = GamePad.GetState(PlayerIndex.One);
        TextInputEXT.TextInput += OnNameTextInput;
        var bounds = game!.Window.ClientBounds;
        TextInputEXT.SetInputRectangle(new Rectangle(bounds.Width / 4, bounds.Height / 3,
            bounds.Width / 2, 36));
        TextInputEXT.StartTextInput();
        SetInfo("ENTER/A TO SAVE, ESC/B TO CANCEL. MAX 16 CHARACTERS.");
        RefreshLabels();
    }

    private static void StopEditingName()
    {
        if (editing != 1) return;
        TextInputEXT.TextInput -= OnNameTextInput;
        TextInputEXT.StopTextInput();
        nameText = PlayerIdentity.Name;
        editing = -1;
        if (settingsMenu is not null) infosField!.SetValue(settingsMenu, null);
        RefreshLabels();
    }

    private static void OnNameTextInput(char character)
    {
        if (editing != 1) return;
        if (character == '\b' && nameText.Length > 0) nameText = nameText[..^1];
        else if (nameText.Length < PlayerIdentity.MaxLength &&
            (char.IsAsciiLetterOrDigit(character) || character is ' ' or '-' or '_' or '\''))
        {
            if (character != ' ' || nameText.Length > 0) nameText += character;
        }
        RefreshLabels();
    }

    public static void Update()
    {
        if (!captureInput || game is null || !game.IsActive) return;
        var keyboard = Keyboard.GetState();
        var pad = GamePad.GetState(PlayerIndex.One);
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !previousKeyboard.IsKeyDown(key);
        bool PadPressed(Buttons button) => pad.IsButtonDown(button) && !previousPad.IsButtonDown(button);
        if (editing == 1)
        {
            if (Pressed(Keys.Escape) || PadPressed(Buttons.B)) StopEditingName();
            else if (Pressed(Keys.Enter) || PadPressed(Buttons.A))
            {
                var candidate = nameText.Trim();
                if (PlayerIdentity.IsValid(candidate))
                {
                    if (PlayerIdentity.TrySetName(candidate)) StopEditingName();
                    else SetInfo("COULD NOT SAVE PLAYER NAME.");
                }
                else SetInfo("TYPE A NAME (1-16 CHARACTERS).");
            }
            RefreshLabels();
        }
        else if (!keyboard.IsKeyDown(Keys.Enter) && !keyboard.IsKeyDown(Keys.Escape) &&
                 !pad.IsButtonDown(Buttons.A) && !pad.IsButtonDown(Buttons.B) &&
                 Mouse.GetState().LeftButton == ButtonState.Released)
            captureInput = false;
        previousKeyboard = keyboard;
        previousPad = pad;
    }

    private static void RefreshLabels()
    {
        if (displayItems is null || assetItems is null || gameplayItems is null || touchpadItems is null || itemTextField is null) return;
        itemTextField.SetValue(touchpadItems.GetValue(2),
            $"TOUCHPAD SIZE: {preferences.TouchpadSize.ToUpperInvariant()}");
        itemTextField.SetValue(gameplayItems.GetValue(1),
            $"  {nameText}{(editing == 1 ? "_" : "")}");
        var displayLabels = new[]
        {
            $"M0DE: {preferences.Mode.ToUpperInvariant()}",
            preferences.Auto ? "RES0LUTI0N: AUTO" : $"RES0LUTI0N: {preferences.Width} X {preferences.Height}",
            $"VSYNC: {(preferences.VSync ? "ON" : "OFF")}",
            $"R3DUCE FLASH1NG: {(preferences.ReduceFlashing ? "ON" : "OFF")}",
            "ASSETS", "BACK"
        };
        for (var i = 0; i < displayLabels.Length; i++)
            itemTextField.SetValue(displayItems.GetValue(i), displayLabels[i]);
        var assetLabels = new[]
        {
            $"I MAED: {AssetChoices.NormalLabel}",
            $"TIME VIKING: {AssetChoices.VikingLabel}",
            $"ENDLESS: {AssetChoices.EndlessLabel}", "BACK"
        };
        for (var i = 0; i < assetLabels.Length; i++)
            itemTextField.SetValue(assetItems.GetValue(i), assetLabels[i]);
    }

    private static void SetInfo(string text)
    {
        if (settingsMenu is not null)
            infosField!.SetValue(settingsMenu, new[] { new StringBuilder(text) });
    }

    public static bool ApplyForProbe(string mode, int width, int height)
    {
        Apply(preferences with { Mode = mode, Auto = false, Width = width, Height = height }, save: false);
        RefreshLabels();
        return preferences.Mode == mode && preferences.Width == width && preferences.Height == height;
    }

    public static void BeginFrame()
    {
        if (game is null) return;
        var device = game.GraphicsDevice;
        SyncSceneSize();
        var resolution = ActiveResolution;
        if (scene is null || scene.IsDisposed ||
            scene.Width != resolution.Width || scene.Height != resolution.Height)
        {
            scene?.Dispose();
            scene = new RenderTarget2D(device, resolution.Width, resolution.Height, false,
                SurfaceFormat.Color, DepthFormat.None);
        }
        device.SetRenderTarget(scene);
        device.Clear(Color.Black);
    }

    public static void EndFrame()
    {
        if (game is null || scene is null) return;
        var device = game.GraphicsDevice;
        device.SetRenderTarget(null);
        device.Clear(Color.Black);
        batch ??= new SpriteBatch(device);
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        var output = device.PresentationParameters;
        var placement = Presentation(output.BackBufferWidth, output.BackBufferHeight,
            (scene.Width, scene.Height));
        batch.Draw(scene, new Rectangle((int)MathF.Round(placement.Offset.X),
            (int)MathF.Round(placement.Offset.Y),
            (int)MathF.Round(scene.Width * placement.Scale),
            (int)MathF.Round(scene.Height * placement.Scale)), Color.White);
        batch.End();
        DrawInputOverlay(device);
        DrawAchievementToast(device);
    }

    private static void DrawAchievementToast(GraphicsDevice device)
    {
        var title = DesktopAchievements.CurrentToast();
        if (title is null || gameFontField?.GetValue(null) is not SpriteFont font || batch is null) return;
        if (overlayPixel is null)
        {
            overlayPixel = new Texture2D(device, 1, 1);
            overlayPixel.SetData([Color.White]);
        }
        var placement = Presentation(device.PresentationParameters.BackBufferWidth,
            device.PresentationParameters.BackBufferHeight, sceneSize);
        var transform = Matrix.CreateScale(placement.Scale, placement.Scale, 1f) *
            Matrix.CreateTranslation(placement.Offset.X, placement.Offset.Y, 0f);
        var x = (int)SceneSize.X - 320;
        const int y = 12;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, transform);
        batch.Draw(overlayPixel, new Rectangle(x, y, 308, 53), new Color(0, 0, 0, 225));
        batch.Draw(overlayPixel, new Rectangle(x, y, 3, 53), Color.Gold);
        batch.DrawString(font, "ACHIEVEMENT UNLOCKED", new Vector2(x + 11, y + 6), Color.Gold);
        var scale = MathF.Min(1f, 285f / MathF.Max(1f, font.MeasureString(title).X));
        batch.DrawString(font, title, new Vector2(x + 11, y + 30), Color.White,
            0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        batch.End();
    }

    private static void DrawInputOverlay(GraphicsDevice device)
    {
        if (game is null || !game.IsActive || batch is null) return;
        var mouse = Mouse.GetState();
        var pointer = MapPointer(mouse.X, mouse.Y, game.Window.ClientBounds);
        var backVisible = DesktopInput.BackButtonVisible();
        var cursorVisible = pointer is not null && DesktopInput.CursorVisible;
        if (!backVisible && !cursorVisible) return;
        var placement = Presentation(device.PresentationParameters.BackBufferWidth,
            device.PresentationParameters.BackBufferHeight, sceneSize);
        var transform = Matrix.CreateScale(placement.Scale, placement.Scale, 1f) *
            Matrix.CreateTranslation(placement.Offset.X, placement.Offset.Y, 0f);
        if (backVisible)
        {
            batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, transform);
            var frame = ReduceFlashing ? 0.125f :
                (float)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 0.6 % 0.5);
            for (var i = 0; i < 2; i++)
            {
                var phase = i * 0.25f + frame;
                if (phase > 0.5f) phase -= 0.5f;
                phase -= 0.25f;
                var brightness = (0.25f - MathF.Abs(phase)) * 4f;
                var position = SceneSize * new Vector2(0.05f - phase * 0.1f, 0.9f);
                drawArrowText!.Invoke(null, [batch, "<", position, 2f,
                    new Color(brightness, brightness, brightness, 1f), arrowCenter!]);
            }
            batch.End();
        }
        if (cursorVisible && pointer is Vector2 cursor)
        {
            if (overlayPixel is null)
            {
                overlayPixel = new Texture2D(device, 1, 1);
                overlayPixel.SetData([Color.White]);
            }
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, transform);
            var x = (int)MathF.Round(cursor.X);
            var y = (int)MathF.Round(cursor.Y);
            var color = new Color(255, 255, 255, 230);
            batch.Draw(overlayPixel, new Rectangle(x - 8, y, 17, 1), color);
            batch.Draw(overlayPixel, new Rectangle(x, y - 8, 1, 17), color);
            batch.End();
        }
    }

    // The game still works in phone coordinates. Transform its batches while the
    // full-resolution scene is bound, leaving text baking and other targets alone.
    private static Matrix SceneTransform(SpriteBatch sprite)
    {
        if (scene is null || scene.IsDisposed ||
            !sprite.GraphicsDevice.GetRenderTargets().Any(target => ReferenceEquals(target.RenderTarget, scene)))
            return Matrix.Identity;
        var placement = Presentation(scene.Width, scene.Height, sceneSize);
        return Matrix.CreateScale(placement.Scale, placement.Scale, 1f) *
            Matrix.CreateTranslation(placement.Offset.X, placement.Offset.Y, 0f);
    }

    public static void Begin(SpriteBatch sprite) =>
        sprite.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, SceneTransform(sprite));

    public static void Begin(SpriteBatch sprite, SpriteSortMode sort, BlendState blend) =>
        sprite.Begin(sort, blend, null, null, null, null, SceneTransform(sprite));

    public static void Begin(SpriteBatch sprite, SpriteSortMode sort, BlendState blend,
        SamplerState sampler, DepthStencilState depth, RasterizerState raster) =>
        sprite.Begin(sort, blend, sampler, depth, raster, null, SceneTransform(sprite));

    public static void Begin(SpriteBatch sprite, SpriteSortMode sort, BlendState blend,
        SamplerState sampler, DepthStencilState depth, RasterizerState raster, Effect effect) =>
        sprite.Begin(sort, blend, sampler, depth, raster, effect, SceneTransform(sprite));

    public static void Begin(SpriteBatch sprite, SpriteSortMode sort, BlendState blend,
        SamplerState sampler, DepthStencilState depth, RasterizerState raster, Effect effect, Matrix transform) =>
        sprite.Begin(sort, blend, sampler, depth, raster, effect, transform * SceneTransform(sprite));

    // Keep at least the phone's original 600x360 view. The other axis grows to
    // match the selected resolution; output bars are applied at presentation time.
    public static (int Width, int Height) CanvasSize(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var scale = Math.Min(width / (double)LogicalWidth, height / (double)LogicalHeight);
        return ((int)Math.Ceiling(width / scale), (int)Math.Ceiling(height / scale));
    }

    public static (Vector2 Offset, float Scale) Presentation(int width, int height,
        (int Width, int Height) canvas)
    {
        if (width <= 0 || height <= 0 || canvas.Width <= 0 || canvas.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var scale = Math.Min(width / (float)canvas.Width, height / (float)canvas.Height);
        return (new Vector2((width - canvas.Width * scale) / 2f,
            (height - canvas.Height * scale) / 2f), scale);
    }

    public static Vector2? MapPointer(int x, int y, Rectangle client)
    {
        return MapPointer(x, y, client, sceneSize);
    }

    public static Vector2? MapPointer(int x, int y, Rectangle client, (int Width, int Height) canvas)
    {
        if (client.Width <= 0 || client.Height <= 0 || x < 0 || y < 0 ||
            x >= client.Width || y >= client.Height) return null;
        var presentation = Presentation(client.Width, client.Height, canvas);
        var mapped = (new Vector2(x, y) - presentation.Offset) / presentation.Scale;
        return mapped.X >= 0 && mapped.Y >= 0 && mapped.X < canvas.Width && mapped.Y < canvas.Height
            ? mapped : null;
    }

    private static void SyncSceneSize()
    {
        if (game is null) return;
        var resolution = ActiveResolution;
        var next = CanvasSize(resolution.Width, resolution.Height);
        if (sceneSize == next) return;
        sceneSize = next;
        var size = SceneSize;
        viewportField!.SetValue(null, size);
        zombieScreenField!.SetValue(null, size);
        vikingScreenField!.SetValue(null, size);
        CenterMenuItems();
    }

    private static float MenuOffset => (sceneSize.Width - LogicalWidth) / 2f;
    private static float MenuScale => (float)menuScaleField!.GetValue(null)!;
    private static float TextMenuOffset => MenuOffset / MenuScale;
    private static float CenteredMenuX => sceneSize.Width / (2f * MenuScale);

    // Leaderboard items are recreated after the menu's initial centering pass.
    public static float CenterLeaderboardHudX(float originalX) => originalX + CenteredMenuX - 240f;

    public static float CenterLeaderboardRowX(float originalX) => originalX + sceneSize.Width / 2f - 280f;

    private static void CenterMenuItems()
    {
        if (menuLevels is null || itemsField is null || itemFlagField is null ||
            itemLocationField is null || itemTiltField is null)
            return;
        var delta = MenuOffset - menuOffsetApplied;
        if (delta == 0) return;
        var locationField = itemLocationField;
        var tiltField = itemTiltField;
        var flagField = itemFlagField;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Shift(Array? items)
        {
            if (items is null) return;
            foreach (var item in items)
            {
                if (item is null || !seen.Add(item)) continue;
                var location = (Vector2)locationField.GetValue(item)!;
                var tilt = (Vector2)tiltField.GetValue(item)!;
                if (location.X <= -900 && tilt.X <= -900) continue;
                var flag = (int)flagField.GetValue(item)!;
                var shift = flag is 0 or 1 ? delta / (float)menuScaleField!.GetValue(null)! : delta;
                locationField.SetValue(item, location + new Vector2(shift, 0));
                tiltField.SetValue(item, tilt + new Vector2(shift, 0));
            }
        }
        for (var key = 0; key < 17; key++)
            if (menuLevels.Contains(key) && menuLevels[key] is { } level)
                Shift(itemsField.GetValue(level) as Array);
        Shift(originalItems);
        Shift(settingsItems);
        Shift(audioItems);
        Shift(touchItems);
        Shift(touchpadItems);
        Shift(gameplayItems);
        Shift(displayItems);
        Shift(assetItems);
        menuOffsetApplied = MenuOffset;
    }

    private static bool Valid(int width, int height) =>
        width is >= 600 and <= 3840 && height is >= 360 and <= 2160;

    private static (int Width, int Height) ActiveResolution =>
        ResolveResolution(preferences.Auto, preferences.Width, preferences.Height,
            desktopSize.Width, desktopSize.Height);

    private static void Apply(DisplayPreferences requested, bool save)
    {
        if (game is null || graphics is null) return;
        if (!Valid(requested.Width, requested.Height) ||
            requested.Mode is not ("Windowed" or "Borderless" or "Fullscreen"))
            throw new ArgumentOutOfRangeException(nameof(requested));
        var old = preferences;
        var oldBorderless = game.Window.IsBorderlessEXT;
        var oldFullscreen = graphics.IsFullScreen;
        var oldVSync = graphics.SynchronizeWithVerticalRetrace;
        var oldWidth = graphics.PreferredBackBufferWidth;
        var oldHeight = graphics.PreferredBackBufferHeight;
        try
        {
            var mode = requested.Mode;
            game.Window.IsBorderlessEXT = mode == "Borderless";
            graphics.IsFullScreen = mode == "Fullscreen";
            graphics.SynchronizeWithVerticalRetrace = requested.VSync;
            var resolution = ResolveResolution(requested.Auto, requested.Width, requested.Height,
                desktopSize.Width, desktopSize.Height);
            graphics.PreferredBackBufferWidth = mode == "Windowed" ? resolution.Width : desktopSize.Width;
            graphics.PreferredBackBufferHeight = mode == "Windowed" ? resolution.Height : desktopSize.Height;
            graphics.ApplyChanges();
            preferences = requested;
            SyncSceneSize();
            if (save) Save();
            Console.WriteLine($"DISPLAY_APPLIED mode={mode} resolution={resolution.Width}x{resolution.Height} auto={requested.Auto} backbuffer={graphics.GraphicsDevice.PresentationParameters.BackBufferWidth}x{graphics.GraphicsDevice.PresentationParameters.BackBufferHeight}");
        }
        catch (Exception exception)
        {
            preferences = old;
            try
            {
                game.Window.IsBorderlessEXT = oldBorderless;
                graphics.IsFullScreen = oldFullscreen;
                graphics.SynchronizeWithVerticalRetrace = oldVSync;
                graphics.PreferredBackBufferWidth = oldWidth;
                graphics.PreferredBackBufferHeight = oldHeight;
                graphics.ApplyChanges();
                SyncSceneSize();
            }
            catch (Exception restoreError)
            {
                Console.Error.WriteLine($"DISPLAY_RESTORE_FAILED {restoreError.GetType().Name}: {restoreError.Message}");
            }
            SetInfo($"Display change failed: {exception.GetType().Name}");
            Console.Error.WriteLine($"DISPLAY_FAILED {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static DisplayPreferences Load()
    {
        try
        {
            var path = Path.Combine(DesktopStorage.SaveDirectory, "display.json");
            if (!File.Exists(path)) return preferences;
            var loaded = JsonSerializer.Deserialize<DisplayPreferences>(File.ReadAllText(path));
            return loaded is not null && Valid(loaded.Width, loaded.Height) &&
                loaded.Mode is "Windowed" or "Borderless" or "Fullscreen" &&
                loaded.TouchpadSize is "Original" or "Small" or "Smallest"
                ? loaded.Auto || Presets.Contains((loaded.Width, loaded.Height))
                    ? loaded : loaded with { Auto = true }
                : preferences;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"DISPLAY_LOAD_SKIPPED {exception.GetType().Name}");
            return preferences;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(DesktopStorage.SaveDirectory);
            var path = Path.Combine(DesktopStorage.SaveDirectory, "display.json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(preferences) + "\n");
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetInfo($"Could not save display settings: {exception.GetType().Name}");
        }
    }

    private sealed record DisplayPreferences(string Mode, int Width, int Height)
    {
        public bool Auto { get; init; } = true;
        public bool VSync { get; init; } = true;
        public bool ReduceFlashing { get; init; } = false;
        public string TouchpadSize { get; init; } = "Original";
    }

    public static float ScaleTouchpad(float original) => original * (preferences.TouchpadSize switch
    {
        "Small" => 0.75f,
        "Smallest" => 0.5f,
        _ => 1f
    });

    public static bool ReduceFlashing => preferences.ReduceFlashing;

    public static int SteadyLightRow(int color) => !ReduceFlashing ? color : color switch
    {
        3 or 8 => 4, // white strobes
        5 => 1,      // purple strobe to steady blue
        6 => 2,      // yellow strobe to steady green
        7 => 0,      // red strobe to steady red
        _ => color
    };
}
