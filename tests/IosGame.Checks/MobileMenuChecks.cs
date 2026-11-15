using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Port.Hooks;
using Port.IosGame;

internal static class MobileMenuChecks
{
    internal static void Run(string gamePath)
    {
        if (DesktopInput.NativeBackButtonEnabled) throw new Exception("Native back must be opt-in.");
        var back = new TouchBackButton();
        var size = new Vector2(783, 360);
        var bounds = DesktopInput.BackButtonBounds(size);
        var center = new Vector2(bounds.Center.X, bounds.Center.Y);
        TouchCollection Contacts(params TouchLocation[] touches) => new(touches);
        TouchLocation Contact(int id, TouchLocationState state, Vector2? position = null) => new(id, state, position ?? center);
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        Assert(!back.Sample(Contacts(Contact(1, TouchLocationState.Pressed)), bounds, false), "Hidden arrow accepted a tap.");
        Assert(!back.Consumes(1), "Hidden arrow swallowed a gameplay touch.");
        Assert(back.Sample(Contacts(Contact(2, TouchLocationState.Pressed)), bounds, true), "Back tap not accepted.");
        Assert(back.Consumes(2), "Back press leaked to the game.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Pressed)), bounds, true), "Repeated fixed update repeated Back.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Moved, new Vector2(400, 200))), bounds, false), "Held back repeated after the menu changed.");
        Assert(back.Consumes(2), "Dragging a captured back contact leaked to the game.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Released)), bounds, false) && back.Consumes(2), "Back release leaked or repeated.");
        Assert(back.Consumes(new GestureSample(GestureType.Tap, TimeSpan.Zero, center, Vector2.Zero, Vector2.Zero, Vector2.Zero)), "Back tap leaked a selection gesture.");
        Assert(!back.Sample(Contacts(Contact(3, TouchLocationState.Pressed, new Vector2(400, 200))), bounds, true), "Menu item became Back.");
        Assert(!back.Consumes(3) && !back.Consumes(default(GestureSample)), "Ordinary menu input was swallowed.");
        Assert(back.Sample(Contacts(Contact(4, TouchLocationState.Pressed)), bounds, true), "Second distinct tap was lost.");
        back.Reset();
        Assert(!back.Consumes(4) && !back.Consumes(default(GestureSample)), "Input capture survived reset.");

        // Use the prepared game's real menu constructors and fields, without a GPU.
        var assembly = Assembly.LoadFrom(Path.GetFullPath(gamePath));
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags instances = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type Type(string name) => assembly.GetType(name, true)!;
        var levelType = Type("IMAK3Z0MB1EGAEM.menu.MenuLevel");
        var itemType = Type("ZombiesWP7.menu.MenuItem");
        var itemField = levelType.GetField("item", instances)!;
        var location = itemType.GetField("loc", instances)!;
        var tilt = itemType.GetField("tiltLoc", instances)!;
        var flag = itemType.GetField("flag", instances)!;
        var levels = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), levelType))!;
        var menuType = Type("IMAK3Z0MB1EGAEM.menu.Menu");
        menuType.GetField("menuLevel", statics)!.SetValue(null, levels);
        var scale = (float)Type("ZombiesWP7.Game1").GetField("MENUSCALE", statics)!.GetValue(null)!;
        for (var i = 0; i < 17; i++) levels[i] = Activator.CreateInstance(levelType)!;
        levels[2] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Pause"))!;
        levels[3] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.EndGameYouSure"))!;
        levels[5] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Settings"))!;
        var snapshots = new Dictionary<object, (Vector2 Location, Vector2 Tilt, int Flag)>(ReferenceEqualityComparer.Instance);
        foreach (var level in levels.Values)
            if (itemField.GetValue(level) is Array items)
                foreach (var item in items)
                    snapshots.TryAdd(item!, ((Vector2)location.GetValue(item)!, (Vector2)tilt.GetValue(item)!, (int)flag.GetValue(item)!));
        var layout = new MobileMenuLayout(assembly);
        foreach (var width in new[] { 783f, 600f, 900f, 783f })
        {
            Assert(layout.Apply(width), "Menu layout did not follow a scene width change.");
            Assert(!layout.Apply(width), "Menu layout applied twice at the same width.");
            foreach (var key in new[] { 2, 3 })
            {
                var items = ((Array)itemField.GetValue(levels[key])!).Cast<object>().ToArray();
                var xs = items.Select(item => ((Vector2)location.GetValue(item)!).X).ToArray();
                Assert(Math.Abs((xs.Min() + xs.Max()) / 2f - width / (2 * scale)) < 0.001f, "Pause group is off center.");
                var first = items[0];
                foreach (var item in items)
                {
                    var actual = (Vector2)location.GetValue(item)!;
                    Assert(Vector2.Distance(actual - (Vector2)location.GetValue(first)!, snapshots[item].Location - snapshots[first].Location) < 0.001f,
                        "Pause rows lost their stagger or vertical positions.");
                    Assert(Vector2.Distance((Vector2)tilt.GetValue(item)! - actual, snapshots[item].Tilt - snapshots[item].Location) < 0.001f, "Menu draw and hit positions diverged.");
                }
            }
            foreach (var item in (Array)itemField.GetValue(levels[5])!)
            {
                var original = snapshots[item!];
                var expected = original.Location + new Vector2((width - 600) / 2 / (original.Flag is 0 or 1 ? scale : 1), 0);
                Assert(Vector2.Distance((Vector2)location.GetValue(item)!, expected) < 0.001f, "Settings row offset changed.");
            }
        }
        Console.WriteLine("IOS_MENU_OK; real pause/confirmation/Settings layouts; width changes; touch back, hold, drag, release and gesture capture; desktop default unchanged");
    }
}
