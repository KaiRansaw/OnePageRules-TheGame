using FDG.ArmyBuilding;

namespace FdgRaylib.Models3D;

/// <summary>
/// The base a unit plays on - Army Forge's size, the one the rules measure from - for a model file kept under
/// <c>UnitModels/&lt;Faction&gt;/&lt;Unit&gt;/</c>. The Asset Lab stands a model on this base, the same one the table
/// draws, so its own sculpted base and its size can be judged against it (Kai, 2026-11-19).
/// </summary>
public static class GameBaseLookup
{
    /// <summary>
    /// The unit's base in mm across (round, house rule), or null when no bundled book has that faction and unit. The
    /// full-size game's book is preferred (Grimdark Future, Age of Fantasy): UnitModels folders are shared by every
    /// mode of a faction and the big game's bases are the usual ones; another mode's book is the fallback.
    /// </summary>
    public static float? BaseMm(IEnumerable<BookFile> books, string faction, string unit)
    {
        if (string.IsNullOrWhiteSpace(faction) || string.IsNullOrWhiteSpace(unit)) return null;
        float? fallback = null;
        foreach (BookFile book in books)
        {
            if (!book.MatchesFactionName(faction)) continue;
            RosterUnit? u = book.Units.FirstOrDefault(x => string.Equals(x.Name, unit, StringComparison.OrdinalIgnoreCase));
            if (u == null) continue;
            float mm = u.Base.RoundDiameterInches * UnitModelSettings.MmPerInch;
            if (book.GameSystem is null or GameSystems.GrimdarkFuture or GameSystems.AgeOfFantasy) return mm;
            fallback ??= mm;
        }
        return fallback;
    }

    /// <summary>Faction and unit from a model's place inside UnitModels ("Alien Hives/Hive Lord/x.glb"), or null.</summary>
    public static (string Faction, string Unit)? FromKey(string? key)
    {
        if (key == null) return null;
        string[] parts = key.Split('/');
        return parts.Length >= 3 ? (parts[0], parts[1]) : null;
    }
}
