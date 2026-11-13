using System.Globalization;
using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FdgRaylib.Forge;

/// <summary>
/// The Army Forge's rules that are not drawing: which upgrades may still be taken and how many, what the
/// points bar and legality badge say, and the small formatters both front ends print. Moved out of the Raylib
/// <c>ArmyForgeScreen</c> (whose tests still reach them through forwarders there) so the Godot Forge uses the
/// SAME availability maths - the rules here are subtle (#324's reservation, #383's per-model budget, combined
/// pairs' linked sections) and two copies would drift.
/// </summary>
public static class ForgeRules
{
    public const int DefaultPointsLimit = 1000;

    /// <summary>
    /// Upper bound for one option's stepper in a counted section: the section's own hard cap ("up to N"),
    /// against the pool it draws from - models the unit may still gain (AddModels), targets it can still
    /// replace (Replace), or one per model (a per-model upgrade). <paramref name="available"/> is measured on
    /// the final compiled state where this option's picks are already consumed, so <paramref name="current"/>
    /// comes back into its own budget. <paramref name="sectionOthersCount"/> is what the section's OTHER
    /// options hold, for a #383 per-model-budget section.
    /// </summary>
    public static int StepperMax(UpgradeSection section, RosterUnit roster, UnitFileEntry compiledUnit,
        int available, int current, int sectionOthersCount = 0)
    {
        int hardBound = section.MaxApplications > 0 ? section.MaxApplications : int.MaxValue;
        int poolBound = section.Variant switch
        {
            UpgradeVariant.AddModels => Math.Max(0, roster.MaxModels - roster.BaseModelCount),
            UpgradeVariant.Replace => available + current,
            _ => compiledUnit.ModelCount,
        };
        int max = Math.Min(hardBound, poolBound);
        if (section.PerModelBudget)
            max = Math.Min(max, Math.Max(0, compiledUnit.ModelCount - sectionOthersCount));
        return max;
    }

    /// <summary>Replace-target availability WITHOUT this section's own choices - the pool an option could draw
    /// on if the section's current pick were released (radio switching, header text).</summary>
    public static int AvailableExcludingSection(BookFile book, BuilderUnit bu, UpgradeSection section)
    {
        RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
        return roster is null ? 0 : ReplacePool(book, bu, roster, section, excludeOwn: true);
    }

    /// <summary>Whether a single-target all-swap that must YIELD to <paramref name="section"/> (#324) is chosen.</summary>
    public static bool YieldingAllSwapChosen(BuilderUnit bu, RosterUnit roster, UpgradeSection section) =>
        bu.Choices.Any(c => YieldsTo(roster, c.SectionId, section));

    /// <summary>The all-swap at <paramref name="sectionId"/> leaves copies for <paramref name="claimant"/> when it is
    /// a single-target all-swap authored ABOVE the claimant competing for the same weapon (ListCompiler's rule).</summary>
    public static bool YieldsTo(RosterUnit roster, string sectionId, UpgradeSection claimant)
    {
        int index = roster.Sections.FindIndex(s => s.Id == sectionId);
        if (index < 0 || index >= roster.Sections.FindIndex(s => s.Id == claimant.Id)) return false;
        string? target = ListCompiler.SingleAllSwapTarget(roster.Sections[index]);
        return target is not null && ListCompiler.CompetesForTarget(claimant, target);
    }

    /// <summary>The pool a Replace section may draw on, on a compile where every all-swap yielding to it (#324)
    /// is dropped; <paramref name="excludeOwn"/> also releases this section's own picks.</summary>
    public static int ReplacePool(BookFile book, BuilderUnit bu, RosterUnit roster, UpgradeSection section, bool excludeOwn)
    {
        var view = new BuilderUnit
        {
            RosterUnitId = bu.RosterUnitId,
            ModelCount = bu.ModelCount,
            Choices = bu.Choices
                .Where(c => !(excludeOwn && c.SectionId == section.Id) && !YieldsTo(roster, c.SectionId, section))
                .ToList(),
        };
        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(book, view);
        return ListCompiler.AvailableApplications(unit.Weapons, items, section.Targets);
    }

    // ---------------------------------------------------------------- upgrade view (what the editor shows)

    public enum EControl { Stepper, PickOne, Tick }

    /// <summary>One option as the upgrade editor shows it: chosen / how many, the most it may take, and - when
    /// it can't be taken - why (Kai, 2026-11-12 mockup: "options a choice rules out go grey with why").</summary>
    public sealed record OptionView(UpgradeOption Option, int Count, int Max, bool Enabled, string? WhyNot)
    {
        public bool Chosen => Count > 0;
    }

