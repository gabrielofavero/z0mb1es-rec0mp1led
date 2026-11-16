using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using Port.Hooks;

internal static class CreatorDragChecks
{
    internal static void Run(string gamePath, string nativeDirectory)
    {
        NativeLibrary.SetDllImportResolver(typeof(Game).Assembly, (name, _, _) => NativeLibrary.Load(
            Path.Combine(Path.GetFullPath(nativeDirectory), OperatingSystem.IsMacOS() ? name switch
            {
                "SDL3" => "libSDL3.0.dylib", "FNA3D" => "libFNA3D.0.dylib",
                "FAudio" => "libFAudio.0.dylib", "theorafile" => "libtheorafile.dylib", _ => name
            } : OperatingSystem.IsWindows() ? name + ".dll" : "lib" + name + ".so.0")));
        const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var assembly = Assembly.LoadFrom(Path.GetFullPath(gamePath));
        var gameType = assembly.GetType("ZombiesWP7.Game1", true)!;
        using var game = (Game)Activator.CreateInstance(gameType)!;
        var graphics = (IGraphicsDeviceManager)game.Services.GetService(typeof(IGraphicsDeviceManager))!;
        graphics.CreateDevice();
        var managerType = assembly.GetType("ZombiesWP7.custom.CustomMgr", true)!;
        var manager = Activator.CreateInstance(managerType)!;
        managerType.GetField("gd", fields)!.SetValue(manager, game.GraphicsDevice);
        gameType.GetField("custom", statics)!.SetValue(null, manager);
        var sourcesField = managerType.GetField("srcImages", fields)!;
        var sources = (IList)Activator.CreateInstance(sourcesField.FieldType)!;
        sourcesField.SetValue(manager, sources);
        var sourceType = sourcesField.FieldType.GenericTypeArguments[0];
        var constructor = sourceType.GetConstructors().Single(c => c.GetParameters().Length == 2);
        var image = ((Array)managerType.GetField("customImg", fields)!.GetValue(manager)!).GetValue(0)!;
        var pointField = image.GetType().GetField("point", fields)!;
        var sizeField = image.GetType().GetField("size", fields)!;
        Vector2 Point() => (Vector2)pointField.GetValue(image)!;
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var scene = typeof(DesktopDisplay).GetField("sceneSize", statics)!;
        var oldScene = scene.GetValue(null);
        var owned = (HashSet<Texture2D>)typeof(DesktopPictures).GetField("SourceTextures", statics)!.GetValue(null)!;
        try
        {
            var emptyMapper = Activator.CreateInstance(assembly.GetType("IMAK3Z0MB1EGAEM.menu.CustomMapper", true)!)!;
            DesktopPictures.UpdateMapperControls(emptyMapper, new Vector2(300, 200), true, default, 0.016f);
            DesktopPictures.UpdateMapperControls(null, null, false, default, 0);
            foreach (var dimensions in new[] { (600, 360), (900, 360), (600, 1300) })
            foreach (var photo in new[] { (400, 400), (266, 400), (400, 266) })
            {
                DesktopPictures.UpdateMapperControls(null, null, false, default, 0);
                scene.SetValue(null, dimensions);
                using var texture = new Texture2D(game.GraphicsDevice, photo.Item1, photo.Item2);
                texture.SetData(Enumerable.Repeat(Color.White, texture.Width * texture.Height).ToArray());
                owned.Add(texture);
                sources.Clear();
                var source = constructor.Invoke(["drag-test", texture]);
                sourceType.GetField("imgIdx", fields)!.SetValue(source, 0);
                sources.Add(source);
                image.GetType().GetField("enabled", fields)!.SetValue(image, true);
                image.GetType().GetField("srcIdx", fields)!.SetValue(image, 0);
                image.GetType().GetField("ID", fields)!.SetValue(image, "drag-test");
                sizeField.SetValue(image, 0.1f);
                var resolution = Math.Max(texture.Width, texture.Height) / 100f;
                var initial = new Vector2(texture.Width, texture.Height) / (2 * resolution);
                pointField.SetValue(image, initial);
                managerType.GetMethod("ProcessCustom", fields)!.Invoke(manager, [0]);
                var mapper = Activator.CreateInstance(assembly.GetType("IMAK3Z0MB1EGAEM.menu.CustomMapper", true)!)!;
                DesktopPictures.LayoutMapper(mapper);
                var scale = (float)gameType.GetField("MENUSCALE", statics)!.GetValue(null)!;
                var center = ((Vector2)mapper.GetType().GetField("rOrig", fields)!.GetValue(mapper)! + new Vector2(0, -20)) * scale;
                void Mouse(Vector2 p, bool down) => DesktopPictures.UpdateMapperControls(mapper, p, down, default, 0.016f);
                void Touch(params TouchLocation[] touches) => DesktopPictures.UpdateMapperControls(mapper, null, false, default, 0.016f, new TouchCollection(touches));
                Mouse(center, true);
                Require(Point() == initial, "Pressing the preview jumped the crop.");
                Mouse(center + new Vector2(12, 8) * scale, true);
                Require(Point().X < initial.X && Point().Y < initial.Y,
                    $"Mouse drag did not move the image with the pointer: scene={dimensions}, photo={photo}, initial={initial}, actual={Point()}, size={sizeField.GetValue(image)}.");
                var moved = Point();
                // The legacy source-image handler must not also reposition this drag.
                var sourceCenter = (Vector2)mapper.GetType().GetField("orig", fields)!.GetValue(mapper)! * scale;
                Require(DesktopPictures.ClickMapperPicture(mapper, sourceCenter) && Point() == moved,
                    "Legacy click handling overwrote the preview drag.");
                Mouse(center, false);
                Mouse(center + new Vector2(20), false);
                Require(Point() == moved, "The crop kept moving after mouse release.");
                pointField.SetValue(image, initial);
                Touch(new TouchLocation(7, TouchLocationState.Pressed, center));
                Touch(new TouchLocation(8, TouchLocationState.Pressed, center - new Vector2(40)),
                    new TouchLocation(7, TouchLocationState.Moved, center + new Vector2(12, 8) * scale));
                Require(Vector2.Distance(Point(), moved) < 0.001f, "Touch drag differs from mouse or changed fingers.");
                Touch(new TouchLocation(7, TouchLocationState.Moved, center + new Vector2(1000)));
                var fitted = DesktopPictures.FitMaskGeometry(texture.Width, texture.Height, Point(), 0.1f);
                Require(Point() == fitted.Center && Point().X >= fitted.Radius && Point().Y >= fitted.Radius,
                    "Dragging beyond the circle exposed empty photo edges.");
                Touch(new TouchLocation(7, TouchLocationState.Released, center));
                var released = Point();
                Touch(new TouchLocation(8, TouchLocationState.Moved, center));
                Require(Point() == released, "A second held finger took over after release.");
                Touch();
                // Zoom remains independent of positioning and clamps at the slider ends.
                var slider = ((Vector2)mapper.GetType().GetField("rOrig", fields)!.GetValue(mapper)! + new Vector2(0, 60)) * scale;
                Mouse(slider, true);
                Mouse(slider + new Vector2(200 * scale, 0), true);
                Require((float)sizeField.GetValue(image)! == 0.5f, "Slider zoom stopped working after a preview drag.");
                Mouse(slider, false);
                // Rotation cancels the drag without turning it into a source edit.
                sizeField.SetValue(image, 0.1f);
                pointField.SetValue(image, initial);
                Mouse(center, true);
                scene.SetValue(null, (dimensions.Item1 + 100, dimensions.Item2));
                Mouse(center + new Vector2(20), true);
                Mouse(center + new Vector2(30), true);
                Require(Point() == initial && DesktopPictures.ClickMapperPicture(mapper, sourceCenter) && Point() == initial,
                    "Rotation did not cancel and consume the preview drag.");
                Mouse(center, false);
                owned.Remove(texture);
            }
            Console.WriteLine("CREATOR_DRAG_OK; mouse/touch parity; no press jump; finger capture; release; bounds; slider; portrait/landscape; square/portrait/landscape photos");
        }
        finally
        {
            DesktopPictures.UpdateMapperControls(null, null, false, default, 0);
            scene.SetValue(null, oldScene);
        }
    }
}
