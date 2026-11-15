using System.Reflection;
using Microsoft.Xna.Framework;

namespace Port.Hooks;

public static partial class DesktopDisplay
{
    private static FieldInfo? cameraZoomField, cameraScrollField, mapSizeField, gameStateField, viewScaleField, vikingZoomField;
    private static MethodInfo? endlessTopLeft, endlessBottomRight, endlessDrawableRect;
    private static FieldInfo? endlessUpdateField;
    private static float vikingCameraScale = 1f;
    private static float? landscapeCameraZoom;
    private static int portraitCameraMode;

    private static void FitGameCamera()
    {
        if (game is null) return;
        if (gameStateField is null)
        {
            const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var assembly = game.GetType().Assembly;
            var camera = assembly.GetType("IMAK3Z0MB1EGAEM.ScrollMan", true)!;
            cameraZoomField = camera.GetField("zoom", statics)!;
            cameraScrollField = camera.GetField("scroll", statics)!;
            mapSizeField = assembly.GetType("IMAK3Z0MB1EGAEM.map.MapMan", true)!.GetField("mapSize", statics)!;
            gameStateField = assembly.GetType("IMAK3Z0MB1EGAEM.director.GameState", true)!.GetField("state", statics)!;
            viewScaleField = game.GetType().GetField("VIEWSCALE", statics)!;
            vikingZoomField = assembly.GetType("Viking_x86.VScroll", true)!.GetField("zoom", statics)!;
            var zombies = assembly.GetType("IMAK3Z0MB1EGAEM.ZombieGame", true)!;
            endlessTopLeft = zombies.GetMethod("GetEndlessRoomTL", statics)!;
            endlessBottomRight = zombies.GetMethod("GetEndlessRoomBR", statics)!;
            endlessUpdateField = zombies.GetField("endlessUpdate", statics)!;
            endlessDrawableRect = endlessUpdateField.FieldType.GetMethod("UpdateDrawableRect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        }
        var mode = (int)gameStateField.GetValue(null)!;
        if (mode == 5)
        {
            var scale = VikingPortraitScale;
            if (scale != vikingCameraScale)
                vikingZoomField!.SetValue(null, (float)vikingZoomField.GetValue(null)! * scale / vikingCameraScale);
            vikingCameraScale = scale;
        }
        else vikingCameraScale = 1f;
        var portrait = sceneSize.Height > sceneSize.Width && mode is 3 or 7;
        if (!portrait)
        {
            if (landscapeCameraZoom is { } previous && mode == portraitCameraMode)
                cameraZoomField!.SetValue(null, previous);
            landscapeCameraZoom = null;
            if (mode == 7) RefreshEndlessDrawable();
            return;
        }
        var zoom = (float)cameraZoomField!.GetValue(null)!;
        if (landscapeCameraZoom is null || portraitCameraMode != mode)
        {
            landscapeCameraZoom = zoom;
            portraitCameraMode = mode;
        }
        var topLeft = Vector2.Zero;
        var bottomRight = (Vector2)mapSizeField!.GetValue(null)!;
        if (mode == 7)
        {
            // These gameplay bounds include a 64-unit tile offset. The room
            // floor, reveal mask and drawable rectangle start on the room grid.
            var tileOffset = new Vector2(64f);
            topLeft = (Vector2)endlessTopLeft!.Invoke(null, null)! - tileOffset;
            bottomRight = (Vector2)endlessBottomRight!.Invoke(null, null)! - tileOffset;
        }
        var bounds = bottomRight - topLeft;
        var viewScale = (float)viewScaleField!.GetValue(null)!;
        if (bounds.X <= 0 || bounds.Y <= 0 || viewScale <= 0) return;
        zoom = Math.Max(zoom, ZombieCoverZoom(SceneSize, bounds, viewScale));
        cameraZoomField.SetValue(null, zoom);
        var scroll = (Vector2)cameraScrollField!.GetValue(null)!;
        cameraScrollField.SetValue(null, ClampCameraToWorld(scroll, SceneSize / (zoom * viewScale), topLeft, bottomRight));
        if (mode == 7) RefreshEndlessDrawable();
    }

    private static void RefreshEndlessDrawable()
    {
        if (endlessUpdateField!.GetValue(null) is { } update)
            endlessDrawableRect!.Invoke(update, null);
    }

    // Endless updates its map culling before ScrollMan.Prepare applies VIEWSCALE.
    // Use the geometry that will actually be rendered, including the zoom fitted above.
    public static Vector2 EndlessViewSize(float width, float height) => SceneSize;
    public static Vector2 EndlessCullWorld(Vector2 pixel, float scale)
    {
        var zoom = (float)cameraZoomField!.GetValue(null)! * (float)viewScaleField!.GetValue(null)!;
        return (pixel - SceneSize / 2f) / (zoom * scale) + (Vector2)cameraScrollField!.GetValue(null)!;
    }

    // Keep a square scene's vertical field of view in portrait while retaining
    // Viking's original animated zoom targets and camera look-ahead.
    private static float VikingPortraitScale => sceneSize.Height > sceneSize.Width ? (float)sceneSize.Height / LogicalWidth : 1f;
    public static float VikingPortraitZoom(float original) => original * VikingPortraitScale;
    public static float VikingLandscapeZoom(float actual) => actual / vikingCameraScale;

    public static Vector2 ClampCameraToWorld(Vector2 center, Vector2 visibleWorld, Vector2 topLeft, Vector2 bottomRight)
    {
        var half = visibleWorld / 2f;
        return new Vector2(
            Math.Clamp(center.X, topLeft.X + half.X, Math.Max(topLeft.X + half.X, bottomRight.X - half.X)),
            Math.Clamp(center.Y, topLeft.Y + half.Y, Math.Max(topLeft.Y + half.Y, bottomRight.Y - half.Y)));
    }

    public static float ZombieCoverZoom(Vector2 viewport, Vector2 world, float viewScale) =>
        Math.Max(viewport.X / (world.X * viewScale), viewport.Y / (world.Y * viewScale));
}
