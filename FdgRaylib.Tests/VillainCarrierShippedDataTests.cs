using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #412 Villain - "AI units within 6" use the Villain's Qua for Morale Tests if it's better than their
// own, as long as the Villain isn't Shaken." (GFSQ Core Rules v2.1.0.)
//
// The engine half is pinned by VillainAuraTests. This is the half that pins the DATA, and it exists
// because the engine half passed for weeks while the mechanic did nothing at all.
//
// The engine looked for a rule named "Villain". No book has one. Star Quest's enemy books instead define
// a rule NAMED "Hero" whose description is Villain's text, verbatim, and put it on their boss units -
// Alien Hives carries it on Grunt Veteran, Snatcher Lord, Prime Warrior and Hive Lord. So the lookup
// never matched anything, in any game, and nothing in the engine suite could tell: a rule that is
// implemented correctly and never reachable looks exactly like a rule that works.
//
// What this fixture therefore asserts is the link between the two halves, which is the thing that was
// broken: the name the engine matches on must be a name the shipped books actually carry, and the Star
// Quest corpus must contain carriers for it. A test that only checked "Villain is implemented" or only
// checked "the books parse" would have passed throughout.
[TestFixture]
public class VillainCarrierShippedDataTests
{
    /// <summary>Star Quest's enemy books - the only corpus where the Villain aura applies.</summary>
    private static IEnumerable<string> StarQuestBooks() =>
        ShippedBooks.AllPaths().Where(p => Path.GetFileName(p)
            .StartsWith("GFSQ-", StringComparison.OrdinalIgnoreCase));

