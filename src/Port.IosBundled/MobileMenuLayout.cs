using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Port.IosGame;

// The personal iOS host keeps the original mobile Settings pages. It must not
// call AttachSettingsMenu, which installs the desktop Settings implementation.
internal sealed class MobileMenuLayout(Assembly assembly)
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly FieldInfo levelsField = assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!.GetField("menuLevel", Static)!;
    private readonly FieldInfo itemsField = assembly.GetType("IMAK3Z0MB1EGAEM.menu.MenuLevel", true)!.GetField("item", Instance)!;
    private readonly FieldInfo locationField = assembly.GetType("ZombiesWP7.menu.MenuItem", true)!.GetField("loc", Instance)!;
    private readonly FieldInfo tiltField = assembly.GetType("ZombiesWP7.menu.MenuItem", true)!.GetField("tiltLoc", Instance)!;
    private readonly FieldInfo flagField = assembly.GetType("ZombiesWP7.menu.MenuItem", true)!.GetField("flag", Instance)!;
    private readonly FieldInfo scaleField = assembly.GetType("ZombiesWP7.Game1", true)!.GetField("MENUSCALE", Static)!;
    private IDictionary? previousLevels;
    private float appliedOffset;
    private float previousWidth;

    internal bool Apply(float width)
    {
        if (levelsField.GetValue(null) is not IDictionary levels || levels.Count != 17) return false;
        if (ReferenceEquals(previousLevels, levels) && previousWidth == width) return false;
        if (!ReferenceEquals(previousLevels, levels)) appliedOffset = 0;
        var scale = (float)scaleField.GetValue(null)!;
        var offset = (width - 600f) / 2f;
        var delta = offset - appliedOffset;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var level in levels.Values)
        {
            if (itemsField.GetValue(level) is not Array items) continue;
            foreach (var item in items)
            {
                if (item is null || !seen.Add(item)) continue;
                var location = (Vector2)locationField.GetValue(item)!;
                var tilt = (Vector2)tiltField.GetValue(item)!;
                if (location.X <= -900 && tilt.X <= -900) continue;
                Shift(item, (int)flagField.GetValue(item)! is 0 or 1 ? delta / scale : delta);
            }
        }
        // Center each pause/confirmation group while preserving its staggered rows.
        foreach (var key in new[] { 2, 3 })
        {
            if (itemsField.GetValue(levels[key]) is not Array items || items.Length == 0) continue;
            var xs = items.Cast<object>().Select(item => ((Vector2)locationField.GetValue(item)!).X).ToArray();
            var shift = width / (2f * scale) - (xs.Min() + xs.Max()) / 2f;
            foreach (var item in items) Shift(item!, shift);
        }
        previousLevels = levels;
        previousWidth = width;
        appliedOffset = offset;
        return true;
    }

    private void Shift(object item, float x)
    {
        var offset = new Vector2(x, 0);
        locationField.SetValue(item, (Vector2)locationField.GetValue(item)! + offset);
        tiltField.SetValue(item, (Vector2)tiltField.GetValue(item)! + offset);
    }
}
