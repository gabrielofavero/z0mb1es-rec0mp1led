using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace Port.Hooks;

/// <summary>Supplies desktop contacts at the original TouchMgr input boundary.</summary>
public static class DesktopInput
{
    private static Game? game;
    private static readonly Queue<GestureSample> gestures = new();
    private static readonly Contact[] contacts = [new(100), new(101), new(102), new(103)];
    private static bool previousAccept;
    private static bool previousBack;
    private static bool previousRightMouse;
    private static bool previousLeftMouse;
    private static bool suppressLeftClick;
    private static int previousWheel;
    private static Point previousMousePosition;
    private static CursorSource cursorSource;
    private static object? focusedLevel;
    private static object? focusedItems;
    private static int focusedIndex;
    private static int previousDirection;
    private static long nextNavigationTick;
    private const float CarouselSwipeSeconds = 0.36f;
    private static object? focusedItem;
    private static bool focusVisible;
    private static bool carouselAnimating;
    private static float carouselStart;
    private static int carouselTarget;
    private static long carouselStartedAt;
    private static int? returnCarouselSlot;
    private static FieldInfo? modeField;
    private static FieldInfo? viewScaleField;
    private static FieldInfo? menuLevelsField;
    private static MethodInfo? menuActiveMethod;
    private static FieldInfo? menuLevelActiveField;
    private static FieldInfo? menuLevelAlphaField;
    private static FieldInfo? menuLevelItemsField;
    private static FieldInfo? menuLevelScrollField;
    private static FieldInfo? menuLevelScrollMaxField;
    private static FieldInfo? leaderboardReadingField;
    private static MethodInfo? leaderboardClickMethod;
    private static FieldInfo? frameTimeField;
    private static FieldInfo? menuItemFlagField;
    private static FieldInfo? menuItemPositionField;
    private static FieldInfo? menuItemClickFrameField;
    private static FieldInfo? menuItemTextField;
    private static FieldInfo? menuItemCheckTextField;
    private static FieldInfo? menuItemLayerField;
    private static MethodInfo? textWidthMethod;
    private static FieldInfo? menuScaleField;
    private static FieldInfo? mainMenuField;
    private static FieldInfo? mainMenuSceneField;
    private static FieldInfo? mainMenuSwipeField;
    private static FieldInfo? mainMenuInFrameField;
    private static FieldInfo? mainMenuOutFrameField;
    private static FieldInfo? sceneVideoField;
    private static FieldInfo? videoTimeField;
    private static Func<Vector2?>? zombieOrigin;
    private static Func<Vector2?>? vikingOrigin;
    public static int Samples { get; private set; }
    public static int ActiveSamples { get; private set; }
    public static int MouseSamples { get; private set; }
    public static int KeyboardSamples { get; private set; }

