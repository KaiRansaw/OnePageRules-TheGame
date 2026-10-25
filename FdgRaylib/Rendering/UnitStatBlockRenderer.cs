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
    public static void Draw(IUnit unit, bool includeRuleDescriptions, float descriptionWrapWidth = 300f,
        QuestProgress? quest = null)
    {
        ImGui.TextUnformatted(unit.Name);

        int live = unit.Models.Count(m => m.GetIsAlive());
        ImGui.TextDisabled($"{live} model{(live == 1 ? "" : "s")}   Qua {unit.Quality}+  Def {unit.Defense}+");
        // #287/#288: wounds, matching the model hover tooltip - a unit placed after taking damage
        // (disembark, reposition) is the case where this matters, and it costs one line otherwise.
        ImGui.TextDisabled($"Wounds {WoundFormat.Fraction(unit.RemainingWounds, unit.MaxWounds)}");
        DrawMobility(unit, quest);

        DrawWeaponsAndRules(unit, includeRuleDescriptions, descriptionWrapWidth);
    }

    // Read-only, non-logging: the move queries ask the rule graph what Fast / Slow / Crippled do, and the
    // dice roller is never reached because no movement rule rolls anything.
    private static readonly RuleEvaluator MoveEvaluator =
        new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: CoreRuleCatalog.CreateResolver());

    /// <summary>
    /// Advance and Rush as the unit will actually get to use them, and a line naming anything that moved
    /// the numbers. <c>IUnit.GetMobility</c> is still a stub that answers the bare 6" / 12", so a unit with
    /// Slow (a Smashed Leg injury) read as full speed on its card while the ring on the table was short.
    /// </summary>
    private static void DrawMobility(IUnit unit, QuestProgress? quest)
    {
        float advance = MovementRuleQueries.EffectiveMoveShootDistance(unit, MoveEvaluator);
        float rush = MovementRuleQueries.EffectiveMaxRushDistance(unit, MoveEvaluator);

        var reasons = new List<string>();
        if (advance < GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES) reasons.Add("Slow");
        else if (advance > GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES) reasons.Add("Fast");

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

    private static void DrawWeaponsAndRules(IUnit unit, bool includeRuleDescriptions, float descriptionWrapWidth)
    {
        var weapons = unit.AllWeapons();
        if (weapons.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Weapons:");
            foreach (var grp in weapons.GroupBy(w => w.Name))
                ImGui.TextUnformatted($"- {grp.First().GetWeaponNameAndStats(grp.Count())}");
        }

        // Unit-wide special rules (live #042 ResolvedRules), minus the engine-internal Disembark/Embark
        // abilities attached to every unit -- same filter the hover tooltip uses.
        var rules = unit.RuleDefinitions
            .Where(r => r.Definition.Name != CoreRuleCatalog.DisembarkRuleName
                     && r.Definition.Name != CoreRuleCatalog.EmbarkRuleName)
            .ToList();
        if (rules.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Special Rules:");
            foreach (var rule in rules)
            {
                ImGui.TextUnformatted($"- {rule.RequestedName}");
                if (includeRuleDescriptions && !string.IsNullOrEmpty(rule.Definition.Description))
                {
                    ImGui.Indent();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + descriptionWrapWidth);
                    ImGui.TextDisabled(rule.Definition.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.Unindent();
                }
            }
        }
    }
}