    private static BookFile Load(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    /// <summary>
    /// Every rule name a unit in this book carries on its own profile, plus the rules its wargear
    /// grants. Upgrade options are deliberately NOT walked: an option's rules are only on the unit if
    /// the option is taken, and a Villain is a property of the printed profile.
    /// </summary>
    private static IEnumerable<string> UnitRuleNames(BookFile book) =>
        book.Units.SelectMany(u => u.Rules.Select(RuleName)
            .Concat(u.Items.SelectMany(i => i.Rules.Select(RuleName))));

    private static string RuleName(SpecialRuleEntry entry) => entry switch
    {
        SpecialRuleEntry_Core core => core.Name,
        SpecialRuleEntry_CoreNumeric numeric => numeric.Name,
        SpecialRuleEntry_Text text => text.Name,
        SpecialRuleEntry_Alias alias => alias.Name,
        _ => entry.PrintableName,
    };

    [Test]
    public void TheStarQuestCorpus_IsBundled()
    {
        // A guard for every other test here: if the Star Quest books are not on disk, the carrier
        // assertions below would pass on an empty set and report nothing.
        Assert.That(StarQuestBooks().Count(), Is.GreaterThan(40),
            "the GF Star Quest books must be bundled - 54 were imported from Army Forge (the index "
            + "lists 56, of which Gangs of New Eden and Wormhole Daemons are empty upstream)");
    }

    [Test]
    public void EveryNameTheEngineReadsAsVillain_IsCarriedBySomeBook()
    {
        // THE test. The engine's list of Villain names is only useful if the books use one of them, and
        // the bug was precisely that they did not. Asserted over the whole list rather than over "Hero"
        // alone, so adding a name that no book carries is visible, and so is OPR renaming the one that
        // does.
        var carried = new HashSet<string>(
            StarQuestBooks().SelectMany(p => UnitRuleNames(Load(p))), StringComparer.OrdinalIgnoreCase);

        var live = FDG.Stages.QuestChallenges.VillainRuleNames
            .Where(name => carried.Contains(name)).ToList();

        Assert.That(live, Is.Not.Empty,
            "no name in QuestChallenges.VillainRuleNames appears on any unit in any Star Quest book, "
            + "so the Villain aura cannot fire. This is how the rule shipped dead: the engine matched "
            + "\"Villain\" and the books carry \"Hero\". Names the books DO carry include: "
            + string.Join(", ", carried.OrderBy(n => n).Take(25)));
    }

    [Test]
    public void TheHeroRule_IsTheNameTheBooksUse()
    {
        // Stated as itself, because it is the surprising half and a failure here should not have to be
        // inferred from the test above.
        var books = StarQuestBooks()
            .Where(p => UnitRuleNames(Load(p)).Any(n => n == "Hero")).ToList();

        Assert.That(books.Count, Is.GreaterThan(40),
            "Star Quest enemy books mark their bosses with the \"Hero\" rule, whose description in "
            + "those books is Villain's wording - so nearly every book should have carriers");
    }

    [Test]
    public void AlienHives_CarriesItOnItsFourBosses()
    {
        // One book pinned by name against the live Army Forge data checked on 2026-10-31, so a future
        // re-import that drops the rule fails with something concrete rather than with a count.
        BookFile book = Load(ShippedBooks.Path("GFSQ-AlienHives" + BookFile.EXTENSION_WITH_PERIOD));

        var carriers = book.Units
            .Where(u => u.Rules.Select(RuleName).Any(n => n == "Hero"))
            .Select(u => u.Name).OrderBy(n => n).ToList();

        Assert.That(carriers, Is.EquivalentTo(new[]
        {
            "Grunt Veteran", "Hive Lord", "Prime Warrior", "Snatcher Lord",
        }), "Alien Hives' Star Quest enemy book puts Hero on exactly its four boss profiles");
    }

    [Test]
    public void TheNamedBosses_CarryUniqueHeroInsteadOfHero()
    {
        // 3.6.0's "Unique Hero" is "Counts as having Hero, and this model may only be taken once per
        // army". Across the WHOLE bundled corpus its carriers have it instead of plain "Hero", never
        // alongside - so a Villain check that knew only "Hero" would light up the generic bosses and
        // leave every named enemy character out. Asserted as the exclusivity, because that is the
        // surprising part and the reason the alias is needed at all.
        var both = new List<string>();
        int uniqueHeroes = 0;

        foreach (string path in StarQuestBooks())
        {
            BookFile book = Load(path);
            foreach (RosterUnit unit in book.Units)
            {
                var names = unit.Rules.Select(RuleName)
                    .Concat(unit.Items.SelectMany(i => i.Rules.Select(RuleName))).ToHashSet();
                if (!names.Contains("Unique Hero")) continue;
                uniqueHeroes++;
                if (names.Contains("Hero")) both.Add($"{Path.GetFileName(path)}: {unit.Name}");
            }
        }

        Assert.That(uniqueHeroes, Is.GreaterThan(20),
            "Star Quest's named enemy characters carry Unique Hero - 30 when this was written");
        Assert.That(both, Is.Empty,
            "no unit carries both Hero and Unique Hero, which is exactly why Unique Hero has to be "
            + "read as a Villain name in its own right rather than assumed redundant");
        Assert.That(FDG.Stages.QuestChallenges.VillainRuleNames, Does.Contain("Unique Hero"),
            "and the engine must know that name, or the named bosses lend nothing");
    }

    [Test]
    public void NoStarQuestBook_CarriesARuleLiterallyNamedVillain()
    {
        // The finding, recorded as an assertion so it stays true or stops being a surprise. If OPR ever
        // starts shipping the literal name, this fails and the comments above need rewriting - which is
        // better than quietly having two spellings in play and no one knowing which one is live.
        var withVillain = StarQuestBooks()
            .Where(p => UnitRuleNames(Load(p)).Any(n => n.Equals("Villain", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName).ToList();

        Assert.That(withVillain, Is.Empty,
            "no book carried a rule named \"Villain\" when this was written; if one does now, the "
            + "engine already handles it (VillainRuleNames lists both) but the explanation of WHY "
            + "\"Hero\" is in that list should be updated");
    }
}