    public sealed record SectionView(UpgradeSection Section, EControl Control, bool Linked, bool NothingToReplace,
        IReadOnlyList<OptionView> Options);

    /// <summary>Every upgrade group of a unit in the list, with what each option can do right now. Same logic as
    /// the Raylib Forge's editor (ForgeUnitDetail.DrawUpgrades), returned as data.</summary>
    public static List<SectionView> Sections(BookFile book, BuilderUnit bu, RosterUnit roster,
        UnitFileEntry compiledUnit, List<ItemEntry> items, bool combined)
    {
        var result = new List<SectionView>();
        foreach (UpgradeSection section in roster.Sections)
        {
            bool isReplace = section.Variant == UpgradeVariant.Replace;
            bool linked = combined && section.Affects == UpgradeAffects.All;
            int available = !isReplace ? int.MaxValue
                : YieldingAllSwapChosen(bu, roster, section)
                    ? ReplacePool(book, bu, roster, section, excludeOwn: false)
                    : ListCompiler.AvailableApplications(compiledUnit.Weapons, items, section.Targets);
            int switchAvailable = isReplace ? AvailableExcludingSection(book, bu, section) : int.MaxValue;
            string targets = section.Targets.Count == 0 ? "it" : string.Join(" + ", section.Targets);
            var options = new List<OptionView>();

            if (section.IsCounted)
            {
                int sectionTotal = section.PerModelBudget
                    ? section.Options.Sum(o => BuilderListEditing.ChoiceCount(bu, section.Id, o.Id)) : 0;
                foreach (UpgradeOption option in section.Options)
                {
                    int v = BuilderListEditing.ChoiceCount(bu, section.Id, option.Id);
                    int max = StepperMax(section, roster, compiledUnit, available, v, sectionTotal - v);
                    string? why = max > v ? null
                        : section.Variant == UpgradeVariant.AddModels ? "The unit is at its biggest size."
                        : isReplace ? $"No {targets} left to replace."
                        : section.PerModelBudget ? "Every model already has one from this group."
                        : section.MaxApplications > 0 ? $"Up to {section.MaxApplications} only." : "One per model.";
                    options.Add(new OptionView(option, v, max, max > 0 || v > 0, max <= v ? why : null));
                }
                result.Add(new SectionView(section, EControl.Stepper, linked, isReplace && switchAvailable == 0, options));
            }
            else if (section.MaxPicks <= 1 && section.Options.Count >= 2)
            {
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = BuilderListEditing.IsChosen(bu, section.Id, option.Id);
                    bool ok = chosen || !(isReplace && switchAvailable == 0);
                    options.Add(new OptionView(option, chosen ? 1 : 0, 1, ok, ok ? null : NoTargetWhy(targets)));
                }
                result.Add(new SectionView(section, EControl.PickOne, linked, isReplace && switchAvailable == 0, options));
            }
            else
            {
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = BuilderListEditing.IsChosen(bu, section.Id, option.Id);
                    bool ok = chosen || !(isReplace && available == 0);
                    options.Add(new OptionView(option, chosen ? 1 : 0, 1, ok, ok ? null : NoTargetWhy(targets)));
                }
                result.Add(new SectionView(section, EControl.Tick, linked, isReplace && switchAvailable == 0, options));
            }
        }
        return result;
    }

    private static string NoTargetWhy(string targets) =>
        $"No {targets} left to replace - another upgrade already swapped it.";

    // ---------------------------------------------------------------- what the screen prints

    public readonly record struct PointsBarState(float Fraction, bool Over, int OverBy, int Left, string Label);

    /// <summary>The points bar: how full, over or not, and its text. Exactly at the limit is NOT over.</summary>
    public static PointsBarState PointsBar(int used, int limit)
    {
        if (limit <= 0) return new PointsBarState(0f, false, 0, 0, $"{used} pts");
        bool over = used > limit;
        float fraction = Math.Clamp(used / (float)limit, 0f, 1f);
        string label = over ? $"OVER LIMIT by {used - limit} pts" : $"{used} / {limit} pts";
        return new PointsBarState(fraction, over, over ? used - limit : 0, over ? 0 : limit - used, label);
    }

    /// <summary>"Legal", or how many errors (errors outrank warnings) / warnings.</summary>
    public static string LegalityBadge(IReadOnlyList<ListIssue> issues)
    {
        int errors = issues.Count(i => i.Severity == ListIssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == ListIssueSeverity.Warning);
        if (errors > 0) return $"{errors} error{(errors == 1 ? "" : "s")}";
        if (warnings > 0) return $"{warnings} warning{(warnings == 1 ? "" : "s")}";
        return "Legal";
    }

    public static string OptionSummary(UpgradeOption o) =>
        o.Cost == 0 ? o.Label : $"{o.Label}  ({o.Cost:+0;-0} pts)";

    public static string CostText(int cost) => cost.ToString("+0;-0;+0", CultureInfo.InvariantCulture);

    private const float MmPerInch = 25.4f;

    public static string BaseSummary(BaseFileEntry b) =>
        b.Shape == EBaseShapeKind.Rectangle
            ? $"{Mm(b.WidthInches)} x {Mm(b.HeightInches)}mm"
            : $"{Mm(b.DiameterInches)}mm";

    private static int Mm(float inches) => (int)MathF.Round(inches * MmPerInch);

    public static bool IsCaster(RosterUnit unit) =>
        unit.Rules.Any(r => r.PrintableName.StartsWith("Caster", StringComparison.Ordinal));

    public static string ItemSummary(ItemEntry i) =>
        i.Rules.Count == 0
            ? $"{i.Quantity}x {i.Name}"
            : $"{i.Quantity}x {i.Name} ({string.Join(", ", i.Rules.Select(r => r.PrintableName))})";

    /// <summary>A unit's Tough rating, or null (read off the typed rule entry; an alias counts).</summary>
    public static string? ToughValue(UnitFileEntry unit)
    {
        foreach (SpecialRuleEntry rule in unit.SpecialRules)
            if (ToughOf(rule) is { } value) return value;
        return null;
    }

    private static string? ToughOf(SpecialRuleEntry rule) => rule switch
    {
        SpecialRuleEntry_CoreNumeric numeric when string.Equals(numeric.Name, "Tough", StringComparison.OrdinalIgnoreCase)
            => numeric.NumericValue.ToString(CultureInfo.InvariantCulture),
        SpecialRuleEntry_Alias alias => ToughOf(alias.AliasedRule),
        _ => null,
    };

    public static string RangeText(float rangeInches) =>
        rangeInches > 0f ? rangeInches.ToString("0.##", CultureInfo.InvariantCulture) + "\"" : "-";

    public static string AttacksText(int attacks) => $"A{attacks}";

    public static string ApText(int ap) => ap > 0 ? ap.ToString(CultureInfo.InvariantCulture) : "-";

    public static string CountedName(int count, string name) => count > 1 ? $"{count}x {name}" : name;

    /// <summary>A file name from what the player typed (#421): strips what the filesystem refuses, collapses
    /// runs of spaces, trims trailing dots and spaces.</summary>
    public static string SanitizeFileName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? ' ' : c);
        string cleaned = string.Join(" ", sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        cleaned = cleaned.TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "Army" : cleaned;
    }

    /// <summary>
    /// Which weapons each model carries, in model order, exactly as the engine hands them out when the unit
    /// is built (UnitData: weapons sorted by how many there are, fewest first, then dealt round the models).
    /// So the look picker can say "Model 1 carries the Bio-Plasma" for the model the game really gives it to.
    /// </summary>
    public static List<List<string>> ModelLoadouts(UnitFileEntry unit)
    {
        var perModel = new List<List<string>>();
        for (int i = 0; i < unit.ModelCount; i++) perModel.Add(new List<string>());
        if (unit.ModelCount <= 0) return perModel;
        var sorted = new List<WeaponFileEntry>(unit.Weapons);
        sorted.Sort((x, y) => x.Quantity.CompareTo(y.Quantity));
        var flat = new List<string>();
        foreach (WeaponFileEntry w in sorted)
            for (int q = 0; q < w.Quantity; q++) flat.Add(w.Name);
        for (int w = 0; w < flat.Count; w++) perModel[w % unit.ModelCount].Add(flat[w]);
        return perModel;
    }

    /// <summary>For each model, the weapons it carries that most of the unit does not (its "special" kit), or an
    /// empty list. Empty for every model when the unit is all alike.</summary>
    public static List<List<string>> SpecialKit(UnitFileEntry unit)
    {
        var loadouts = ModelLoadouts(unit);
        var counts = new Dictionary<string, int>();
        foreach (var l in loadouts) foreach (string w in l.Distinct()) counts[w] = counts.GetValueOrDefault(w) + 1;
        int half = unit.ModelCount / 2;
        return loadouts.Select(l => l.Where(w => counts[w] <= half && counts[w] < unit.ModelCount).Distinct().ToList()).ToList();
    }
}
