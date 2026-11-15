using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace Port.Hooks;

/// <summary>Captures a back-arrow contact through release, without leaking a menu tap.</summary>
public sealed class TouchBackButton
{
    public const float ArrowScale = 2.5f;
    private readonly HashSet<int> held = new();
    private readonly HashSet<int> consumed = new();
    private readonly HashSet<int> present = new();
    private bool suppressGestures;
    private Vector2 sceneSize;
    private bool visible;

    // The circle is centered on the left edge, level with the animated arrows.
    // Include their full horizontal travel, the larger glyph, and finger padding.
    public static float TouchRadius(Vector2 size) => MathF.Max(72f, size.X * 0.075f + 20f);

    public static bool HitTest(Vector2 position, Vector2 size) =>
        position.X >= 0 && position.X < size.X && position.Y >= 0 && position.Y < size.Y &&
        Vector2.DistanceSquared(position, new Vector2(0, size.Y * 0.9f)) <=
            TouchRadius(size) * TouchRadius(size);

    public void Reset()
    {
        held.Clear();
        consumed.Clear();
        present.Clear();
        suppressGestures = false;
        visible = false;
        sceneSize = Vector2.Zero;
    }

    public bool Sample(TouchCollection touches, Vector2 size, bool visible)
    {
        sceneSize = size;
        this.visible = visible;
        consumed.Clear();
        var pressed = false;
        var ordinaryPress = false;
        present.Clear();
        foreach (var touch in touches)
        {
            present.Add(touch.Id);
            if (touch.State == TouchLocationState.Pressed)
            {
                if (visible && HitTest(touch.Position, size))
                {
                    pressed |= held.Add(touch.Id);
                    suppressGestures = true;
                }
                else ordinaryPress = true;
            }
            if (!held.Contains(touch.Id)) continue;
            consumed.Add(touch.Id);
            // Fixed updates can sample the same Released contact more than once.
            // Keep ownership until it disappears from the native collection.
        }
        held.IntersectWith(present);
        if (held.Count == 0 && ordinaryPress) suppressGestures = false;
        return pressed;
    }

    public bool Consumes(int contactId) => consumed.Contains(contactId);
    public bool Consumes(GestureSample gesture) => suppressGestures ||
        (visible && HitTest(gesture.Position, sceneSize));
}
