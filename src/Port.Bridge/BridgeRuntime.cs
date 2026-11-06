using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

namespace Port.Bridge;

/// <summary>Tracks service registration for a specific FNA game instance.</summary>
public static class BridgeRuntime
{
    private static readonly ConditionalWeakTable<Game, ServiceState> States = new();
    private static WeakReference<Game>? currentGame;

    internal static void Attach(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        States.GetValue(game, static _ => new ServiceState());
        currentGame = new WeakReference<Game>(game);
    }

    public static bool IsAttached(Game game) => States.TryGetValue(game, out _);

    internal static bool[] AchievementFlags()
    {
        if (currentGame is null || !currentGame.TryGetTarget(out var game)) return new bool[20];
        var profile = game.GetType().GetField("profile", System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null);
        return (bool[]?)profile?.GetType().GetField("achievables")?.GetValue(profile) is { } flags
            ? (bool[])flags.Clone() : new bool[20];
    }

    private sealed class ServiceState;
}

public sealed class BridgeApiUnavailableException : NotSupportedException
{
    public string Api { get; }

    public BridgeApiUnavailableException(string api) : base($"Desktop bridge API is not implemented: {api}")
    {
        Api = api;
        Trace.TraceError(Message);
    }
}

internal static class Unavailable
{
    internal static T Api<T>(string signature) => throw new BridgeApiUnavailableException(signature);
    internal static void Api(string signature) => throw new BridgeApiUnavailableException(signature);
}
