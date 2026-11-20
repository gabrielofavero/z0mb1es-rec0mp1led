using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

/// <summary>Uses Android's active controller as player one, retaining SDL as a fallback.</summary>
public static class AndroidGamePad
{
    public static AndroidControllerState Input { get; } = new();

    public static GamePadState GetState(PlayerIndex player)
    {
        var state = player == PlayerIndex.One ? Input.GetState() : default;
        return state.IsConnected ? state : Microsoft.Xna.Framework.Input.GamePad.GetState(player);
    }
}
