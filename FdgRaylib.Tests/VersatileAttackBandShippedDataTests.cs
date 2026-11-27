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

// Versatile Warrior (Army Forge 3.6.0, all six games): "When this unit is activated, pick one effect: until the end of the
// activation, when shooting/charging enemies over 9"/6" away, all models with this rule in it either get AP(+1) or get +1
// to hit rolls." Its two picks, Versatile Attack (Piercing) / (Precision), still carried the 3.5 "over 9"" for both, so
// a charge from 7-9" got nothing (found by the Lane 1 deep hunt, 2026-11-27 - the 2026-11-18 audit moved the rest of the
// 9"/6" family to AttackedFromBeyond and missed these two).
[TestFixture]
public class VersatileAttackBandShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [Test]
    public void EveryShippedVersatileAttack_IsNineInchesShot_SixInchesCharged()
    {
        var all = new List<(string Where, SpecialRuleDefinition Rule)>();
        foreach (string supplement in new[] { "GdfRuleSupplement.json", "AofRuleSupplement.json" })
            foreach (SpecialRuleDefinition rule in BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, supplement))))
                if (rule.Name.StartsWith("Versatile Attack (", StringComparison.Ordinal)) all.Add((supplement, rule));
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions.Where(r => r.Name.StartsWith("Versatile Attack (", StringComparison.Ordinal)))
                all.Add((Path.GetFileName(path), rule));
        }

        Assert.That(all.Select(a => a.Rule.Name).Distinct(),
            Is.EquivalentTo(new[] { "Versatile Attack (Piercing)", "Versatile Attack (Precision)" }));
        var wrong = all.Where(a => a.Rule.Passive.Count != 1
                || a.Rule.Passive[0].Condition is not Condition.AttackedFromBeyond { ShotInches: 9f, ChargedInches: 6f })
            .Select(a => $"{a.Where}: {a.Rule.Name}").ToList();
        Assert.That(wrong, Is.Empty, "still a single 'over 9\"': " + string.Join(", ", wrong));
    }
}
