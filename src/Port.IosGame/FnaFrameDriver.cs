using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Xna.Framework;

namespace Port.IosGame;

// The FNA version is pinned. Preserve Game.Run's initialization/exit lifecycle,
// but yield between frames to UIKit instead of nesting a blocking Game.Run loop
// inside SDL's UIApplication startup callback.
internal sealed class FnaFrameDriver(Game game) : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Initialize = Method("DoInitialize");
    private static readonly MethodInfo Begin = Method("BeginRun");
    private static readonly MethodInfo Register = Method("BeforeLoop");
    private static readonly MethodInfo End = Method("EndRun");
    private static readonly MethodInfo Unregister = Method("AfterLoop");
    private static readonly MethodInfo Exiting = typeof(Game).GetMethod("OnExiting", Private,
        null, [typeof(object), typeof(EventArgs)], null) ?? throw new MissingMethodException("FNA OnExiting");
    private static readonly FieldInfo Initialized = Field("hasInitialized", typeof(bool));
    private static readonly FieldInfo Timer = Field("gameTimer", typeof(Stopwatch));
    private static readonly FieldInfo Running = Field("RunApplication", typeof(bool));
    private bool started;
    private bool begun;
    private bool registered;
    private bool disposed;
    private bool inFrame;

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started) throw new InvalidOperationException("A game session can only start once.");
        started = true;
        Call(Initialize);
        Initialized.SetValue(game, true);
        Call(Begin);
        begun = true;
        Call(Register);
        registered = true;
        Timer.SetValue(game, Stopwatch.StartNew());
    }

    internal bool Step()
    {
        if (disposed) return false;
        if (!started || !registered) throw new InvalidOperationException("The game session has not started.");
        if (inFrame) throw new InvalidOperationException("A display callback reentered the game frame.");
        if (!(bool)Running.GetValue(game)!) return false;
        inFrame = true;
        try { game.RunOneFrame(); return (bool)Running.GetValue(game)!; }
        finally { inFrame = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var errors = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
        if (begun)
        {
            Attempt(() => Call(Exiting, game, EventArgs.Empty));
            Attempt(() => Call(End));
        }
        if (registered) Attempt(() => Call(Unregister));
        Attempt(game.Dispose);
        if (errors.Count > 0) throw new AggregateException("Game shutdown failed.", errors);
    }

    private void Call(MethodInfo method, params object[] args)
    {
        try { method.Invoke(game, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }
    private static MethodInfo Method(string name) => typeof(Game).GetMethod(name, Private,
        null, Type.EmptyTypes, null) ?? throw new MissingMethodException("Pinned FNA lifecycle: " + name);
    private static FieldInfo Field(string name, Type expected)
    {
        var field = typeof(Game).GetField(name, Private);
        return field?.FieldType == expected ? field : throw new MissingFieldException("Pinned FNA lifecycle: " + name);
    }
}
