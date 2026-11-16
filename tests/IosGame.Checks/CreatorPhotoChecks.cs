using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Port.Hooks;

internal static class CreatorPhotoChecks
{
    internal static void Run(string gamePath)
    {
        const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var assembly = Assembly.LoadFrom(Path.GetFullPath(gamePath));
        var game = assembly.GetType("ZombiesWP7.Game1", true)!;
        var mapperType = assembly.GetType("IMAK3Z0MB1EGAEM.menu.CustomMapper", true)!;
        var managerType = assembly.GetType("ZombiesWP7.custom.CustomMgr", true)!;
        var manager = Activator.CreateInstance(managerType)!;
        var sourceField = managerType.GetField("srcImages", fields)!;
        var sources = (IList)Activator.CreateInstance(sourceField.FieldType)!;
        sourceField.SetValue(manager, sources);
        var sourceType = sourceField.FieldType.GenericTypeArguments[0];
        var sourceConstructor = sourceType.GetConstructors().Single(c => c.GetParameters().Length == 2);
        var customField = game.GetField("custom", statics)!;
        var oldCustom = customField.GetValue(null);
        var scene = typeof(DesktopDisplay).GetField("sceneSize", statics)!;
        var oldScene = scene.GetValue(null);
        var directory = Path.Combine(Path.GetTempPath(), "z0mb1es-creator-" + Guid.NewGuid().ToString("N"));
        var original = directory + ".original.jpg";
        var oldPicker = DesktopPictures.PhotoSelectionRequested;
        var requests = 0;
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        try
        {
            Directory.CreateDirectory(directory);
            DesktopPictures.Configure(directory, managePhotos: true);
            DesktopPictures.PhotoSelectionRequested = () => { requests++; return Array.Empty<string>(); };
            customField.SetValue(null, manager);
            var mapper = Activator.CreateInstance(mapperType)!;
            var itemsField = mapperType.BaseType!.GetField("item", fields)!;
            object[] Items() => ((Array)itemsField.GetValue(mapper)!).Cast<object>().ToArray();
            string Label(object item) => (string)item.GetType().GetField("text", fields)!.GetValue(item)!;
            Vector2 Position(object item) => (Vector2)item.GetType().GetField("loc", fields)!.GetValue(item)!;
            void Click(string label) => mapperType.GetMethod("Click", fields)!.Invoke(mapper, [label]);

            mapperType.GetMethod("Init", fields)!.Invoke(mapper, null);
            DesktopPictures.LayoutMapper(mapper);
            Assert(Items().Select(Label).SequenceEqual(["Choose Photos"]), "Empty creator has no photo action or still shows source controls.");
            var emptyInfo = mapperType.BaseType.GetField("infos", fields)!.GetValue(mapper);
            Click("Choose Photos");
            DesktopPictures.LayoutMapper(mapper);
            Assert(requests == 1 && sources.Count == 0 && ReferenceEquals(emptyInfo, mapperType.BaseType.GetField("infos", fields)!.GetValue(mapper)),
                "Canceling photo selection changed the empty collection or lost its invitation.");

            // Simulate the native import callback replacing the source list while
            // the creator stays open. No graphics device is needed for menu checks.
            sources.Add(sourceConstructor.Invoke(["first", null]));
            sources.Add(sourceConstructor.Invoke(["second", null]));
            DesktopPictures.LayoutMapper(mapper);
            Assert(Items().Select(Label).SequenceEqual(["<", ">", "use", "Add Photos", "Remove Photo"]), "Imported photos did not activate creator controls immediately.");
            Click("Add Photos");
            Assert(requests == 2 && sources.Count == 2, "Add Photos did not route through the prepared game's click hook or cancellation cleared photos.");
            Click(">");
            Assert((int)mapperType.GetField("curPic", fields)!.GetValue(mapper)! == 1, "Existing source navigation stopped working.");
            foreach (var dimensions in new[] { (600, 360), (783, 360), (900, 360), (600, 800), (600, 1300) })
            {
                scene.SetValue(null, dimensions);
                DesktopPictures.LayoutMapper(mapper);
                var scale = (float)game.GetField("MENUSCALE", statics)!.GetValue(null)!;
                foreach (var item in Items())
                {
                    var position = Position(item) * scale;
                    Assert(position.X > 0 && position.X < dimensions.Item1 && position.Y > 0 && position.Y < dimensions.Item2,
                        "Creator action moved outside the rotated scene.");
                    Assert(Position(item) == (Vector2)item.GetType().GetField("tiltLoc", fields)!.GetValue(item)!, "Creator draw and touch positions differ after rotation.");
                }
                Assert(Vector2.Distance(Position(Items()[3]), Position(Items()[4])) >= 180, "Photo actions overlap each other.");
            }

            // Remove the final imported copy through the real click hook. Keep a
            // separate original outside the managed folder to prove it is retained.
            sources.Clear();
            sources.Add(sourceConstructor.Invoke(["last", null]));
            var copy = Path.Combine(directory, "photo.png");
            File.WriteAllText(copy, "imported copy");
            File.WriteAllText(original, "original");
            var customImage = ((Array)managerType.GetField("customImg", fields)!.GetValue(manager)!).GetValue(0)!;
            customImage.GetType().GetField("enabled", fields)!.SetValue(customImage, true);
            customImage.GetType().GetField("ID", fields)!.SetValue(customImage, "last");
            sourceType.GetField("imgIdx", fields)!.SetValue(sources[0], 0);
            var sourceFiles = (IDictionary)typeof(DesktopPictures).GetField("SourceFiles", statics)!.GetValue(null)!;
            sourceFiles["last"] = copy;
            mapperType.GetField("curPic", fields)!.SetValue(mapper, 99);
            DesktopPictures.LayoutMapper(mapper);
            Click("Remove Photo");
            Assert(!File.Exists(copy) && File.Exists(original) && sources != sourceField.GetValue(manager), "Removing a photo did not replace the collection or affected the original.");
            Assert(!(bool)customImage.GetType().GetField("enabled", fields)!.GetValue(customImage)!, "Removing a photo left its saved zombie mask enabled.");
            Assert(((IList)sourceField.GetValue(manager)!).Count == 0 && Items().Select(Label).SequenceEqual(["Choose Photos"]), "Removing the last photo left stale textures or controls.");
            Assert((int)mapperType.GetField("curPic", fields)!.GetValue(mapper)! == 0, "Empty collection retained an invalid source index.");
            Console.WriteLine("CREATOR_PHOTOS_OK; native picker click routing; empty/cancel; live import; navigation; rotation; remove final copy");
        }
        finally
        {
            DesktopPictures.PhotoSelectionRequested = oldPicker;
            DesktopPictures.Configure(null);
            customField.SetValue(null, oldCustom);
            scene.SetValue(null, oldScene);
            File.Delete(original);
            Directory.Delete(directory, true);
        }
    }
}
