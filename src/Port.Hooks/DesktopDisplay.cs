using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

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
    private static FieldInfo? viewportField;
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
    private static Array? displayItems;
    private static Array? customItems;
    private static object? previousInfos;
    private static bool displayPage;
    private static bool touchPage;
    private static bool audioPage;
    private static bool customPage;
    private static int editing = -1;
    private static bool captureInput;
    private static KeyboardState previousKeyboard;
    private static GamePadState previousPad;
    private static string widthText = "600";
    private static string heightText = "360";

    public static bool CapturesInput => captureInput;
    public static Vector2 SceneSize => new(sceneSize.Width, sceneSize.Height);
    public static bool IsDisplayPage => displayPage;
    public static bool IsTouchPage => touchPage;
    public static bool IsCustomPage => customPage;
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
        var assembly = runningGame.GetType().Assembly;
        const BindingFlags staticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        viewportField = runningGame.GetType().GetField("VIEWPORT", staticFields)
            ?? throw new MissingFieldException("Game1.VIEWPORT");
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
        originalItems = settingsItems = audioItems = touchItems = displayItems = customItems = null;
        previousInfos = null;
        displayPage = touchPage = audioPage = customPage = captureInput = false;
        editing = -1;
        preferences = Load();
        widthText = preferences.Width.ToString();
        heightText = preferences.Height.ToString();
    }

    public static void ApplyStartup(bool windowed)
    {
        var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        desktopSize = (display.Width, display.Height);
        Apply(windowed ? preferences with { Mode = "Windowed" } : preferences, save: false);
    }

    public static (int Width, int Height)[] AvailablePresets(int displayWidth, int displayHeight) =>
        Presets.Where(preset => preset.Width <= displayWidth && preset.Height <= displayHeight).ToArray();

    public static void AttachSettingsMenu()
    {
        if (settingsMenu is not null || game is null) return;
        var assembly = game.GetType().Assembly;
        var menu = assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!;
        if (menu.GetField("menuLevel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(null) is not IDictionary levels || levels.Count != 17 ||
            levels[5] is not { } settings) return;
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
        menuLevels = levels;
        CenterMenuItems();
        var constructor = itemType.GetConstructor([typeof(string), typeof(Vector2), typeof(float)])
            ?? throw new MissingMethodException("MenuItem(string, Vector2, float)");
        object Item(string text, float y) => constructor.Invoke([text, new Vector2(250 + TextMenuOffset, y), 0.9f]);
        settingsItems = Array.CreateInstance(itemType, 8);
        Array.Copy(oldItems, settingsItems, 5);
        settingsItems.SetValue(Item("GRAPH1CS", 125), 5);
        settingsItems.SetValue(Item("AUD10", 175), 6);
        settingsItems.SetValue(Item("1NPUT", 225), 7);
        audioItems = Array.CreateInstance(itemType, 6);
        Array.Copy(oldItems, audioItems, 5);
        audioItems.SetValue(Item("BACK", 250), 5);
        touchItems = Array.CreateInstance(itemType, 6);
        Array.Copy(oldItems, touchItems, 5);
        touchItems.SetValue(Item("BACK", 250), 5);
        displayItems = Array.CreateInstance(itemType, 6);
        var displayLabels = new[] { "M0DE", "PRES3T", "VSYNC", "CUST0M RES0LUTI0N", "R3DUCE FLASH1NG", "BACK" };
        for (var i = 0; i < displayLabels.Length; i++)
            displayItems.SetValue(constructor.Invoke([displayLabels[i],
                new Vector2(250 + TextMenuOffset, 95 + i * 34), 0.7f]), i);
        customItems = Array.CreateInstance(itemType, 4);
        var customLabels = new[] { "W1DTH", "HE1GHT", "APPLY CUST0M", "BACK" };
        for (var i = 0; i < customLabels.Length; i++)
            customItems.SetValue(Item(customLabels[i], 100 + i * 50), i);
        settingsMenu = settings;
        ShowSettingsPage();
        RefreshLabels();
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
        touchPage = displayPage = audioPage = customPage = false;
        for (var i = 0; i < 3; i++) PlaceOriginal(i, false, Vector2.Zero);
        PlaceOriginal(3, false, Vector2.Zero);
        PlaceOriginal(4, false, Vector2.Zero);
        itemsField!.SetValue(settingsMenu, settingsItems);
        titleField!.SetValue(settingsMenu, "s3tt1ngs");
    }

    private static void ShowTouchPage()
    {
        displayPage = audioPage = customPage = false;
        touchPage = true;
        PlaceOriginal(0, true, new Vector2(250, 100));
        PlaceOriginal(1, true, new Vector2(250, 150));
        PlaceOriginal(2, true, new Vector2(250, 200));
        PlaceOriginal(3, false, Vector2.Zero);
        PlaceOriginal(4, false, Vector2.Zero);
        itemsField!.SetValue(settingsMenu, touchItems);
        titleField!.SetValue(settingsMenu, "1nput");
    }

    private static void ShowAudioPage()
    {
        displayPage = touchPage = customPage = false;
        audioPage = true;
        for (var i = 0; i < 3; i++) PlaceOriginal(i, false, Vector2.Zero);
        PlaceOriginal(3, true, new Vector2(250, 135));
        PlaceOriginal(4, true, new Vector2(250, 190));
        itemsField!.SetValue(settingsMenu, audioItems);
        titleField!.SetValue(settingsMenu, "aud10");
    }

    private static void ShowDisplayPage()
    {
        touchPage = audioPage = customPage = false;
        displayPage = true;
        titleField!.SetValue(settingsMenu, "d1splay");
        itemsField!.SetValue(settingsMenu, displayItems);
        RefreshLabels();
    }

    private static void ShowCustomPage()
    {
        displayPage = audioPage = false;
        customPage = true;
        widthText = preferences.Width.ToString();
        heightText = preferences.Height.ToString();
        titleField!.SetValue(settingsMenu, "cust0m");
        itemsField!.SetValue(settingsMenu, customItems);
        RefreshLabels();
    }

    public static bool HandleSettingsClick(object settings, string text)
    {
        if (!ReferenceEquals(settings, settingsMenu)) return false;
        if (!displayPage && !touchPage && !audioPage && !customPage)
        {
            if (text is not ("GRAPH1CS" or "AUD10" or "1NPUT")) return false;
            previousInfos = infosField!.GetValue(settings);
            infosField.SetValue(settings, null);
            if (text == "1NPUT")
            {
                ShowTouchPage();
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
            if (text != "BACK") return false;
            CloseDisplayPage();
            return true;
        }
        if (customPage)
        {
            if (text.StartsWith("W1DTH:", StringComparison.Ordinal)) StartEditing(2);
            else if (text.StartsWith("HE1GHT:", StringComparison.Ordinal)) StartEditing(3);
            else if (text == "APPLY CUST0M")
            {
                if (int.TryParse(widthText, out var width) && int.TryParse(heightText, out var height) &&
                    Valid(width, height)) Apply(preferences with { Width = width, Height = height }, save: true);
                else SetInfo("Resolution must be 600-3840 x 360-2160.");
            }
            else if (text == "BACK") ReturnToDisplayPage();
            else return false;
            RefreshLabels();
            return true;
        }
        if (text.StartsWith("M0DE:", StringComparison.Ordinal))
        {
            var modes = new[] { "Windowed", "Borderless", "Fullscreen" };
            var index = Array.IndexOf(modes, preferences.Mode);
            Apply(preferences with { Mode = modes[(index + 1) % modes.Length] }, save: true);
        }
        else if (text.StartsWith("PRES3T:", StringComparison.Ordinal))
        {
            var available = AvailablePresets(desktopSize.Width, desktopSize.Height);
            var index = Array.FindIndex(available, preset =>
                preset.Width == preferences.Width && preset.Height == preferences.Height);
            if (available.Length > 0)
            {
                var preset = available[index < 0 ? 0 : (index + 1) % available.Length];
                Apply(preferences with { Width = preset.Width, Height = preset.Height }, save: true);
                widthText = preferences.Width.ToString();
                heightText = preferences.Height.ToString();
            }
        }
        else if (text.StartsWith("VSYNC:", StringComparison.Ordinal))
            Apply(preferences with { VSync = !preferences.VSync }, save: true);
        else if (text == "CUST0M RES0LUTI0N") ShowCustomPage();
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
        if (!ReferenceEquals(settings, settingsMenu) || (!displayPage && !touchPage && !audioPage && !customPage)) return false;
        if (customPage)
        {
            ReturnToDisplayPage();
            return true;
        }
        CloseDisplayPage();
        return true;
    }

    private static void ReturnToDisplayPage()
    {
        editing = -1;
        captureInput = true;
        infosField!.SetValue(settingsMenu, null);
        ShowDisplayPage();
    }

    private static void CloseDisplayPage()
    {
        editing = -1;
        captureInput = true;
        ShowSettingsPage();
        infosField!.SetValue(settingsMenu, previousInfos);
    }

    private static void StartEditing(int field)
    {
        editing = field;
        captureInput = true;
        if (field == 2) widthText = "";
        else heightText = "";
        previousKeyboard = Keyboard.GetState();
        previousPad = GamePad.GetState(PlayerIndex.One);
        RefreshLabels();
    }

    public static void Update()
    {
        if (!captureInput || game is null || !game.IsActive) return;
        var keyboard = Keyboard.GetState();
        var pad = GamePad.GetState(PlayerIndex.One);
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !previousKeyboard.IsKeyDown(key);
        bool PadPressed(Buttons button) => pad.IsButtonDown(button) && !previousPad.IsButtonDown(button);
        if (editing >= 0)
        {
            if (Pressed(Keys.Escape) || PadPressed(Buttons.B))
            {
                widthText = preferences.Width.ToString();
                heightText = preferences.Height.ToString();
                editing = -1;
            }
            else if (Pressed(Keys.Enter) || PadPressed(Buttons.A)) editing = -1;
            else
            {
                for (var digit = 0; digit < 10; digit++)
                    if (Pressed((Keys)((int)Keys.D0 + digit)) ||
                        Pressed((Keys)((int)Keys.NumPad0 + digit))) AppendDigit((char)('0' + digit));
                if (Pressed(Keys.Back))
                {
                    if (editing == 2 && widthText.Length > 0) widthText = widthText[..^1];
                    if (editing == 3 && heightText.Length > 0) heightText = heightText[..^1];
                }
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

    private static void AppendDigit(char digit)
    {
        if (editing == 2 && widthText.Length < 4) widthText += digit;
        if (editing == 3 && heightText.Length < 4) heightText += digit;
    }

    private static void RefreshLabels()
    {
        if (displayItems is null || customItems is null || itemTextField is null) return;
        var displayLabels = new[]
        {
            $"M0DE: {preferences.Mode.ToUpperInvariant()}",
            AvailablePresets(desktopSize.Width, desktopSize.Height).Any(preset =>
                preset.Width == preferences.Width && preset.Height == preferences.Height)
                ? $"PRES3T: {preferences.Width} X {preferences.Height}" : "PRES3T: CUST0M",
            $"VSYNC: {(preferences.VSync ? "ON" : "OFF")}",
            "CUST0M RES0LUTI0N",
            $"R3DUCE FLASH1NG: {(preferences.ReduceFlashing ? "ON" : "OFF")}", "BACK"
        };
        for (var i = 0; i < displayLabels.Length; i++)
            itemTextField.SetValue(displayItems.GetValue(i), displayLabels[i]);
        var customLabels = new[]
        {
            $"W1DTH: {widthText}{(editing == 2 ? "_" : "")}",
            $"HE1GHT: {heightText}{(editing == 3 ? "_" : "")}",
            "APPLY CUST0M", "BACK"
        };
        for (var i = 0; i < customLabels.Length; i++)
            itemTextField.SetValue(customItems.GetValue(i), customLabels[i]);
    }

    private static void SetInfo(string text)
    {
        if (settingsMenu is not null)
            infosField!.SetValue(settingsMenu, new[] { new StringBuilder(text) });
    }

    public static bool ApplyForProbe(string mode, int width, int height)
    {
        Apply(preferences with { Mode = mode, Width = width, Height = height }, save: false);
        widthText = preferences.Width.ToString();
        heightText = preferences.Height.ToString();
        RefreshLabels();
        return preferences.Mode == mode && preferences.Width == width && preferences.Height == height;
    }

    public static void BeginFrame()
    {
        if (game is null) return;
        var device = game.GraphicsDevice;
        SyncSceneSize();
        if (scene is null || scene.IsDisposed ||
            scene.Width != sceneSize.Width || scene.Height != sceneSize.Height)
        {
            scene?.Dispose();
            scene = new RenderTarget2D(device, sceneSize.Width, sceneSize.Height, false,
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
        var presentation = Presentation(device.PresentationParameters.BackBufferWidth,
            device.PresentationParameters.BackBufferHeight, sceneSize);
        batch.Draw(scene, presentation.Offset, null, Color.White, 0, Vector2.Zero,
            presentation.Scale, SpriteEffects.None, 0);
        batch.End();
    }

    // Keep at least the phone's original 600x360 view. The other axis grows to
    // match the backbuffer, so the camera shows more world instead of adding bars.
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
        var parameters = game.GraphicsDevice.PresentationParameters;
        var next = CanvasSize(parameters.BackBufferWidth, parameters.BackBufferHeight);
        if (sceneSize == next) return;
        sceneSize = next;
        var size = SceneSize;
        viewportField!.SetValue(null, size);
        zombieScreenField!.SetValue(null, size);
        vikingScreenField!.SetValue(null, size);
        CenterMenuItems();
    }

    private static float MenuOffset => (sceneSize.Width - LogicalWidth) / 2f;
    private static float TextMenuOffset => MenuOffset / (float)menuScaleField!.GetValue(null)!;

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
        Shift(displayItems);
        Shift(customItems);
        menuOffsetApplied = MenuOffset;
    }

    private static bool Valid(int width, int height) =>
        width is >= 600 and <= 3840 && height is >= 360 and <= 2160;

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
            graphics.PreferredBackBufferWidth = mode == "Borderless" ? desktopSize.Width : requested.Width;
            graphics.PreferredBackBufferHeight = mode == "Borderless" ? desktopSize.Height : requested.Height;
            graphics.ApplyChanges();
            SyncSceneSize();
            preferences = requested;
            if (save) Save();
            Console.WriteLine($"DISPLAY_APPLIED mode={mode} resolution={requested.Width}x{requested.Height} backbuffer={graphics.GraphicsDevice.PresentationParameters.BackBufferWidth}x{graphics.GraphicsDevice.PresentationParameters.BackBufferHeight}");
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
                loaded.Mode is "Windowed" or "Borderless" or "Fullscreen" ? loaded : preferences;
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
        public bool VSync { get; init; } = true;
        public bool ReduceFlashing { get; init; } = false;
    }

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
