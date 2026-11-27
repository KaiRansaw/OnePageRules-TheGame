using System;
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

// Spell Accumulator (Army Forge 3.6.0): GF, Firefight, AoF and Skirmish end with "Friendly casters may only use this rule
// if this unit isn't Shaken."; GF: Star Quest and AoF: Quest print the rule without that sentence. Until 2026-11-27 every
// book carried the Shaken gate, so a Quest enemy accumulator stopped lending tokens while Shaken.
[TestFixture]
public class SpellAccumulatorPerSystemShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [TestCase("Grimdark Future", true)]
    [TestCase("GF Firefight", true)]
    [TestCase("Age of Fantasy", true)]
    [TestCase("AoF Skirmish", true)]
    [TestCase("GF Star Quest", false)]
    [TestCase("AoF Quest", false)]
    public void LendingIsGatedOnShaken_OnlyWhereThatGamePrintsIt(string folder, bool gated)
    {
        int found = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(BooksDirectory, folder), "*.fdgbook"))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            SpecialRuleDefinition? rule = book.RuleDefinitions.FirstOrDefault(r => r.Name == "Spell Accumulator");
            if (rule == null) continue;
            found++;
            HookEntry lending = rule.Passive.Single(p => p.Effect is Effect.EnableSpellLending);
            if (gated)
                Assert.That(lending.Condition, Is.EqualTo(new Condition.Not(new Condition.TokenPresent(TokenType.Shaken))), Path.GetFileName(path));
            else
                Assert.That(lending.Condition, Is.InstanceOf<Condition.Always>(), Path.GetFileName(path));
        }
        Assert.That(found, Is.GreaterThanOrEqualTo(2), folder);
    }
}
