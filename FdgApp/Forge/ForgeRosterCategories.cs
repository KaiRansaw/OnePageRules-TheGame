using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering;

/// <summary>
/// The category boxes of the Army Forge roster (wish list, "Army Forge", 2026-11-07). Kai asked for the
/// units to be grouped "by each unit's rules - check the Army Forge data before building".
///
/// <para>
/// <b>This is Army Forge's own grouping, not one of ours.</b> The official Army Forge site groups a book's
/// units in its list builder with one function (read out of its published front-end on 2026-11-07). It
/// looks only at each unit's own special rules, its Tough rating and its size, so the same answer can be
/// computed from the bundled books without any new data:
/// </para>
/// <list type="number">
/// <item>a narrative hero (Army Forge's unit type 1) -&gt; <b>Narrative Heroes</b>. The bundled books do not
/// keep that type number, but across all 298 official books of the six systems a unit is type 1 exactly
/// when it carries <c>Unique Hero</c>, so that rule stands in for it with no exceptions;</item>
/// <item>has <c>Hero</c> -&gt; <b>Heroes</b>;</item>
/// <item>has <c>Aircraft</c> -&gt; <b>Aircraft</b>;</item>
/// <item>has <c>Entrenched</c>, <c>Artillery</c> or <c>Mobile Artillery</c> -&gt; <b>Artillery</b>;</item>
/// <item>a single model with Tough 18+ -&gt; <b>Titans</b>; 9+ -&gt; <b>Vehicles / Monsters</b>;
/// 6+ -&gt; <b>Light Vehicles / Small Monsters</b>;</item>
/// <item>everything else -&gt; <b>Core Units</b>.</item>
/// </list>
/// <para>
/// The Tough thresholds are per game, as Army Forge has them: Firefight and Skirmish multiply them by 3
/// (their models carry three times the wounds), and both Quest games divide the unit's Tough by 3 first.
/// Inside a box, hero boxes run most expensive first; every other box runs biggest unit first, then
/// cheapest - Army Forge's automatic order (no official book uses its manual-order switch).
/// </para>
/// <para>Pure and ImGui-free so the sort is pinned by tests. The names are Army Forge's, ASCII already.</para>
/// </summary>
internal static class ForgeRosterCategories
{
    /// <summary>The boxes, in Army Forge's order.</summary>
    internal enum ECategory
    {
        NarrativeHeroes,
        Heroes,
        CoreUnits,
        LightVehiclesSmallMonsters,
        VehiclesMonsters,
        Artillery,
        Titans,
        Aircraft,
    }

    /// <summary>A box and its units, already in the box's order.</summary>
    internal sealed record Group(ECategory Category, IReadOnlyList<RosterUnit> Units)
    {
        public string Title => ForgeRosterCategories.Title(Category);
    }

    internal static string Title(ECategory category) => category switch
    {
        ECategory.NarrativeHeroes => "Narrative Heroes",
        ECategory.Heroes => "Heroes",
        ECategory.CoreUnits => "Core Units",
        ECategory.LightVehiclesSmallMonsters => "Light Vehicles / Small Monsters",
        ECategory.VehiclesMonsters => "Vehicles / Monsters",
        ECategory.Artillery => "Artillery",
        ECategory.Titans => "Titans",
        ECategory.Aircraft => "Aircraft",
        _ => category.ToString(),
    };

    /// <summary>Army Forge's name for a unit's box, singular-ish, for the unit panel ("Heroes").</summary>
    internal static string TitleFor(RosterUnit unit, string? gameSystem) => Title(Classify(unit, gameSystem));

    /// <summary>Which box <paramref name="unit"/> goes in, for a book of <paramref name="gameSystem"/>.</summary>
    internal static ECategory Classify(RosterUnit unit, string? gameSystem)
    {
        string system = GameSystems.Normalize(gameSystem);
        bool quest = system == GameSystems.GFStarQuest || system == GameSystems.AoFQuest;
        bool skirmish = system == GameSystems.GFFirefight || system == GameSystems.AoFSkirmish;
        int scale = skirmish ? 3 : 1;

        if (Has(unit, "Unique Hero")) return ECategory.NarrativeHeroes;
        if (Has(unit, "Hero")) return ECategory.Heroes;
        if (Has(unit, "Aircraft")) return ECategory.Aircraft;
        if (Has(unit, "Entrenched") || Has(unit, "Artillery") || Has(unit, "Mobile Artillery")) return ECategory.Artillery;

        if (unit.BaseModelCount == 1 && Tough(unit) is int tough)
        {
            double rating = quest ? tough / 3.0 : tough;
            if (rating >= 18 * scale) return ECategory.Titans;
            if (rating >= 9 * scale) return ECategory.VehiclesMonsters;
            if (rating >= 6 * scale) return ECategory.LightVehiclesSmallMonsters;
        }
        return ECategory.CoreUnits;
    }

    /// <summary>
    /// The book's units in their boxes: only boxes that hold something, in Army Forge's box order, each
    /// box in Army Forge's own order. Every unit of the book appears exactly once.
    /// </summary>
    internal static IReadOnlyList<Group> GroupBook(BookFile book)
    {
        var buckets = new Dictionary<ECategory, List<RosterUnit>>();
        foreach (RosterUnit unit in book.Units)
        {
            ECategory category = Classify(unit, book.GameSystem);
            if (!buckets.TryGetValue(category, out List<RosterUnit>? list)) buckets[category] = list = new List<RosterUnit>();
            list.Add(unit);
        }

        var groups = new List<Group>();
        foreach (ECategory category in Enum.GetValues<ECategory>())
        {
            if (!buckets.TryGetValue(category, out List<RosterUnit>? units)) continue;
            // OrderBy is stable, as lodash's orderBy is, so ties keep the book's order.
            IEnumerable<RosterUnit> ordered = IsHeroBox(category)
                ? units.OrderByDescending(u => u.BasePointCost)
                : units.OrderByDescending(u => u.BaseModelCount).ThenBy(u => u.BasePointCost);
            groups.Add(new Group(category, ordered.ToList()));
        }
        return groups;
    }

    private static bool IsHeroBox(ECategory category) =>
        category is ECategory.Heroes or ECategory.NarrativeHeroes;

    private static bool Has(RosterUnit unit, string ruleName) =>
        unit.Rules.Any(r => string.Equals(NameOf(r), ruleName, StringComparison.Ordinal));

    /// <summary>The unit's own Tough rating, or null. Only a rule actually named Tough counts - Army Forge
    /// reads the rule by name, and so does this.</summary>
    private static int? Tough(RosterUnit unit)
    {
        foreach (SpecialRuleEntry rule in unit.Rules)
            if (rule is SpecialRuleEntry_CoreNumeric { Name: "Tough" } tough)
                return tough.NumericValue;
        return null;
    }

    private static string NameOf(SpecialRuleEntry rule) => rule switch
    {
        SpecialRuleEntry_Core core => core.Name,
        SpecialRuleEntry_CoreNumeric numeric => numeric.Name,
        SpecialRuleEntry_Text text => text.Name,
        SpecialRuleEntry_Alias alias => alias.Name,
        _ => rule.PrintableName,
    };
}
