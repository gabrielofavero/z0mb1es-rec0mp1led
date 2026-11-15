using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Microsoft.Xna.Framework.Graphics;
using SDL3;

namespace Port.Hooks;

public static partial class DesktopDisplay
{
    private static readonly Dictionary<object, Vector2> menuAnchors = new(ReferenceEqualityComparer.Instance);
    private static (float Top, float Scale) menuPanelApplied;
    private static int menuHeightApplied = LogicalHeight;
    private static Vector4 mobileSafeInsets;
    public static bool ResponsiveOrientationEnabled => !mobile || preferences.ResponsiveOrientation;
    public static event Action<bool>? ResponsiveOrientationChanged;
    // Fractions of the current usable window: left, top, right, bottom.
    public static Func<Vector4>? MobileSafeInsets { get; set; }

    private static void ApplyOrientationPolicy()
    {
        if (!mobile) return;
        if (game is not null) SDL.SDL_SetHint(SDL.SDL_HINT_ORIENTATIONS, preferences.ResponsiveOrientation
            ? "LandscapeLeft LandscapeRight Portrait" : "LandscapeLeft LandscapeRight");
        ResponsiveOrientationChanged?.Invoke(preferences.ResponsiveOrientation);
    }

    private static Point? MobileDrawableSize()
    {
        if (!mobile || game is null || game.Window.Handle == IntPtr.Zero ||
            !SDL.SDL_GetWindowSizeInPixels(game.Window.Handle, out var width, out var height) ||
            width <= 0 || height <= 0) return null;
        return new Point(width, height);
    }

    private static void SetMobileBackbuffer(PresentationParameters output, Point size)
    {
        output.BackBufferWidth = size.X;
        output.BackBufferHeight = size.Y;
        output.DisplayOrientation = size.Y > size.X ? DisplayOrientation.Portrait :
            game!.Window.CurrentOrientation == DisplayOrientation.LandscapeRight
                ? DisplayOrientation.LandscapeRight : DisplayOrientation.LandscapeLeft;
    }

    private static void PrepareMobileDevice(object? sender, PreparingDeviceSettingsEventArgs args)
    {
        if (MobileDrawableSize() is { } size)
            SetMobileBackbuffer(args.GraphicsDeviceInformation.PresentationParameters, size);
    }

    private static void SyncMobileBackbuffer()
    {
        if (MobileDrawableSize() is not { } size || game?.GraphicsDevice is not { } device) return;
        var output = device.PresentationParameters;
        if (output.BackBufferWidth == size.X && output.BackBufferHeight == size.Y) return;
        // UIKit window resizing and FNA's display-orientation events can arrive
        // independently. Repair stale geometry before drawing to the real surface.
        if (device.GetRenderTargets().Length != 0) return;
        var next = output.Clone();
        SetMobileBackbuffer(next, size);
        device.Reset(next);
        Console.WriteLine($"DISPLAY_BACKBUFFER_SYNC previous={output.BackBufferWidth}x{output.BackBufferHeight} native={size.X}x{size.Y}");
    }

    private static void OnWindowResized(object? sender, EventArgs args) => RefreshLayout();

    // Called before input as well as drawing, including the first frame after rotation.
    public static void RefreshLayout()
    {
        if (game?.GraphicsDevice is null) return;
        SyncMobileBackbuffer();
        mobileSafeInsets = mobile ? MobileSafeInsets?.Invoke() ?? Vector4.Zero : Vector4.Zero;
        SyncSceneSize();
        AttachSettingsMenu();
        CenterMenuItems();
        if (mobile)
        {
            TouchPanel.DisplayWidth = sceneSize.Width;
            TouchPanel.DisplayHeight = sceneSize.Height;
        }
    }

    private static int PortraitLayoutHeight => sceneSize.Height > sceneSize.Width ? sceneSize.Height : LogicalHeight;
    private static (float Top, float Scale) MenuPanel
    {
        get
        {
            if (PortraitLayoutHeight == LogicalHeight) return (50f, 1f);
            var top = HudMargin.Y;
            var usable = sceneSize.Height - 2f * top;
            var height = Math.Min(620f, usable * 0.7f);
            return (top + (usable - height) / 2f, height / 300f);
        }
    }

    private static float MenuY(float y)
    {
        var panel = MenuPanel;
        return (panel.Top + (y * MenuScale - 50f) * panel.Scale) / MenuScale;
    }

    private static void LayoutFixedItem(object item, bool force = false)
    {
        var location = (Vector2)itemLocationField!.GetValue(item)!;
        if (!menuAnchors.TryGetValue(item, out var authored)) menuAnchors[item] = authored = location;
        var target = new Vector2(authored.X + TextMenuOffset, MenuY(authored.Y));
        if (!force && Vector2.DistanceSquared(target, location) < 0.000001f) return;
        itemLocationField.SetValue(item, target);
        // Rotation changes layout immediately; do not interpolate from a position
        // clamped in the previous orientation into the new row's hit target.
        itemTiltField!.SetValue(item, target);
    }

    public static void LayoutMenuLevel(object level)
    {
        if (itemsField is null || (!(bool)level.GetType().GetField("active", Fields)!.GetValue(level)! &&
            (float)level.GetType().GetField("alpha", Fields)!.GetValue(level)! <= 0) ||
            (bool)level.GetType().GetField("scrolling", Fields)!.GetValue(level)! ||
            (bool)level.GetType().GetField("isCustomMapper", Fields)!.GetValue(level)!) return;
        if (itemsField.GetValue(level) is Array items)
            foreach (var item in items)
                if (item is not null && ((Vector2)itemLocationField!.GetValue(item)!).X > -900) LayoutFixedItem(item);
    }

    public static float MenuItemBottom(float original) => PortraitLayoutHeight > LogicalHeight
        ? Math.Max(original, MenuY(original) + 16f) : original;

    public static float MenuScrollLimit(float maximum, object? level = null) =>
        PortraitAchievementScrollLimit(level) ?? Math.Max(0, maximum - Math.Max(0, sceneSize.Height - LogicalHeight));

    public static float MenuTitleY(float y, object level)
    {
        if ((bool)level.GetType().GetField("scrolling", Fields)!.GetValue(level)!) return y;
        if ((bool)level.GetType().GetField("isCustomMapper", Fields)!.GetValue(level)!)
            return y + (PortraitLayoutHeight > LogicalHeight ? HudMargin.Y : 0);
        return MenuPanel.Top + (y - 50f) * MenuPanel.Scale;
    }
    public static float MenuInfoOffset(float y) => PortraitLayoutHeight > LogicalHeight ? y + 25f : y;
    public static Vector2 ModeTitlePosition(float x, float y) => new(x, ModeTitleY(y));
    public static Vector2 ModeStartPosition(float x, float y) => new(x, ModeStartY(y));
    private static float ModeTitleY(float y) => PortraitLayoutHeight > LogicalHeight
        ? sceneSize.Height * 0.43f + y - 110f : y;
    private static float ModeStartY(float y) => PortraitLayoutHeight > LogicalHeight ? sceneSize.Height * 0.62f : y;
    public static Vector2 CenterCarousel(Vector2 position, float viewScale) => position +
        (SceneSize - new Vector2(LogicalWidth, LogicalHeight)) * (0.5f - 0.1f * viewScale);
    public static float MenuInfoSpacing(float spacing, object level) =>
        (bool)level.GetType().GetField("scrolling", Fields)!.GetValue(level)! ? spacing :
        PortraitLayoutHeight > LogicalHeight ? spacing * 1.3f : spacing;
}
