using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

/// <summary>Scales the phone scene and adds a native Display page to Settings.</summary>
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
    private static (int Width, int Height) desktopSize;
    private static DisplayPreferences preferences = new("Fullscreen", 600, 360);
    private static object? settingsMenu;
    private static FieldInfo? itemsField;
    private static FieldInfo? titleField;
    private static FieldInfo? infosField;
    private static FieldInfo? itemTextField;
    private static FieldInfo? itemLocationField;
    private static FieldInfo? itemTiltField;
    private static Array? originalItems;
    private static Array? settingsItems;
    private static Array? touchItems;
    private static Array? displayItems;
    private static object? previousInfos;
    private static bool displayPage;
    private static bool touchPage;
    private static int editing = -1;
    private static bool captureInput;
    private static KeyboardState previousKeyboard;
    private static GamePadState previousPad;
    private static string widthText = "600";
    private static string heightText = "360";

    public static bool CapturesInput => captureInput;
    public static bool IsDisplayPage => displayPage;
    public static bool IsTouchPage => touchPage;

    public static void Configure(Game runningGame)
    {
        game = runningGame;
        graphics = (GraphicsDeviceManager?)runningGame.Services.GetService(typeof(IGraphicsDeviceManager))
            ?? throw new InvalidOperationException("Game has no graphics manager.");
        scene?.Dispose();
        scene = null;
        batch?.Dispose();
        batch = null;
        desktopSize = default;
        settingsMenu = null;
        itemsField = titleField = infosField = itemTextField = null;
        itemLocationField = itemTiltField = null;
        originalItems = settingsItems = touchItems = displayItems = null;
        previousInfos = null;
        displayPage = touchPage = captureInput = false;
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
                .GetValue(null) is not IDictionary levels || levels[5] is not { } settings) return;
        var baseType = settings.GetType().BaseType!;
        itemsField = baseType.GetField("item", Fields)!;
        titleField = baseType.GetField("title", Fields)!;
        infosField = baseType.GetField("infos", Fields)!;
        if (itemsField.GetValue(settings) is not Array oldItems || oldItems.Length != 5)
            throw new InvalidOperationException("Original Settings menu item layout differs.");
        originalItems = oldItems;
        var itemType = assembly.GetType("ZombiesWP7.menu.MenuItem", true)!;
        itemTextField = itemType.GetField("text", Fields)!;
        itemLocationField = itemType.GetField("loc", Fields)!;
        itemTiltField = itemType.GetField("tiltLoc", Fields)!;
        var constructor = itemType.GetConstructor([typeof(string), typeof(Vector2), typeof(float)])
            ?? throw new MissingMethodException("MenuItem(string, Vector2, float)");
        object Item(string text, float y) => constructor.Invoke([text, new Vector2(250, y), 0.9f]);
        settingsItems = Array.CreateInstance(itemType, 7);
        Array.Copy(oldItems, settingsItems, 5);
        settingsItems.SetValue(Item("T0UCH", 170), 5);
        settingsItems.SetValue(Item("DISPLAY", 215), 6);
        touchItems = Array.CreateInstance(itemType, 6);
        Array.Copy(oldItems, touchItems, 5);
        touchItems.SetValue(Item("BACK", 250), 5);
        displayItems = Array.CreateInstance(itemType, 7);
        var labels = new[] { "M0DE", "PRES3T", "W1DTH", "HE1GHT", "VSYNC", "APPLY CUST0M", "BACK" };
        for (var i = 0; i < labels.Length; i++)
            displayItems.SetValue(Item(labels[i], 80 + i * 30), i);
        settingsMenu = settings;
        ShowSettingsPage();
        RefreshLabels();
    }

    private static void PlaceOriginal(int index, bool visible, Vector2 position)
    {
        var item = originalItems!.GetValue(index)!;
        var location = visible ? position : new Vector2(-1000, -1000);
        itemLocationField!.SetValue(item, location);
        itemTiltField!.SetValue(item, location);
    }

    private static void ShowSettingsPage()
    {
        touchPage = displayPage = false;
        for (var i = 0; i < 3; i++) PlaceOriginal(i, false, Vector2.Zero);
        PlaceOriginal(3, true, new Vector2(125, 110));
        PlaceOriginal(4, true, new Vector2(270, 110));
        itemsField!.SetValue(settingsMenu, settingsItems);
        titleField!.SetValue(settingsMenu, "s3tt1ngs");
    }

    private static void ShowTouchPage()
    {
        displayPage = false;
        touchPage = true;
        PlaceOriginal(0, true, new Vector2(250, 100));
        PlaceOriginal(1, true, new Vector2(250, 150));
        PlaceOriginal(2, true, new Vector2(250, 200));
        PlaceOriginal(3, false, Vector2.Zero);
        PlaceOriginal(4, false, Vector2.Zero);
        itemsField!.SetValue(settingsMenu, touchItems);
        titleField!.SetValue(settingsMenu, "t0uch");
    }

    public static bool HandleSettingsClick(object settings, string text)
    {
        if (!ReferenceEquals(settings, settingsMenu)) return false;
        if (!displayPage && !touchPage)
        {
            if (text is not ("DISPLAY" or "T0UCH")) return false;
            previousInfos = infosField!.GetValue(settings);
            infosField.SetValue(settings, null);
            if (text == "T0UCH")
            {
                ShowTouchPage();
                return true;
            }
            displayPage = true;
            titleField!.SetValue(settings, "d1splay");
            itemsField!.SetValue(settings, displayItems);
            widthText = preferences.Width.ToString();
            heightText = preferences.Height.ToString();
            RefreshLabels();
            return true;
        }
        if (touchPage)
        {
            if (text != "BACK") return false;
            CloseDisplayPage();
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
        else if (text.StartsWith("W1DTH:", StringComparison.Ordinal)) StartEditing(2);
        else if (text.StartsWith("HE1GHT:", StringComparison.Ordinal)) StartEditing(3);
        else if (text.StartsWith("VSYNC:", StringComparison.Ordinal))
            Apply(preferences with { VSync = !preferences.VSync }, save: true);
        else if (text == "APPLY CUST0M")
        {
            if (int.TryParse(widthText, out var width) && int.TryParse(heightText, out var height) &&
                Valid(width, height)) Apply(preferences with { Width = width, Height = height }, save: true);
            else SetInfo("Resolution must be 600-3840 x 360-2160.");
        }
        else if (text == "BACK") CloseDisplayPage();
        else return false;
        RefreshLabels();
        return true;
    }

    public static bool HandleSettingsCancel(object settings)
    {
        if (!ReferenceEquals(settings, settingsMenu) || (!displayPage && !touchPage)) return false;
        CloseDisplayPage();
        return true;
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
        if (displayItems is null || itemTextField is null) return;
        var labels = new[]
        {
            $"M0DE: {preferences.Mode.ToUpperInvariant()}",
            AvailablePresets(desktopSize.Width, desktopSize.Height).Any(preset =>
                preset.Width == preferences.Width && preset.Height == preferences.Height)
                ? $"PRES3T: {preferences.Width} X {preferences.Height}" : "PRES3T: CUST0M",
            $"W1DTH: {widthText}{(editing == 2 ? "_" : "")}",
            $"HE1GHT: {heightText}{(editing == 3 ? "_" : "")}",
            $"VSYNC: {(preferences.VSync ? "ON" : "OFF")}",
            "APPLY CUST0M", "BACK"
        };
        for (var i = 0; i < labels.Length; i++)
            itemTextField.SetValue(displayItems.GetValue(i), labels[i]);
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
        if (scene is null || scene.IsDisposed)
            scene = new RenderTarget2D(device, LogicalWidth, LogicalHeight, false,
                SurfaceFormat.Color, DepthFormat.None);
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
        var bounds = Destination(device.PresentationParameters.BackBufferWidth,
            device.PresentationParameters.BackBufferHeight);
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(scene, bounds, Color.White);
        batch.End();
    }

    public static Rectangle Destination(int width, int height)
    {
        var scale = Math.Min(width / (double)LogicalWidth, height / (double)LogicalHeight);
        var drawnWidth = (int)Math.Round(LogicalWidth * scale);
        var drawnHeight = (int)Math.Round(LogicalHeight * scale);
        return new Rectangle((width - drawnWidth) / 2, (height - drawnHeight) / 2,
            drawnWidth, drawnHeight);
    }

    public static Vector2? MapPointer(int x, int y, Rectangle client)
    {
        if (client.Width <= 0 || client.Height <= 0) return null;
        var bounds = Destination(client.Width, client.Height);
        if (!bounds.Contains(x, y)) return null;
        return new Vector2((x - bounds.X) * LogicalWidth / (float)bounds.Width,
            (y - bounds.Y) * LogicalHeight / (float)bounds.Height);
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
    }
}
