using Microsoft.Xna.Framework;

namespace Port.Hooks;

/// <summary>Controller focus colors for the original menus.</summary>
public static class MenuVisuals
{
    public static Color Highlight(Color original, object item)
    {
        if (!DesktopInput.IsFocusedMenuItem(item)) return original;
        if (DesktopDisplay.ReduceFlashing) return new Color(0.85f, 0.85f, 0.85f, original.A / 255f);
        return new Color(
            0.4f + 0.6f * Random.Shared.NextSingle(),
            0.4f + 0.6f * Random.Shared.NextSingle(),
            0.4f + 0.6f * Random.Shared.NextSingle(),
            original.A / 255f);
    }
}
