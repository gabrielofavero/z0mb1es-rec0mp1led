using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

/// <summary>Chooses the next menu item from its actual screen position.</summary>
public static class MenuNavigation
{
    public static Vector2 Direction(KeyboardState keyboard, GamePadState pad)
    {
        var x = (pad.DPad.Right == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D) ? 1 : 0) -
            (pad.DPad.Left == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A) ? 1 : 0);
        var y = (pad.DPad.Down == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S) ? 1 : 0) -
            (pad.DPad.Up == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W) ? 1 : 0);
        if (x == 0 && y == 0)
        {
            x = pad.ThumbSticks.Left.X > 0.5f ? 1 : pad.ThumbSticks.Left.X < -0.5f ? -1 : 0;
            y = pad.ThumbSticks.Left.Y > 0.5f ? -1 : pad.ThumbSticks.Left.Y < -0.5f ? 1 : 0;
        }
        return new Vector2(x, y);
    }

    public static int CarouselNext(float time, int direction, int lastOption = 6) =>
        Math.Clamp((int)MathF.Floor(time + 0.5f) + Math.Sign(direction), 0, lastOption);

    public static float AchievementScroll(float current, float maximum, int wheelDelta,
        float rightStickY, float frameTime)
    {
        var stick = MathF.Abs(rightStickY) < 0.2f ? 0f : rightStickY;
        var movement = -wheelDelta * 0.6f - stick * 750f * Math.Clamp(frameTime, 0f, 0.05f);
        return Math.Clamp(current + movement, 0f, maximum);
    }

    public static float CarouselSwipePosition(float start, float target, float progress)
    {
        var remaining = 1f - Math.Clamp(progress, 0f, 1f);
        return start + (target - start) * (1f - remaining * remaining * remaining);
    }

    public static int Next(IReadOnlyList<Vector2> positions, int selected, int horizontal, int vertical)
    {
        if (positions.Count == 0) return -1;
        if (selected < 0 || selected >= positions.Count) return 0;
        if (horizontal == 0 && vertical == 0) return selected;

        if (horizontal != 0)
        {
            var visualOrder = Enumerable.Range(0, positions.Count)
                .OrderBy(index => positions[index].Y)
                .ThenBy(index => positions[index].X).ToArray();
            var at = Array.IndexOf(visualOrder, selected);
            return visualOrder[(at + (horizontal > 0 ? 1 : visualOrder.Length - 1)) % visualOrder.Length];
        }

        var origin = positions[selected];
        var axis = vertical;
        var best = -1;
        var bestScore = float.PositiveInfinity;
        for (var i = 0; i < positions.Count; i++)
        {
            if (i == selected) continue;
            var delta = positions[i] - origin;
            var along = delta.Y * axis;
            var across = MathF.Abs(delta.X);
            if (along <= 1f) continue;
            var score = along + across * 0.35f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) return best;
        // Wrap to the opposite end, preferring the same column when possible.
        bestScore = float.PositiveInfinity;
        for (var i = 0; i < positions.Count; i++)
        {
            var point = positions[i];
            var score = point.Y * axis + MathF.Abs(point.X - origin.X) * 0.35f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }
}
