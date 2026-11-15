using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace Port.Hooks;

/// <summary>Captures a back-arrow contact through release, without leaking a menu tap.</summary>
public sealed class TouchBackButton
{
    private readonly HashSet<int> held = new();
    private readonly HashSet<int> consumed = new();
    private readonly HashSet<int> present = new();
    private bool suppressGestures;

    public void Reset()
    {
        held.Clear();
        consumed.Clear();
        present.Clear();
        suppressGestures = false;
    }

    public bool Sample(TouchCollection touches, Rectangle bounds, bool visible)
    {
        consumed.Clear();
        var pressed = false;
        present.Clear();
        foreach (var touch in touches)
        {
            present.Add(touch.Id);
            if (touch.State == TouchLocationState.Pressed)
            {
                if (visible && bounds.Contains(new Point((int)touch.Position.X, (int)touch.Position.Y)))
                {
                    pressed |= held.Add(touch.Id);
                    suppressGestures = true;
                }
                else if (held.Count == 0) suppressGestures = false;
            }
            if (!held.Contains(touch.Id)) continue;
            consumed.Add(touch.Id);
            if (touch.State is TouchLocationState.Released or TouchLocationState.Invalid) held.Remove(touch.Id);
        }
        held.IntersectWith(present);
        return pressed;
    }

    public bool Consumes(int contactId) => consumed.Contains(contactId);
    public bool Consumes(GestureSample gesture) => suppressGestures;
}
