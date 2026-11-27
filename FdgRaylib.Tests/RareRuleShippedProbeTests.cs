using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Tests;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Lane 1 item 3 (2026-11-27): rules too rare to show up in the 216-game traced sweep, checked against every copy the
// game ships (both supplements and every book's embedded copy), through the engine's own non-logging queries.
//   Quick Throw - "This model may always target enemies that are not in line of sight when shooting with Indirect
//                  weapons (instead of only when using Hold actions)." (AoF / AoF: Quest / GF: Star Quest, Army Forge)
//   Traversal   - "May move through friendly and enemy units." (AoF / AoFS / AoF: Quest, Army Forge)
//   Puncture    - "Ignores Regeneration, and when attacking units where most models have Defense 2+ to Defense 3+,
//                  this weapon gets AP(+2)."
//   Purge       - "When attacking units that are Shaken or Fatigued, this weapon gets AP(+2)."
[TestFixture]
public class RareRuleShippedProbeTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<(string Where, SpecialRuleDefinition Rule)> Shipped(string name)
    {
        var all = new List<(string, SpecialRuleDefinition)>();
        foreach (string supplement in new[] { "GdfRuleSupplement.json", "AofRuleSupplement.json" })
            foreach (SpecialRuleDefinition rule in BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, supplement))))
                if (rule.Name == name) all.Add((supplement, rule));
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions.Where(r => r.Name == name))
                all.Add((Path.GetFileName(path), rule));
        }
        return all;
    }

    private static UnitData Unit(GameDataStore store)
    {
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
        var bindings = new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(model)) };
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Probe", quality: 4, defense: 4, modelBindings: bindings);
        return store.GetDataBinding<UnitData>(store.Create(unit)).GetValue();
    }

    [Test]
    public void QuickThrow_EveryShippedCopy_LetsIndirectWeaponsIgnoreSightAfterMoving()
    {
        var copies = Shipped("Quick Throw");
        Assert.That(copies, Is.Not.Empty);
        var wrong = new List<string>();
        foreach ((string where, SpecialRuleDefinition rule) in copies)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            UnitData juggler = Unit(store);
            juggler.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

            // A weapon-scoped rule bought at unit level (the Juggler upgrade) rides every weapon
            // (GameBootstrap.AttachRulesFromArmyList); a unit-scoped copy would sit on the unit.
            var bombs = new Weapon("Juggler Bombs", rangeInches: 12f, attacks: 1, armorPenetration: 2);
            bombs.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            var strike = new Weapon("Strike", rangeInches: 12f, attacks: 1, armorPenetration: 0);
            if (rule.Scope == ERuleScope.Unit) juggler.AttachRuleDefinition(new ResolvedRule("Quick Throw", rule));
            else
            {
                bombs.AttachRuleDefinition(new ResolvedRule("Quick Throw", rule));
                strike.AttachRuleDefinition(new ResolvedRule("Quick Throw", rule));
            }

            if (!SightRuleQueries.IgnoresTerrain(juggler, bombs, evaluator)) wrong.Add($"{where}: Indirect after moving still needs sight");
            if (SightRuleQueries.IgnoresTerrain(juggler, strike, evaluator)) wrong.Add($"{where}: a weapon without Indirect ignores sight");
        }
        Assert.That(wrong, Is.Empty, string.Join("; ", wrong));

        // Control: without Quick Throw, Indirect after a move needs sight (the four standard games' Hold-only Indirect).
        var s = GameDataStore.GameDataStoreBuilder.GetDefault();
        UnitData plain = Unit(s);
        plain.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));
        var mortar = new Weapon("Mortar", rangeInches: 12f, attacks: 1, armorPenetration: 0);
        mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
        Assert.That(SightRuleQueries.IgnoresTerrain(plain, mortar, new RuleEvaluator(new FixedDiceRoller(4))), Is.False);
    }

    [Test]
    public void Traversal_EveryShippedCopy_MovesThroughEnemyUnits()
    {
        var copies = Shipped("Traversal");
        Assert.That(copies, Is.Not.Empty);
        var wrong = new List<string>();
        foreach ((string where, SpecialRuleDefinition rule) in copies)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            UnitData ghost = Unit(store);
            ghost.AttachRuleDefinition(new ResolvedRule("Traversal", rule));
            if (!MovementRuleQueries.CanMoveThroughEnemies(ghost, new RuleEvaluator(new FixedDiceRoller(4)))) wrong.Add(where);
        }
        Assert.That(wrong, Is.Empty, "can't pass through enemy units: " + string.Join(", ", wrong));

        var s = GameDataStore.GameDataStoreBuilder.GetDefault();
        Assert.That(MovementRuleQueries.CanMoveThroughEnemies(Unit(s), new RuleEvaluator(new FixedDiceRoller(4))), Is.False, "control");
    }

    // Puncture / Purge share the shape the engine already proves for the conditional AP rules: a -2 Save modifier
    // queued at Shooting_OnHitRollComplete under the target condition (plus Puncture's Regeneration ignore).
    [Test]
    public void PunctureAndPurge_EveryShippedCopy_IsAConditionalAPTwo()
    {
        var wrong = new List<string>();
        foreach ((string where, SpecialRuleDefinition rule) in Shipped("Puncture"))
        {
            bool ap = rule.Passive.Any(h => h.HookID == EHookID.Shooting_OnHitRollComplete
                && h.Effect is Effect.RollModifier { RollKind: ERollKind.Save, Delta: -2 }
                && h.Condition is Condition.StatLessOrEqualTo { Stat: EStatKind.Defense, StatValue: 3 });
            bool regen = rule.Passive.Any(h => h.Effect is Effect.IgnoreRule { RuleName: "Regeneration" } && h.Condition is Condition.Always);
            if (!ap || !regen || rule.Scope != ERuleScope.Weapon) wrong.Add($"{where}: Puncture");
        }
        foreach ((string where, SpecialRuleDefinition rule) in Shipped("Purge"))
        {
            HookEntry h = rule.Passive.Single();
            bool ok = h.HookID == EHookID.Shooting_OnHitRollComplete
                && h.Effect is Effect.RollModifier { RollKind: ERollKind.Save, Delta: -2 }
                && h.Condition is Condition.Or { Left: Condition.TargetTokenPresent l, Right: Condition.TargetTokenPresent r }
                && new[] { l.TType.Id, r.TType.Id }.OrderBy(x => x).SequenceEqual(new[] { "Fatigued", "Shaken" });
            if (!ok || rule.Scope != ERuleScope.Weapon) wrong.Add($"{where}: Purge");
        }
        Assert.That(Shipped("Puncture").Concat(Shipped("Purge")), Is.Not.Empty);
        Assert.That(wrong, Is.Empty, string.Join(", ", wrong));
    }
}
