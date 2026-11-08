using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Port.Hooks;

var target = new Vector2(600, 360);
Check(DesktopInput.MapPointer(300, 180, new Rectangle(0, 0, 600, 360), target), 300, 180);
Check(DesktopInput.MapPointer(600, 360, new Rectangle(0, 0, 1200, 720), target), 300, 180);
Check(DesktopInput.MapPointer(500, 250, new Rectangle(0, 0, 1000, 500), target), 300, 180);
Check(DesktopInput.AimDirection(new Vector2(500, 180), new Vector2(300, 180)), 1, 0);
Check(DesktopInput.AimDirection(new Vector2(300, 20), new Vector2(300, 180)), 0, -1);
Check(DesktopInput.AimDirection(new Vector2(300, 180), new Vector2(300, 180)), 0, 0);
var leftStick = DesktopInput.VirtualStickCenter(target, 0.75f, right: false);
var rightStick = DesktopInput.VirtualStickCenter(target, 0.75f, right: true);
Check(leftStick, 90, 270);
Check(rightStick, 510, 270);
Check(DesktopInput.VirtualStickCenter(new Vector2(860, 360), 0.75f, right: true), 770, 270);
if (DesktopInput.MapPointer(10, 250, new Rectangle(0, 0, 1000, 500), target) is not null ||
    DesktopInput.MapPointer(990, 250, new Rectangle(0, 0, 1000, 500), target) is not null ||
    DesktopInput.MapPointer(0, 0, new Rectangle(0, 0, 0, 500), target) is not null)
    throw new Exception("Legacy input mapping accepted a margin or empty window.");
Console.WriteLine("INPUT_MAP_OK");

var ultrawide = DesktopDisplay.Presentation(3440, 1440, DesktopDisplay.CanvasSize(3440, 1440));
if (ultrawide.Offset != Vector2.Zero || MathF.Abs(ultrawide.Scale - 4) > 0.0001f ||
    DesktopDisplay.CanvasSize(3440, 1440) != (860, 360) ||
    DesktopDisplay.CanvasSize(3840, 2160) != (640, 360) ||
    DesktopDisplay.CanvasSize(800, 600) != (600, 450))
    throw new Exception($"Ultrawide presentation scale differs: {ultrawide}.");
var widePresentation = DesktopDisplay.Presentation(2560, 1080, DesktopDisplay.CanvasSize(2560, 1080));
if (MathF.Abs(widePresentation.Offset.X) > 0.001f || widePresentation.Offset.Y < 0 ||
    MathF.Abs(widePresentation.Scale - 2560f / 854) > 0.0001f)
    throw new Exception("2560x1080 presentation did not keep one scale on both axes.");
Check(DesktopDisplay.MapPointer(1280, 540, new Rectangle(0, 0, 2560, 1080),
    DesktopDisplay.CanvasSize(2560, 1080)), 427, 180);
if (DesktopDisplay.MapPointer(1280, 0, new Rectangle(0, 0, 2560, 1080),
        DesktopDisplay.CanvasSize(2560, 1080)) is not null)
    throw new Exception("Pointer mapping accepted the ultrawide presentation margin.");
Check(DesktopDisplay.MapPointer(1720, 720, new Rectangle(0, 0, 3440, 1440),
    DesktopDisplay.CanvasSize(3440, 1440)), 430, 180);
Check(DesktopDisplay.MapPointer(100, 720, new Rectangle(0, 0, 3440, 1440),
    DesktopDisplay.CanvasSize(3440, 1440)), 25, 180);
Check(DesktopDisplay.MapPointer(400, 550, new Rectangle(0, 0, 800, 600),
    DesktopDisplay.CanvasSize(800, 600)), 300, 412.5f);
if (DesktopDisplay.MapPointer(3440, 720, new Rectangle(0, 0, 3440, 1440),
        DesktopDisplay.CanvasSize(3440, 1440)) is not null)
    throw new Exception("Out-of-window coordinate was accepted.");
Console.WriteLine("INPUT_DISPLAY_MAP_OK");
if (!DesktopDisplay.AvailablePresets(2560, 1080).SequenceEqual(
        [(600, 360), (1280, 720), (1920, 1080), (2560, 1080)]) ||
    DesktopDisplay.AvailablePresets(3840, 2160).Length != 8 ||
    DesktopDisplay.AvailablePresets(500, 300).Length != 0)
    throw new Exception("Display preset filtering differs from desktop bounds.");

