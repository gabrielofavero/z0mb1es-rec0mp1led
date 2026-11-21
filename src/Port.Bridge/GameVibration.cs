using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using SDL3;

[assembly: InternalsVisibleTo("Bridge.Checks")]

namespace Port.Bridge;

/// <summary>A host-provided vibration motor. Android can supply its device implementation here.</summary>
public interface IVibrationOutput
{
    bool TryStart(TimeSpan duration);
    void Stop();
}

/// <summary>Routes the original phone vibration requests to the available hardware.</summary>
public static class GameVibration
{
    private static readonly VibrationRouter Router = new(new ControllerVibration());
    private static Game? game;
    private static Func<bool>? enabled;
    private static bool absenceReported;

    public static IVibrationOutput? DeviceOutput { get; set; }

    public static void Configure(Game runningGame, Func<bool> vibrationEnabled)
    {
        Stop();
        game = runningGame;
        enabled = vibrationEnabled;
        runningGame.Components.Add(new Monitor(runningGame));
        runningGame.Deactivated += StopForEvent;
        runningGame.Exiting += StopForEvent;
        runningGame.Disposed += StopForEvent;
    }

    internal static void Start(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        if (duration == TimeSpan.Zero) { Stop(); return; }
        if (game is null)
        {
            ReportUnavailable();
            return;
        }
        if (!game.IsActive || enabled?.Invoke() != true) { Stop(); return; }
        if (!Router.Start(duration, DeviceOutput)) ReportUnavailable();
    }

    public static void Stop() => Router.Stop();

    private static void StopForEvent(object? sender, EventArgs args) => Stop();

    private static void ReportUnavailable()
    {
        if (absenceReported) return;
        absenceReported = true;
        Trace.TraceInformation("VibrateController.Start(TimeSpan): no supported vibration output is available.");
    }

    private sealed class Monitor(Game runningGame) : GameComponent(runningGame)
    {
        public override void Update(GameTime gameTime)
        {
            if (!Game.IsActive || enabled?.Invoke() != true) Stop();
        }
    }
}

internal sealed class VibrationRouter(IVibrationOutput controller)
{
    private IVibrationOutput? active;

    internal bool Start(TimeSpan duration, IVibrationOutput? device)
    {
        Stop();
        if (TryStart(controller, duration)) { active = controller; return true; }
        if (device is not null && TryStart(device, duration)) { active = device; return true; }
        return false;
    }

    internal void Stop()
    {
        var output = active;
        active = null;
        try { output?.Stop(); }
        catch (Exception error) { Trace.TraceWarning("Vibration stop failed: " + error.Message); }
    }

    private static bool TryStart(IVibrationOutput output, TimeSpan duration)
    {
        try { return output.TryStart(duration); }
        catch (Exception error)
        {
            Trace.TraceWarning("Vibration unavailable: " + error.Message);
            try { output.Stop(); }
            catch (Exception stopError) { Trace.TraceWarning("Vibration cleanup failed: " + stopError.Message); }
            return false;
        }
    }
}

internal sealed class ControllerVibration : IVibrationOutput
{
    private readonly HashSet<uint> active = new();

    public bool TryStart(TimeSpan duration)
    {
        // Use SDL's timed rumble directly: the pinned FNA SetVibration API supplies
        // zero duration and does not expose the original phone effect's lifetime.
        var milliseconds = (uint)Math.Clamp(Math.Ceiling(duration.TotalMilliseconds), 1, uint.MaxValue);
        var ids = SDL.SDL_GetGamepads(out var count);
        try
        {
            for (var index = 0; index < count; index++)
            {
                var id = unchecked((uint)Marshal.ReadInt32(ids, index * sizeof(uint)));
                // FNA owns these handles and handles connection/disconnection events.
                var pad = SDL.SDL_GetGamepadFromID(id);
                if (pad != IntPtr.Zero && SDL.SDL_RumbleGamepad(pad, 42598, 42598, milliseconds))
                    active.Add(id);
            }
        }
        finally { SDL.SDL_free(ids); }
        return active.Count > 0;
    }

    public void Stop()
    {
        foreach (var id in active)
        {
            var pad = SDL.SDL_GetGamepadFromID(id);
            if (pad != IntPtr.Zero) SDL.SDL_RumbleGamepad(pad, 0, 0, 0);
        }
        active.Clear();
    }
}
