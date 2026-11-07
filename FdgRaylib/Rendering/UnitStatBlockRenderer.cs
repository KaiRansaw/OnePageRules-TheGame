using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Shared read-only unit stat block: name, model count + Quality/Defense, mobility, each distinct weapon on
/// its own datasheet line (rules included, via <c>GetWeaponNameAndStats</c>), then the unit's own special
/// rules. Drawn into whatever ImGui container the caller opens -- the deploy panel (#223's sibling) and the
/// deploy / activate unit-picker hover tooltip (#223) both use it, so the two stay in sync. ASCII only
/// (CLAUDE.md).
/// </summary>
public static class UnitStatBlockRenderer
{
    /// <param name="includeRuleDescriptions">When true, each special rule's description is shown under it
    /// (the fuller hover-tooltip treatment); when false, only the rule names (the compact panel treatment).</param>
    /// <param name="descriptionWrapWidth">How wide a rule description may run before wrapping, in pixels
    /// from the text's own left edge. The default suits an auto-sizing tooltip; a docked panel (#288) passes
    /// its own content width so long descriptions wrap inside the panel instead of running off it.</param>
    /// <param name="quest">Star Quest progress, when there is one: Crippled and Bad weather halve a
    /// hero's move, and the card has to say so or the ring on the table and the card disagree.</param>
    /// <param name="systemSlug">
    /// Which game's wording the rule hovers show (#420). OPR rewords the same rule between systems -
    /// Star Quest's Fear decides whether you take a Morale Test where Grimdark Future's decides who won
    /// the melee - so a shared blurb is wrong in most of them. Null falls back to the authored
    /// description, which is what a caller outside a game (no system to speak of) gets.
    /// </param>
    public static void Draw(IUnit unit, bool includeRuleDescriptions, float descriptionWrapWidth = 300f,
        QuestProgress? quest = null, string? systemSlug = null)
    {
        ImGui.TextUnformatted(unit.Name);

        int live = unit.Models.Count(m => m.GetIsAlive());
        // Kai, 2026-11-06: the unit's points on its card.
        string pts = unit is UnitData { PointCost: > 0 } ud ? $"   {ud.PointCost} pts" : "";
        ImGui.TextDisabled($"{live} model{(live == 1 ? "" : "s")}   Qua {unit.Quality}+  Def {unit.Defense}+{pts}");
        // #287/#288: wounds, matching the model hover tooltip - a unit placed after taking damage
        // (disembark, reposition) is the case where this matters, and it costs one line otherwise.
        ImGui.TextDisabled($"Wounds {WoundFormat.Fraction(unit.RemainingWounds, unit.MaxWounds)}");
        // #414: and WHICH model is carrying the damage. The line above is the unit's total, which cannot
        // distinguish "three fresh models" from "two fresh and one on its last wound" - and since OPR fills
        // one model at a time, that distinction is the unit's actual staying power. Amber, because it is a
        // warning rather than a stat, and absent entirely when no model is part-wounded.
        if (WoundedModelReadout.CardLine(unit) is { } woundedLine)
            UiText.Colored(new Vector4(1f, 0.72f, 0.35f, 1f), woundedLine);
        DrawMobility(unit, quest);

        DrawWeaponsAndRules(unit, includeRuleDescriptions, descriptionWrapWidth, systemSlug);
    }

    // Read-only, non-logging: the move queries ask the rule graph what Fast / Slow / Crippled do, and the
    // dice roller is never reached because no movement rule rolls anything.
    // The table's evaluator, so a rule an aura grants (Highborn Boost) shows on the card too - the core
    // catalog alone can't resolve an army's own rule names.
    internal static RuleEvaluator MoveEvaluator => ReadOnlyRuleEvaluators.For(FdgRaylib.Rendering.Resolvers.PopupAnchor.TableState);

    /// <summary>
    /// Advance and Rush as the unit will actually get to use them, and a line naming anything that moved
    /// the numbers. <c>IUnit.GetMobility</c> is still a stub that answers the bare 6" / 12", so a unit with
    /// Slow (a Smashed Leg injury) read as full speed on its card while the ring on the table was short.
    /// </summary>
    private static void DrawMobility(IUnit unit, QuestProgress? quest)
    {
        float advance = MovementRuleQueries.EffectiveMoveShootDistance(unit, MoveEvaluator);
        float rush = MovementRuleQueries.EffectiveMaxRushDistance(unit, MoveEvaluator);

        // The rules themselves (Fast, Slow, Rapid Advance...), not a guess from the numbers.
        var reasons = new List<string>(MovementRuleQueries.MovementModifierRuleNames(unit, MoveEvaluator));
        if (reasons.Count == 0 && advance < GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES) reasons.Add("Slow");
        else if (reasons.Count == 0 && advance > GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES) reasons.Add("Fast");

        // Star Quest halves the whole move for a Crippled hero and for everyone in bad weather; the two
        // stack. Mirrors QuestActions.MoveFactor, which is what the move resolver applies.
        float factor = 1f;
        if (quest != null)
        {
            if (unit is UnitData { IsQuestHero: true } hero
                && FDG.Quest.QuestHero.FromJson(hero.QuestHeroJson)?.Conditions.Contains("Crippled") == true)
            {
                factor *= 0.5f;
                reasons.Add("Crippled");
            }
            if (quest.Weather == "Bad")
            {
                factor *= 0.5f;
                reasons.Add("bad weather");
            }
        }

        ImGui.TextDisabled($"Advance {advance * factor:0.#}\"   Rush {rush * factor:0.#}\"");
        if (reasons.Count > 0)
            UiText.Colored(new Vector4(1f, 0.72f, 0.35f, 1f), "Move changed by: " + string.Join(", ", reasons));
    }

