using Microsoft.Xna.Framework;

namespace Port.Hooks;

/// <summary>Controller focus colors for the original menus.</summary>
public static class MenuVisuals
{
    // The original renderer generates a new random tint for these calls every frame.
    // Keep their original alpha so translucent game effects remain translucent.
    public static Color Steady(Color original) => DesktopDisplay.ReduceFlashing
        ? new Color(0.75f, 0.75f, 0.75f, original.A / 255f)
        : original;

    public static Color SteadySpawn(Color original) => DesktopDisplay.ReduceFlashing
        ? new Color(0.8f, 0.15f, 0.15f, original.A / 255f)
        : original;

    public static int FlashCharacter(int original) => DesktopDisplay.ReduceFlashing ? -1 : original;

    // Used only for the phone game's Metal background selection in MapMan.DrawMap.
    public static int SteadyMetalBeat(int original) => DesktopDisplay.ReduceFlashing ? 0 : original;

    public static Color Highlight(Color original, object item)
    {
        if (!DesktopInput.IsFocusedMenuItem(item)) return original;
        if (DesktopDisplay.ReduceFlashing) return new Color(0.5f, 0.5f, 0.5f, original.A / 255f);
        return new Color(
            0.4f + 0.6f * Random.Shared.NextSingle(),
            0.4f + 0.6f * Random.Shared.NextSingle(),
            0.4f + 0.6f * Random.Shared.NextSingle(),
            original.A / 255f);
    }
}
