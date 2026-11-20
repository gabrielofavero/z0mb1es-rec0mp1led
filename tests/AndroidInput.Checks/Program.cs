using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;

var input = new AndroidControllerState();
Check(!input.GetState().IsConnected, "No controller is fabricated before input.");
input.SetButton(1, Buttons.A, true);
Check(input.GetState().IsConnected && input.GetState().Buttons.A == ButtonState.Pressed, "A press connects player one.");
input.SetMotion(1, new Vector2(0.8f, -0.6f), new Vector2(-0.7f, 0.5f), 0.4f, 0.8f, -1, 1);
var state = input.GetState();
Check(state.Buttons.A == ButtonState.Pressed && state.ThumbSticks.Left.X > 0.5f &&
    state.ThumbSticks.Right.Y > 0.2f && state.Triggers.Right > state.Triggers.Left &&
    state.DPad.Left == ButtonState.Pressed && state.DPad.Down == ButtonState.Pressed,
    "Motion preserves buttons and supplies independent sticks, triggers and hats.");
input.SetButton(1, Buttons.A, false);
Check(input.GetState().Buttons.A == ButtonState.Released && input.GetState().ThumbSticks.Left.X > 0.5f,
    "Button release preserves held motion.");
input.SetButton(1, Buttons.DPadLeft, true);
input.SetMotion(1, Vector2.Zero, Vector2.Zero, 0, 0, 0, 0);
Check(input.GetState().DPad.Left == ButtonState.Pressed, "Releasing the hat does not release a D-pad key.");
input.SetButton(1, Buttons.DPadLeft, false);
Check(input.GetState().DPad.Left == ButtonState.Released, "Both D-pad sources release independently.");
input.SetButton(1, Buttons.LeftTrigger, true);
input.SetMotion(1, Vector2.Zero, Vector2.Zero, 0.5f, 0, 0, 0);
Check(input.GetState().Triggers.Left == 1, "Digital trigger press survives an analog motion event.");
input.SetButton(1, Buttons.LeftTrigger, false);
Check(input.GetState().Triggers.Left > 0 && input.GetState().Triggers.Left < 1, "Analog trigger survives digital release.");
input.SetButton(2, Buttons.Start, true);
Check(input.GetState().Buttons.Start == ButtonState.Pressed && input.GetState().Triggers.Left == 0,
    "Switching controllers clears the old controller's held state.");
input.SetButton(1, Buttons.A, false);
input.SetMotion(1, Vector2.Zero, Vector2.Zero, 0, 0, 0, 0);
input.Reset(1);
Check(input.GetState().Buttons.Start == ButtonState.Pressed, "Inactive device releases/removal do not steal player one.");
input.Reset(2);
Check(!input.GetState().IsConnected, "Removing the active controller clears its state.");
input.SetButton(3, Buttons.B, true);
input.Reset();
Check(!input.GetState().IsConnected && input.GetState().Buttons.B == ButtonState.Released,
    "Focus loss clears held buttons and disconnects the event adapter.");
input.SetMotion(3, Vector2.Zero, Vector2.Zero, 0, 0, 0.1f, 0);
Check(!input.GetState().IsConnected, "Idle device noise does not select a controller after reset.");
input.SetButton(-1, Buttons.A, true);
input.SetButton(-1, Buttons.A, false);
Check(input.GetState().IsConnected && input.GetState().Buttons.A == ButtonState.Released,
    "Virtual devices with negative IDs are accepted, and release retains a neutral controller state.");
input.Reset();
Near(AndroidControllerState.NormalizeAxis(0.05f, -1, 1, 0.1f), 0, "Hardware flat region");
Near(AndroidControllerState.NormalizeAxis(-1, -1, 1, 0), -1, "Signed stick minimum");
Near(AndroidControllerState.NormalizeAxis(255, 0, 255, 0), 1, "Unsigned stick maximum");
Near(AndroidControllerState.NormalizeAxis(0, -1, 1, 0, trigger: true), 0.5f, "Signed trigger midpoint");
Near(AndroidControllerState.NormalizeAxis(0.75f, 0, 1, 0, trigger: true), 0.75f, "Android trigger range");
Near(AndroidControllerState.NormalizeAxis(5, -1, 1, 0), 1, "Out-of-range clamping");
Near(AndroidControllerState.NormalizeAxis(float.NaN, -1, 1, 0), 0, "Invalid sample");
Near(AndroidControllerState.NormalizeAxis(1, 1, 1, 0), 0, "Invalid range");
Console.WriteLine("ANDROID_CONTROLLER_STATE_OK buttons sticks hats triggers device-switch disconnect focus-reset normalization");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < 0.001f, message);