    public static void Configure(Game runningGame)
    {
        game = runningGame;
        gestures.Clear();
        foreach (var contact in contacts) contact.Reset();
        previousAccept = previousBack = false;
        previousRightMouse = previousLeftMouse = suppressLeftClick = false;
        var initialMouse = Mouse.GetState();
        previousWheel = initialMouse.ScrollWheelValue;
        previousMousePosition = new Point(initialMouse.X, initialMouse.Y);
        cursorSource = CursorSource.None;
        focusedLevel = null;
        focusedItems = null;
        focusedItem = null;
        focusVisible = false;
        focusedIndex = previousDirection = 0;
        nextNavigationTick = 0;
        carouselAnimating = false;
        carouselStart = carouselTarget = 0;
        carouselStartedAt = 0;
        returnCarouselSlot = null;
        Samples = ActiveSamples = MouseSamples = KeyboardSamples = 0;
        var assembly = runningGame.GetType().Assembly;
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        modeField = RequireType(assembly, "IMAK3Z0MB1EGAEM.director.GameState").GetField("state", staticFlags)
            ?? throw new MissingFieldException("GameState.state");
        viewScaleField = RequireField(runningGame.GetType(), "VIEWSCALE", staticFlags);
        var menu = RequireType(assembly, "IMAK3Z0MB1EGAEM.menu.Menu");
        menuLevelsField = menu.GetField("menuLevel", staticFlags)
            ?? throw new MissingFieldException("Menu.menuLevel");
        menuActiveMethod = menu.GetMethod("GetActive", staticFlags)
            ?? throw new MissingMethodException("Menu.GetActive");
        var menuLevel = RequireType(assembly, "IMAK3Z0MB1EGAEM.menu.MenuLevel");
        menuLevelActiveField = RequireField(menuLevel, "active", instanceFlags);
        menuLevelAlphaField = RequireField(menuLevel, "alpha", instanceFlags);
        menuLevelItemsField = RequireField(menuLevel, "item", instanceFlags);
        menuLevelScrollField = RequireField(menuLevel, "scroll", instanceFlags);
        menuLevelScrollMaxField = RequireField(menuLevel, "scrollMax", instanceFlags);
        var leaderboardType = RequireType(assembly, "IMAK3Z0MB1EGAEM.menu.Leaderboards");
        leaderboardReadingField = RequireField(leaderboardType, "reading", instanceFlags);
        leaderboardClickMethod = leaderboardType.GetMethod("Click", instanceFlags)
            ?? throw new MissingMethodException("Leaderboards.Click");
        frameTimeField = RequireField(runningGame.GetType(), "frameTime", staticFlags);
        var menuItem = RequireType(assembly, "ZombiesWP7.menu.MenuItem");
        menuItemFlagField = RequireField(menuItem, "flag", instanceFlags);
        menuItemPositionField = RequireField(menuItem, "tiltLoc", instanceFlags);
        menuItemClickFrameField = RequireField(menuItem, "clickFrame", instanceFlags);
        menuItemTextField = RequireField(menuItem, "text", instanceFlags);
        menuItemCheckTextField = RequireField(menuItem, "checkText", instanceFlags);
        menuItemLayerField = RequireField(menuItem, "layer", instanceFlags);
        textWidthMethod = RequireType(assembly, "IMAK3Z0MB1EGAEM.Text")
            .GetMethod("GetStringWidth", staticFlags, [typeof(string)])
            ?? throw new MissingMethodException("Text.GetStringWidth");
        menuScaleField = RequireField(runningGame.GetType(), "MENUSCALE", staticFlags);
        mainMenuField = RequireField(runningGame.GetType(), "mainMenu", staticFlags);
        var mainMenu = RequireType(assembly, "Viking_x86.main.MainMenu");
        mainMenuSceneField = RequireField(mainMenu, "sceneMgr", instanceFlags);
        mainMenuSwipeField = RequireField(mainMenu, "swipeCoast", instanceFlags);
        mainMenuInFrameField = RequireField(mainMenu, "inFrame", instanceFlags);
        mainMenuOutFrameField = RequireField(mainMenu, "outFrame", instanceFlags);
        sceneVideoField = RequireField(RequireType(assembly, "SceneEdit.scene.SceneMgr"), "video", instanceFlags);
        videoTimeField = RequireField(RequireType(assembly, "SceneEdit.scene.Video"), "time", instanceFlags);

        var heroes = RequireType(assembly, "IMAK3Z0MB1EGAEM.character.CharMan")
            .GetField("hero", staticFlags) ?? throw new MissingFieldException("CharMan.hero");
        var heroLoc = RequireType(assembly, "IMAK3Z0MB1EGAEM.character.Hero")
            .GetField("loc", instanceFlags) ?? throw new MissingFieldException("Hero.loc");
        var zombieScreen = ScreenMethod(RequireType(assembly, "IMAK3Z0MB1EGAEM.ScrollMan"));
        zombieOrigin = () => ScreenPosition((heroes.GetValue(null) as Array)?.GetValue(0), heroLoc, zombieScreen);

        var vgame = runningGame.GetType().GetField("vgame", staticFlags)
            ?? throw new MissingFieldException("Game1.vgame");
        var manager = RequireType(assembly, "Viking_x86.VikingGame").GetField("charMgr", instanceFlags)
            ?? throw new MissingFieldException("VikingGame.charMgr");
        var characters = RequireType(assembly, "Viking_x86.character.CharacterMgr")
            .GetField("character", instanceFlags) ?? throw new MissingFieldException("CharacterMgr.character");
        var characterLoc = RequireType(assembly, "Viking_x86.character.Character")
            .GetField("loc", instanceFlags) ?? throw new MissingFieldException("Character.loc");
        var vikingScreen = ScreenMethod(RequireType(assembly, "Viking_x86.VScroll"));
        vikingOrigin = () =>
        {
            var running = vgame.GetValue(null);
            var characterManager = running is null ? null : manager.GetValue(running);
            return ScreenPosition(characterManager is null ? null :
                (characters.GetValue(characterManager) as Array)?.GetValue(0), characterLoc, vikingScreen);
        };
    }

