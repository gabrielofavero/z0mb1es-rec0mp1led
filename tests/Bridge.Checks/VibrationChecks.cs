using Microsoft.Devices;
using Port.Bridge;
using System.Runtime.InteropServices;
using SDL3;

internal static class VibrationChecks
{
    internal static void Run()
    {
        var controller = new Output();
        var device = new Output();
        var router = new VibrationRouter(controller);
        var duration = TimeSpan.FromMilliseconds(250);
        if (!router.Start(duration, device) || controller.Duration != duration || device.Starts != 0)
            throw new Exception("Controller rumble was not preferred over device vibration.");
        router.Start(TimeSpan.FromSeconds(1), device);
        if (controller.Stops != 1 || controller.Duration != TimeSpan.FromSeconds(1))
            throw new Exception("A repeated vibration request did not replace the previous effect.");
        controller.Available = false;
        if (!router.Start(duration, device) || device.Duration != duration || controller.Stops != 2)
            throw new Exception("Disconnecting or unsupported controller did not fall back to device vibration.");
        router.Stop();
        router.Stop();
        if (device.Stops != 1) throw new Exception("Feedback cancellation was not idempotent.");
        controller.Throws = true;
        if (!router.Start(duration, device) || device.Starts != 2)
            throw new Exception("A controller failure prevented device haptics.");
        router.Stop();
        controller.Throws = false;
        device.Available = false;
        if (router.Start(duration, device) || router.Start(duration, null))
            throw new Exception("Unsupported vibration hardware was reported as available.");
        controller.Available = true;
        if (!router.Start(duration, device)) throw new Exception("A reconnected controller was not used.");
        router.Stop();
        try
        {
            VibrateController.Default.Start(TimeSpan.FromMilliseconds(-1));
            throw new Exception("A negative vibration duration was accepted.");
        }
        catch (ArgumentOutOfRangeException) { }
        VibrateController.Default.Start(TimeSpan.Zero);
        CheckNativeController();
        Console.WriteLine("VIBRATION_CHECKS_OK controller preference, durations, replacement, fallback, cancellation, reconnection and unavailable hardware");
    }

    private static void CheckNativeController()
    {
        if (!SDL.SDL_InitSubSystem(SDL.SDL_InitFlags.SDL_INIT_GAMEPAD)) throw new Exception(SDL.SDL_GetError());
        ushort lowMotor = 0, highMotor = 0;
        RumbleCallback callback = (_, low, high) => { lowMotor = low; highMotor = high; return 1; };
        var descriptor = new SDL.SDL_VirtualJoystickDesc
        {
            version = (uint)Marshal.SizeOf<SDL.SDL_VirtualJoystickDesc>(),
            type = (ushort)SDL.SDL_JoystickType.SDL_JOYSTICK_TYPE_GAMEPAD,
            naxes = 6, nbuttons = 15,
            Rumble = Marshal.GetFunctionPointerForDelegate(callback)
        };
        var id = SDL.SDL_AttachVirtualJoystick(ref descriptor);
        if (id == 0) throw new Exception(SDL.SDL_GetError());
        var pad = SDL.SDL_OpenGamepad(id);
        var output = new ControllerVibration();
        try
        {
            if (pad == IntPtr.Zero || !output.TryStart(TimeSpan.FromMilliseconds(40)) || lowMotor == 0 || highMotor == 0)
                throw new Exception("Timed rumble did not reach the SDL controller motors: " + SDL.SDL_GetError());
            Thread.Sleep(80);
            SDL.SDL_UpdateJoysticks();
            if (lowMotor != 0 || highMotor != 0) throw new Exception("Controller rumble did not expire.");
            if (!output.TryStart(TimeSpan.FromSeconds(1))) throw new Exception("Controller did not rumble again.");
            output.Stop();
            if (lowMotor != 0 || highMotor != 0) throw new Exception("Controller cancellation did not stop both motors.");
            output.TryStart(TimeSpan.FromSeconds(1));
            SDL.SDL_DetachVirtualJoystick(id);
            output.Stop();
        }
        finally
        {
            output.Stop();
            if (pad != IntPtr.Zero) SDL.SDL_CloseGamepad(pad);
            if (SDL.SDL_IsJoystickVirtual(id)) SDL.SDL_DetachVirtualJoystick(id);
            SDL.SDL_QuitSubSystem(SDL.SDL_InitFlags.SDL_INIT_GAMEPAD);
            GC.KeepAlive(callback);
        }
        Console.WriteLine("VIBRATION_SDL_OK timed motor output, expiry, cancellation and disconnect");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte RumbleCallback(IntPtr userData, ushort low, ushort high);

    private sealed class Output : IVibrationOutput
    {
        internal bool Available = true;
        internal bool Throws;
        internal int Starts;
        internal int Stops;
        internal TimeSpan Duration;
        public bool TryStart(TimeSpan duration)
        {
            Starts++;
            if (Throws) throw new InvalidOperationException("Simulated device failure");
            Duration = duration;
            return Available;
        }
        public void Stop() => Stops++;
    }
}
