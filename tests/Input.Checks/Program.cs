using Microsoft.Xna.Framework;
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
CheckIndex(MenuNavigation.Next(settings, 3, -1, 0), 3); // no left neighbor
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
