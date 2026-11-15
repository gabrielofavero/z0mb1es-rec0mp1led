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
        var directory = Path.Combine(Path.GetTempPath(), "z0mb1es-mobile-menu-" + Guid.NewGuid().ToString("N"));
        DesktopStorage.Configure(directory);
        try { RunChecks(gamePath); }
        finally
        {
            DesktopStorage.Configure(null);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void RunChecks(string gamePath)
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
        var accel = Activator.CreateInstance(Type("ZombiesWP7.AccelMgr"))!;
        accel.GetType().GetField("state", instances)!.SetValue(accel, new Vector3(0, 0, -0.5f));
        Type("ZombiesWP7.Game1").GetField("accelMgr", statics)!.SetValue(null, accel);
        Type("ZombiesWP7.Game1").GetField("frameTime", statics)!.SetValue(null, 1f / 60f);
        var animate = itemType.GetMethod("Update", instances)!;
        void CheckAnimatedRows(IEnumerable<object> rows)
        {
            foreach (var row in rows)
            {
                var target = (Vector2)location.GetValue(row)!;
                if (target.X < -900) continue;
                for (var frame = 0; frame < 60; frame++) animate.Invoke(row, null);
                Assert(Vector2.Distance(target, (Vector2)tilt.GetValue(row)!) < 0.01f,
                    "Menu animation collapsed a portrait row instead of retaining its draw/hit position.");
            }
        }
        var hooks = typeof(DesktopDisplay);
        void Set(string name, object? value) => hooks.GetField(name, statics)!.SetValue(null, value);
        void Invoke(string name) => hooks.GetMethod(name, statics)!.Invoke(null, null);
        CheckSafeArea();
        Set("mobile", true);
        Assert(!DesktopDisplay.ResponsiveOrientationEnabled, "Auto rotate must default off on mobile.");
        Set("menuScaleField", Type("ZombiesWP7.Game1").GetField("MENUSCALE", statics));
        hooks.GetMethod("AttachSettingsMenu", statics, [typeof(Assembly)])!.Invoke(null, [assembly]);
        var settings = levels[5]!;
        var text = itemType.GetField("text", instances)!;
        object[] Items() => ((Array)itemField.GetValue(settings)!).Cast<object>().ToArray();
        string[] Labels() => Items().Select(item => (string)text.GetValue(item)!).ToArray();
        void CheckStaggeredRows()
        {
            var visible = Items().Where(row => ((Vector2)location.GetValue(row)!).X > -900).ToArray();
            for (var i = 1; i < visible.Length; i++)
            {
                var center = DesktopDisplay.SceneSize.X / (2f * scale);
                var before = ((Vector2)location.GetValue(visible[i - 1])!).X - center;
                var after = ((Vector2)location.GetValue(visible[i])!).X - center;
                Assert(before * after < 0, "Settings rows must alternate left/right on every page.");
            }
        }
        void Click(string label)
        {
            Assert(DesktopDisplay.HandleSettingsClick(settings, label), "Settings click was not handled: " + label);
            CheckStaggeredRows();
            if (label == "GRAPH1CS") Assert((string)levelType.GetField("title", instances)!.GetValue(settings)! == "graph1cs", "Graphics category title differs from its entry.");
        }
        Assert(Labels().Contains("GRAPH1CS") && Labels().Contains("AUD10") && Labels().Contains("GAMEPLAY"), "Modified Settings categories missing.");
        var pauseOffsets = new Dictionary<object, Vector2>(ReferenceEqualityComparer.Instance);
        foreach (var key in new[] { 2, 3 })
            foreach (var item in (Array)itemField.GetValue(levels[key])!)
                pauseOffsets[item!] = (Vector2)location.GetValue(item)!;
        foreach (var (width, height) in new[] { (783, 360), (600, 1300), (600, 800), (600, 360), (900, 360), (783, 360) })
        {
            Set("sceneSize", (width, height));
            Invoke("CenterMenuItems");
            foreach (var key in new[] { 2, 3 })
            {
                var group = ((Array)itemField.GetValue(levels[key])!).Cast<object>().ToArray();
                CheckAnimatedRows(group);
                var xs = group.Select(item => ((Vector2)location.GetValue(item)!).X).ToArray();
                Assert(Math.Abs((xs.Min() + xs.Max()) / 2f - width / (2f * scale)) < 0.001f, "Pause group is off center.");
                foreach (var item in group)
                {
                    var actual = (Vector2)location.GetValue(item)! - (Vector2)location.GetValue(group[0])!;
                    var authored = pauseOffsets[item] - pauseOffsets[group[0]];
                    Assert(Math.Abs(actual.X - authored.X) < 0.001f, "Pause rows lost their horizontal stagger.");
                    if (height <= width) Assert(Math.Abs(actual.Y - authored.Y) < 0.001f, "Landscape row positions drifted after rotation.");
                }
                if (height > width)
                    Assert(group.All(row => ((Vector2)location.GetValue(row)!).Y * scale > height * 0.35f), "Portrait pause options are stuck at the top.");
            }
            Click("GRAPH1CS");
            CheckAnimatedRows(Items());
            var labels = Labels();
            Assert(labels.Length == 6 && labels[0].StartsWith("M0DE:") && labels[1] == "AUTO R0TATE: OFF" && labels[2].StartsWith("R3DUCE FLASH1NG:") && labels[3] == "SAFE AREA: ON" && labels[4] == "ASSETS" && labels[5] == "BACK", "Mobile Graphics rows differ.");
            Click("SAFE AREA: ON");
            Assert(!DesktopDisplay.SafeAreaEnabled && Labels()[3] == "SAFE AREA: OFF", "Mobile safe area toggle failed.");
            Click("SAFE AREA: OFF");
            Assert(DesktopDisplay.SafeAreaEnabled && Labels().SequenceEqual(labels), "Safe area did not restore immediately.");
            var offsets = new[] { -60f, 30f, -50f, 35f, -55f, 10f };
            for (var i = 0; i < offsets.Length; i++)
            {
                var item = Items()[i];
                var actual = (Vector2)location.GetValue(item)!;
                Assert(Math.Abs(actual.X - (width / (2f * scale) + offsets[i])) < 0.001f, "Mobile Graphics lost its stagger.");
                Assert(Vector2.Distance(actual, (Vector2)tilt.GetValue(item)!) < 0.001f, "Graphics hit and draw positions differ.");
            }
            var notified = false;
            void OrientationChanged(bool enabled) => notified = enabled;
            DesktopDisplay.ResponsiveOrientationChanged += OrientationChanged;
            Click("AUTO R0TATE: OFF");
            Assert(DesktopDisplay.ResponsiveOrientationEnabled && notified && Labels()[1] == "AUTO R0TATE: ON", "Auto rotate did not enable immediately.");
            var saved = File.ReadAllText(Path.Combine(DesktopStorage.SaveDirectory, "display.json"));
            Assert(saved.Contains("\"ResponsiveOrientation\":true"), "Auto rotate was not saved.");
            var reloaded = hooks.GetMethod("Load", statics)!.Invoke(null, null)!;
            Assert((bool)reloaded.GetType().GetProperty("ResponsiveOrientation")!.GetValue(reloaded)!, "Auto rotate did not survive loading preferences.");
            Click("AUTO R0TATE: ON");
            DesktopDisplay.ResponsiveOrientationChanged -= OrientationChanged;
            Assert(!DesktopDisplay.ResponsiveOrientationEnabled && !notified, "Auto rotate did not return to the landscape default.");
            if (height > width)
                Assert(((Vector2)location.GetValue(Items()[5])!).Y * scale > height * 0.55f &&
                    ((Vector2)location.GetValue(Items()[5])!).Y * scale < height * 0.9f, "Portrait options do not form a centered panel.");
            Click("VSYNC: OFF");
            Click("RES0LUTI0N: 600 X 360");
            Assert(Labels().SequenceEqual(labels), "Hidden display controls changed mobile settings.");
            Click("ASSETS");
            CheckAnimatedRows(Items());
            Assert(DesktopDisplay.IsAssetPage, "Assets page missing.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings) && DesktopDisplay.IsDisplayPage, "Assets Back did not return to Graphics.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings), "Graphics Back failed.");
            Click("GAMEPLAY");
            Click("1NPUT");
            Click("T0UCHPADS");
            CheckAnimatedRows(Items());
            Assert(DesktopDisplay.IsTouchpadPage && Labels().Any(label => label.StartsWith("TOUCHPAD SIZE:")), "Modified touchpad settings missing.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings) && DesktopDisplay.IsTouchPage, "Touchpads Back failed.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings) && DesktopDisplay.IsGameplayPage, "Input Back failed.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings), "Gameplay Back failed.");
            Click("AUD10");
            Assert(Labels().Contains("BACK"), "Audio Back missing.");
            Assert(DesktopDisplay.HandleSettingsCancel(settings), "Audio Back failed.");
        }
        // Starting a mode recreates the menus while the current view may already
        // be portrait. Exercise the new constructors, not only the old instances.
        Set("sceneSize", (600, 1300));
        Invoke("CenterMenuItems");
        levels[2] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Pause"))!;
        levels[3] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.EndGameYouSure"))!;
        levels[5] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Settings"))!;
        var freshPause = ((Array)itemField.GetValue(levels[2])!).Cast<object>().ToArray();
        var authoredYs = freshPause.Select(row => ((Vector2)location.GetValue(row)!).Y).ToArray();
        hooks.GetMethod("AttachSettingsMenu", statics, [typeof(Assembly)])!.Invoke(null, [assembly]);
        settings = levels[5]!;
        CheckAnimatedRows(freshPause);
        Assert(freshPause.All(row => ((Vector2)location.GetValue(row)!).Y * scale > 1300 * 0.35f), "Recreated portrait menu retained landscape row positions.");
        for (var rotation = 0; rotation < 5; rotation++)
        {
            Set("sceneSize", (783, 360)); Invoke("CenterMenuItems"); CheckAnimatedRows(freshPause);
            for (var row = 0; row < freshPause.Length; row++)
                Assert(Math.Abs(((Vector2)location.GetValue(freshPause[row])!).Y - authoredYs[row]) < 0.001f, "Recreated menu drifted or collapsed after returning to landscape.");
            Set("sceneSize", (600, 1300)); Invoke("CenterMenuItems"); CheckAnimatedRows(freshPause);
        }
        Set("sceneSize", (783, 360)); Invoke("CenterMenuItems");
        Assert(!DesktopDisplay.BuildVersionInfo()[0].ToString().Contains('+'), "User-facing version leaks the commit hash.");
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
        // Recreate Settings to verify the desktop exposes all seven Graphics rows.
        levels[5] = Activator.CreateInstance(Type("IMAK3Z0MB1EGAEM.menu.Settings"))!;
        settings = levels[5]!;
        hooks.GetMethod("AttachSettingsMenu", statics, [typeof(Assembly)])!.Invoke(null, [assembly]);
        Click("GRAPH1CS");
        Assert(DesktopDisplay.ResponsiveOrientationEnabled && !Labels().Any(label => label.StartsWith("AUTO R0TATE:")), "Desktop must support portrait without the mobile switch.");
        Assert(Labels().Length == 7 && Labels().Any(label => label.StartsWith("RES0LUTI0N:")) && Labels().Any(label => label.StartsWith("VSYNC:")) && Labels().Contains("SAFE AREA: ON"), "Desktop display controls missing.");
        var desktopOffsets = new[] { -60f, 40f, -45f, 50f, -35f, 55f, -10f };
        for (var i = 0; i < desktopOffsets.Length; i++)
        {
            var position = (Vector2)location.GetValue(Items()[i])!;
            Assert(Math.Abs(position.X - (783 / (2f * scale) + desktopOffsets[i])) < 0.001f,
                "Desktop Graphics lost its stagger.");
            Assert(position.Y * scale < 340, "Desktop Graphics row extends below the viewport.");
        }
        Console.WriteLine("IOS_MENU_OK; shared Settings; mobile Graphics constraints; staggered rows across widths; subpage navigation; desktop controls; touch Back capture");
    }

    private static void CheckSafeArea()
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var hooks = typeof(DesktopDisplay);
        var preferences = hooks.GetField("preferences", statics)!;
        var mobile = hooks.GetField("mobile", statics)!;
        var preferenceType = preferences.GetValue(null)!.GetType();
        object Fresh() => Activator.CreateInstance(preferenceType, ["Fullscreen", 600, 360])!;
        void Load(bool onMobile)
        {
            mobile.SetValue(null, onMobile);
            preferences.SetValue(null, Fresh());
            preferences.SetValue(null, hooks.GetMethod("Load", statics)!.Invoke(null, null));
        }
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        var path = Path.Combine(DesktopStorage.SaveDirectory, "display.json");
        Load(false);
        Assert(!DesktopDisplay.SafeAreaEnabled && DesktopDisplay.HudMargin == Vector2.Zero, "Fresh desktop safe area should be off.");
        Load(true);
        Assert(DesktopDisplay.SafeAreaEnabled, "Fresh mobile safe area should be on.");
        Directory.CreateDirectory(DesktopStorage.SaveDirectory);
        File.WriteAllText(path, "{\"Mode\":\"Fullscreen\",\"Width\":600,\"Height\":360}");
        Load(false);
        Assert(!DesktopDisplay.SafeAreaEnabled, "Legacy desktop save should default safe area off.");
        Load(true);
        Assert(DesktopDisplay.SafeAreaEnabled, "Legacy mobile save should default safe area on.");
        var margin = DesktopDisplay.HudMargin;
        Assert(margin.X > 0 && margin.Y > 0, "Safe area needs both horizontal and vertical margins.");
        foreach (var size in new[] { new Vector2(600, 360), new Vector2(783, 360), new Vector2(600, 450), new Vector2(900, 360), new Vector2(600, 1300), new Vector2(600, 800) })
        {
            var pause = DesktopDisplay.SafePausePosition(size.X - 40, 30);
            Assert(pause == new Vector2(size.X - 40 - margin.X, 30 + margin.Y) && DesktopInput.PauseHitTest(pause, size), "Inset pause glyph and hitbox differ.");
            Assert(!DesktopInput.PauseHitTest(new Vector2(size.X - 11, 1), size), "Old pause edge still receives taps.");
            Assert(DesktopInput.VirtualStickCenter(size, 0.75f, false) == new Vector2(90, size.Y - 90) + new Vector2(margin.X, -margin.Y) &&
                DesktopInput.VirtualStickCenter(size, 0.75f, true) == new Vector2(size.X - 90, size.Y - 90) - margin, "Fixed input and overlay centers differ.");
            Assert(DesktopDisplay.SafeHudLeft(120) == 120 + margin.X && DesktopDisplay.SafeHudTop(-25) == -25 + margin.Y &&
                DesktopDisplay.SafeHudBottom(size.Y) == size.Y - margin.Y, "Player or moon HUD margin differs.");
            Assert(DesktopDisplay.MapPointer(0, 0, new Rectangle(0, 0, (int)size.X, (int)size.Y), ((int)size.X, (int)size.Y)) == Vector2.Zero,
                "Safe area restricted full-screen relative input.");
        }
        var scene = hooks.GetField("sceneSize", statics)!;
        var nativeInsets = hooks.GetField("mobileSafeInsets", statics)!;
        var oldScene = scene.GetValue(null);
        scene.SetValue(null, (600, 1300));
        nativeInsets.SetValue(null, new Vector4(0, 0.09f, 0, 0.04f));
        Assert(DesktopDisplay.SafePausePosition(560, 30).Y >= 147 &&
            DesktopInput.VirtualStickCenter(new Vector2(600, 1300), 0.75f, false).Y <= 1158,
            "Portrait HUD or sticks overlap the native cutout/system bar.");
        preferenceType.GetProperty("SafeArea")!.SetValue(preferences.GetValue(null), false);
        Assert(DesktopDisplay.HudMargin == Vector2.Zero && DesktopDisplay.SafePausePosition(560, 30).Y == 30,
            "Native insets ignored the user's safe-area opt-out.");
        nativeInsets.SetValue(null, Vector4.Zero);
        scene.SetValue(null, oldScene);
        // Save explicit choices and reload on each host, including a mobile opt-out.
        foreach (var enabled in new[] { false, true })
        {
            preferenceType.GetProperty("SafeArea")!.SetValue(preferences.GetValue(null), enabled);
            hooks.GetMethod("Save", statics)!.Invoke(null, null);
            foreach (var onMobile in new[] { false, true })
            {
                Load(onMobile);
                Assert(DesktopDisplay.SafeAreaEnabled == enabled, "Saved safe area choice did not survive restart.");
            }
        }
        File.Delete(path);
        Load(true);
        mobile.SetValue(null, false);
        Assert(DesktopDisplay.SafePausePosition(560, 30) == new Vector2(560, 30) &&
            DesktopInput.VirtualStickCenter(new Vector2(600, 360), 0.75f, false) == new Vector2(90, 270), "Disabled safe area changed layout.");
        Console.WriteLine("SAFE_AREA_OK; host defaults; legacy save migration; explicit choice persistence; HUD/overlay/input margins; full-screen pointer input");
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
