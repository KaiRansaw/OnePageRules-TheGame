using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #414 - the data behind the once-per-army limit, and the rename that made it dead code.
//
// The engine half is pinned by UniqueHeroTests. This is the half that explains why the bug existed and
// catches the next rename, because the code was never wrong in isolation:
//
//   ListValidator has raised a blocking Error for a second copy of a Unique unit since the Army Forge
//   existed. It matched the rule named "Unique", which is what OPR 3.5 called it. 3.6 folded the rule
//   into "Unique Hero". Across the 242 bundled books there are 213 references to the new name and NONE
//   to the old one - so for every book in the game, that Error could not fire. Code, comment and test
//   all agreed with each other and none of them agreed with the data.
//
// So the assertions here are about the corpus, not about one rule: how many carriers there are, that
// the engine recognises every spelling the corpus actually uses, and that the carriers are the named
// characters rather than something broader.
[TestFixture]
public class UniqueHeroShippedDataTests
{
    private static BookFile Load(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    /// <summary>Every rule name on a unit, including the ones its upgrade items grant.</summary>
    private static IEnumerable<string> RuleNames(RosterUnit unit) =>
        unit.Rules.Select(Name).Concat(unit.Items.SelectMany(i => i.Rules.Select(Name)));

    private static string Name(SpecialRuleEntry entry) => entry switch
    {
        SpecialRuleEntry_Core core => core.Name,
        SpecialRuleEntry_CoreNumeric num => num.Name,
        SpecialRuleEntry_Alias alias => Name(alias.AliasedRule),
        _ => entry.ToString() ?? "",
    };

    [Test]
    public void TheCorpus_IsFullOfOncePerArmyUnits()
    {
        // The size of what was unenforced. If this ever drops to near zero, either the books stopped
        // using the rule or - far more likely - it has been renamed again and the count below is the
        // only thing that would say so.
        int carriers = ShippedBooks.AllPaths()
            .Sum(p => Load(p).Units.Count(u => RuleNames(u).Any(CoreRuleCatalog.IsUniqueRuleName)));

        Assert.That(carriers, Is.GreaterThan(100),
            "the bundled books carried 213 once-per-army references when this was written. A sharp drop "
            + "means the rule has been renamed again and nothing is enforcing the limit.");
    }

    [Test]
    public void EverySpellingTheBooksUse_IsOneTheEngineKnows()
    {
        // The self-maintaining half, and the one that would have caught the original bug on the day the
        // 3.6 books landed. Any rule whose name contains "Unique" is making a once-per-army claim; if
        // the engine does not recognise the exact spelling, the claim is not enforced. "Unique Hero" was
        // exactly such a name for months.
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string path in ShippedBooks.AllPaths())
        {
            foreach (RosterUnit unit in Load(path).Units)
            {
                foreach (string name in RuleNames(unit))
                {
                    if (name.Contains("Unique", StringComparison.Ordinal)
                        && !CoreRuleCatalog.IsUniqueRuleName(name))
                    {
                        unknown.Add(name);
                    }
                }
            }
        }

        Assert.That(unknown, Is.Empty,
            "these rule names claim a unit is unique and the engine does not recognise them, so nothing "
            + "limits how many you may take. Add them to CoreRuleCatalog.UniqueRuleNames: ["
            + string.Join(", ", unknown) + "]");
    }

    [Test]
    public void EveryOncePerArmyUnit_IsAlsoAHero()
    {
        // 3.6's wording is "Counts as having Hero, and this model may only be taken once per army" - one
        // rule, both halves. So every carrier must also read as a Hero, which is what makes the force-org
        // cap, the unit-join and base sizing correct for it. If a book ever ships the once-per-army half
        // on something that is not a Hero, the two name lists have to stop being coupled and this says so.
        var notHeroes = new List<string>();

        foreach (string path in ShippedBooks.AllPaths())
        {
            foreach (RosterUnit unit in Load(path).Units)
            {
                var names = RuleNames(unit).ToList();
                if (names.Any(CoreRuleCatalog.IsUniqueRuleName) && !names.Any(CoreRuleCatalog.IsHeroRuleName))
                    notHeroes.Add($"{Path.GetFileNameWithoutExtension(path)}: {unit.Name}");
            }
        }

        Assert.That(notHeroes, Is.Empty,
            "a once-per-army unit that is not a Hero would need the two rule-name lists separated");
    }

    [Test]
    public void TheSupplement_GivesUniqueHeroItsCountsAsHeroGrant()
    {
        // The in-game half, and the reason the catalog must NOT define this rule itself: the behaviour is
        // a hook. A hookless marker copy in CoreRuleCatalog would shadow this definition and silently
        // drop the grant, leaving a rule that resolves, validates, and does nothing.
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json");
        SpecialRuleDefinition? uniqueHero = BookRuleSupplement.LoadDefinitions(File.ReadAllText(path))
            .FirstOrDefault(d => d.Name == CoreRuleCatalog.UniqueHeroRuleName);

        Assert.That(uniqueHero, Is.Not.Null,
            $"the supplement must define {CoreRuleCatalog.UniqueHeroRuleName}");
        Assert.That(uniqueHero!.Passive.Any(h =>
                h.HookID == EHookID.Lifecycle_OnUnitCreated
                && h.Effect is Effect.AddRule { RuleName: "Hero" }),
            Is.True,
            "3.6.0: \"Counts as having Hero\" - without this grant a named character is not a Hero in "
            + "play, whatever the army-building checks say");
    }
}