var settingsRoot = new[] { new Vector2(167, 110), new Vector2(312, 110),
    new Vector2(250, 170), new Vector2(250, 215) };
CheckIndex(MenuNavigation.Next(settingsRoot, 0, 1, 0), 1);
CheckIndex(MenuNavigation.Next(settingsRoot, 1, -1, 0), 0);
CheckIndex(MenuNavigation.Next(settingsRoot, 1, 0, 1), 2);
CheckIndex(MenuNavigation.Next(settingsRoot, 2, 0, 1), 3);
CheckIndex(MenuNavigation.Next(settingsRoot, 3, 0, -1), 2);
var touchPage = new[] { new Vector2(250, 100), new Vector2(250, 150),
    new Vector2(250, 200), new Vector2(250, 250) };
CheckIndex(MenuNavigation.Next(touchPage, 2, 0, 1), 3);
CheckIndex(MenuNavigation.Next(touchPage, 3, 0, -1), 2);
var displayPage = new[] { new Vector2(250, 100), new Vector2(250, 140),
    new Vector2(250, 180), new Vector2(250, 220), new Vector2(250, 260) };
CheckIndex(MenuNavigation.Next(displayPage, 2, 0, 1), 3);
CheckIndex(MenuNavigation.Next(displayPage, 4, 0, -1), 3);
var customPage = new[] { new Vector2(250, 100), new Vector2(250, 150),
    new Vector2(250, 200), new Vector2(250, 250) };
CheckIndex(MenuNavigation.Next(customPage, 1, 0, 1), 2);
CheckIndex(MenuNavigation.CarouselNext(0, 1), 1);
CheckIndex(MenuNavigation.CarouselNext(6, 1), 6);
CheckIndex(MenuNavigation.CarouselNext(0, -1), 0);
var backBounds = DesktopInput.BackButtonBounds(new Vector2(860, 360));
if (!backBounds.Contains(new Point(21, 324)) || !backBounds.Contains(new Point(65, 324)))
    throw new Exception("Back hit area misses one of the animated left arrows.");
CheckSwipe(0, 1, 0, 0);
CheckSwipe(0, 1, 0.5f, 0.875f);
CheckSwipe(0, 1, 1, 1);
CheckSwipe(2, 1, 0.5f, 1.125f);
CheckDirection(new KeyboardState(Keys.D), default, 1, 0);
CheckDirection(new KeyboardState(Keys.Right), default, 1, 0);
CheckDirection(new KeyboardState(Keys.W), default, 0, -1);
CheckDirection(default, new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.DPadRight), 1, 0);
CheckDirection(default, new GamePadState(new Vector2(0.8f, 0), Vector2.Zero, 0, 0), 1, 0);
CheckScroll(0, 1240, -120, 0, 1f / 30f, 72);
CheckScroll(72, 1240, 120, 0, 1f / 30f, 0);
CheckScroll(0, 1240, 0, -1, 1f / 30f, 25);
CheckScroll(25, 1240, 0, 1, 1f / 30f, 0);
CheckScroll(1230, 1240, -120, -1, 1f / 30f, 1240);
CheckScroll(50, 1240, 0, -0.1f, 1f / 30f, 50);
Console.WriteLine("INPUT_MENU_NAV_OK");

static void CheckScroll(float current, float maximum, int wheel, float stick, float time, float expected)
{
    var actual = MenuNavigation.AchievementScroll(current, maximum, wheel, stick, time);
    if (MathF.Abs(actual - expected) > 0.001f)
        throw new Exception($"Expected achievement scroll {expected}; got {actual}.");
}

static void Check(Vector2? actual, float x, float y)
{
    if (actual is not Vector2 position || Vector2.DistanceSquared(position, new Vector2(x, y)) > 0.01f)
        throw new Exception($"Expected ({x},{y}); got {actual}.");
}

static void CheckIndex(int actual, int expected)
{
    if (actual != expected) throw new Exception($"Expected menu index {expected}; got {actual}.");
}

static void CheckSwipe(float start, float target, float progress, float expected)
{
    var actual = MenuNavigation.CarouselSwipePosition(start, target, progress);
    if (MathF.Abs(actual - expected) > 0.0001f)
        throw new Exception($"Expected carousel position {expected}; got {actual}.");
}

static void CheckDirection(KeyboardState keyboard, GamePadState pad, float x, float y)
{
    var actual = MenuNavigation.Direction(keyboard, pad);
    if (actual != new Vector2(x, y))
        throw new Exception($"Expected menu direction ({x},{y}); got {actual}.");
}
