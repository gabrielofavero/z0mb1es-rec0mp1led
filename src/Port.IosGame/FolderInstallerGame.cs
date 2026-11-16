using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Port.Prepare;
using SDL3;

namespace Port.IosGame;

internal sealed record SetupRow(string Id, Rectangle Bounds, List<string> Lines, float Scale, Action? Action, bool Enabled);

internal sealed class FolderInstallerGame : Game
{
    private static readonly Color MenuRed = new(224, 0, 0);
    private static readonly Color DarkRed = new(112, 0, 0);
    private static readonly Color MutedText = new(184, 184, 184);
    private const int ButtonInset = 18;
    private static float LineHeight(float size) => 8 * size + 6;
    private readonly FolderSetup setup;
    private readonly Func<Game, Vector4> safeInsets;
    private readonly Action<string> log;
    private readonly GraphicsDeviceManager graphics;
    private readonly RasterizerState clipped = new() { ScissorTestEnable = true, CullMode = CullMode.None };
    private SpriteBatch batch = null!;
    private Texture2D pixel = null!;
    private SetupFont font = null!;
    private float scale = 1;
    private float scroll;
    private float contentBottom;
    private Rectangle viewport;
    private List<SetupRow> rows = [];
    private bool pressed;
    private bool dragged;
    private Vector2 pressPoint;
    private float pressScroll;
    private string? captured;
    private int? finger;
    private int wheel;
    private KeyboardState previousKeys;
    private int focus;
    private bool keyboardFocus;
    private SetupPage lastPage;
    private int frames;
    internal IReadOnlyList<SetupRow> Rows => rows;
    internal Rectangle SetupViewport => viewport;
    internal float ScrollOffset => scroll;

