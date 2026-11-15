using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework.Input;
using Port.Bridge;
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
        // Exercise the padding beyond the desktop rectangle, not just the glyph.
        var center = new Vector2(8, 270);
        foreach (var width in new[] { 600, 783, 900 })
        {
            var viewport = new Vector2(width, 360);
            if (!TouchBackButton.HitTest(new Vector2(0, 324), viewport) ||
                !TouchBackButton.HitTest(new Vector2(8, 270), viewport) ||
                !TouchBackButton.HitTest(new Vector2(65, 324), viewport) ||
                !TouchBackButton.HitTest(new Vector2(width * 0.075f + 12, 336), viewport))
                throw new Exception("Mobile Back misses the screen edge, finger padding, or animated arrow.");
            if (TouchBackButton.HitTest(new Vector2(70, 260), viewport) ||
                TouchBackButton.HitTest(new Vector2(100, 324), viewport) ||
                TouchBackButton.HitTest(new Vector2(-1, 324), viewport) ||
                TouchBackButton.HitTest(new Vector2(0, 360), viewport))
                throw new Exception("Mobile Back accepts a circle corner or off-screen contact.");
        }
        TouchCollection Contacts(params TouchLocation[] touches) => new(touches);
        TouchLocation Contact(int id, TouchLocationState state, Vector2? position = null) => new(id, state, position ?? center);
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        Assert(!back.Sample(Contacts(Contact(1, TouchLocationState.Pressed)), size, false), "Hidden arrow accepted a tap.");
        Assert(!back.Consumes(1), "Hidden arrow swallowed a gameplay touch.");
        Assert(back.Sample(Contacts(Contact(2, TouchLocationState.Pressed)), size, true), "Back tap not accepted.");
        Assert(back.Consumes(2), "Back press leaked to the game.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Pressed)), size, true), "Repeated fixed update repeated Back.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Moved, new Vector2(400, 200))), size, false), "Held back repeated after the menu changed.");
        Assert(back.Consumes(2), "Dragging a captured back contact leaked to the game.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Released)), size, false) && back.Consumes(2), "Back release leaked or repeated.");
        Assert(!back.Sample(Contacts(Contact(2, TouchLocationState.Released)), size, false) && back.Consumes(2), "Repeated release leaked to tap-to-start.");
        Assert(!back.Sample(Contacts(), size, false), "Empty update repeated Back.");
        Assert(back.Consumes(new GestureSample(GestureType.Tap, TimeSpan.Zero, center, Vector2.Zero, Vector2.Zero, Vector2.Zero)), "Back tap leaked a selection gesture.");
        Assert(!back.Sample(Contacts(Contact(3, TouchLocationState.Pressed, new Vector2(400, 200))), size, true), "Menu item became Back.");
        Assert(!back.Consumes(3) && !back.Consumes(default(GestureSample)), "Ordinary menu input was swallowed.");
        Assert(back.Sample(Contacts(Contact(4, TouchLocationState.Pressed)), size, true), "Second distinct tap was lost.");
        Assert(!back.Sample(Contacts(
            Contact(5, TouchLocationState.Pressed, new Vector2(400, 200)),
            Contact(4, TouchLocationState.Released)), size, false), "Concurrent menu press repeated Back.");
        Assert(back.Consumes(4) && !back.Consumes(5), "Concurrent press lost Back ownership or swallowed another contact.");
        Assert(back.Consumes(default(GestureSample)), "Concurrent press leaked a captured Back gesture after navigation.");
        back.Reset();
        back.Sample(Contacts(Contact(6, TouchLocationState.Pressed, new Vector2(400, 200))), size, true);
        Assert(back.Consumes(new GestureSample(GestureType.Tap, TimeSpan.Zero, center, Vector2.Zero, Vector2.Zero, Vector2.Zero)), "Back area accepted a delayed selection gesture.");
        Assert(!back.Consumes(new GestureSample(GestureType.Tap, TimeSpan.Zero, new Vector2(400, 200), Vector2.Zero, Vector2.Zero, Vector2.Zero)), "Back area swallowed an ordinary selection gesture.");
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
        var levels = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), levelType))!;
        var menuType = Type("IMAK3Z0MB1EGAEM.menu.Menu");
        menuType.GetField("menuLevel", statics)!.SetValue(null, levels);
        var scale = (float)Type("ZombiesWP7.Game1").GetField("MENUSCALE", statics)!.GetValue(null)!;
        for (var i = 0; i < 17; i++) levels[i] = Activator.CreateInstance(levelType)!;
        levels[2] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Pause"))!;
        levels[3] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.EndGameYouSure"))!;
        levels[5] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Settings"))!;
        var hooks = typeof(DesktopDisplay);
        void Set(string name, object? value) => hooks.GetField(name, statics)!.SetValue(null, value);
        void Invoke(string name) => hooks.GetMethod(name, statics)!.Invoke(null, null);
        Set("mobile", true);
        Set("menuScaleField", Type("ZombiesWP7.Game1").GetField("MENUSCALE", statics));
        hooks.GetMethod("AttachSettingsMenu", statics, [typeof(Assembly)])!.Invoke(null, [assembly]);
        var settings = levels[5]!;
        var text = itemType.GetField("text", instances)!;
        object[] Items() => ((Array)itemField.GetValue(settings)!).Cast<object>().ToArray();
        string[] Labels() => Items().Select(item => (string)text.GetValue(item)!).ToArray();
        void Click(string label) => Assert(DesktopDisplay.HandleSettingsClick(settings, label), "Settings click was not handled: " + label);
        Assert(Labels().Contains("GRAPH1CS") && Labels().Contains("AUD10") && Labels().Contains("GAMEPLAY"), "Modified Settings categories missing.");
        var pauseOffsets = new Dictionary<object, Vector2>(ReferenceEqualityComparer.Instance);
        foreach (var key in new[] { 2, 3 })
            foreach (var item in (Array)itemField.GetValue(levels[key])!)
                pauseOffsets[item!] = (Vector2)location.GetValue(item)!;
        foreach (var width in new[] { 783, 600, 900, 783 })
        {
            Set("sceneSize", (width, 360));
            Invoke("CenterMenuItems");
            foreach (var key in new[] { 2, 3 })
            {
                var group = ((Array)itemField.GetValue(levels[key])!).Cast<object>().ToArray();
                var xs = group.Select(item => ((Vector2)location.GetValue(item)!).X).ToArray();
                Assert(Math.Abs((xs.Min() + xs.Max()) / 2f - width / (2f * scale)) < 0.001f, "Pause group is off center.");
                foreach (var item in group)
                    Assert(Vector2.Distance((Vector2)location.GetValue(item)! - (Vector2)location.GetValue(group[0])!,
                        pauseOffsets[item] - pauseOffsets[group[0]]) < 0.001f, "Pause rows lost their stagger.");
            }
            Click("GRAPH1CS");
            var labels = Labels();
            Assert(labels.Length == 4 && labels[0].StartsWith("M0DE:") && labels[1].StartsWith("R3DUCE FLASH1NG:") && labels[2] == "ASSETS" && labels[3] == "BACK", "Mobile Graphics rows differ.");
            var offsets = new[] { -60f, 50f, -55f, 10f };
            for (var i = 0; i < offsets.Length; i++)
            {
                var item = Items()[i];
                var actual = (Vector2)location.GetValue(item)!;
                Assert(Math.Abs(actual.X - (width / (2f * scale) + offsets[i])) < 0.001f, "Mobile Graphics lost its stagger.");
                Assert(Vector2.Distance(actual, (Vector2)tilt.GetValue(item)!) < 0.001f, "Graphics hit and draw positions differ.");
            }
            Click("VSYNC: OFF");
            Click("RES0LUTI0N: 600 X 360");
            Assert(Labels().SequenceEqual(labels), "Hidden display controls changed mobile settings.");
            Click("ASSETS");
            Assert(DesktopDisplay.IsAssetPage, "Assets page missing.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings) && DesktopDisplay.IsDisplayPage, "Assets Back did not return to Graphics.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings), "Graphics Back failed.");
            Click("GAMEPLAY");
            Click("1NPUT");
            Click("T0UCHPADS");
            Assert(DesktopDisplay.IsTouchpadPage && Labels().Any(label => label.StartsWith("TOUCHPAD SIZE:")), "Modified touchpad settings missing.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings) && DesktopDisplay.IsTouchPage, "Touchpads Back failed.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings) && DesktopDisplay.IsGameplayPage, "Input Back failed.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings), "Gameplay Back failed.");
            Click("AUD10");
            Assert(Labels().Contains("BACK"), "Audio Back missing.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings), "Audio Back failed.");
        }
        CheckPlayerName(assembly);
        // Verify stale desktop preferences cannot override mobile display constraints.
        var preferences = hooks.GetField("preferences", statics)!.GetValue(null)!;
        var preferenceType = preferences.GetType();
        preferenceType.GetProperty("Auto")!.SetValue(preferences, false);
        preferenceType.GetProperty("VSync")!.SetValue(preferences, false);
        var normalized = hooks.GetMethod("MobilePreferences", statics)!.Invoke(null, [preferences])!;
        Assert((bool)preferenceType.GetProperty("Auto")!.GetValue(normalized)! &&
               (bool)preferenceType.GetProperty("VSync")!.GetValue(normalized)!, "Mobile did not force Auto resolution and VSync.");
        Set("mobile", false);
        Assert(ReferenceEquals(preferences, hooks.GetMethod("MobilePreferences", statics)!.Invoke(null, [preferences])), "Desktop preferences changed.");
        // Recreate Settings to verify the desktop still exposes all six Graphics rows.
        levels[5] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Settings"))!;
        settings = levels[5]!;
        hooks.GetMethod("AttachSettingsMenu", statics, [typeof(Assembly)])!.Invoke(null, [assembly]);
        Click("GRAPH1CS");
        Assert(Labels().Length == 6 && Labels().Any(label => label.StartsWith("RES0LUTI0N:")) && Labels().Any(label => label.StartsWith("VSYNC:")), "Desktop display controls missing.");
        Console.WriteLine("IOS_MENU_OK; shared Settings; mobile Graphics constraints; staggered rows across widths; subpage navigation; desktop controls; touch Back capture");
    }

    private static void CheckPlayerName(Assembly assembly)
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var hooks = typeof(DesktopDisplay);
        var gameName = assembly.GetType("ZombiesWP7.Game1", true)!.GetField("gamerName", statics)!;
        var previousName = gameName.GetValue(null);
        var directory = Path.Combine(Path.GetTempPath(), "z0mb1es-name-edit-" + Guid.NewGuid().ToString("N"));
        void Set(string field, object? value) => hooks.GetField(field, statics)!.SetValue(null, value);
        object? Get(string field) => hooks.GetField(field, statics)!.GetValue(null);
        void Input(string text)
        {
            foreach (var character in text)
                hooks.GetMethod("OnNameTextInput", statics)!.Invoke(null, [character]);
        }
        void Update() => hooks.GetMethod("UpdateNameInput", statics)!.Invoke(null,
            [default(KeyboardState), default(GamePadState)]);
        void Begin()
        {
            Set("editing", 1);
            Set("nameText", "");
            Set("saveNameRequested", false);
            Set("cancelNameRequested", false);
        }
        try
        {
            PlayerIdentity.Configure(directory);
            Set("gamerNameField", gameName);
            var cached = new StringBuilder(PlayerIdentity.DefaultName);
            gameName.SetValue(null, cached);
            Begin();
            Input("Pause Hero\r\n");
            Update(); // Software Return was pressed and released between frames.
            if (PlayerIdentity.Name != "Pause Hero" || cached.ToString() != "Pause Hero" ||
                !ReferenceEquals(gameName.GetValue(null), cached) || (int)Get("editing")! != -1 ||
                (string)Get("nameText")! != "Pause Hero")
                throw new Exception("Software Return did not save and hot-apply the name to the game's shared builder.");
            PlayerIdentity.Configure(directory); // Restart with the same save root.
            if (PlayerIdentity.Name != "Pause Hero" ||
                Microsoft.Xna.Framework.GamerServices.Gamer.SignedInGamers[PlayerIndex.One].Gamertag != "Pause Hero")
                throw new Exception("Name from the pause editor did not survive restart.");
            Begin();
            Input("Discarded\u001b");
            Update();
            if (PlayerIdentity.Name != "Pause Hero" || cached.ToString() != "Pause Hero")
                throw new Exception("Cancelling a name edit changed the saved or displayed name.");
            Begin();
            Input("\r");
            Update();
            if ((int)Get("editing")! != 1 || PlayerIdentity.Name != "Pause Hero")
                throw new Exception("Blank name was saved or closed the editor.");
            Input("New Hero\n");
            Update();
            if (PlayerIdentity.Name != "New Hero" || cached.ToString() != "New Hero")
                throw new Exception("Correcting an invalid name did not submit through software Return.");
            // A failed disk write must keep the old identity and the editor open.
            var blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "not a directory");
            PlayerIdentity.Configure(blocked);
            Begin();
            Input("Unsaved\r");
            Update();
            if ((int)Get("editing")! != 1 || PlayerIdentity.Name != PlayerIdentity.DefaultName ||
                cached.ToString() != "New Hero")
                throw new Exception("A failed name save changed the identity or closed the editor.");
            Input("\u001b");
            Update();
            Console.WriteLine("PLAYER_NAME_EDIT_OK; software Return; immediate shared-name refresh; restart persistence; cancel; invalid input; failed save");
        }
        finally
        {
            gameName.SetValue(null, previousName);
            PlayerIdentity.Configure(Path.Combine(directory, "empty"));
            Set("nameText", PlayerIdentity.Name);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

}
