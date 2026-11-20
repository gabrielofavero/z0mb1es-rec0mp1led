using Android.Hardware.Input;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;

namespace Port.AndroidGame;

internal sealed class AndroidControllerInput : Java.Lang.Object, InputManager.IInputDeviceListener
{
    private readonly InputManager manager;
    private readonly HashSet<int> reported = [];

    internal AndroidControllerInput(InputManager manager)
    {
        this.manager = manager;
        manager.RegisterInputDeviceListener(this, new Handler(Looper.MainLooper!));
        foreach (var id in InputDevice.GetDeviceIds() ?? []) ReportDevice(id);
    }

    internal bool Process(KeyEvent e)
    {
        if (e.Action is not (KeyEventActions.Down or KeyEventActions.Up)) return false;
        // Some virtual gamepads tag button events as keyboard input. Explicit
        // gamepad keycodes are still unambiguous; keyboard arrows/Enter are not.
        if (!ControllerSource(e.Source) && !ControllerSource(e.Device?.Sources ?? 0) &&
            e.KeyCode is not (>= Keycode.ButtonA and <= Keycode.ButtonMode)) return false;
        var button = e.KeyCode switch
        {
            Keycode.ButtonA or Keycode.DpadCenter or Keycode.Enter => Buttons.A,
            Keycode.ButtonB => Buttons.B,
            Keycode.ButtonX => Buttons.X,
            Keycode.ButtonY => Buttons.Y,
            Keycode.ButtonL1 => Buttons.LeftShoulder,
            Keycode.ButtonR1 => Buttons.RightShoulder,
            Keycode.ButtonL2 => Buttons.LeftTrigger,
            Keycode.ButtonR2 => Buttons.RightTrigger,
            Keycode.ButtonThumbl => Buttons.LeftStick,
            Keycode.ButtonThumbr => Buttons.RightStick,
            Keycode.ButtonStart or Keycode.Menu => Buttons.Start,
            Keycode.ButtonSelect or Keycode.Back => Buttons.Back,
            Keycode.DpadUp => Buttons.DPadUp,
            Keycode.DpadDown => Buttons.DPadDown,
            Keycode.DpadLeft => Buttons.DPadLeft,
            Keycode.DpadRight => Buttons.DPadRight,
            _ => (Buttons)0
        };
        if (button == 0) return false;
        ReportEvent(e.DeviceId, e.Source);
        AndroidGamePad.Input.SetButton(e.DeviceId, button, e.Action == KeyEventActions.Down);
        return true;
    }

    internal bool Process(MotionEvent e)
    {
        if (!ControllerSource(e.Source) || e.ActionMasked != MotionEventActions.Move) return false;
        ReportEvent(e.DeviceId, e.Source);
        var rightX = !HasAxis(e, Axis.Z) && HasAxis(e, Axis.Rx) ? Axis.Rx : Axis.Z;
        var rightY = !HasAxis(e, Axis.Rz) && HasAxis(e, Axis.Ry) ? Axis.Ry : Axis.Rz;
        AndroidGamePad.Input.SetMotion(e.DeviceId,
            new Vector2(ReadAxis(e, Axis.X), -ReadAxis(e, Axis.Y)),
            new Vector2(ReadAxis(e, rightX), -ReadAxis(e, rightY)),
            ReadTrigger(e, Axis.Ltrigger, Axis.Brake), ReadTrigger(e, Axis.Rtrigger, Axis.Gas),
            e.GetAxisValue(Axis.HatX), e.GetAxisValue(Axis.HatY));
        return true;
    }

    private static bool HasAxis(MotionEvent e, Axis axis) => e.Device?.GetMotionRange(axis) is not null;

    private static float ReadTrigger(MotionEvent e, Axis primary, Axis alternative) =>
        HasAxis(e, primary) ? ReadAxis(e, primary, trigger: true) :
        HasAxis(e, alternative) ? ReadAxis(e, alternative, trigger: true) :
        Math.Max(ReadAxis(e, primary, trigger: true), ReadAxis(e, alternative, trigger: true));

    private static float ReadAxis(MotionEvent e, Axis axis, bool trigger = false)
    {
        var range = e.Device?.GetMotionRange(axis);
        return AndroidControllerState.NormalizeAxis(e.GetAxisValue(axis), range?.Min ?? (trigger ? 0 : -1),
            range?.Max ?? 1, range?.Flat ?? 0, trigger);
    }

    private static bool ControllerSource(InputSourceType source) =>
        (source & InputSourceType.Gamepad) == InputSourceType.Gamepad ||
        (source & InputSourceType.Joystick) == InputSourceType.Joystick ||
        (source & InputSourceType.Dpad) == InputSourceType.Dpad;

    private void ReportEvent(int id, InputSourceType source)
    {
        if (!reported.Add(id)) return;
        MainActivity.Log($"controller-events device={id} source=0x{(int)source:x} route=android-player-one");
    }

    private static void ReportDevice(int id)
    {
        var device = InputDevice.GetDevice(id);
        if (device is not null && ControllerSource(device.Sources))
            MainActivity.Log($"controller-device id={id} name={device.Name} sources=0x{(int)device.Sources:x}");
    }

    public void OnInputDeviceAdded(int deviceId) => ReportDevice(deviceId);
    public void OnInputDeviceChanged(int deviceId)
    {
        AndroidGamePad.Input.Reset(deviceId);
        ReportDevice(deviceId);
    }
    public void OnInputDeviceRemoved(int deviceId)
    {
        AndroidGamePad.Input.Reset(deviceId);
        reported.Remove(deviceId);
        MainActivity.Log($"controller-removed device={deviceId}");
    }

    internal void Stop()
    {
        manager.UnregisterInputDeviceListener(this);
        AndroidGamePad.Input.Reset();
    }
}