    /// <summary>
    /// Weapons and the unit's own special rules, with every rule name <b>gold, underlined and explained
    /// on hover</b> (Kai, 2026-11-02).
    ///
    /// <para>
    /// Special rules used to print as one line per rule with the description indented underneath, which
    /// meant two units' worth of rules filled the panel and pushed the tokens off the bottom. They are
    /// one comma-joined line now - <i>Devout, Regeneration Aura</i> - and the description that used to
    /// take four lines is a hover away. Same treatment for the rule names inside a weapon's stat line,
    /// which is where Blast(3) and Reliable live.
    /// </para>
    /// <para>
    /// The hover is <see cref="RuleHoverText"/>, the same thing the army list overlay and the shoot panel
    /// use, so a gold underlined word means one thing everywhere in the game.
    /// </para>
    /// </summary>
    /// <param name="includeRuleDescriptions">
    /// Kept for the callers that pass it, but it no longer gates the descriptions - those are on the
    /// hover now and cost no space, so there is nothing to turn off. It still decides whether rule names
    /// are <b>hoverable</b> at all: a caller drawing into a context with no live ImGui window to hover
    /// (a draw-list-only panel) passes false and gets plain text.
    /// </param>
    private static void DrawWeaponsAndRules(IUnit unit, bool includeRuleDescriptions,
        float descriptionWrapWidth, string? systemSlug)
    {
        float wrap = Math.Max(120f, descriptionWrapWidth);
        var weapons = unit.AllWeapons();
        if (weapons.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Weapons:");
            foreach (var grp in weapons.GroupBy(w => w.Name))
            {
                IWeapon weapon = grp.First();
                if (!includeRuleDescriptions)
                {
                    ImGui.TextUnformatted($"- {weapon.GetWeaponNameAndStats(grp.Count())}");
                    continue;
                }
                RuleHoverText.DrawWrapped(WeaponLineSegments(weapon, grp.Count(), systemSlug), wrap);
            }
        }

        // Unit-wide special rules (live #042 ResolvedRules), minus the engine-internal Disembark/Embark
        // abilities attached to every unit -- same filter the hover tooltip uses.
        var rules = unit.RuleDefinitions
            .Where(r => r.Definition.Name != CoreRuleCatalog.DisembarkRuleName
                     && r.Definition.Name != CoreRuleCatalog.EmbarkRuleName)
            .ToList();
        if (rules.Count == 0) return;

        ImGui.Spacing();
        ImGui.TextUnformatted("Special Rules:");
        if (!includeRuleDescriptions)
        {
            foreach (ResolvedRule rule in rules) ImGui.TextUnformatted($"- {rule.RequestedName}");
            return;
        }

        var segments = new List<RuleHoverText.Segment>();
        foreach (ResolvedRule rule in rules)
        {
            if (segments.Count > 0) segments.Add(new RuleHoverText.Segment(", ", null, null));
            segments.Add(new RuleHoverText.Segment(rule.RequestedName, rule.RequestedName,
                OfficialRuleText.Display(rule.Definition, systemSlug)));
        }
        RuleHoverText.DrawWrapped(segments, wrap);
    }

    /// <summary>
    /// A weapon's datasheet line split so its rule names are their own hoverable runs, with the leading
    /// text byte-identical to <c>GetWeaponNameAndStats</c>'s - so the only visible change is that the
    /// rule names are gold and underlined.
    /// </summary>
    internal static List<RuleHoverText.Segment> WeaponLineSegments(IWeapon weapon, int count,
        string? systemSlug)
    {
        string full = weapon.GetWeaponNameAndStats(count);
        var byName = new Dictionary<string, ResolvedRule>(StringComparer.Ordinal);
        foreach (ResolvedRule rule in weapon.RuleDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(rule.RequestedName)) byName[rule.RequestedName] = rule;
        }

        var segments = new List<RuleHoverText.Segment> { new("- ", null, null) };
        int at = 0;
        // Walked left to right against the printed line rather than rebuilt from the parts: the line's
        // exact wording belongs to GetWeaponNameAndStats, and reproducing it here would be a second
        // formatter to keep in step. A rule the formatter chose not to print simply is not found and is
        // skipped, which is the right answer either way.
        //
        // Each step takes the EARLIEST remaining match, and the LONGEST among any that start together.
        // Sorting the names by length up front and walking them in that order looks equivalent and is
        // not: the cursor only moves forward, so consuming a long name late in the line strands every
        // shorter name that appeared before it. Earliest-then-longest is what the Battle Report's
        // splitter does, for the same reason.
        while (at < full.Length)
        {
            int bestAt = -1;
            string bestName = "";
            foreach (string name in byName.Keys)
            {
                int found = full.IndexOf(name, at, StringComparison.Ordinal);
                if (found < 0) continue;
                if (bestAt < 0 || found < bestAt || (found == bestAt && name.Length > bestName.Length))
                {
                    bestAt = found;
                    bestName = name;
                }
            }
            if (bestAt < 0) break;

            if (bestAt > at) segments.Add(new RuleHoverText.Segment(full[at..bestAt], null, null));
            segments.Add(new RuleHoverText.Segment(bestName, bestName,
                OfficialRuleText.Display(byName[bestName].Definition, systemSlug)));
            at = bestAt + bestName.Length;
        }

        if (at < full.Length) segments.Add(new RuleHoverText.Segment(full[at..], null, null));
        return segments;
    }
}
