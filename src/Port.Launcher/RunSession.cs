using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.GamerServices;
using Port.Hooks;

internal static class RunSession
{
    public static int Start(string[] args)
    {
        if (args.Length < 4 || args[2] != "--content")
        {
            Console.Error.WriteLine("Usage: Port.Launcher --run <prepared.dll> --content <prepared-content-root> [--frames N] [--windowed] [--save-dir <directory>] [--probe-services] [--probe-mode normal|endless|viking] [--probe-menus]");
            return 2;
        }
        int? frameLimit = null;
        var windowed = false;
        var probeServices = false;
        var probeMenus = false;
        string? probeMode = null;
        string? saveDirectory = null;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--windowed") windowed = true;
            else if (args[i] == "--probe-services") probeServices = true;
            else if (args[i] == "--probe-menus") probeMenus = true;
            else if (args[i] == "--probe-mode" && ++i < args.Length &&
                     args[i] is "normal" or "endless" or "viking") probeMode = args[i];
            else if (args[i] == "--frames" && ++i < args.Length &&
                     int.TryParse(args[i], out var frames) && frames > 0) frameLimit = frames;
            else if (args[i] == "--save-dir" && ++i < args.Length &&
                     !string.IsNullOrWhiteSpace(args[i])) saveDirectory = args[i];
            else
            {
                Console.Error.WriteLine("RUN_FAIL invalid command line");
                return 2;
            }
        }
        if ((probeMode is not null || probeMenus) && (saveDirectory is null ||
            (probeMode is not null && (probeServices || probeMenus)) ||
            (probeMenus && probeServices)))
        {
            Console.Error.WriteLine("RUN_FAIL diagnostic probes require an isolated --save-dir and cannot be combined");
            return 2;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Report("unhandled-thread", eventArgs.ExceptionObject as Exception);
        Trace.Listeners.Add(new ConsoleTraceListener(useErrorStream: true));
        Trace.AutoFlush = true;

        try
        {
            var dll = Path.GetFullPath(args[1]);
            var content = Path.GetFullPath(args[3]);
            VerifyInputs(dll, content);
            DesktopStorage.Configure(saveDirectory);
            Directory.SetCurrentDirectory(content);
            var assembly = Assembly.LoadFrom(dll);
            if (assembly.GetName().Name != "ZombiesWP7")
                throw new InvalidDataException("Prepared assembly identity is not ZombiesWP7.");
            var gameType = assembly.GetType("ZombiesWP7.Game1", throwOnError: true)!;
            using var game = (Game)(Activator.CreateInstance(gameType)
                ?? throw new InvalidDataException("Game1 construction returned null."));
            game.IsMouseVisible = true;
            game.Window.AllowUserResizing = true;
            DesktopDisplay.Configure(game);
            DesktopInput.Configure(game);
            game.Content.RootDirectory = Path.Combine(content, "Content");
            var monitor = new RunMonitor(game, gameType, frameLimit, windowed, probeServices, probeMode, probeMenus);
            game.Components.Add(monitor);
            Console.WriteLine("RUN_START original=ZombiesWP7.Game1 method=Run");
            try { game.Run(); }
            catch (Exception error)
            {
                Console.Error.WriteLine($"RUN_PRIMARY_EXCEPTION {error}");
                throw;
            }
            Console.WriteLine($"RUN_END updates={monitor.Updates} frames={monitor.Frames} menuReached={monitor.MenuReached} menuAnimated={monitor.MenuAnimated} rendered={monitor.RenderedMenu} serviceScreens={monitor.ServiceScreens} serviceVerified={monitor.ServiceStatesVerified} modeProbe={probeMode} modeVerified={monitor.ModeVerified} menusVerified={monitor.MenusVerified} inputSamples={DesktopInput.Samples} activeSamples={DesktopInput.ActiveSamples} mouseSamples={DesktopInput.MouseSamples} keyboardSamples={DesktopInput.KeyboardSamples} movementSeen={monitor.MovementSeen} aimSeen={monitor.AimSeen} clickSeen={monitor.ClickSeen} backSeen={monitor.BackSeen} pauseSeen={monitor.PauseSeen} hostQuit={monitor.HostQuit} timedOut={monitor.TimedOut}");
            return monitor.MenuReached && monitor.MenuAnimated && monitor.RenderedMenu &&
                monitor.Frames >= 2 && !monitor.TimedOut && (!probeServices || monitor.ServiceStatesVerified) &&
                (probeMode is null || monitor.ModeVerified) && (!probeMenus || monitor.MenusVerified) ? 0 : 1;
        }
        catch (Exception error)
        {
            Report("run", error);
            return 1;
        }
    }

