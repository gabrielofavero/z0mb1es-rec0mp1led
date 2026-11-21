using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Port.AndroidGame;
using Port.IosGame;
using Port.Prepare;

internal static class PickerCallbackChecks
{
    internal static void Run(string nativeDirectory)
    {
        FrameDriverChecks.ResolveNative(nativeDirectory);
        var root = Path.Combine(Path.GetTempPath(), "z0mb1es-picker-" + Guid.NewGuid().ToString("N"));
        var saves = Path.Combine(root, "Saves");
        var store = new PersistentAssetStore(Path.Combine(root, "Assets"), saves,
            (_, _, _, _) => throw new Exception("Unexpected preparation."));
        try
        {
            using var setup = new FolderSetup(Path.Combine(root, "picks"), saves, store,
                null, true, Console.WriteLine, usesPicker: true);
            setup.ContinueWarning();
            using var game = new FolderInstallerGame(setup, false, _ => Vector4.Zero, Console.WriteLine);
            var pending = new ConcurrentQueue<Action>();
            game.Components.Add(new PendingActions(game, pending));
            game.Components.Add(new Monitor(game, setup, pending, root));
            game.Run();
            Console.WriteLine("PICKER_CALLBACKS_OK canceled picker; selected file validation; copy failure; picker reopens");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Monitor(FolderInstallerGame game, FolderSetup setup,
        ConcurrentQueue<Action> pending, string root) : DrawableGameComponent(game)
    {
        private int frames;
        private int stage;
        private bool delivered;

        private void Open(Action completion)
        {
            game.PickerOpen = true;
            delivered = false;
            pending.Enqueue(() => { completion(); game.PickerOpen = false; delivered = true; });
        }

        public override void Draw(GameTime gameTime)
        {
            if (++frames > 180) throw new Exception("Picker callback stalled while PickerOpen was set.");
            if (stage == 0)
            {
                Open(() => { }); // Android's canceled-picker result.
                stage = 1;
            }
            else if (stage == 1 && delivered)
            {
                Require(!game.PickerOpen && setup.Page == SetupPage.Files, "Cancel did not restore the Files page.");
                var copy = Path.Combine(root, "phone.xap");
                File.WriteAllText(copy, "invalid package");
                Open(() => setup.TakePickedFile("phone", copy));
                stage = 2;
            }
            else if (stage == 2 && delivered && setup.Page == SetupPage.Files)
            {
                Require(!game.PickerOpen && setup.Phone is null && setup.Status.Length > 0,
                    "Selected file did not reach validation and recover from rejection.");
                Open(() => setup.SetStatus("Could not copy that file. Try again."));
                stage = 3;
            }
            else if (stage == 3 && delivered)
            {
                Require(!game.PickerOpen && setup.Status == "Could not copy that file. Try again.",
                    "Copy failure left the picker blocked.");
                Open(() => { });
                stage = 4;
            }
            else if (stage == 4 && delivered)
            {
                Require(!game.PickerOpen, "Picker could not reopen after completion.");
                game.Exit();
            }
            base.Draw(gameTime);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
