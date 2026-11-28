using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Ravage Aura (Army Forge 3.6.0): "This model and its unit get Ravage(+1)." (GF / AoF) / "This model's unit and up to 2
// friendly units picked before the start of the game get Ravage(+1) ..." (GFF / AoFS / both Quests). Ravage(X): "Once per
// round, when it's this model's turn to strike in melee, roll X dice before attacking, unless this unit is Fatigued. For
// each 6+ the target takes one wound."
//
// Found by the Lane 1 deep hunt (2026-11-28) comparing copies of the same rule: the shipped data had two versions and both
// were wrong. GF / GFF / GFSQ granted plain "Ravage" by name - an (X) rule, which a grant can't carry, so the engine
// skipped it ("reads arguments, but grants carry none") and the banner did nothing. AoF / AoFS / AoFQ rolled the extra die
// itself but with no Fatigued / once-per-round gate, and could not be handed to picked units. Now every copy is an aura of
// "Ravage Boost" - one more Ravage die per model, with Ravage's own two gates - so the pick screen works too.
[TestFixture]
public class RavageAuraShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<(string Where, IReadOnlyList<SpecialRuleDefinition> Rules)> Sources()
    {
        foreach (string supplement in new[] { "GdfRuleSupplement.json", "AofRuleSupplement.json" })
            yield return (supplement, BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, supplement))).ToList());
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
            yield return (Path.GetFileName(path), JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!.RuleDefinitions);
    }

    [Test]
    public void EveryRavageAura_IsAnAuraOfRavageBoost_DefinedBesideIt()
    {
        int auras = 0;
        var wrong = new List<string>();
        foreach ((string where, IReadOnlyList<SpecialRuleDefinition> rules) in Sources())
        {
            SpecialRuleDefinition? aura = rules.FirstOrDefault(r => r.Name == "Ravage Aura");
            if (aura == null) continue;
            auras++;
            if (aura.Passive.Count != 1 || aura.Passive[0].Effect is not Effect.Aura { RuleName: "Ravage Boost" })
                wrong.Add($"{where}: Ravage Aura is not an aura of Ravage Boost");
            if (rules.All(r => r.Name != "Ravage Boost"))
                wrong.Add($"{where}: no Ravage Boost definition");
        }
        Assert.That(auras, Is.EqualTo(11), "9 books + both supplements");
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
    }

    [Test]
    public void RavageBoost_IsOneDiePerModel_AtSix_OncePerRound_NotWhenFatigued()
    {
        foreach ((string where, IReadOnlyList<SpecialRuleDefinition> rules) in Sources())
        {
            SpecialRuleDefinition? boost = rules.FirstOrDefault(r => r.Name == "Ravage Boost");
            if (boost == null) continue;
            HookEntry hook = boost.Passive.Single();
            Assert.That(hook.HookID, Is.EqualTo(EHookID.Melee_OnChargeContact), where);
            Assert.That(hook.Effect, Is.EqualTo(new Effect.DealAutoWounds(new ValueSource.Literal(1), 6)), where);
            Assert.That(boost.EngineArgumentCount, Is.EqualTo(0), where + ": grantable by name");
            var gate = (Condition.And)hook.Condition;
            Assert.That(new[] { gate.Left, gate.Right }.Select(c => ((Condition.TokenPresent)((Condition.Not)c).Inner).TType),
                Is.EquivalentTo(new[] { TokenType.Fatigued, TokenType.RavagedThisRound }), where);
        }
    }

    // ---------- behaviour through the live evaluator (kept from #376 S3, now on the granted Ravage Boost) ----------
    // #376 S3's owner ruling still holds: the +1 is a data-only standalone definition, never an argumented grant (grants
    // are name-only). A unit that receives the aura holds Ravage Boost; the harness attaches it directly - the grant
    // itself is the engine's ordinary Effect.Aura path. ResolveRavageWoundsStage sums the dice of every op in one
    // threshold group, so 1 die per model beside core Ravage(X) is the same as Ravage(X+1).

    [Test]
    public void BoostAlone_ContributesOneDiePerLivingModel()
    {
        var harness = new Harness();
        IUnit attacker = harness.BuildUnit(models: 3, "Ravage Boost");
        IUnit defender = harness.BuildUnit(models: 1);

        Assert.That(harness.DiceAtSix(attacker, defender), Is.EqualTo(3),
            "a unit of 3 receiving Ravage(+1) rolls 3 extra dice.");
    }

    [Test]
    public void BoostOnTopOfCoreRavage_SumsToTheUpgradedRating()
    {
        var harness = new Harness();
        IUnit attacker = harness.BuildUnit(models: 2, "Ravage Boost");
        harness.AttachCoreRavage(attacker, rating: 2);
        IUnit defender = harness.BuildUnit(models: 1);

        Assert.That(harness.DiceAtSix(attacker, defender), Is.EqualTo(6),
            "Ravage(2) x 2 models + the boost's 1 x 2 models = the 6 dice Ravage(3) x 2 would roll.");
    }

    [Test]
    public void Boost_KeepsRavagesGates_FatiguedAndOncePerRound()
    {
        var harness = new Harness();
        IUnit defender = harness.BuildUnit(models: 1);

        IUnit fatigued = harness.BuildUnit(models: 3, "Ravage Boost");
        fatigued.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Fatigued));
        Assert.That(harness.DiceAtSix(fatigued, defender), Is.EqualTo(0), "unless this unit is Fatigued");

        IUnit spent = harness.BuildUnit(models: 3, "Ravage Boost");
        spent.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.RavagedThisRound));
        Assert.That(harness.DiceAtSix(spent, defender), Is.EqualTo(0), "once per round");
    }

    /// <summary>Live resolver + evaluator; the ops collected the way ResolveRavageWoundsStage does.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;

        public Harness()
        {
            var byName = BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "AofRuleSupplement.json")))
                .ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            _resolver.Register(byName["Ravage Aura"]);
            _resolver.Register(byName["Ravage Boost"]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
        }

        public IUnit BuildUnit(int models, params string[] ruleNames)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(), gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: bindings);
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public void AttachCoreRavage(IUnit unit, int rating) =>
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(rating) }));

        public int DiceAtSix(IUnit attacker, IUnit defender) =>
            _evaluator.EvaluateAll(new ChargeContactContext(attacker, defender),
                    RuleParticipant.Actor(attacker))
                .OfType<RuleOperation.InvokeDealAutoWounds>()
                .Where(op => op.SuccessThreshold == 6)
                .Sum(op => op.DiceCount);
    }
}
