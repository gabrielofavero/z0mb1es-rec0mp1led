using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Runtime.InteropServices;

int? exitAfterFrames = null;
if (args.Length != 0)
{
    if (args.Length != 2 || args[0] != "--frames" || !int.TryParse(args[1], out var frames) || frames < 1)
    {
        Console.Error.WriteLine("Usage: Port.Smoke [--frames <positive count>]");
        return 2;
    }
    exitAfterFrames = frames;
}

foreach (var file in new[] { "SDL3.dll", "FNA3D.dll", "FAudio.dll", "libtheorafile.dll", "D3D12/D3D12Core.dll" })
{
    var path = Path.Combine(AppContext.BaseDirectory, file);
    if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var handle))
    {
        Console.Error.WriteLine($"SMOKE_FAIL native dependency unavailable: {file} ({path})");
        return 1;
    }
    NativeLibrary.Free(handle);
}

try
{
    using var game = new SmokeGame(exitAfterFrames);
    game.Run();
    Console.WriteLine($"SMOKE_OK frames={game.DrawnFrames}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"SMOKE_FAIL {error}");
    if (IsNativeLoadFailure(error))
        Console.Error.WriteLine("Native-load failure: check the pinned win-x64 DLLs beside Port.Smoke.exe.");
    return 1;
}

static bool IsNativeLoadFailure(Exception error) =>
    error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException ||
    (error.InnerException is not null && IsNativeLoadFailure(error.InnerException));

internal sealed class SmokeGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly int? exitAfterFrames;
    public int DrawnFrames { get; private set; }

    public SmokeGame(int? exitAfterFrames)
    {
        this.exitAfterFrames = exitAfterFrames;
        graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 360 };
        IsMouseVisible = true;
        Window.Title = "FNA authored smoke test";
    }

    protected override void Initialize()
    {
        base.Initialize();
        Console.WriteLine($"SMOKE_WINDOW {GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}");
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape) ||
            (exitAfterFrames is int limit && DrawnFrames >= limit))
            Exit();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        DrawnFrames++;
        if (DrawnFrames == 1) Console.WriteLine("SMOKE_FRAME cleared");
        base.Draw(gameTime);
    }
}