    internal FolderInstallerGame(FolderSetup setup, bool mobile, Func<Game, Vector4> safeInsets, Action<string> log)
    {
        this.setup = setup;
        this.safeInsets = safeInsets;
        this.log = log;
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960, PreferredBackBufferHeight = 600,
            IsFullScreen = mobile, SynchronizeWithVerticalRetrace = true
        };
        graphics.PreparingDeviceSettings += (_, args) =>
        {
            if (mobile && NativeSize(out var width, out var height))
            {
                args.GraphicsDeviceInformation.PresentationParameters.BackBufferWidth = width;
                args.GraphicsDeviceInformation.PresentationParameters.BackBufferHeight = height;
            }
        };
        Window.Title = "Z0MB1ES - Setup";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        Deactivated += (_, _) => setup.Cancel();
    }

    protected override void LoadContent()
    {
        batch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData([Color.White]);
        font = new SetupFont(GraphicsDevice);
        log("files-setup=graphics-ready");
        base.LoadContent();
    }

    private bool NativeSize(out int width, out int height) =>
        SDL.SDL_GetWindowSizeInPixels(Window.Handle, out width, out height) && width > 0 && height > 0;

    protected override void Update(GameTime gameTime)
    {
        setup.Poll();
        if (setup.LaunchRequested) { Exit(); return; }
        if (NativeSize(out var width, out var height))
        {
            var current = GraphicsDevice.PresentationParameters;
            if (current.BackBufferWidth != width || current.BackBufferHeight != height)
            {
                var next = current.Clone(); next.BackBufferWidth = width; next.BackBufferHeight = height;
                GraphicsDevice.Reset(next);
                pressed = false; finger = null;
            }
        }
        TouchPanel.DisplayWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
        TouchPanel.DisplayHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
        if (lastPage != setup.Page) { scroll = 0; focus = 0; pressed = false; lastPage = setup.Page; }
        Layout();
        var touches = TouchPanel.GetState();
        if (finger is null)
            foreach (var touch in touches)
                if (touch.State == TouchLocationState.Pressed) { finger = touch.Id; break; }
        if (finger is { } id)
        {
            if (touches.FindById(id, out var touch))
            {
                Pointer(touch.Position, touch.State != TouchLocationState.Released);
                if (touch.State == TouchLocationState.Released) finger = null;
            }
            else { pressed = false; finger = null; }
        }
        else
        {
            var mouse = Mouse.GetState();
            Pointer(new(mouse.X, mouse.Y), mouse.LeftButton == ButtonState.Pressed);
            Scroll((wheel - mouse.ScrollWheelValue) / 2f);
            wheel = mouse.ScrollWheelValue;
        }
        var keys = Keyboard.GetState();
        bool New(Keys key) => keys.IsKeyDown(key) && !previousKeys.IsKeyDown(key);
        var buttons = rows.Where(r => r.Action is not null && r.Enabled).ToArray();
        if (buttons.Length > 0)
        {
            if (New(Keys.Down) || New(Keys.Tab)) focus = (focus + 1) % buttons.Length;
            if (New(Keys.Up)) focus = (focus + buttons.Length - 1) % buttons.Length;
            focus = Math.Min(focus, buttons.Length - 1);
            if (New(Keys.Up) || New(Keys.Down) || New(Keys.Tab))
            {
                keyboardFocus = true;
                var selected = buttons[focus].Bounds;
                if (selected.Bottom > viewport.Bottom + scroll) Scroll(selected.Bottom - viewport.Bottom - scroll);
                if (selected.Top < viewport.Top + scroll) Scroll(selected.Top - viewport.Top - scroll);
            }
            if (New(Keys.Enter) || New(Keys.Space)) buttons[focus].Action!();
        }
        if (New(Keys.Escape)) setup.Cancel();
        previousKeys = keys;
        base.Update(gameTime);
    }

    // Physical pixels enter the same capture/drag/release path for mouse and touch.
    internal void Pointer(Vector2 physical, bool down)
    {
        var point = physical / scale;
        var content = point + new Vector2(0, scroll);
        if (down && !pressed)
        {
            keyboardFocus = false;
            pressed = true; dragged = false; pressPoint = point; pressScroll = scroll;
            captured = viewport.Contains((int)point.X, (int)point.Y) ? rows.FirstOrDefault(r =>
                r.Enabled && r.Action is not null && r.Bounds.Contains((int)content.X, (int)content.Y))?.Id : null;
        }
        else if (down && pressed)
        {
            if (Vector2.Distance(point, pressPoint) > 10) dragged = true;
            if (dragged) scroll = Math.Clamp(pressScroll + pressPoint.Y - point.Y, 0, MaximumScroll);
        }
        else if (!down && pressed)
        {
            pressed = false;
            if (!dragged && viewport.Contains((int)point.X, (int)point.Y))
                rows.FirstOrDefault(r => r.Id == captured && r.Enabled && r.Bounds.Contains((int)content.X, (int)content.Y))?.Action?.Invoke();
            captured = null;
        }
    }

    private float MaximumScroll => Math.Max(0, contentBottom - viewport.Bottom);
    internal void Scroll(float delta) => scroll = Math.Clamp(scroll + delta, 0, MaximumScroll);

    private void Layout()
    {
        var output = GraphicsDevice.PresentationParameters;
        var logicalWidth = output.BackBufferWidth > output.BackBufferHeight ? 960 : 480;
        scale = output.BackBufferWidth / (float)logicalWidth;
        var logicalHeight = output.BackBufferHeight / scale;
        var safe = safeInsets(this);
        viewport = new Rectangle((int)(safe.X * logicalWidth) + 20, (int)(safe.Y * logicalHeight) + 20,
            Math.Max(80, (int)((1 - safe.X - safe.Z) * logicalWidth) - 40),
            Math.Max(80, (int)((1 - safe.Y - safe.W) * logicalHeight) - 40));
        if (viewport.Width > 800)
        {
            viewport.X += (viewport.Width - 800) / 2;
            viewport.Width = 800;
        }
        rows = [];
        var y = viewport.Y;
        void Text(string value, float size = 2.5f, int gap = 20)
        {
            var lines = SetupFont.Wrap(value, viewport.Width, size);
            var height = (int)MathF.Ceiling((lines.Count - 1) * LineHeight(size) + 7 * size);
            rows.Add(new("", new(viewport.X, y, viewport.Width, height), lines, size, null, true));
            y += height + gap;
        }
        void Button(string id, string value, Action action, bool enabled = true)
        {
            var lines = SetupFont.Wrap(value, viewport.Width - 2 * ButtonInset, 2.5f);
            var height = Math.Max(60, (int)MathF.Ceiling((lines.Count - 1) * LineHeight(2.5f) + 7 * 2.5f + 32));
            rows.Add(new(id, new(viewport.X, y, viewport.Width, height), lines, 2.5f, action, enabled));
            y += height + 12;
        }
        Text("Z0MB1ES / SETUP", 4, gap: 28);
        if (setup.Page == SetupPage.Warning)
        {
            Text(FirstLaunchWarning.Title, 3);
            Text("Swipe to scroll.", 2);
            Text(FirstLaunchWarning.Text);
            Button("reduce", (setup.ReduceFlashing ? "[X] " : "[ ] ") + FirstLaunchWarning.CheckboxLabel,
                () => setup.ReduceFlashing = !setup.ReduceFlashing);
            Button("continue", "Continue", setup.ContinueWarning);
        }
        else if (setup.Page == SetupPage.Working)
        {
            Text(setup.Status, 3);
            Button("cancel", "Cancel", setup.Cancel);
        }
        else if (setup.Page == SetupPage.Restart)
        {
            Text(setup.Status);
        }
        else
        {
            Text("Files / Z0MB1ES / Imports\nAdd packages, then Rescan.", 2);
            string Name(ImportFile? file, bool phone) => file is not null ? phone ? "XAP" : "Enhanced" :
                setup.Active is not null ? "Keep" : phone ? "XAP required" : "Original";
            Button("phone", "Phone: " + Name(setup.Phone, true), () => setup.Cycle("phone"));
            Button("normal", "I MAED: " + Name(setup.Normal, false), () => setup.Cycle("normal"));
            Button("viking", "Time Viking: " + Name(setup.Viking, false), () => setup.Cycle("viking"));
            Button("rescan", "Rescan", setup.Rescan);
            Button("import", setup.AllowLaunch ? "Import and play" : "Import", setup.Import, setup.CanImport);
            if (setup.CanPlay) Button("play", "Play installed game", setup.Play);
        }
        if (setup.Page != SetupPage.Working && setup.Status.Length > 0) Text(setup.Status, 2);
        contentBottom = y;
        scroll = Math.Clamp(scroll, 0, MaximumScroll);
    }

    protected override void Draw(GameTime gameTime)
    {
        Layout();
        GraphicsDevice.Clear(Color.Black);
        GraphicsDevice.ScissorRectangle = new Rectangle((int)(viewport.X * scale), (int)(viewport.Y * scale),
            (int)(viewport.Width * scale), (int)(viewport.Height * scale));
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, clipped, null, Matrix.CreateTranslation(0, -scroll, 0) * Matrix.CreateScale(scale));
        var focused = keyboardFocus ? rows.Where(r => r.Action is not null && r.Enabled).ElementAtOrDefault(focus)?.Id : null;
        foreach (var row in rows)
        {
            var isButton = row.Action is not null;
            var primary = row.Id is "continue" or "import" or "play";
            var highlighted = row.Enabled && (row.Id == focused || pressed && !dragged && row.Id == captured);
            if (isButton) DrawButton(row.Bounds, row.Enabled, primary, highlighted);
            var textHeight = (row.Lines.Count - 1) * LineHeight(row.Scale) + 7 * row.Scale;
            var point = new Vector2(row.Bounds.X + (isButton ? ButtonInset : 0),
                row.Bounds.Y + (isButton ? (row.Bounds.Height - textHeight) / 2 : 0));
            var ink = !row.Enabled ? new Color(104, 104, 104) :
                row == rows[0] ? MenuRed : !isButton && row.Scale <= 2 ? MutedText : Color.White;
            foreach (var line in row.Lines)
            {
                font.Draw(batch, line, point, row.Scale, ink);
                point.Y += LineHeight(row.Scale);
            }
            if (row == rows[0])
            {
                batch.Draw(pixel, new Rectangle(row.Bounds.X, row.Bounds.Bottom + 2, Math.Min(96, row.Bounds.Width), 3), MenuRed);
                batch.Draw(pixel, new Rectangle(row.Bounds.X + Math.Min(96, row.Bounds.Width), row.Bounds.Bottom + 3,
                    Math.Max(0, row.Bounds.Width - 96), 1), DarkRed);
            }
        }
        batch.End();
        if (++frames == 1) log("files-setup=first-frame page=" + setup.Page);
        base.Draw(gameTime);
    }

    private void DrawButton(Rectangle bounds, bool enabled, bool primary, bool highlighted)
    {
        // Keep the one-pixel outline inside the scroll viewport's scissor edge.
        bounds.Width--;
        bounds.Height--;
        var edge = !enabled ? new Color(48, 48, 48) : primary || highlighted ? MenuRed : DarkRed;
        if (enabled && (primary || highlighted))
            batch.Draw(pixel, new Rectangle(bounds.X + 6, bounds.Y + 6, bounds.Width - 12, bounds.Height - 12),
                highlighted ? new Color(96, 0, 0) : new Color(36, 0, 0));
        // Clipped corners and a red leading stroke echo the game's angular menu art.
        const int cut = 8;
        Line(new(bounds.Left + cut, bounds.Top), new(bounds.Right, bounds.Top), edge);
        Line(new(bounds.Right, bounds.Top), new(bounds.Right, bounds.Bottom - cut), edge);
        Line(new(bounds.Right, bounds.Bottom - cut), new(bounds.Right - cut, bounds.Bottom), edge);
        Line(new(bounds.Right - cut, bounds.Bottom), new(bounds.Left, bounds.Bottom), edge);
        Line(new(bounds.Left, bounds.Bottom), new(bounds.Left, bounds.Top + cut), edge);
        Line(new(bounds.Left, bounds.Top + cut), new(bounds.Left + cut, bounds.Top), edge);
        batch.Draw(pixel, new Rectangle(bounds.Left, bounds.Top + cut, enabled ? 3 : 1, bounds.Height - cut),
            enabled ? MenuRed : edge);
    }

    private void Line(Vector2 from, Vector2 to, Color color)
    {
        var delta = to - from;
        batch.Draw(pixel, from, null, color, MathF.Atan2(delta.Y, delta.X), Vector2.Zero,
            new Vector2(delta.Length(), 1), SpriteEffects.None, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            setup.Dispose();
            font?.Dispose(); pixel?.Dispose(); batch?.Dispose(); clipped.Dispose();
        }
        base.Dispose(disposing);
    }
}
