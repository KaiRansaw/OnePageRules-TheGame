using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// "This model gets X on its weapons in melee" / "... when shooting" (Army Forge 3.6.0, every game that has them). The data
// played each as "at the start of this unit's own activation, give it plain X until the activation ends", which was wrong
// three ways (found by the Lane 1 deep hunt, 2026-11-28):
//   - X also applied to the other kind of attack in that activation (Reliable when Shooting gave 2+ in melee on a charge);
//   - X never applied in the ENEMY's activation - a strike-back, or being charged (Brace in Melee did nothing at all);
//   - as a spell mark ("friendly units get Rending on their weapons in melee / Unstoppable on their weapons when shooting
//     against it once": Magic Fists and 39 books' worth of "Unstoppable when shooting" marks) the rule is handed over
//     INSIDE an attack, where no activation starts - so the spell did nothing.
// Each is now X's own hooks with the melee / shooting gate, live whenever the unit attacks.
[TestFixture]
public class GatedWeaponRuleShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static readonly string[] Melee =
    {
        "Rending on their weapons in melee", "Shred on its weapons in melee", "Unstoppable on its weapons in melee",
        "Bane on its weapons in melee",
    };
    private static readonly string[] Shooting =
    {
        "Shred on its weapons when shooting", "Unstoppable on their weapons when shooting", "Reliable when Shooting",
    };
    // Their plain rule only ever works in melee / against shooting anyway (Thrust "when charging", Unwieldy "when
    // charging", Brace "enemies charging this unit", Anti-Cover - cover is a shooting thing), so they carry X's hooks as-is.
    private static readonly string[] Inherent =
    {
        "Thrust in Melee", "Unwieldy on its weapons in melee", "Brace in Melee", "Anti-Cover when Shooting",
        // 2026-11-28 (later): the ungated mark rules had the same activation-start build, so their spells ("friendly
        // units get Bane / Shred on their weapons when attacking / Surge / Indirect on their weapons against it once")
        // did nothing; and "Indirect when shooting" (Indirect when Shooting Aura) still played 3.5's Indirect - no Hold
        // needed, -1 to hit after moving. Each is now its plain rule's hooks (Indirect: the 3.6.0 Hold-only one).
        "Bane on their weapons when attacking", "Shred on their weapons when attacking", "Surge on their weapons",
        "Indirect on their weapons", "Indirect when shooting",
    };

    [Test]
    public void IndirectWhenShooting_IsTheHoldOnlyIndirect()
    {
        foreach ((string where, SpecialRuleDefinition rule) in Shipped()
                     .Where(a => a.Rule.Name is "Indirect when shooting" or "Indirect on their weapons"))
        {
            Assert.That(rule.Passive.Select(h => h.Effect.GetType()),
                Is.EquivalentTo(new[] { typeof(Effect.IgnoreLineOfSight), typeof(Effect.IgnoreCover) }), where + ": no -1 to hit");
            Assert.That(rule.Passive.All(h => h.Condition is Condition.Not { Inner: Condition.TokenPresent { TType.Id: "MovedThisRound" } }),
                Is.True, where + ": only when the unit hasn't moved (a Hold)");
        }
    }

    private static List<(string Where, SpecialRuleDefinition Rule)> Shipped()
    {
        var names = new HashSet<string>(Melee.Concat(Shooting).Concat(Inherent));
        var all = new List<(string, SpecialRuleDefinition)>();
        foreach (string supplement in new[] { "GdfRuleSupplement.json", "AofRuleSupplement.json" })
            foreach (SpecialRuleDefinition rule in BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, supplement))))
                if (names.Contains(rule.Name)) all.Add((supplement, rule));
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions.Where(r => names.Contains(r.Name)))
                all.Add((Path.GetFileName(path), rule));
        }
        return all;
    }

    private static bool Gated(Condition c, bool melee) =>
        (melee ? c is Condition.IsMelee : c is Condition.Not { Inner: Condition.IsMelee })
        || (c is Condition.And and && Gated(and.Left, melee));

    [Test]
    public void NoneIsAnActivationStartGrant_AndEveryHookCarriesItsGate()
    {
        var all = Shipped();
        Assert.That(all.Select(a => a.Rule.Name).Distinct().Count(), Is.EqualTo(Melee.Length + Shooting.Length + Inherent.Length),
            "every one of the family is still shipped somewhere");

        var wrong = new List<string>();
        foreach ((string where, SpecialRuleDefinition rule) in all)
        {
            if (rule.Passive.Count == 0 || rule.Passive.Any(h => h.HookID.ToString().StartsWith("Activation_", StringComparison.Ordinal)))
                wrong.Add($"{where}: {rule.Name} is still an activation grant");
            else if (Melee.Contains(rule.Name) && !rule.Passive.All(h => Gated(h.Condition, melee: true)))
                wrong.Add($"{where}: {rule.Name} has a hook without the melee gate");
            else if (Shooting.Contains(rule.Name) && !rule.Passive.All(h => Gated(h.Condition, melee: false)))
                wrong.Add($"{where}: {rule.Name} has a hook without the shooting gate");
        }
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
    }

    [Test]
    public void BraceInMelee_IsBraceOne_WhenCharged()
    {
        foreach ((string where, SpecialRuleDefinition rule) in Shipped().Where(a => a.Rule.Name == "Brace in Melee"))
        {
            Assert.That(rule.Passive.Single().HookID, Is.EqualTo(EHookID.Melee_OnChargeContact), where);
            Assert.That(rule.Passive.Single().Seat, Is.EqualTo(ERuleSeat.Subject), where + ": the charged unit's rule");
            Assert.That(rule.Passive.Single().Effect,
                Is.EqualTo(new Effect.ChargeImpactHits(new ValueSource.Negate(new ValueSource.Literal(1)), 0)),
                where + ": Brace(1) - enemies charging it get -1 Impact roll");
        }
    }

    [Test]
    public void EveryCopyOfARule_IsTheSame()
    {
        var differing = Shipped().GroupBy(a => a.Rule.Name)
            .Where(g => g.Select(a => JsonSerializer.Serialize(a.Rule.Passive, RuleJson.Options)).Distinct().Count() > 1)
            .Select(g => g.Key).ToList();
        Assert.That(differing, Is.Empty);
    }
}
