using FDG;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FdgRaylib.Game;

/// <summary>
/// The side trays beside the table (Kai, 2026-11-26, mockup A of "Side Tables Mockups": a tray at each end, one per side,
/// with Ambush / Scout / Reserves / Destroyed). This decides which units and models sit in which bay; the Godot table
/// draws them. Everything here is open information in the books (reserves and casualties are on the table's edge).
/// </summary>
public static class OffTable
{
    public enum Bay { Ambush, Scout, Reserves, Destroyed }

    public static readonly IReadOnlyList<Bay> Bays = new[] { Bay.Ambush, Bay.Scout, Bay.Reserves, Bay.Destroyed };

    public static string Title(Bay bay) => bay switch
    {
        Bay.Ambush => "AMBUSH",
        Bay.Scout => "SCOUT",
        Bay.Reserves => "RESERVES",
        _ => "DESTROYED",
    };

    public static string Hint(Bay bay) => bay switch
    {
        Bay.Ambush => "Waiting in Ambush - arrives at the start of a later round.",
        Bay.Scout => "Scouts - placed after every other unit has deployed.",
        Bay.Reserves => "Off the table for another reason (reserves, respawns, detachments to come, the sewers...).",
        _ => "Casualties - every model that has been killed.",
    };

    /// <summary>One unit's models in one bay.</summary>
    public sealed record Entry(IUnit Unit, Bay Bay, IReadOnlyList<IModel> Models);

    /// <summary>Every unit off the table, by bay. <paramref name="round"/> is the round number, null in deployment.</summary>
    public static List<Entry> Sort(IEnumerable<IUnit> units, int? round)
    {
        var list = new List<Entry>();
        foreach (IUnit unit in units)
        {
            var dead = unit.Models.Where(m => m.RemainingWounds <= 0).ToList();
            var alive = unit.Models.Where(m => m.RemainingWounds > 0).ToList();
            if (alive.Count > 0 && Where(unit, alive, round) is { } bay) list.Add(new Entry(unit, bay, alive));
            if (dead.Count > 0) list.Add(new Entry(unit, Bay.Destroyed, dead));
        }
        return list;
    }

    private static Bay? Where(IUnit unit, List<IModel> alive, int? round)
    {
        if (alive.Any(m => !(m.Position.x == 0 && m.Position.z == 0))) return null; // on the table
        if (unit.Tokens.HasToken(TokenType.EmbarkedIn)) return null; // inside its transport
        if (ReserveRules.IsInReserve(unit))
        {
            bool other = unit.Tokens.HasToken(TokenType.Respawning) || unit.Tokens.HasToken(TokenType.HeldDetachment);
            return !other && HasRule(unit, "Ambush") ? Bay.Ambush : Bay.Reserves;
        }
        // Not in reserve and not placed: in deployment a Scout waits for its turn; the rest are still to deploy
        // (they're in the army list, not on a tray). After deployment anything off the table is held somewhere.
        if (round == null) return HasRule(unit, "Scout") ? Bay.Scout : null;
        return Bay.Reserves;
    }

    private static bool HasRule(IUnit unit, string name) =>
        unit.RuleDefinitions.Any(r => r.RequestedName.Contains(name, StringComparison.OrdinalIgnoreCase))
        || unit.Models.Any(m => m.RuleDefinitions.Any(r => r.RequestedName.Contains(name, StringComparison.OrdinalIgnoreCase)));
}
