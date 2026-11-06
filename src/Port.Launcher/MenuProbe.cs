using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;

/// <summary>Developer diagnostic for original menu construction and drawing.</summary>
internal sealed class MenuProbe
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Game game;
    private readonly Type menuType;
    private IDictionary? levels;
    private int current = -1;
    private int openedAt;
    private bool captured;

    public bool Verified { get; private set; }
    public int CurrentLevel => current;
    public bool ShouldCapture(int updates) => current >= 0 && current < 17 &&
        current is not (11 or 16) && !captured && updates - openedAt >= 25;
    public void MarkCaptured() => captured = true;

    public MenuProbe(Game game, Type gameType)
    {
        this.game = game;
        menuType = gameType.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!;
    }

    public void Update(int updates, bool mainMenuReady)
    {
        if (!mainMenuReady) return;
        levels ??= (IDictionary?)menuType.GetField("menuLevel", Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Original menu has not been initialized.");
        if (levels.Count != 17) throw new InvalidOperationException("Original menu level count changed.");
        if (current == -1)
        {
            Open(0, updates);
            return;
        }
        if (current >= 17 || updates - openedAt < 32) return;
        var level = levels![current] ?? throw new InvalidOperationException($"Missing original menu level {current}.");
        var baseType = level.GetType().BaseType!;
        var active = (bool)baseType.GetField("active", Instance)!.GetValue(level)!;
        var alpha = (float)baseType.GetField("alpha", Instance)!.GetValue(level)!;
        if (current is 11 or 16)
        {
            var thanks = levels[12]!;
            var thanksActive = (bool)thanks.GetType().BaseType!
                .GetField("active", Instance)!.GetValue(thanks)!;
            if (active || !thanksActive)
                throw new InvalidOperationException($"Menu probe level {current} did not follow the original full-game redirect.");
            Console.WriteLine($"MENU_PROBE level={current} type={level.GetType().Name} redirected=ThanksUpsell trial=False");
            if (current == 16)
            {
                menuType.GetMethod("SetLevel", Static)!.Invoke(null, [-1]);
                Verified = true;
                game.Exit();
                return;
            }
            Open(current + 1, updates);
            return;
        }
        if (!active || alpha < 0.9f || !captured)
            throw new InvalidOperationException($"Menu probe level {current} did not remain visible.");
        Console.WriteLine($"MENU_PROBE level={current} type={level.GetType().Name} active={active} alpha={alpha} captured={captured}");
        if (current == 16)
        {
            menuType.GetMethod("SetLevel", Static)!.Invoke(null, [-1]);
            Verified = true;
            game.Exit();
            return;
        }
        Open(current + 1, updates);
    }

    private void Open(int key, int updates)
    {
        var level = levels![key] ?? throw new InvalidOperationException($"Missing original menu level {key}.");
        var type = level.GetType();
        var prepare = type.GetMethod(key switch
        {
            5 or 10 => "SetFromPlayer",
            7 or 8 or 9 => "Init",
            _ => "__none__"
        }, Instance);
        prepare?.Invoke(level, null);
        menuType.GetMethod("SetLevel", Static)!.Invoke(null, [key]);
        current = key;
        openedAt = updates;
        captured = false;
        Console.WriteLine($"MENU_PROBE_OPEN level={key} type={type.Name} update={updates}");
    }
}
