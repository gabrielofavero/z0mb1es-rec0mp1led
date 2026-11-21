using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace Port.AndroidGame;

internal sealed class PendingActions(Game game, ConcurrentQueue<Action> actions) : GameComponent(game)
{
    public override void Update(GameTime gameTime)
    {
        while (actions.TryDequeue(out var action)) action();
        base.Update(gameTime);
    }
}
