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
if (DesktopInput.MapPointer(10, 250, new Rectangle(0, 0, 1000, 500), target) is not null ||
    DesktopInput.MapPointer(990, 250, new Rectangle(0, 0, 1000, 500), target) is not null ||
    DesktopInput.MapPointer(0, 0, new Rectangle(0, 0, 0, 500), target) is not null)
    throw new Exception("Letterbox or empty-window coordinate was accepted.");
Console.WriteLine("INPUT_MAP_OK");

var ultrawide = DesktopDisplay.Destination(3440, 1440);
if (ultrawide != new Rectangle(520, 0, 2400, 1440))
    throw new Exception($"Ultrawide presentation rectangle differs: {ultrawide}.");
Check(DesktopDisplay.MapPointer(1720, 720, new Rectangle(0, 0, 3440, 1440)), 300, 180);
if (DesktopDisplay.MapPointer(100, 720, new Rectangle(0, 0, 3440, 1440)) is not null ||
    DesktopDisplay.Destination(3840, 2160) != new Rectangle(120, 0, 3600, 2160))
    throw new Exception("Ultrawide or 4K presentation mapping differs.");
Console.WriteLine("INPUT_DISPLAY_MAP_OK");
if (!DesktopDisplay.AvailablePresets(2560, 1080).SequenceEqual(
        [(600, 360), (1280, 720), (1920, 1080), (2560, 1080)]) ||
    DesktopDisplay.AvailablePresets(3840, 2160).Length != 8 ||
    DesktopDisplay.AvailablePresets(500, 300).Length != 0)
    throw new Exception("Display preset filtering differs from desktop bounds.");

var settings = new[]
{
    new Vector2(175, 160), new Vector2(230, 200), new Vector2(210, 240),
    new Vector2(125, 120), new Vector2(270, 120)
};
CheckIndex(MenuNavigation.Next(settings, 3, 1, 0), 4);  // SFX to BGM
CheckIndex(MenuNavigation.Next(settings, 4, -1, 0), 3);
CheckIndex(MenuNavigation.Next(settings, 3, 0, 1), 0);  // top row to vibration
CheckIndex(MenuNavigation.Next(settings, 0, 0, 1), 1);
CheckIndex(MenuNavigation.Next(settings, 1, 0, 1), 2);
CheckIndex(MenuNavigation.Next(settings, 2, 0, 1), 4);  // wrap to top row
CheckIndex(MenuNavigation.Next(settings, 4, 1, 0), 0);  // advance to the next row
CheckIndex(MenuNavigation.Next(settings, 3, -1, 0), 2); // wrap to the last option
var displaySettings = settings.Append(new Vector2(236, 285)).ToArray();
CheckIndex(MenuNavigation.Next(displaySettings, 2, 0, 1), 5);
CheckIndex(MenuNavigation.Next(displaySettings, 5, 0, -1), 2);
var settingsRoot = new[] { new Vector2(125, 110), new Vector2(270, 110),
    new Vector2(250, 170), new Vector2(250, 215) };
CheckIndex(MenuNavigation.Next(settingsRoot, 0, 1, 0), 1);
CheckIndex(MenuNavigation.Next(settingsRoot, 1, 0, 1), 2);
CheckIndex(MenuNavigation.Next(settingsRoot, 2, 0, 1), 3);
var touchPage = new[] { new Vector2(250, 100), new Vector2(250, 150),
    new Vector2(250, 200), new Vector2(250, 250) };
CheckIndex(MenuNavigation.Next(touchPage, 2, 0, 1), 3);
CheckIndex(MenuNavigation.Next(touchPage, 3, 0, -1), 2);
CheckIndex(MenuNavigation.CarouselNext(0, 1), 1);
CheckIndex(MenuNavigation.CarouselNext(6, 1), 6);
CheckIndex(MenuNavigation.CarouselNext(0, -1), 0);
CheckSwipe(0, 1, 0, 0);
CheckSwipe(0, 1, 0.5f, 0.875f);
CheckSwipe(0, 1, 1, 1);
CheckSwipe(2, 1, 0.5f, 1.125f);
CheckDirection(new KeyboardState(Keys.D), default, 1, 0);
CheckDirection(new KeyboardState(Keys.Right), default, 1, 0);
CheckDirection(new KeyboardState(Keys.W), default, 0, -1);
CheckDirection(default, new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.DPadRight), 1, 0);
CheckDirection(default, new GamePadState(new Vector2(0.8f, 0), Vector2.Zero, 0, 0), 1, 0);
Console.WriteLine("INPUT_MENU_NAV_OK");

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