    public static GamePadState GetGamePadState(PlayerIndex player)
    {
        if (DesktopDisplay.CapturesInput) return default;
        var state = GamePad.GetState(player);
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var pointer = game is null ? null : DesktopDisplay.MapPointer(mouse.X, mouse.Y, game.Window.ClientBounds);
        var backClick = (game?.IsActive ?? false) && pointer is Vector2 position &&
            BackButtonVisible() && BackButtonBounds(DesktopDisplay.SceneSize).Contains(new Point((int)position.X, (int)position.Y));
        var rightMouse = (game?.IsActive ?? false) && mouse.RightButton == ButtonState.Pressed;
        var leftMouse = (game?.IsActive ?? false) && mouse.LeftButton == ButtonState.Pressed;
        var leftBack = backClick && leftMouse && !previousLeftMouse;
        if (!leftMouse) suppressLeftClick = false;
        if (leftBack) suppressLeftClick = true;
        var mouseBack = (rightMouse && !previousRightMouse) ||
            leftBack;
        previousRightMouse = rightMouse;
        previousLeftMouse = leftMouse;
        var playing = IsPlaying(ReadMode());
        var back = (game?.IsActive ?? false) &&
            (keyboard.IsKeyDown(Keys.Back) || keyboard.IsKeyDown(Keys.Escape) ||
             (playing ? state.Buttons.Start == ButtonState.Pressed ||
                 (MenuIsActive() && (state.Buttons.B == ButtonState.Pressed ||
                                     state.Buttons.Back == ButtonState.Pressed))
                 : state.Buttons.B == ButtonState.Pressed ||
                   state.Buttons.Back == ButtonState.Pressed));
        var pressed = (back && !previousBack) || mouseBack;
        previousBack = back;
        return pressed ? new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.Back) :
            state.Buttons.Back == ButtonState.Pressed ?
                new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0) : state;
    }

    public static TouchCollection GetState()
    {
        var mode = ReadMode();
        RestoreCarouselOnReturn(mode);
        DesktopPictures.Update();
        Samples++;
        if (DesktopDisplay.CapturesInput)
        {
            var capturedMouse = Mouse.GetState();
            previousWheel = capturedMouse.ScrollWheelValue;
            previousMousePosition = new Point(capturedMouse.X, capturedMouse.Y);
            cursorSource = CursorSource.None;
            gestures.Clear();
            foreach (var contact in contacts) contact.Reset();
            return new TouchCollection([]);
        }
        var native = TouchPanel.GetState();
        if (game is null) return native;
        var touches = new List<TouchLocation>(native.Count + contacts.Length);
        foreach (var touch in native) touches.Add(touch);

        var active = game.IsActive;
        if (active) ActiveSamples++;
        var keyboard = active ? Keyboard.GetState() : default;
        var pad = active ? GamePad.GetState(PlayerIndex.One) : default;
        var playing = IsPlaying(mode) && !MenuIsActive();
        var left = Direction(keyboard, Keys.A, Keys.D, Keys.W, Keys.S) +
            new Vector2(pad.ThumbSticks.Left.X, -pad.ThumbSticks.Left.Y);
        var right = Direction(keyboard, Keys.Left, Keys.Right, Keys.Up, Keys.Down) +
            new Vector2(pad.ThumbSticks.Right.X, -pad.ThumbSticks.Right.Y);
        var size = DesktopDisplay.SceneSize;
        var viewScale = (float)viewScaleField!.GetValue(null)!;
        const float radius = 80f;
        var leftCenter = VirtualStickCenter(size, viewScale, right: false);
        var rightCenter = VirtualStickCenter(size, viewScale, right: true);
        contacts[0].Sample(active && playing && left.LengthSquared() > 0.04f,
            leftCenter + Clamp(left) * radius, touches, pressOrigin: leftCenter);
        contacts[1].Sample(active && playing && right.LengthSquared() > 0.04f,
            rightCenter + Clamp(right) * radius, touches, pressOrigin: rightCenter);

        var mouse = Mouse.GetState();
        var wheelDelta = mouse.ScrollWheelValue - previousWheel;
        previousWheel = mouse.ScrollWheelValue;
        var pointer = DesktopDisplay.MapPointer(mouse.X, mouse.Y, game.Window.ClientBounds);
        var inside = active && pointer is not null;
        UpdateCursorSource(native.Count > 0, mouse, keyboard, wheelDelta, inside && mode != 0);
        var backClick = inside && pointer is Vector2 backPosition && BackButtonVisible() &&
            BackButtonBounds(size).Contains(new Point((int)backPosition.X, (int)backPosition.Y));
        var pauseHotspot = pointer is Vector2 point && point.X > size.X - 70 &&
            point.X < size.X - 10 && point.Y >= 0 && point.Y < 60;
        if (inside && (mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed)) MouseSamples++;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.S) ||
            keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Enter) ||
            keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.Right) ||
            keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.Down)) KeyboardSamples++;
        var shoot = inside && playing && !pauseHotspot && !backClick && !suppressLeftClick &&
            mouse.LeftButton == ButtonState.Pressed;
        var origin = shoot ? (mode == 5 ? vikingOrigin?.Invoke() : zombieOrigin?.Invoke()) : null;
        var aim = pointer is Vector2 target && origin is Vector2 player
            ? AimDirection(target, player) : Vector2.Zero;
        contacts[2].Sample(shoot && origin is not null && aim != Vector2.Zero,
            rightCenter + aim * radius, touches, pressOrigin: rightCenter);
        var pointAndClick = inside && !suppressLeftClick && !backClick &&
            mouse.LeftButton == ButtonState.Pressed && (!playing || pauseHotspot);
        contacts[3].Sample(pointAndClick, pointer ?? Vector2.Zero,
            touches, inside ? gestures : null);

        var level = active ? ActiveMenuLevel() : null;
        var leaderboard = level?.GetType().FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards";
        if (level?.GetType().FullName is "IMAK3Z0MB1EGAEM.menu.Achievements" or
            "IMAK3Z0MB1EGAEM.menu.Credits")
            ScrollLongMenu(level, inside ? wheelDelta : 0, pad.ThumbSticks.Right.Y,
                (float)frameTimeField!.GetValue(null)!);
        if (leaderboard && level is not null && LeaderboardIsReading(level))
            menuLevelScrollField!.SetValue(level, 0f);
        if (leaderboard && level is not null && inside && wheelDelta != 0 && !LeaderboardIsReading(level))
        {
            if ((float)menuLevelScrollMaxField!.GetValue(level)! > 0f)
                ScrollLeaderboards(level, wheelDelta, 0, 0f);
            else
                TurnLeaderboard(level, wheelDelta < 0 ? 1 : -1);
        }
        var hoverItems = level is null ? [] : ClickableItems(level);
        var items = leaderboard ? [] : hoverItems;
        var itemArray = level is null ? null : menuLevelItemsField!.GetValue(level);
        if (!ReferenceEquals(level, focusedLevel) || !ReferenceEquals(itemArray, focusedItems))
        {
            focusedLevel = level;
            focusedItems = itemArray;
            focusedIndex = InitialFocus(items);
            previousDirection = 0;
            nextNavigationTick = 0;
        }
        else if ((focusedIndex < 0 && items.Count > 0) || focusedIndex >= items.Count)
            focusedIndex = InitialFocus(items);
        var direction = active ? MenuNavigation.Direction(keyboard, pad) : Vector2.Zero;
        var directionCode = direction.X < 0 ? -1 : direction.X > 0 ? 1 :
            direction.Y < 0 ? -2 : direction.Y > 0 ? 2 : 0;
        var now = Stopwatch.GetTimestamp();
        var navigate = directionCode != 0 &&
            (directionCode != previousDirection || now >= nextNavigationTick);
        if (navigate)
        {
            if (level is not null)
            {
                if (leaderboard)
                {
                    if (direction.X != 0) TurnLeaderboard(level, (int)direction.X);
                    else if (direction.Y != 0) ScrollLeaderboards(level, 0, (int)direction.Y, 0f);
                }
                else
                {
                    focusedIndex = MenuNavigation.Next(items.Select(item => item.Position).ToArray(),
                        focusedIndex, (int)direction.X, (int)direction.Y);
                    focusVisible = true;
                }
            }
            else if (mode == 1 && direction.X != 0)
                MoveCarousel((int)direction.X);
            nextNavigationTick = now + (long)(Stopwatch.Frequency *
                (directionCode != previousDirection ? 0.35 : 0.12));
        }
        previousDirection = directionCode;
        if (leaderboard && level is not null && MathF.Abs(pad.ThumbSticks.Right.Y) >= 0.2f)
            ScrollLeaderboards(level, 0, 0, pad.ThumbSticks.Right.Y);
        if (inside && mode == 1 && level is null && wheelDelta != 0)
            MoveCarousel(wheelDelta < 0 ? 1 : -1);
        if (active && mode == 1 && level is null && native.Count == 0 && !pointAndClick)
            AdvanceCarousel(now);
        else
            carouselAnimating = false;
        var hovered = cursorSource == CursorSource.Mouse && inside && pointer is Vector2 hoverPoint &&
            level is not null && (float)menuLevelAlphaField!.GetValue(level)! > 0.9f
            ? HoveredItem(hoverItems, hoverPoint) : -1;
        if (cursorSource == CursorSource.Mouse && level is not null)
        {
            focusedItem = hovered >= 0 ? hoverItems[hovered].Item : null;
            focusVisible = focusedItem is not null;
            if (!leaderboard && hovered >= 0) focusedIndex = hovered;
        }
        else if (level is not null && focusedIndex >= 0 && focusedIndex < items.Count)
        {
            var selected = items[focusedIndex].Item;
            focusedItem = selected;
        }
        else focusedItem = null;
        if (focusedItem is { } highlighted)
        {
            var frame = (float)menuItemClickFrameField!.GetValue(highlighted)!;
            if (frame < 0.07f) menuItemClickFrameField.SetValue(highlighted, 0.07f);
        }
        var accept = active && (keyboard.IsKeyDown(Keys.Enter) ||
            pad.Buttons.A == ButtonState.Pressed);
        if (accept && !previousAccept)
        {
            if (level is not null && focusedIndex >= 0 && focusedIndex < items.Count &&
                (float)menuLevelAlphaField!.GetValue(level)! > 0.9f)
                gestures.Enqueue(Tap(items[focusedIndex].Position * (float)menuScaleField!.GetValue(null)!));
            else if (level is null && mode is 1 or 2 or 4 or 6)
            {
                if (mode == 1) FinishCarousel();
                gestures.Enqueue(Tap(size / 2f));
            }
        }
        previousAccept = accept;
        return new TouchCollection(touches.ToArray());
    }

    public static bool IsGestureAvailable => !DesktopDisplay.CapturesInput &&
        (TouchPanel.IsGestureAvailable || gestures.Count > 0);

    public static GestureSample ReadGesture() => TouchPanel.IsGestureAvailable
        ? TouchPanel.ReadGesture()
        : gestures.Dequeue();

    private static GestureSample Tap(Vector2 position) =>
        new(GestureType.Tap, TimeSpan.Zero, position, Vector2.Zero, Vector2.Zero, Vector2.Zero);

    public static bool IsFocusedMenuItem(object item) => focusVisible && ReferenceEquals(item, focusedItem);

    public static bool CursorVisible => game?.IsActive == true && ReadMode() != 0 &&
        cursorSource == CursorSource.Mouse;

    public static bool ShouldShowTouchPause() => cursorSource == CursorSource.Touch;

    private static void UpdateCursorSource(bool touchActive, MouseState mouse,
        KeyboardState keyboard, int wheelDelta, bool inside)
    {
        var moved = mouse.X != previousMousePosition.X || mouse.Y != previousMousePosition.Y;
        previousMousePosition = new Point(mouse.X, mouse.Y);
        if (touchActive)
            cursorSource = CursorSource.Touch;
        else if (inside && (moved || wheelDelta != 0 ||
                 mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed ||
                 mouse.MiddleButton == ButtonState.Pressed))
            cursorSource = CursorSource.Mouse;
        else if (keyboard.GetPressedKeys().Length > 0)
            cursorSource = CursorSource.Keyboard;
        else if (ControllerActive())
            cursorSource = CursorSource.Controller;
    }

    private static bool ControllerActive()
    {
        for (var player = PlayerIndex.One; player <= PlayerIndex.Four; player++)
        {
            var pad = GamePad.GetState(player);
            if (!pad.IsConnected) continue;
            if (pad.ThumbSticks.Left.LengthSquared() > 0.09f ||
                pad.ThumbSticks.Right.LengthSquared() > 0.09f ||
                pad.Triggers.Left > 0.15f || pad.Triggers.Right > 0.15f ||
                pad.Buttons.A == ButtonState.Pressed || pad.Buttons.B == ButtonState.Pressed ||
                pad.Buttons.X == ButtonState.Pressed || pad.Buttons.Y == ButtonState.Pressed ||
                pad.Buttons.Start == ButtonState.Pressed || pad.Buttons.Back == ButtonState.Pressed ||
                pad.Buttons.LeftShoulder == ButtonState.Pressed ||
                pad.Buttons.RightShoulder == ButtonState.Pressed ||
                pad.DPad.Up == ButtonState.Pressed || pad.DPad.Down == ButtonState.Pressed ||
                pad.DPad.Left == ButtonState.Pressed || pad.DPad.Right == ButtonState.Pressed)
                return true;
        }
        return false;
    }

    private enum CursorSource { None, Mouse, Keyboard, Controller, Touch }

    public static bool BackButtonVisible()
    {
        var level = ActiveMenuLevel();
        if (level is not null)
            return (float)menuLevelAlphaField!.GetValue(level)! > 0.9f;
        return ReadMode() is 2 or 4 or 6;
    }

    public static Rectangle BackButtonBounds(Vector2 size)
    {
        var width = Math.Max(44, (int)(size.X * 0.05f) + 12);
        return new Rectangle((int)(size.X * 0.05f) - width / 2,
            (int)(size.Y * 0.9f) - 20, width, 40);
    }

    private static Vector2 Direction(KeyboardState state, Keys negativeX, Keys positiveX,
        Keys negativeY, Keys positiveY) => new(
            (state.IsKeyDown(positiveX) ? 1 : 0) - (state.IsKeyDown(negativeX) ? 1 : 0),
            (state.IsKeyDown(positiveY) ? 1 : 0) - (state.IsKeyDown(negativeY) ? 1 : 0));

    private static Vector2 Clamp(Vector2 vector) => vector.LengthSquared() > 1
        ? Vector2.Normalize(vector) : vector;

    private static void ScrollLongMenu(object level, int wheelDelta, float rightStickY, float frameTime)
    {
        var current = (float)menuLevelScrollField!.GetValue(level)!;
        var maximum = (float)menuLevelScrollMaxField!.GetValue(level)!;
        menuLevelScrollField.SetValue(level, MenuNavigation.AchievementScroll(
            current, maximum, wheelDelta, rightStickY, frameTime));
    }

    private static void ScrollLeaderboards(object level, int wheelDelta, int verticalStep, float rightStickY)
    {
        if (LeaderboardIsReading(level)) return;
        var current = (float)menuLevelScrollField!.GetValue(level)!;
        var maximum = (float)menuLevelScrollMaxField!.GetValue(level)!;
        menuLevelScrollField.SetValue(level, MenuNavigation.LeaderboardScroll(current, maximum,
            wheelDelta, verticalStep, rightStickY, (float)frameTimeField!.GetValue(null)!));
    }

    private static void TurnLeaderboard(object level, int direction)
    {
        if (LeaderboardIsReading(level)) return;
        menuLevelScrollField!.SetValue(level, 0f);
        leaderboardClickMethod!.Invoke(level, [direction > 0 ? ">" : "<"]);
    }

    private static bool LeaderboardIsReading(object level) =>
        (bool)leaderboardReadingField!.GetValue(level)!;

    public static Vector2 VirtualStickCenter(Vector2 size, float viewScale, bool right)
    {
        var inset = 120f * viewScale;
        return new Vector2(right ? size.X - inset : inset, size.Y - inset);
    }

    private static void MoveCarousel(int direction)
    {
        var menu = mainMenuField?.GetValue(null);
        if (menu is null || (float)mainMenuInFrameField!.GetValue(menu)! > 0f ||
            (float)mainMenuOutFrameField!.GetValue(menu)! > 0f) return;
        var scene = mainMenuSceneField!.GetValue(menu);
        var video = scene is null ? null : sceneVideoField!.GetValue(scene);
        if (video is null) return;
        var time = (float)videoTimeField!.GetValue(video)!;
        var current = carouselAnimating ? carouselTarget : (int)MathF.Floor(time + 0.5f);
        var next = MenuNavigation.CarouselNext(current, direction);
        if (next == current) return;
        mainMenuSwipeField!.SetValue(menu, Vector2.Zero);
        carouselStart = time;
        carouselTarget = next;
        carouselStartedAt = Stopwatch.GetTimestamp();
        carouselAnimating = true;
    }

    private static void AdvanceCarousel(long now)
    {
        if (!carouselAnimating) return;
        var menu = mainMenuField?.GetValue(null);
        var scene = menu is null ? null : mainMenuSceneField!.GetValue(menu);
        var video = scene is null ? null : sceneVideoField!.GetValue(scene);
        if (video is null) { carouselAnimating = false; return; }
        var progress = (float)((now - carouselStartedAt) /
            (Stopwatch.Frequency * (double)CarouselSwipeSeconds));
        videoTimeField!.SetValue(video, MenuNavigation.CarouselSwipePosition(
            carouselStart, carouselTarget, progress));
        if (progress >= 1f) carouselAnimating = false;
    }

    private static void FinishCarousel()
    {
        if (!carouselAnimating) return;
        var menu = mainMenuField?.GetValue(null);
        var scene = menu is null ? null : mainMenuSceneField!.GetValue(menu);
        var video = scene is null ? null : sceneVideoField!.GetValue(scene);
        if (video is not null) videoTimeField!.SetValue(video, (float)carouselTarget);
        carouselAnimating = false;
    }

    private static void RestoreCarouselOnReturn(int mode)
    {
        if (mode is 4 or 5) returnCarouselSlot = 1;
        else if (mode is 6 or 7) returnCarouselSlot = 2;
        else if (mode is 2 or 3) returnCarouselSlot = null;
        else if (mode == 1 && returnCarouselSlot is int slot)
        {
            var menu = mainMenuField?.GetValue(null);
            var scene = menu is null ? null : mainMenuSceneField!.GetValue(menu);
            var video = scene is null ? null : sceneVideoField!.GetValue(scene);
            if (video is null) return;
            videoTimeField!.SetValue(video, (float)slot);
            carouselAnimating = false;
            returnCarouselSlot = null;
        }
    }

    private static object? ActiveMenuLevel()
    {
        if (menuLevelsField?.GetValue(null) is not IDictionary levels) return null;
        try
        {
            foreach (DictionaryEntry entry in levels)
                if (entry.Value is { } level && (bool)menuLevelActiveField!.GetValue(level)!)
                    return level;
        }
        catch (InvalidOperationException)
        {
            // The original loading thread can add levels while input is sampled.
            return null;
        }
        return null;
    }

    private static List<(object Item, Vector2 Position)> ClickableItems(object level)
    {
        var result = new List<(object, Vector2)>();
        if (menuLevelItemsField!.GetValue(level) is not Array items) return result;
        foreach (var item in items)
        {
            if (item is null || DesktopDisplay.IsStaticPlayerNameLabel(item) ||
                (int)menuItemFlagField!.GetValue(item)! is not (0 or 1)) continue;
            var position = (Vector2)menuItemPositionField!.GetValue(item)!;
            if (position.X < 0 || position.Y < 0) continue;
            result.Add((item, position));
        }
        return result;
    }

    private static int HoveredItem(List<(object Item, Vector2 Position)> items, Vector2 pointer)
    {
        var scale = (float)menuScaleField!.GetValue(null)!;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i].Item;
            var flag = (int)menuItemFlagField!.GetValue(item)!;
            var label = flag == 1
                ? ((string[])menuItemCheckTextField!.GetValue(item)!)[0]
                : (string)menuItemTextField!.GetValue(item)!;
            var width = (float)textWidthMethod!.Invoke(null, [label])!;
            var layer = (float)menuItemLayerField!.GetValue(item)!;
            if (MenuNavigation.HitTest(pointer, items[i].Position * scale,
                    width * layer * scale, 5f * layer * scale))
                return i;
        }
        return -1;
    }

    private static int InitialFocus(List<(object Item, Vector2 Position)> items)
    {
        var index = -1;
        for (var i = 0; i < items.Count; i++)
            if (index < 0 || items[i].Position.Y < items[index].Position.Y ||
                (items[i].Position.Y == items[index].Position.Y &&
                 items[i].Position.X < items[index].Position.X)) index = i;
        return index;
    }

    public static Vector2 AimDirection(Vector2 pointer, Vector2 player)
    {
        var delta = pointer - player;
        return delta.LengthSquared() < 1f ? Vector2.Zero : Vector2.Normalize(delta);
    }

    private static int ReadMode() => (int?)modeField?.GetValue(null) ?? 0;
    private static bool IsPlaying(int mode) => mode is 3 or 5 or 7;
    private static bool MenuIsActive() => menuLevelsField?.GetValue(null) is not null &&
        (bool)(menuActiveMethod?.Invoke(null, null) ?? false);

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true)!;

    private static FieldInfo RequireField(Type type, string name, BindingFlags flags) =>
        type.GetField(name, flags) ?? throw new MissingFieldException(type.FullName, name);

    private static MethodInfo ScreenMethod(Type type) => type.GetMethods(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Single(method => method.Name == "GetScreenLoc" &&
            method.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual([typeof(Vector2), typeof(float)]));

    private static Vector2? ScreenPosition(object? actor, FieldInfo location, MethodInfo transform) =>
        actor is null ? null : (Vector2)transform.Invoke(null, [location.GetValue(actor)!, 1f])!;

    public static Vector2? MapPointer(int x, int y, Rectangle client, Vector2 target)
    {
        if (client.Width <= 0 || client.Height <= 0 || target.X <= 0 || target.Y <= 0)
            return null;
        var scale = MathF.Min(client.Width / target.X, client.Height / target.Y);
        var width = target.X * scale;
        var height = target.Y * scale;
        var localX = x - (client.Width - width) / 2f;
        var localY = y - (client.Height - height) / 2f;
        return localX >= 0 && localY >= 0 && localX < width && localY < height
            ? new Vector2(localX / scale, localY / scale) : null;
    }

    private sealed class Contact(int id)
    {
        private bool down;
        private Vector2 previous;
        private Vector2 start;
        private int frames;

        public void Reset() { down = false; previous = start = Vector2.Zero; frames = 0; }

        public void Sample(bool pressed, Vector2 position, List<TouchLocation> output,
            Queue<GestureSample>? tapQueue = null, Vector2? pressOrigin = null)
        {
            if (pressed)
            {
                if (!down && pressOrigin is not null) position = pressOrigin.Value;
                output.Add(new TouchLocation(id, down ? TouchLocationState.Moved : TouchLocationState.Pressed,
                    position, down ? TouchLocationState.Moved : TouchLocationState.Invalid, previous));
                if (!down) { start = position; frames = 0; }
                down = true;
                previous = position;
                frames++;
            }
            else if (down)
            {
                output.Add(new TouchLocation(id, TouchLocationState.Released, previous,
                    TouchLocationState.Moved, previous));
                if (tapQueue is not null && frames <= 30 && Vector2.DistanceSquared(start, previous) <= 225)
                    tapQueue.Enqueue(Tap(previous));
                down = false;
            }
        }
    }
}
