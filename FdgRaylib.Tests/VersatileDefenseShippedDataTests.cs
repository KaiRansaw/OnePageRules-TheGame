using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Versatile Defense (Army Forge 3.6.0): "When a unit where most models have this rule is shot/charged from over 9"/6"
// away, pick one effect: the unit either gets +1 to Defense rolls, or enemy units get -1 to hit rolls." The engine asks
// the defender as the attack comes in (FDG.VersatileDefense - VersatileDefenseRuleIntegrationTests). Until 2026-11-27 the
// data played the older wording: a pick at deployment / activation start granting "Versatile Defense (Guard)" or
// "(Evasion)" until the unit's next activation. This pins that no shipped book still carries that stance.
[TestFixture]
public class VersatileDefenseShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<(string Where, SpecialRuleDefinition Rule)> AllShipped()
    {
        foreach (string supplement in new[] { "GdfRuleSupplement.json", "AofRuleSupplement.json" })
            foreach (SpecialRuleDefinition rule in BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, supplement))))
                yield return (supplement, rule);
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions) yield return (Path.GetFileName(path), rule);
        }
    }

    [Test]
    public void EveryShippedVersatileDefense_IsPickedByTheEngine_NotAStanceAtActivation()
    {
        var all = AllShipped().ToList();
        var rules = all.Where(a => a.Rule.Name == VersatileDefense.Name).ToList();
        Assert.That(rules.Count, Is.GreaterThanOrEqualTo(37), "the Disciples / Havoc / Brute books of every system");
        var wrong = rules.Where(a => a.Rule.Passive.Count != 0 || a.Rule.Activated.Count != 0).Select(a => a.Where).ToList();
        Assert.That(wrong, Is.Empty, "still a hook: " + string.Join(", ", wrong));
        Assert.That(all.Where(a => a.Rule.Name.StartsWith("Versatile Defense (", StringComparison.Ordinal)).Select(a => a.Where),
            Is.Empty, "the old Guard / Evasion stance rules are gone");
    }

    [Test]
    public void TheAura_StillConfersVersatileDefense()
    {
        var auras = AllShipped().Where(a => a.Rule.Name == "Versatile Defense Aura").ToList();
        Assert.That(auras, Is.Not.Empty);
        foreach ((string where, SpecialRuleDefinition rule) in auras)
        {
            HookEntry entry = rule.Passive.Single();
            Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>(), where);
            Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(VersatileDefense.Name), where);
        }
    }
}
