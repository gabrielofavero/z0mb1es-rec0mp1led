using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Port.Hooks;
using SDL3;

internal static class MobileDisplayChecks
{
    // Requires a graphical desktop session. Exercise the mobile display hooks
    // against a real SDL window/device, including stale iOS backbuffer geometry.
    internal static void Run(string gamePath, string nativeDirectory)
    {
        NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, (name, _, _) => NativeLibrary.Load(
            Path.Combine(Path.GetFullPath(nativeDirectory), OperatingSystem.IsMacOS() ? name switch
            {
                "SDL3" => "libSDL3.0.dylib", "FNA3D" => "libFNA3D.0.dylib",
                "FAudio" => "libFAudio.0.dylib", "theorafile" => "libtheorafile.dylib", _ => name
            } : OperatingSystem.IsWindows() ? name + ".dll" : "lib" + name + ".so.0")));
        var saves = Path.Combine(Path.GetTempPath(), "z0mb1es-display-" + Guid.NewGuid().ToString("N"));
        DesktopStorage.Configure(saves);
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(gamePath));
            using var game = (Game)Activator.CreateInstance(assembly.GetType("ZombiesWP7.Game1", true)!)!;
            DesktopDisplay.Configure(game, mobile: true);
            Require(!DesktopDisplay.ResponsiveOrientationEnabled, "Fresh mobile launch enabled Auto Rotate.");
            var manager = (GraphicsDeviceManager)game.Services.GetService(typeof(IGraphicsDeviceManager))!;
            ((IGraphicsDeviceManager)manager).CreateDevice();
            DesktopDisplay.ApplyStartup(windowed: true);
            var device = game.GraphicsDevice;
            Point Drawable()
            {
                Require(SDL.SDL_GetWindowSizeInPixels(game.Window.Handle, out var width, out var height), "No SDL drawable size.");
                return new Point(width, height);
            }
            void Window(int width, int height)
            {
                Require(SDL.SDL_SetWindowSize(game.Window.Handle, width, height), "SDL window resize failed.");
                SDL.SDL_PumpEvents();
            }
            void CheckPhysicalBuffer()
            {
                var size = Drawable();
                var output = device.PresentationParameters;
                Require(output.BackBufferWidth == size.X && output.BackBufferHeight == size.Y,
                    $"Backbuffer {output.BackBufferWidth}x{output.BackBufferHeight} differs from native drawable {size.X}x{size.Y}; presentation would stretch.");
            }
            void StalePortraitBuffer()
            {
                var size = Drawable();
                var stale = device.PresentationParameters.Clone();
                stale.BackBufferWidth = Math.Min(size.X, size.Y);
                stale.BackBufferHeight = Math.Max(size.X, size.Y);
                stale.DisplayOrientation = DisplayOrientation.Portrait;
                device.Reset(stale);
            }
            void AutoRotate(bool enabled)
            {
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                var preferences = typeof(DesktopDisplay).GetField("preferences", flags)!.GetValue(null)!;
                preferences.GetType().GetProperty("ResponsiveOrientation")!.SetValue(preferences, enabled);
            }
            using var pixel = new Texture2D(device, 1, 1);
            pixel.SetData([Color.White]);
            using var batch = new SpriteBatch(device);
            void CheckSquare()
            {
                DesktopDisplay.BeginFrame();
                DesktopDisplay.Begin(batch);
                batch.Draw(pixel, new Rectangle(50, 50, 60, 60), Color.Lime);
                batch.End();
                DesktopDisplay.EndFrame();
                CheckPhysicalBuffer();
                var size = Drawable();
                var pixels = new Color[size.X * size.Y];
                device.GetBackBufferData(pixels);
                var minX = size.X; var minY = size.Y; var maxX = -1; var maxY = -1;
                for (var y = 0; y < size.Y; y++)
                for (var x = 0; x < size.X; x++)
                    if (pixels[y * size.X + x].G > 220 && pixels[y * size.X + x].R < 20)
                    {
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
                Require(maxX >= minX && Math.Abs((maxX - minX) - (maxY - minY)) <= 1,
                    "Mobile presentation stretched a square across the native drawable.");
            }
            Window(900, 414);
            StalePortraitBuffer();
            DesktopDisplay.RefreshLayout();
            CheckPhysicalBuffer();
            var landscape = Drawable();
            manager.PreferredBackBufferWidth = landscape.Y;
            manager.PreferredBackBufferHeight = landscape.X;
            manager.ApplyChanges();
            Require(Drawable() == landscape, "A stale graphics preference resized the native landscape window.");
            CheckPhysicalBuffer();
            Require(game.Window.AllowUserResizing, "Mobile host ignores SDL window resize events.");
            Require(DesktopDisplay.SceneSize.X > DesktopDisplay.SceneSize.Y, "Disabled Auto Rotate changed landscape layout.");
            CheckSquare();
            Console.WriteLine("MOBILE_DISPLAY_FRESH_BOOT_OK; Auto Rotate off; stale portrait backbuffer repaired; square keeps its aspect");

            // UIKit and display orientation events can arrive in either order.
            Window(414, 900);
            DesktopDisplay.RefreshLayout();
            Require(DesktopDisplay.SceneSize.X > DesktopDisplay.SceneSize.Y, "A transition bypassed the landscape lock.");
            CheckSquare();
            AutoRotate(true);
            DesktopDisplay.RefreshLayout();
            Require(DesktopDisplay.SceneSize.Y > DesktopDisplay.SceneSize.X, "Enabled Auto Rotate ignored the portrait drawable.");
            CheckSquare();
            CheckEndlessCulling(assembly);
            Window(900, 414);
            StalePortraitBuffer();
            DesktopDisplay.RefreshLayout();
            CheckSquare();
            AutoRotate(false);
            DesktopDisplay.RefreshLayout();
            CheckSquare();
            Console.WriteLine("MOBILE_DISPLAY_ROTATION_OK; locked transition, enabled portrait, return to landscape and disable; uniform presentation");
        }
        finally
        {
            DesktopStorage.Configure(null);
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    private static void CheckEndlessCulling(Assembly assembly)
    {
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags instances = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var camera = assembly.GetType("IMAK3Z0MB1EGAEM.ScrollMan", true)!;
        var zoomField = camera.GetField("zoom", statics)!;
        var scrollField = camera.GetField("scroll", statics)!;
        var mapField = assembly.GetType("IMAK3Z0MB1EGAEM.map.MapMan", true)!.GetField("map", statics)!;
        var previousZoom = zoomField.GetValue(null);
        var previousScroll = scrollField.GetValue(null);
        var previousMap = mapField.GetValue(null);
        try
        {
            var map = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(mapField.FieldType);
            mapField.SetValue(null, map);
            var updateType = assembly.GetType("Viking_x86.zomb1es.endless.EndlessUpdate", true)!;
            var update = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(updateType);
            var nodeType = assembly.GetType("Viking_x86.zomb1es.endless.EndlessNode", true)!;
            var rooms = Array.CreateInstance(nodeType, 3, 3);
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
            {
                var node = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(nodeType);
                nodeType.GetField("alpha", instances)!.SetValue(node, 1f);
                rooms.SetValue(node, x, y);
            }
            updateType.GetField("room", instances)!.SetValue(update, rooms);
            zoomField.SetValue(null, 2f);
            foreach (var center in new[] { new Vector2(614.4f, 460.8f), new Vector2(1843.2f, 1382.4f), new Vector2(3072f, 2304f) })
            {
                scrollField.SetValue(null, center);
                updateType.GetMethod("UpdateDrawableRect", instances)!.Invoke(update, null);
                var visible = (Rectangle)map.GetType().GetField("drawableRect", instances)!.GetValue(map)!;
                var top = DesktopDisplay.EndlessCullWorld(Vector2.Zero, 1f);
                var bottom = DesktopDisplay.EndlessCullWorld(DesktopDisplay.SceneSize, 1f);
                Require(top.X >= visible.Left - 1 && top.Y >= visible.Top - 1 &&
                    bottom.X <= visible.Right + 1 && bottom.Y <= visible.Bottom + 1,
                    $"Endless leaves undrawn portrait chunks: view {top}..{bottom}, drawable {visible}.");
            }
            Require(DesktopDisplay.EndlessMaskBottomEdge() == DesktopDisplay.SceneSize.Y,
                "Endless reveal mask stops before the portrait view's bottom edge.");
            Console.WriteLine("ENDLESS_PORTRAIT_CULLING_OK; real map culling covers full rendered viewport across rooms; reveal mask fills height");
        }
        finally
        {
            zoomField.SetValue(null, previousZoom);
            scrollField.SetValue(null, previousScroll);
            mapField.SetValue(null, previousMap);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
