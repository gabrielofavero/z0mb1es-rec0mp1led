using CoreHaptics;
using AudioToolbox;
using Foundation;
using Port.Bridge;
using UIKit;

namespace Port.IosGame;

/// <summary>Device haptics for both iOS hosts; unsupported devices return quietly.</summary>
internal sealed class DeviceVibration : IVibrationOutput
{
    private static NSObject? resignObserver;
    private CHHapticEngine? engine;
    private ICHHapticPatternPlayer? player;
    private bool playbackReported;

    internal static void Configure()
    {
        GameVibration.DeviceOutput = new DeviceVibration();
        // The importer host pauses its display loop before the next SDL event
        // pump, so stop motors directly at UIKit's lifecycle boundary as well.
        resignObserver ??= UIApplication.Notifications.ObserveWillResignActive((_, _) => GameVibration.Stop());
    }

    public bool TryStart(TimeSpan duration)
    {
        try
        {
            return TryStartHaptics(duration);
        }
        catch (Exception error)
        {
            // Native interop failures must reach the same phone fallback as
            // NSError failures, rather than disappearing in the shared router.
            return Failed(error.ToString());
        }
    }

    private bool TryStartHaptics(TimeSpan duration)
    {
        if (!CHHapticEngine.GetHardwareCapabilities().SupportsHaptics)
            return TrySystemVibration();
        Stop();
        if (engine is null)
        {
            engine = new CHHapticEngine(out var creationError);
            if (creationError is not null) return Failed(creationError.LocalizedDescription);
            engine.PlaysHapticsOnly = true;
            engine.AutoShutdownEnabled = true;
        }
        // Starting each time also recovers after backgrounding, interruptions,
        // automatic shutdown, or a Core Haptics server reset. Players are rebuilt.
        if (!engine.Start(out var startError)) return Failed(startError?.LocalizedDescription);
        using var intensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 0.65f);
        using var sharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.4f);
        using var pulse = new CHHapticEvent(CHHapticEventType.HapticContinuous,
            [intensity, sharpness], 0, duration.TotalSeconds);
        using var pattern = new CHHapticPattern([pulse], Array.Empty<CHHapticDynamicParameter>(), out var patternError);
        if (patternError is not null) return Failed(patternError.LocalizedDescription);
        player = engine.CreatePlayer(pattern, out var playerError);
        if (playerError is not null || player is null) return Failed(playerError?.LocalizedDescription);
        if (!player.Start(0, out var playError)) return Failed(playError?.LocalizedDescription);
        ReportPlayback("core-haptics");
        return true;
    }

    private bool TrySystemVibration()
    {
        // iPads have no device motor. This fixed pulse is also a fallback when
        // supported Core Haptics hardware cannot start its engine or player.
        if (!UIDevice.CurrentDevice.Model.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase)) return false;
        SystemSound.Vibrate.PlaySystemSound();
        ReportPlayback("system-vibration");
        return true;
    }

    private void ReportPlayback(string output)
    {
        if (playbackReported) return;
        playbackReported = true;
        GameTrace.Write("device-vibration-playback=" + output);
    }

    public void Stop()
    {
        if (player is null) return;
        player.Stop(0, out _);
        player.Dispose();
        player = null;
    }

    private bool Failed(string? reason)
    {
        GameTrace.Write("device-vibration-unavailable=" + reason);
        // A broken player must not prevent the independent system pulse.
        var failedPlayer = player;
        var failedEngine = engine;
        player = null;
        engine = null;
        try
        {
            failedPlayer?.Stop(0, out _);
            failedPlayer?.Dispose();
            failedEngine?.Dispose();
        }
        catch (Exception error)
        {
            GameTrace.Write("device-vibration-cleanup-failed=" + error.Message);
        }
        return TrySystemVibration();
    }
}
