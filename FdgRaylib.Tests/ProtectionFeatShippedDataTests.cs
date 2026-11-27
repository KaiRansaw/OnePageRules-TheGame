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

// Protection Feat (Army Forge 3.6.0): "Once per game, when this unit takes wounds and most of its models have this rule,
// you may use this rule and roll one die per wound, and on a 5+ it is ignored." The engine asks it as the wounds land
// (FDG.ProtectionFeat - ProtectionFeatRuleIntegrationTests). Until 2026-11-27 the data made it a brace taken at the
// unit's own activation, lasting until its next one, through a "Protection Feat Guard" rule. This pins that no shipped
// book still carries that brace - it would offer the feat a second way.
[TestFixture]
public class ProtectionFeatShippedDataTests
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
    public void EveryShippedProtectionFeat_IsReadByTheEngine_NotBracedAtActivation()
    {
        var all = AllShipped().ToList();
        var feats = all.Where(a => a.Rule.Name == ProtectionFeat.Name).ToList();
        Assert.That(feats.Count, Is.GreaterThanOrEqualTo(13), "GF, Firefight, Star Quest x3 books, AoF / Skirmish / AoF: Quest Saurians and Crazed Zealots");
        var wrong = feats.Where(a => a.Rule.Passive.Count != 0 || a.Rule.Activated.Count != 0).Select(a => a.Where).ToList();
        Assert.That(wrong, Is.Empty, "still a hook: " + string.Join(", ", wrong));
        Assert.That(all.Where(a => a.Rule.Name == "Protection Feat Guard").Select(a => a.Where), Is.Empty,
            "the old activation brace is gone");
    }

    [Test]
    public void TheAura_StillConfersProtectionFeat()
    {
        var auras = AllShipped().Where(a => a.Rule.Name == "Protection Feat Aura").ToList();
        Assert.That(auras, Is.Not.Empty);
        foreach ((string where, SpecialRuleDefinition rule) in auras)
        {
            HookEntry entry = rule.Passive.Single();
            Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>(), where);
            Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(ProtectionFeat.Name), where);
        }
    }
}