    private static void VerifyInputs(string dll, string content)
    {
        var runtimeRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        content = content.TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(dll), runtimeRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(content, runtimeRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prepared DLL and content must be staged beside the launcher for FNA TitleContainer paths.");
        var gameManifest = Path.Combine(Path.GetDirectoryName(dll)!, "patch-manifest.json");
        using var patch = JsonDocument.Parse(File.ReadAllText(gameManifest));
        if (patch.RootElement.GetProperty("sourceSha256").GetString() !=
                "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823" ||
            patch.RootElement.GetProperty("preparedSha256").GetString() != Hash(dll))
            throw new InvalidDataException("Prepared DLL differs from its verified patch manifest.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "content-manifest.json")));
        if (manifest.RootElement.GetProperty("SourceXapSha256").GetString() !=
            "6b88c7fbd95444d2560b4b11e6f35d40a6c0f7abc30706155dcbccd18bc333fe")
            throw new InvalidDataException("Prepared content is not from the supported XAP.");
        if (!File.Exists(Path.Combine(content, "Content", "Arial.xnb")))
            throw new InvalidDataException("Prepared content is missing Arial.xnb.");
        foreach (var output in manifest.RootElement.GetProperty("Outputs").EnumerateArray())
        {
            var relative = output.GetProperty("Path").GetString()
                ?? throw new InvalidDataException("Content manifest has an empty output path.");
            var path = Path.GetFullPath(Path.Combine(content, relative));
            if (!path.StartsWith(content + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(path) ||
                new FileInfo(path).Length != output.GetProperty("Bytes").GetInt64() ||
                Hash(path) != output.GetProperty("Sha256").GetString())
                throw new InvalidDataException($"Prepared content differs from its manifest: {relative}");
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Report(string stage, Exception? error)
    {
        var cause = error?.GetBaseException();
        if (error is not null) Console.Error.WriteLine($"RUN_EXCEPTION {error}");
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            eventType = "run-failure", stage,
            exception = cause?.GetType().FullName ?? "unknown",
            message = cause?.Message ?? "Unknown error"
        }));
    }
}

internal sealed class RunMonitor : DrawableGameComponent
{
    private readonly Type gameType;
    private readonly int? frameLimit;
    private readonly bool windowed;
    private readonly bool probeServices;
    private readonly ModeProbe? modeProbe;
    private readonly MenuProbe? menuProbe;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    public int Updates { get; private set; }
    public int Frames { get; private set; }
    public bool MenuReached { get; private set; }
    public bool MenuAnimated { get; private set; }
    public bool RenderedMenu { get; private set; }
    public bool HostQuit { get; private set; }
    public bool TimedOut { get; private set; }
    public bool MovementSeen { get; private set; }
    public bool AimSeen { get; private set; }
    public bool ClickSeen { get; private set; }
    public bool BackSeen { get; private set; }
    public bool PauseSeen { get; private set; }
    public int ServiceScreens { get; private set; }
    private bool achievementStateVerified;
    private bool leaderboardStateVerified;
    public bool ServiceStatesVerified => achievementStateVerified && leaderboardStateVerified;
    public bool ModeVerified => modeProbe?.Verified ?? false;
    public bool MenusVerified => menuProbe?.Verified ?? false;
    private bool achievementCaptured;
    private bool leaderboardCaptured;
    private int achievementOpenedAt;
    private int leaderboardOpenedAt;
    private float? firstMenuFrame;

    public RunMonitor(Game game, Type gameType, int? frameLimit, bool windowed, bool probeServices, string? probeMode, bool probeMenus) : base(game)
    {
        this.gameType = gameType;
        this.frameLimit = frameLimit;
        this.windowed = windowed;
        this.probeServices = probeServices;
        if (probeMode is not null) modeProbe = new ModeProbe(game, gameType, probeMode);
        if (probeMenus) menuProbe = new MenuProbe(game, gameType);
    }

    public override void Initialize()
    {
        base.Initialize();
        DesktopDisplay.ApplyStartup(windowed);
        var viewport = gameType.GetField("VIEWPORT", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
        Console.WriteLine($"RUN_DISPLAY originalViewport={viewport} actualBackBuffer={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight} fullscreen={GraphicsDevice.PresentationParameters.IsFullScreen} windowHandle={Game.Window.Handle} active={Game.IsActive}");
    }

    public override void Update(GameTime gameTime)
    {
        Updates++;
        DesktopDisplay.AttachSettingsMenu();
        DesktopDisplay.Update();
        var touch = gameType.Assembly.GetType("ZombiesWP7.TouchMgr");
        const BindingFlags touchFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        MovementSeen |= ((Vector2?)touch?.GetField("lVec", touchFlags)?.GetValue(null))?.LengthSquared() > 0.01f;
        AimSeen |= ((Vector2?)touch?.GetField("rVec", touchFlags)?.GetValue(null))?.LengthSquared() > 0.01f;
        ClickSeen |= (bool?)touch?.GetField("isClick", touchFlags)?.GetValue(null) ?? false;
        BackSeen |= (bool?)touch?.GetField("isBack", touchFlags)?.GetValue(null) ?? false;
        var stateType = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.director.GameState");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var state = (int?)stateType?.GetField("state", flags)?.GetValue(null);
        var menuType = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu");
        var levels = menuType?.GetField("menuLevel", flags)?.GetValue(null) as System.Collections.IDictionary;
        if (probeServices && MenuReached && MenuAnimated && levels is not null)
        {
            if (ServiceScreens == 0 && Updates >= 130)
            {
                ProbeServiceMenu(menuType!, levels, "Achievements");
                achievementOpenedAt = Updates;
            }
            else if (ServiceScreens == 1 && Updates >= achievementOpenedAt + 35)
            {
                ProbeServiceMenu(menuType!, levels, "Leaderboards");
                leaderboardOpenedAt = Updates;
            }
            if (ServiceScreens == 1 && !achievementStateVerified && Updates >= achievementOpenedAt + 25)
                achievementStateVerified = InspectServiceMenu(levels, "Achievements");
            if (ServiceScreens == 2 && !leaderboardStateVerified && Updates >= leaderboardOpenedAt + 25)
                leaderboardStateVerified = InspectServiceMenu(levels, "Leaderboards");
        }
        var pause = levels?.Contains(2) == true ? levels[2] : null;
        var pauseActive = (bool?)pause?.GetType().GetField("active",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(pause) ?? false;
        PauseSeen |= pauseActive;
        var mainMenu = (int?)stateType?.GetField("STATE_MAINMENU", flags)?.GetValue(null);
        var menuObject = gameType.GetField("mainMenu", flags)?.GetValue(null);
        var loadComplete = (bool?)gameType.GetField("loadcomplete",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(Game) ?? false;
        if (state == mainMenu && state is not null && menuObject is not null && loadComplete)
        {
            MenuReached = true;
            var menuFrame = (float?)menuObject.GetType().GetField("frame",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuObject);
            firstMenuFrame ??= menuFrame;
            if (menuFrame != firstMenuFrame) MenuAnimated = true;
        }
        modeProbe?.Update(Updates, MenuReached && MenuAnimated);
        menuProbe?.Update(Updates, MenuReached && MenuAnimated);
        if (Updates % 60 == 0)
        {
            var trialType = gameType.Assembly.GetType("ZombiesWP7.xdktools.TrialMgr");
            var trial = (bool?)trialType?.GetField("trial", flags)?.GetValue(null);
            Console.WriteLine($"RUN_PROGRESS updates={Updates} frames={Frames} state={state} trial={trial} pauseActive={pauseActive} menuObject={menuObject is not null} loadComplete={loadComplete} menuAnimated={MenuAnimated}");
        }
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Q) &&
            (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)))
        {
            HostQuit = true;
            Game.Exit();
        }
        if (frameLimit.HasValue && elapsed.Elapsed > TimeSpan.FromSeconds(60))
        {
            TimedOut = true;
            Console.Error.WriteLine("RUN_TIMEOUT 60s");
            Game.Exit();
        }
        base.Update(gameTime);
    }

    private void ProbeServiceMenu(Type menuType, System.Collections.IDictionary levels, string name)
    {
        foreach (System.Collections.DictionaryEntry entry in levels)
        {
            if (entry.Value?.GetType().Name != name) continue;
            entry.Value.GetType().GetMethod("Init", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(entry.Value, null);
            menuType.GetMethod("SetLevel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, [entry.Key]);
            ServiceScreens++;
            Console.WriteLine($"RUN_SERVICE_SCREEN name={name} level={entry.Key}");
            if (name == "Achievements")
            {
                var gamer = Gamer.SignedInGamers[PlayerIndex.One];
                var result = gamer.BeginGetAchievements(null!, null!);
                var achievements = gamer.EndGetAchievements(result);
                Console.WriteLine($"RUN_SERVICE_ACHIEVEMENTS count={achievements.Count} earned={Enumerable.Range(0, achievements.Count).Count(index => achievements[index].IsEarned)}");
                var player = gameType.GetField("player", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                var settings = player?.GetType().GetField("settings")?.GetValue(player);
                var bgm = (bool?)settings?.GetType().GetField("bgm")?.GetValue(settings);
                Console.WriteLine($"RUN_SERVICE_SETTINGS bgm={bgm}");
            }
            return;
        }
        throw new InvalidDataException($"Original service menu is missing: {name}");
    }

    private static bool InspectServiceMenu(System.Collections.IDictionary levels, string name)
    {
        foreach (System.Collections.DictionaryEntry entry in levels)
        {
            if (entry.Value?.GetType().Name != name) continue;
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = entry.Value.GetType();
            var parent = type.BaseType!;
            var items = parent.GetField("item", fields)?.GetValue(entry.Value) as Array;
            var active = parent.GetField("active", fields)?.GetValue(entry.Value);
            var alpha = parent.GetField("alpha", fields)?.GetValue(entry.Value);
            var infos = parent.GetField("infos", fields)?.GetValue(entry.Value) as Array;
            var valid = active is true && alpha is float value && value > 0.9f &&
                (name == "Achievements" ? items?.Length == 20 :
                    items?.Length == 2 && infos?.Length == 6 &&
                    infos.GetValue(4)?.ToString() == "Online leaderboards" &&
                    infos.GetValue(5)?.ToString() == "are unavailable offline.");
            Console.WriteLine($"RUN_SERVICE_STATE name={name} active={active} items={items?.Length} infos={infos?.Length} alpha={alpha} valid={valid}");
            return valid;
        }
        return false;
    }

    public override void Draw(GameTime gameTime)
    {
        Frames++;
        if (probeServices && !achievementCaptured && ServiceScreens == 1 && Updates >= achievementOpenedAt + 25)
        {
            CaptureScreen("achievements", "SERVICE");
            achievementCaptured = true;
        }
        if (probeServices && !leaderboardCaptured && ServiceScreens == 2 && Updates >= leaderboardOpenedAt + 25)
        {
            CaptureScreen("leaderboards", "SERVICE");
            leaderboardCaptured = true;
        }
        if (modeProbe?.ShouldCapture(Updates) == true)
        {
            CaptureScreen($"mode-{modeProbe.Mode}", "MODE");
            modeProbe.MarkCaptured();
        }
        if (modeProbe?.ShouldCapturePause(Updates) == true)
        {
            CaptureScreen($"pause-{modeProbe.Mode}", "MODE");
            modeProbe.MarkPauseCaptured();
        }
        if (modeProbe?.ShouldCaptureSplash(Updates) == true)
        {
            CaptureScreen($"splash-{modeProbe.Mode}", "MODE");
            modeProbe.MarkSplashCaptured();
        }
        if (modeProbe?.ShouldCaptureModeMenu(Updates) == true)
        {
            CaptureScreen($"mode-menu-{modeProbe.Mode}", "MODE");
            modeProbe.MarkModeMenuCaptured();
        }
        if (menuProbe?.ShouldCapture(Updates) == true)
        {
            CaptureScreen($"menu-{menuProbe.CurrentLevel:D2}", "MENU");
            menuProbe.MarkCaptured();
        }
        if (menuProbe?.ShouldCaptureDisplay(Updates) == true)
        {
            CaptureScreen("display-page", "MENU");
            menuProbe.MarkDisplayCaptured();
        }
        if (menuProbe?.ShouldCaptureCustom(Updates) == true)
        {
            CaptureScreen("custom-page", "MENU");
            menuProbe.MarkCustomCaptured();
        }
        if (menuProbe?.ShouldCaptureTouch(Updates) == true)
        {
            CaptureScreen("touch-page", "MENU");
            menuProbe.MarkTouchCaptured();
        }
        if (MenuReached && !RenderedMenu && Frames % 30 == 0)
        {
            var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
            var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
            var pixels = new Color[width * height];
            GraphicsDevice.GetBackBufferData(pixels);
            var visible = pixels.Count(pixel => pixel.R != 0 || pixel.G != 0 || pixel.B != 0);
            var distinct = pixels.Distinct().Take(8).Count();
            RenderedMenu = visible > pixels.Length / 20 && distinct >= 8;
            Console.WriteLine($"RUN_RENDER frame={Frames} size={width}x{height} nonBlackPixels={visible} sampleColors={distinct} menuVisible={RenderedMenu}");
        }
        if (frameLimit is int limit && Frames >= limit) Game.Exit();
        base.Draw(gameTime);
    }

    private void CaptureScreen(string name, string category)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        var path = Path.Combine(AppContext.BaseDirectory,
            category == "SERVICE" ? $"service-{name}.bmp" : $"{name}.bmp");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4d42);
        writer.Write(54 + width * height * 4);
        writer.Write((uint)0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)0);
        writer.Write(width * height * 4);
        writer.Write((uint)2835);
        writer.Write((uint)2835);
        writer.Write((uint)0);
        writer.Write((uint)0);
        for (var y = height - 1; y >= 0; y--)
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                writer.Write(pixel.B);
                writer.Write(pixel.G);
                writer.Write(pixel.R);
                writer.Write((byte)255);
            }
        Console.WriteLine($"RUN_{category}_CAPTURE name={name} path={path}");
    }
}
