using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Port.Bridge;
using Port.Hooks;
using Port.IosGame;
using Port.Prepare;

internal static class FrameDriverChecks
{
    // Exercise the exact importer frame driver with a freshly imported game and
    // a real SDL/Metal device. UIKit visibility still requires an iPhone check.
    internal static void ResolveNative(string nativeDirectory)
    {
        NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, (name, _, _) => NativeLibrary.Load(
            Path.Combine(Path.GetFullPath(nativeDirectory), OperatingSystem.IsMacOS() ? name switch
            {
                "SDL3" => "libSDL3.0.dylib", "FNA3D" => "libFNA3D.0.dylib",
                "FAudio" => "libFAudio.0.dylib", "theorafile" => "libtheorafile.dylib", _ => name
            } : OperatingSystem.IsWindows() ? name + ".dll" : "lib" + name + ".so.0")));
    }

    internal static void Run(string importedRoot, string nativeDirectory, bool resolveNative = true)
    {
        if (resolveNative) ResolveNative(nativeDirectory);
        var root = Path.GetFullPath(importedRoot);
        var dll = Path.Combine(root, "game", "ZombiesWP7.dll");
        var content = Path.Combine(root, "content");
        PreparedGameVerifier.Verify(dll, content);
        var saves = Path.Combine(Path.GetTempPath(), "z0mb1es-importer-frames-" + Guid.NewGuid().ToString("N"));
        DesktopStorage.Configure(saves);
        PlayerIdentity.Configure(saves);
        GameContent.Configure(content);
        AssetChoices.Initialize(Path.Combine(root, "optional-art"), null, null, null);
        var type = Assembly.LoadFrom(dll).GetType("ZombiesWP7.Game1", true)!;
        var game = (Game)Activator.CreateInstance(type)!;
        game.Content.Dispose();
        game.Content = new CaseMappedContentManager(game.Services, Path.Combine(content, "Content"));
        DesktopDisplay.Configure(game, mobile: true);
        DesktopInput.Configure(game);
        var monitor = new Monitor(game, type);
        game.Components.Add(monitor);
        var exits = 0;
        game.Exiting += (_, _) => exits++;
        using var driver = new FnaFrameDriver(game);
        try
        {
            driver.Start();
            var timeout = Stopwatch.StartNew();
            while (monitor.ReadyFrames < 120 && timeout.Elapsed < TimeSpan.FromSeconds(45))
                Require(driver.Step(), "The imported game exited before its menu loaded.");
            Require(monitor.ReadyFrames >= 120, "The imported game did not reach its menu.");
            var output = game.GraphicsDevice.PresentationParameters;
            var pixels = new Color[output.BackBufferWidth * output.BackBufferHeight];
            game.GraphicsDevice.GetBackBufferData(pixels);
            Require(pixels.Select(p => p.PackedValue).Distinct().Take(16).Count() == 16,
                "The imported menu rendered a blank backbuffer.");
            game.Exit();
            Require(!driver.Step(), "Exit did not stop the frame callbacks.");
            driver.Dispose();
            driver.Dispose();
            Require(exits == 1 && !driver.Step(), "Shutdown repeated or accepted another frame.");
            Console.WriteLine($"IOS_IMPORTER_FRAMES_OK readyFrames={monitor.ReadyFrames}; real imported menu; nonblank Metal output; exit/dispose passed");
        }
        finally
        {
            driver.Dispose();
            if (Directory.Exists(saves)) Directory.Delete(saves, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Monitor(Game game, Type type) : DrawableGameComponent(game)
    {
        private readonly FieldInfo ready = type.GetField("loadcomplete", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        internal int ReadyFrames { get; private set; }
        public override void Initialize()
        {
            base.Initialize();
            DesktopDisplay.ApplyStartup(windowed: true);
        }
        public override void Update(GameTime gameTime)
        {
            if ((bool)ready.GetValue(Game)!) DesktopDisplay.AttachSettingsMenu();
            DesktopDisplay.Update();
            base.Update(gameTime);
        }
        public override void Draw(GameTime gameTime)
        {
            if ((bool)ready.GetValue(Game)!) ReadyFrames++;
            base.Draw(gameTime);
        }
    }
}
