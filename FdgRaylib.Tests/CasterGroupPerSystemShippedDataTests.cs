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

// Caster Group reads differently per game (Army Forge 3.6.0):
//   GF / AoF: "Pick one model ... to have Caster(X), where X is the total number of models with this rule ... The caster
//             loses all unspent spell tokens at the end of the round."
//   Firefight / Skirmish / Star Quest / AoF: Quest: "This model counts as having Caster(1)."
// Caster(1) keeps its tokens from round to round (up to 6), like every Caster. Until 2026-11-27 every book carried the
// GF version, so a skirmish / Quest Caster Group lost its token each round and could never save up for a spell
// costing 2 or 3.
[TestFixture]
public class CasterGroupPerSystemShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [TestCase("Grimdark Future", typeof(TokenClearTrigger.RoundEnd))]
    [TestCase("Age of Fantasy", typeof(TokenClearTrigger.RoundEnd))]
    [TestCase("GF Firefight", typeof(TokenClearTrigger.ManualOnly))]
    [TestCase("AoF Skirmish", typeof(TokenClearTrigger.ManualOnly))]
    [TestCase("GF Star Quest", typeof(TokenClearTrigger.ManualOnly))]
    [TestCase("AoF Quest", typeof(TokenClearTrigger.ManualOnly))]
    public void CasterGroupTokens_FollowThatGamesWording(string folder, Type clear)
    {
        int found = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(BooksDirectory, folder), "*.fdgbook"))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            SpecialRuleDefinition? rule = book.RuleDefinitions.FirstOrDefault(r => r.Name == "Caster Group");
            if (rule == null) continue;
            found++;
            Effect.GrantToken grant = rule.Passive.Select(p => p.Effect).OfType<Effect.GrantToken>()
                .Single(g => g.TType == TokenType.SpellTokens);
            Assert.That(grant.Clear, Is.InstanceOf(clear), Path.GetFileName(path));
        }
        Assert.That(found, Is.GreaterThanOrEqualTo(2), folder);
    }
}
