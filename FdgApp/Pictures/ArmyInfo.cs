using FDG;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Rendering;

namespace FdgRaylib.Pictures;

/// <summary>
/// What the Choose an Army info panel shows about an army (Kai, 2026-11-17, "Choose an Army Mockups" option C): Army
/// Forge's own description, its group, how many units and spells the book has, the army's own rules written out, and
/// three signature units. Everything comes from the bundled book and Army Forge's data - nothing is written by hand.
/// </summary>
public static class ArmyInfo
{
    public sealed record Rule(string Name, string Text);
    public sealed record Unit(string Name, int Points, string Category);
    public sealed record Info(string Name, string Group, string About, int Units, int Spells,
        IReadOnlyList<Rule> ArmyRules, IReadOnlyList<Unit> Signature);

    /// <summary>Rules almost every unit has that say nothing about the army.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase) { "Tough", "Hero", "Unique Hero", "Fearless" };

    /// <summary>The bundled book of <paramref name="army"/> in <paramref name="system"/>, read once with the shared library.</summary>
    public static async Task<Info?> GetAsync(EGameSystem system, string army)
    {
        List<BookFile> books = await BookLibrary.LoadAsync().ConfigureAwait(false);
        string slug = GameSystems.Normalize(GameSystemInfo.Slug(system));
        BookFile? book = books.FirstOrDefault(b => b.Name == army && GameSystems.Normalize(b.GameSystem) == slug);
        return book == null ? null : From(book, system);
    }

    public static Info From(BookFile book, EGameSystem system) =>
        new(book.Name, ArmyGroups.GroupOf(system, book.Name), ArmyGroups.AboutOf(system, book.Name),
            book.Units.Count, book.Spells?.Count ?? 0, ArmyRules(book), Signature(book));

    private static string NameOf(SpecialRuleEntry e) => e switch
    {
        SpecialRuleEntry_Core c => c.Name,
        SpecialRuleEntry_CoreNumeric n => n.Name,
        SpecialRuleEntry_Alias a => a.Name,
        _ => e.PrintableName,
    };

    /// <summary>
    /// The army's own rules: the rules at least three units in four carry (Hive Bond, Battleborn, Fanatic...), most
    /// common first, at most two, leaving out the ones every army has (Tough, Hero, Fearless). Written out in full
    /// with the book's / OPR's wording (Kai: no hover for this one).
    /// </summary>
    public static IReadOnlyList<Rule> ArmyRules(BookFile book)
    {
        int units = book.Units.Count;
        if (units == 0) return Array.Empty<Rule>();
        var first = new Dictionary<string, SpecialRuleEntry>(StringComparer.OrdinalIgnoreCase);
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (RosterUnit u in book.Units)
            foreach (string name in u.Rules.Select(NameOf).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                count[name] = count.GetValueOrDefault(name) + 1;
                if (!first.ContainsKey(name)) first[name] = u.Rules.First(r => string.Equals(NameOf(r), name, StringComparison.OrdinalIgnoreCase));
            }
        int needed = (int)Math.Ceiling(units * 0.75);
        RuleGlossary glossary = RuleGlossary.Build(book);
        return count.Where(kv => kv.Value >= needed && !Generic.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(kv => new Rule(first[kv.Key].PrintableName, glossary.Describe(first[kv.Key]) ?? ""))
            .ToList();
    }

    /// <summary>
    /// Three units that show what the army is: its most expensive Hero, its most expensive Core unit and its most
    /// expensive big model (vehicle, monster, titan, artillery or aircraft) - in Army Forge's own boxes. A box the
    /// book doesn't have is filled from the next most expensive units.
    /// </summary>
    public static IReadOnlyList<Unit> Signature(BookFile book)
    {
        var classed = book.Units.Select(u => (Unit: u, Cat: ForgeRosterCategories.Classify(u, book.GameSystem))).ToList();
        var picks = new List<(RosterUnit Unit, ForgeRosterCategories.ECategory Cat)>();
        void Best(Func<ForgeRosterCategories.ECategory, bool> box)
        {
            var hit = classed.Where(c => box(c.Cat) && !picks.Any(p => p.Unit == c.Unit)).OrderByDescending(c => c.Unit.BasePointCost).FirstOrDefault();
            if (hit.Unit != null) picks.Add(hit);
        }
        Best(c => c == ForgeRosterCategories.ECategory.Heroes);
        Best(c => c == ForgeRosterCategories.ECategory.CoreUnits);
        Best(c => c is ForgeRosterCategories.ECategory.LightVehiclesSmallMonsters or ForgeRosterCategories.ECategory.VehiclesMonsters
            or ForgeRosterCategories.ECategory.Titans or ForgeRosterCategories.ECategory.Artillery or ForgeRosterCategories.ECategory.Aircraft);
        while (picks.Count < 3 && picks.Count < classed.Count) Best(_ => true);
        return picks.Select(p => new Unit(p.Unit.Name, p.Unit.BasePointCost, ForgeRosterCategories.Title(p.Cat))).ToList();
    }
}
