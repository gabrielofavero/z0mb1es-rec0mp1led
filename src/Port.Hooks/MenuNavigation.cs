using Microsoft.Xna.Framework;

namespace Port.Hooks;

/// <summary>Chooses the next menu item from its actual screen position.</summary>
public static class MenuNavigation
{
    public static int Next(IReadOnlyList<Vector2> positions, int selected, int horizontal, int vertical)
    {
        if (positions.Count == 0) return -1;
        if (selected < 0 || selected >= positions.Count) return 0;
        if (horizontal == 0 && vertical == 0) return selected;

        var origin = positions[selected];
        var axis = horizontal != 0 ? horizontal : vertical;
        var horizontalMove = horizontal != 0;
        var best = -1;
        var bestScore = float.PositiveInfinity;
        for (var i = 0; i < positions.Count; i++)
        {
            if (i == selected) continue;
            var delta = positions[i] - origin;
            var along = (horizontalMove ? delta.X : delta.Y) * axis;
            var across = MathF.Abs(horizontalMove ? delta.Y : delta.X);
            if (along <= 1f || (horizontalMove && across > 25f)) continue;
            var score = along + across * 0.35f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) return best;
        if (horizontalMove) return selected;

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
