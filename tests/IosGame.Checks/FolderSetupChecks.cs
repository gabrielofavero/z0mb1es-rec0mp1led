using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Port.Art;
using Port.IosGame;
using Port.Prepare;
using Port.Hooks;
using SDL3;

internal static class FolderSetupChecks
{
    internal static void Flow(string xap, string inputs, string root, string audio, string normal, string viking)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new Exception("Check output must be new.");
        var imports = Path.Combine(root, "Imports");
        var saves = Path.Combine(root, "Saves");
        Directory.CreateDirectory(imports); Directory.CreateDirectory(saves);
        var save = Path.Combine(saves, "savegame");
        File.WriteAllText(save, "keep player progress");
        var preferencePath = Path.Combine(saves, "assets.json");
        File.WriteAllText(preferencePath, """
            {"Format":1,"Normal":"original","Viking":"original","Endless":"original","FutureChoice":"keep"}
            """);
        var phoneCopy = Path.Combine(imports, "phone.XAP");
        var normalCopy = Path.Combine(imports, "zombies-package");
        var vikingCopy = Path.Combine(imports, "viking-package");
        File.Copy(xap, phoneCopy); File.Copy(normal, normalCopy); File.Copy(viking, vikingCopy);
        File.WriteAllText(Path.Combine(imports, "incomplete.xap"), "copy incomplete");
        File.WriteAllText(Path.Combine(imports, "README.txt"), ImportFolder.Instructions);
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(imports, "linked.xap"), Path.GetFullPath(phoneCopy));
        string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var originals = new[] { xap, normal, viking }.Select(Hash).ToArray();
        using var converter = new CheckAudio(audio);
        var preparations = 0;
        var store = new PersistentAssetStore(Path.Combine(root, "Assets"), saves, (package, work, log, token) =>
        {
            preparations++;
            return IosGamePreparation.Prepare(package, work, inputs, converter, log, token);
        }, new PortableArtImporter());
        var commits = 0;
        using (var setup = new FolderSetup(imports, saves, store, null, true, Console.WriteLine, () => commits++))
        {
            Require(setup.Page == SetupPage.Warning && setup.Files.Phone.Count == 0 && !setup.CanImport, "Setup bypassed the warning.");
            setup.ReduceFlashing = true;
            setup.ContinueWarning(); Wait(setup);
            Require(FirstLaunchWarning.IsAcknowledged(saves) && setup.Page == SetupPage.Files, "Continue did not reach Files.");
            Require(setup.Files.Phone.Count == 1 && setup.Phone?.Path == phoneCopy && setup.Files.Notes.Count == 1,
                "Mixed-case phone source, unfinished copy or link classification failed.");
            Require(setup.Files.Normal.Count == 1 && setup.Files.Viking.Count == 1 && setup.Normal?.Kind == "i-maed-360" && setup.Viking?.Kind == "time-viking-360",
                "Verified artwork was not selected automatically for both games.");
            setup.Cycle("normal"); setup.Rescan(); Wait(setup);
            Require(setup.Normal is null && setup.Viking is not null, "Rescan overwrote a deliberate artwork choice.");
            setup.Cycle("normal");
            setup.Import(); setup.Cancel(); Wait(setup);
            Require(store.TryOpen() is null && setup.Active is null && !setup.LaunchRequested && commits == 0, "Canceled first import activated data.");
            setup.Import(); Wait(setup);
            Require(setup.LaunchRequested && setup.Active is not null && store.TryOpen() is not null, "Files import did not request game handoff.");
            Require(commits == 1 && JsonNode.Parse(File.ReadAllText(preferencePath))!["FutureChoice"]!.GetValue<string>() == "keep",
                "Import completion or preference merge failed.");
            DesktopStorage.Configure(saves);
            AssetChoices.Initialize(setup.Active!.ArtRoot, null, null, null);
            Require(AssetChoices.NormalLabel == "ENHANCED" && AssetChoices.VikingLabel == "ENHANCED" && AssetChoices.EndlessLabel == "ENHANCED",
                "Imported artwork is unavailable or stale Original preferences still override the selection.");
            Require(File.ReadAllText(save) == "keep player progress", "Import changed the save.");
            Require(File.Exists(phoneCopy) && File.Exists(normalCopy) && File.Exists(vikingCopy), "Import deleted a source.");
        }
        var count = preparations;
        File.Delete(phoneCopy);
        var preferences = JsonNode.Parse(File.ReadAllText(preferencePath))!;
        preferences["Viking"] = "original";
        File.WriteAllText(preferencePath, preferences.ToJsonString());
        using (var setup = new FolderSetup(imports, saves, store, store.TryOpen(), true, Console.WriteLine))
        {
            Wait(setup);
            Require(setup.Page == SetupPage.Files && setup.Phone is null && setup.CanPlay, "Reimport required the original XAP.");
            setup.Cycle("viking"); setup.Import(); Wait(setup);
            Require(setup.LaunchRequested && preparations == count, "Artwork-only reimport prepared game code again.");
            Require(JsonNode.Parse(File.ReadAllText(preferencePath))!["Viking"]!.GetValue<string>() == "original",
                "Reimport changed a mode whose source was left on Keep.");
        }
        var manifest = File.ReadAllBytes(Path.Combine(root, "Assets/current/asset-manifest.json"));
        using (var setup = new FolderSetup(imports, saves, store, store.TryOpen(), true, Console.WriteLine))
        {
            Wait(setup);
            File.WriteAllText(normalCopy, "changed after scanning");
            setup.Import(); Wait(setup);
            Require(!setup.LaunchRequested && setup.Active is not null && manifest.SequenceEqual(File.ReadAllBytes(
                Path.Combine(root, "Assets/current/asset-manifest.json"))), "Changed source replaced the active install.");
        }
        var requested = true;
        using (var setup = new FolderSetup(imports, saves, store, store.TryOpen(), false, Console.WriteLine, () => requested = false))
        {
            Wait(setup);
            Require(!setup.CanPlay && setup.Viking is not null, "Reimport allowed a loaded game assembly to relaunch.");
            setup.Import(); Wait(setup);
            Require(!requested && setup.Page == SetupPage.Restart && !setup.LaunchRequested,
                "Direct reimport did not clear its request or preserve the fresh-launch boundary.");
        }
        File.Delete(normalCopy); File.Delete(vikingCopy);
        using (var setup = new FolderSetup(imports, saves, store, store.TryOpen(), true, Console.WriteLine))
        {
            Wait(setup); setup.Play();
            Require(setup.LaunchRequested && !setup.CanImport, "Source-free restart failed.");
        }
        Require(originals.SequenceEqual(new[] { xap, normal, viking }.Select(Hash)) && File.ReadAllText(save) == "keep player progress",
            "Setup modified original packages or saves.");
        PickerFlow(root, saves, store, xap, normal, viking);
        Require(originals.SequenceEqual(new[] { xap, normal, viking }.Select(Hash)) && File.ReadAllText(save) == "keep player progress",
            "Picker modified original packages or saves.");
        Console.WriteLine("FILES_SETUP_FLOW_OK warning; real sources; canceled/failed import; artwork-only reimport; source-free restart; sources and saves preserved");
    }

    private static void PickerFlow(string root, string saves, PersistentAssetStore store, string xap, string normal, string viking)
    {
        var staging = Path.Combine(root, "PickerStaging");
        string ProviderCopy(string source)
        {
            var path = Path.Combine(root, Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            File.Copy(source, path);
            return path;
        }
        using (var setup = new FolderSetup(staging, saves, store, null, true, Console.WriteLine, usesPicker: true))
        {
            Require(setup.Page == SetupPage.Files && !setup.CanImport, "Picker unexpectedly scanned public Imports.");
            setup.TakePickedFile("phone", ProviderCopy(xap)); setup.Cancel(); Wait(setup);
            Require(setup.Phone is null && !setup.CanImport, "Canceled picker verification selected a file.");
            setup.TakePickedFile("phone", ProviderCopy(xap)); Wait(setup);
            var previous = setup.Phone;
            Require(previous is not null && previous.Path.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                "Picker did not take ownership of its temporary copy.");
            setup.TakePickedFile("phone", ProviderCopy(viking)); Wait(setup);
            Require(setup.Phone == previous && File.Exists(previous!.Path), "Wrong-slot selection replaced the verified phone package.");
            setup.TakePickedFile("normal", ProviderCopy(normal)); Wait(setup);
            var replaced = setup.Normal!.Path;
            setup.TakePickedFile("normal", ProviderCopy(normal)); Wait(setup);
            Require(!File.Exists(replaced) && setup.Normal is not null, "Replacement retained an obsolete staging copy.");
            setup.TakePickedFile("viking", ProviderCopy(viking)); Wait(setup);
            setup.ClearPickedFile("viking");
            Require(setup.Viking is null, "Clear did not deselect optional artwork.");
            setup.TakePickedFile("viking", ProviderCopy(viking)); Wait(setup);
            setup.Import(); Wait(setup);
            Require(setup.LaunchRequested && setup.Active is not null && !Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Any(),
                "Successful picker import retained source-package copies.");
            DesktopStorage.Configure(saves);
            AssetChoices.Initialize(setup.Active!.ArtRoot, null, null, null);
            Require(AssetChoices.NormalLabel == "ENHANCED" && AssetChoices.VikingLabel == "ENHANCED" && AssetChoices.EndlessLabel == "ENHANCED",
                "Picker artwork did not activate Enhanced.");
        }
        Require(!Directory.Exists(staging), "Picker disposal retained staging.");
        using (var setup = new FolderSetup(staging, saves, store, store.TryOpen(), false, Console.WriteLine, usesPicker: true))
        {
            Require(!setup.CanPlay && !setup.CanImport, "Reimport picker did not preserve its fresh-launch boundary.");
            setup.TakePickedFile("normal", ProviderCopy(normal)); Wait(setup);
            setup.Import(); Wait(setup);
            Require(setup.Page == SetupPage.Restart && !setup.LaunchRequested && !Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Any(),
                "Artwork-only picker reimport failed or retained packages.");
        }
        using (var setup = new FolderSetup(staging, saves, store, store.TryOpen(), true, Console.WriteLine, usesPicker: true))
        {
            setup.TakePickedFile("normal", ProviderCopy(normal)); Wait(setup);
            Require(setup.CanPlay, "Picker prevented source-free installed play.");
        }
        Require(!Directory.Exists(staging), "Abandoned picker selection was retained after disposal.");
        Console.WriteLine("PACKAGE_PICKER_FLOW_OK canceled/invalid selection; replacement; clear; full import; Enhanced; artwork-only repair; temporary cleanup; original sources preserved");
    }

    private static void Wait(FolderSetup setup)
    {
        var timeout = Stopwatch.StartNew();
        while (setup.Page == SetupPage.Working && timeout.Elapsed < TimeSpan.FromMinutes(3))
        { setup.Poll(); Thread.Sleep(10); }
        Require(setup.Page != SetupPage.Working, "Setup operation timed out.");
    }

    internal static void Render(string activeRoot, string nativeDirectory, string output)
    {
        FrameDriverChecks.ResolveNative(nativeDirectory);
        Directory.CreateDirectory(output);
        var root = Path.Combine(Path.GetTempPath(), "z0mb1es-files-ui-" + Guid.NewGuid().ToString("N"));
        var saves = Path.Combine(root, "Saves");
        var active = new ActiveAssetSet(activeRoot, Path.Combine(activeRoot, "game/ZombiesWP7.dll"),
            Path.Combine(activeRoot, "content"), Path.Combine(activeRoot, "optional-art"), "test");
        var store = new PersistentAssetStore(Path.Combine(root, "Assets"), saves, (_, _, _, _) => throw new Exception("Unexpected preparation."));
        try
        {
            using (var setup = new FolderSetup(Path.Combine(root, "Picker"), saves, store, active, true, Console.WriteLine, usesPicker: true))
            using (var game = new FolderInstallerGame(setup, false, _ => new Vector4(0.04f, 0.04f, 0.04f, 0.04f), Console.WriteLine))
            {
                var picked = new List<string>();
                game.PickFile = slot => picked.Add(slot);
                game.Components.Add(new RenderMonitor(game, setup, saves, output, picked));
                game.Run();
                Require(setup.LaunchRequested, "Setup did not hand off from the Play button.");
            }
            // Exercise a second real FNA window/device after setup disposal.
            FrameDriverChecks.Run(activeRoot, nativeDirectory, resolveNative: false);
            using (var repair = new FolderSetup(Path.Combine(root, "Imports"), saves, store, active, false, Console.WriteLine))
            using (var screen = new FolderInstallerGame(repair, false, _ => Vector4.Zero, Console.WriteLine))
            {
                screen.Components.Add(new ReimportMonitor(screen, repair));
                screen.Run();
            }
            Console.WriteLine("FILES_SETUP_RENDER_OK portrait/landscape; warning/text/buttons; safe insets; scroll/drag/tap; Continue; source-free Play; setup-to-original-to-reimport windows");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class ReimportMonitor(Game game, FolderSetup setup) : DrawableGameComponent(game)
    {
        private int frames;
        public override void Draw(GameTime gameTime)
        {
            if (++frames == 30)
            {
                var pp = GraphicsDevice.PresentationParameters;
                var pixels = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
                GraphicsDevice.GetBackBufferData(pixels);
                Require(pixels.Count(c => c.R > 220 && c.G > 220 && c.B > 220) > 200 && setup.Page == SetupPage.Files && !setup.CanPlay,
                    "Reimport setup failed after disposing the original game.");
                Game.Exit();
            }
            base.Draw(gameTime);
        }
    }

    private sealed class RenderMonitor(FolderInstallerGame game, FolderSetup setup, string saves, string output, List<string> picked) : DrawableGameComponent(game)
    {
        private int frame;
        private bool warningContinued;
        private void Capture(string name)
        {
            var pp = GraphicsDevice.PresentationParameters;
            var pixels = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
            GraphicsDevice.GetBackBufferData(pixels);
            Require(pixels.Count(c => c.R > 220 && c.G > 220 && c.B > 220) > 800, "Setup text did not render.");
            using var texture = new Texture2D(GraphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
            texture.SetData(pixels);
            using var file = File.Create(Path.Combine(output, name + ".png"));
            texture.SaveAsPng(file, texture.Width, texture.Height);
        }
        private Vector2 Center(string id)
        {
            var bounds = game.Rows.Single(r => r.Id == id).Bounds;
            var pp = GraphicsDevice.PresentationParameters;
            var scale = pp.BackBufferWidth / (pp.BackBufferWidth > pp.BackBufferHeight ? 960f : 480f);
            return new Vector2(bounds.Center.X, bounds.Center.Y - game.ScrollOffset) * scale;
        }
        private void Tap(string id)
        {
            var p = Center(id); game.Pointer(p, true); game.Pointer(p, false);
        }
        public override void Draw(GameTime gameTime)
        {
            frame++;
            if (frame == 10)
            {
                Capture("warning-landscape");
                Require(SDL.SDL_SetWindowSize(game.Window.Handle, 414, 900), "Portrait resize failed.");
            }
            if (frame == 20) { Capture("warning-portrait"); game.Scroll(10000); }
            if (frame == 25)
            {
                Capture("warning-controls");
                var before = setup.ReduceFlashing;
                var p = Center("reduce");
                game.Pointer(p, true); game.Pointer(p - new Vector2(0, 60), true); game.Pointer(p - new Vector2(0, 60), false);
                Require(setup.ReduceFlashing == before, "Scroll drag activated the checkbox.");
                game.Scroll(10000);
            }
            if (frame == 27)
            {
                Tap("reduce");
                Require(setup.ReduceFlashing, "Checkbox tap did not toggle the choice.");
                Tap("continue");
                Require(FirstLaunchWarning.IsAcknowledged(saves), "Continue did not acknowledge the warning.");
                warningContinued = true;
            }
            if (frame > 35 && warningContinued && setup.Page == SetupPage.Files)
            {
                Require(game.Rows.All(row => row.Id != "rescan"), "Picker still shows Rescan.");
                game.Scroll(0);
                Tap("phone"); Tap("normal"); Tap("viking");
                Require(picked.SequenceEqual(new[] { "phone", "normal", "viking" }), "Package rows did not request their file picker.");
                Capture("files-portrait"); game.Scroll(10000); warningContinued = false;
            }
            if (frame > 40 && setup.CanPlay) Tap("play");
            if (frame > 150) throw new Exception("Setup did not finish its UI flow.");
            base.Draw(gameTime);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
