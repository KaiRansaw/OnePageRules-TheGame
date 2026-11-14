using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #466 - which of this machine's players have pressed Concede. The Mission tab's button writes it (main
/// thread); <see cref="GuiConcedeCheckResolver"/> reads it when the engine asks at the start of a round
/// (engine thread), hence the lock. One per game: it lives on that game's <see cref="GuiResolverOverlay"/>.
/// </summary>
public sealed class ConcedeIntent
{
    private readonly object _lock = new();
    private readonly HashSet<PlayerID> _pending = new();

    public bool Has(PlayerID player)
    {
        lock (_lock) return _pending.Contains(player);
    }

    public void Set(PlayerID player, bool concede)
    {
        lock (_lock)
        {
            if (concede) _pending.Add(player);
            else _pending.Remove(player);
        }
    }
}
