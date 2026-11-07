using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Hooks;

/// <summary>Desktop title and controller focus colors drawn through the original text atlas.</summary>
public static class MenuVisuals
{
    private static MethodInfo? drawText;
    private static FieldInfo? screenSize;
    private static MethodInfo? menuActive;
    private static object? centered;

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

    public static void DrawTitle(SpriteBatch sprite)
    {
        if (drawText is null)
        {
            // The gameplay assembly is already loaded by the host. Find its original text renderer.
            var text = AppDomain.CurrentDomain.GetAssemblies()
                .Select(candidate => candidate.GetType("IMAK3Z0MB1EGAEM.Text", false))
                .FirstOrDefault(candidate => candidate is not null)
                ?? throw new MissingMemberException("Original Text renderer was not loaded.");
            drawText = text.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(method => method.Name == "DrawString" && method.GetParameters().Length == 6 &&
                    method.GetParameters()[1].ParameterType == typeof(string));
            centered = Enum.Parse(text.GetNestedType("Justify", BindingFlags.Public | BindingFlags.NonPublic)!, "Center");
            screenSize = text.Assembly.GetType("IMAK3Z0MB1EGAEM.ScrollMan", true)!
                .GetField("screenSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            menuActive = text.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!
                .GetMethod("GetActive", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        }
        if ((bool)menuActive!.Invoke(null, null)!) return;
        var size = (Vector2)screenSize!.GetValue(null)!;
        var color = DesktopDisplay.ReduceFlashing ? Color.White : new Color(
            0.4f + 0.6f * Random.Shared.NextSingle(),
            0.4f + 0.6f * Random.Shared.NextSingle(),
            0.4f + 0.6f * Random.Shared.NextSingle(), 1f);
        sprite.Begin(SpriteSortMode.Deferred, BlendState.Additive);
        drawText.Invoke(null, [sprite, "z0mb1es", size * new Vector2(0.5f, 0.11f), 10f, color, centered]);
        sprite.End();
    }
}
