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

    internal static void Configure()
    {
        GameVibration.DeviceOutput = new DeviceVibration();
        // The importer host pauses its display loop before the next SDL event
        // pump, so stop motors directly at UIKit's lifecycle boundary as well.
        resignObserver ??= UIApplication.Notifications.ObserveWillResignActive((_, _) => GameVibration.Stop());
    }

    public bool TryStart(TimeSpan duration)
    {
        if (!CHHapticEngine.GetHardwareCapabilities().SupportsHaptics)
        {
            // Older iPhones still expose the system vibration pulse even when
            // they cannot play Core Haptics patterns. iPads have no device motor.
            if (!UIDevice.CurrentDevice.Model.StartsWith("iPhone", StringComparison.OrdinalIgnoreCase)) return false;
            SystemSound.Vibrate.PlaySystemSound();
            return true;
        }
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
        return true;
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
        Stop();
        engine?.Dispose();
        engine = null;
        return false;
    }
}
