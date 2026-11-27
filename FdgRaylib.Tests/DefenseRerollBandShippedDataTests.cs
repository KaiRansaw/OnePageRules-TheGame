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

// Army Forge 3.6.1 (harvested 2026-11-27): Fragment, Obliterate and Reap now make the target re-roll successful
// unmodified Defense results of 4-6 (were 5-6), and Fragment now also fires against Defense 4+ (was 5+):
//   Fragment   - "When attacking units where most models have Defense 4+ to Defense 6+, the target must re-roll
//                 successful unmodified Defense results of 4-6."
//   Obliterate - "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, the
//                 target must re-roll successful unmodified Defense results of 4-6." (Tough 9 / 27 in the
//                 skirmish / Quest games)
//   Reap       - "When attacking units that are Shaken or Fatigued, the target must re-roll successful unmodified
//                 Defense results of 4-6."
// The engine's re-roll band is pure data (RerollCondition.OnUnmodifiedValue.MinValue, pinned engine-side by
// RerollThresholdRuleIntegrationTests), so these pin the DATA: both supplements and every book's embedded copy.
[TestFixture]
public class DefenseRerollBandShippedDataTests
{
    private static readonly string[] Rules = { "Fragment", "Obliterate", "Reap" };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<(string Where, SpecialRuleDefinition Rule)> EveryShippedDefinition()
    {
        foreach (string supplement in new[] { "GdfRuleSupplement.json", "AofRuleSupplement.json" })
        {
            foreach (SpecialRuleDefinition rule in BookRuleSupplement.LoadDefinitions(
                         File.ReadAllText(Path.Combine(BooksDirectory, supplement))))
            {
                if (Rules.Contains(rule.Name)) yield return (supplement, rule);
            }
        }

        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions.Where(r => Rules.Contains(r.Name)))
            {
                yield return (Path.GetFileName(path), rule);
            }
        }
    }

    [Test]
    public void EveryShippedCopy_RerollsDefenseFourToSix()
    {
        var all = EveryShippedDefinition().ToList();
        Assert.That(all.Select(d => d.Rule.Name).Distinct(), Is.EquivalentTo(Rules),
            "each of the three rules is shipped somewhere");

        var wrong = new List<string>();
        foreach ((string where, SpecialRuleDefinition rule) in all)
        {
            var rerolls = rule.Passive.Select(e => e.Effect).OfType<Effect.Reroll>().ToList();
            if (rerolls.Count != 1
                || rerolls[0].Roll != ERollKind.Save
                || rerolls[0].Condition is not RerollCondition.OnUnmodifiedValue { MinValue: 4 })
            {
                wrong.Add($"{where}: {rule.Name}");
            }

            if (!(rule.Description ?? "").Contains("Defense results of 4-6", StringComparison.Ordinal))
            {
                wrong.Add($"{where}: {rule.Name} (description)");
            }
        }

        Assert.That(wrong, Is.Empty, "copies still on the old 5-6 band: " + string.Join(", ", wrong));
    }

    [Test]
    public void Fragment_FiresAgainstDefenseFourPlus()
    {
        var wrong = new List<string>();
        foreach ((string where, SpecialRuleDefinition rule) in EveryShippedDefinition().Where(d => d.Rule.Name == "Fragment"))
        {
            HookEntry entry = rule.Passive.Single();
            if (entry.Condition is not Condition.StatGreaterOrEqualTo { Stat: EStatKind.Defense, StatValue: 4 })
            {
                wrong.Add(where);
            }

            if (!(rule.Description ?? "").Contains("Defense 4+ to Defense 6+", StringComparison.Ordinal))
            {
                wrong.Add(where + " (description)");
            }
        }

        Assert.That(wrong, Is.Empty, "'units where most models have Defense 4+ to Defense 6+': " + string.Join(", ", wrong));
    }
}
