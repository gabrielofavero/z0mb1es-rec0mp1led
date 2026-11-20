using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

/// <summary>Combines Android button and motion events into one player's state.</summary>
public sealed class AndroidControllerState
{
    private readonly object gate = new();
    private int? device;
    private Buttons buttons;
    private Buttons hats;
    private Vector2 left;
    private Vector2 right;
    private float leftTrigger;
    private float rightTrigger;

    public GamePadState GetState()
    {
        lock (gate)
            return device is null ? default : new GamePadState(left, right,
                Math.Max(leftTrigger, (buttons & Buttons.LeftTrigger) != 0 ? 1f : 0f),
                Math.Max(rightTrigger, (buttons & Buttons.RightTrigger) != 0 ? 1f : 0f), buttons | hats);
    }

    public void SetButton(int deviceId, Buttons button, bool pressed)
    {
        lock (gate)
        {
            // Releases from an old controller must not steal player one back.
            if (device != deviceId && !pressed) return;
            Select(deviceId);
            if (pressed) buttons |= button;
            else buttons &= ~button;
        }
    }

    public void SetMotion(int deviceId, Vector2 leftStick, Vector2 rightStick,
        float triggerLeft, float triggerRight, float hatX, float hatY)
    {
        lock (gate)
        {
            var active = leftStick.LengthSquared() > 0.04f || rightStick.LengthSquared() > 0.04f ||
                triggerLeft > 0.1f || triggerRight > 0.1f || Math.Abs(hatX) > 0.5f || Math.Abs(hatY) > 0.5f;
            if (device != deviceId && !active) return;
            Select(deviceId);
            left = leftStick;
            right = rightStick;
            leftTrigger = triggerLeft;
            rightTrigger = triggerRight;
            hats = (hatX < -0.5f ? Buttons.DPadLeft : hatX > 0.5f ? Buttons.DPadRight : 0) |
                (hatY < -0.5f ? Buttons.DPadUp : hatY > 0.5f ? Buttons.DPadDown : 0);
        }
    }

    public void Reset(int? removedDevice = null)
    {
        lock (gate)
        {
            if (removedDevice is not null && device != removedDevice) return;
            Clear();
            device = null;
        }
    }

    public static float NormalizeAxis(float value, float minimum, float maximum, float flat, bool trigger = false)
    {
        if (!float.IsFinite(value) || !float.IsFinite(minimum) || !float.IsFinite(maximum) || maximum <= minimum)
            return 0;
        var center = trigger ? minimum : (minimum + maximum) / 2f;
        if (Math.Abs(value - center) <= Math.Max(0, flat)) return 0;
        var scale = trigger ? maximum - minimum : (maximum - minimum) / 2f;
        return Math.Clamp((value - center) / scale, trigger ? 0 : -1, 1);
    }

    private void Select(int deviceId)
    {
        if (device == deviceId) return;
        Clear();
        device = deviceId;
    }

    private void Clear()
    {
        buttons = hats = 0;
        left = right = Vector2.Zero;
        leftTrigger = rightTrigger = 0;
    }
}
