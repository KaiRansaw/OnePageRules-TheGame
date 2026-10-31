using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Quest;
using FDG.Stages;
using FDG.StateMachine;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #414 - the imported Star Quest rules data and the engine's hardcoded copies must agree.
//
// There are two sources of truth for the same thing and there always will be, for a reason:
//
//   * StarQuestRules.json is what Army Forge publishes - 33 consumables with their tiers, costs and
//     rules text, 6 injuries, 4 status conditions. Authoritative, and re-imported whenever OPR changes
//     something.
//   * QuestItems.All, StarQuestMissionRules.Injuries and QuestTreasure's condition constants are what
//     the engine PLAYS. They have to be code, because each one needs a behaviour attached - an item that
//     is a string in a JSON file cannot restore 3 wounds.
//
// The parsed side (SqRules.Consumables / Injuries / Conditions) is currently read by nothing at all.
// That is fine - it is not the live list - but it leaves the two free to drift apart silently, and a
// re-import that renamed an item or moved a cost would simply not show up. The shop would sell the old
// price forever and nobody would know which was right.
//
// So this fixture is the join. It is also the reason the duplication is acceptable: two sources of truth
// with a test between them is a different thing from two sources of truth.
[TestFixture]
public class StarQuestImportedRulesAgreementTests
{
    [SetUp]
    public void SetUp() =>
        Assert.That(StarQuestData.LoadError, Is.Null, StarQuestData.LoadError);

    // ── Consumables ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void EveryImportedConsumable_IsOneTheEngineSells()
    {
        // Catches an item ADDED by a re-import. Until it is in QuestItems with an effect behind it, the
        // shop cannot sell it and a hero can never get one - so a new item should fail here loudly rather
        // than be invisible.
        var missing = StarQuestData.Rules.Consumables
            .Select(item => item.Name)
            .Where(name => QuestItems.Find(name) == null)
            .ToList();

        Assert.That(missing, Is.Empty,
            "these items are in the imported data and not in QuestItems - add them with their effect, "
            + "or to QuestItems.NotPossible if this engine cannot play them");
    }

    [Test]
    public void EveryItemTheEngineSells_IsOneTheBooksPublish()
    {
        // And the other direction, which catches an item REMOVED or renamed upstream. An item the books
        // no longer have is one the shop should stop selling.
        var names = StarQuestData.Rules.Consumables.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var extra = QuestItems.All.Select(i => i.Name).Where(n => !names.Contains(n)).ToList();

        Assert.That(extra, Is.Empty,
            "these items are sold by the engine and are not in the imported data - either the import is "
            + "stale or the books dropped them");
    }

    [Test]
    public void TierAndCostMatchTheImportedData()
    {
        // The two numbers a player feels: what it costs and what level unlocks it. A silent disagreement
        // here is a shop charging the wrong price, which looks like a balance decision rather than a bug.
        var wrong = new List<string>();

        foreach (SqItem imported in StarQuestData.Rules.Consumables)
        {
            if (QuestItems.Find(imported.Name) is not { } engine) continue;    // covered above
            if (imported.Tier is { } tier && tier != engine.Tier)
                wrong.Add($"{imported.Name}: tier {engine.Tier} in the engine, {tier} imported");
            if (imported.Cost is { } cost && cost != engine.Cost)
                wrong.Add($"{imported.Name}: {engine.Cost}c in the engine, {cost}c imported");
        }

        Assert.That(wrong, Is.Empty, string.Join(" | ", wrong));
    }

    [Test]
    public void TheRulesTextMatchesTheImportedData()
    {
        // The description is what the player reads in the shop and on the skill menu before spending
        // credits, so it is the one that has to be the book's words rather than a paraphrase that was
        // right once. Whitespace is normalised: the import carries the PDF's line breaks.
        var wrong = new List<string>();

        foreach (SqItem imported in StarQuestData.Rules.Consumables)
        {
            if (QuestItems.Find(imported.Name) is not { } engine) continue;
            if (imported.Description is not { } text) continue;
            if (!Normalise(text).Equals(Normalise(engine.Description), StringComparison.Ordinal))
                wrong.Add($"{imported.Name}:\n    engine:   {engine.Description}\n    imported: {text}");
        }

        Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
    }

    // ── Injuries ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheInjuriesAreTheSameSix()
    {
        // Compared as a SET, deliberately. The engine's list is indexed by a D6 and the import does not
        // preserve the book's order (it arrives as Blinded Eye, Chest Wound, Spinal Injury, ...), so an
        // ordered comparison here would fail for a reason that is not a fault. The order itself is pinned
        // separately below, against the book rather than against the import.
        var imported = StarQuestData.Rules.Injuries.Select(i => i.Name).ToList();
        var engine = StarQuestMissionRules.Injuries.Skip(1).ToList();   // [0] is the unused 0 face

        Assert.That(engine, Is.EquivalentTo(imported));
    }

    [Test]
    public void TheInjuryTableIsTheBooksD6Order()
    {
        // GFSQ Core Rules / Beginner's Guide injury table: 1 Chest Wound, 2 Blinded Eye, 3 Smashed Leg,
        // 4 Broken Arm, 5 Spinal Injury, 6 Crushed Spirit. The engine indexes this array with the roll,
        // so the ORDER is the rule - getting it wrong gives a hero the wrong injury and nothing anywhere
        // looks broken. The import cannot pin this because its order is arbitrary.
        Assert.That(StarQuestMissionRules.Injuries, Is.EqualTo(new[]
        {
            "", "Chest Wound", "Blinded Eye", "Smashed Leg", "Broken Arm", "Spinal Injury", "Crushed Spirit",
        }).AsCollection);
    }

    // ── Status conditions ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheConditionsAreTheSameFour()
    {
        var imported = StarQuestData.Rules.Conditions.Select(c => c.Name).ToList();
        var engine = new[]
        {
            QuestTreasure.Afflicted, QuestTreasure.Crippled, QuestTreasure.Diseased, QuestTreasure.Impaired,
        };

        Assert.That(engine, Is.EquivalentTo(imported),
            "a condition the engine does not know would be applied to a hero and never tested away");
    }

    // ── The parsed-but-unread lists ─────────────────────────────────────────────────────────────────

    [Test]
    public void TheImportedListsAreNotEmpty()
    {
        // The quiet failure this whole fixture rests on: if the import produced nothing, every comparison
        // above would pass by comparing against an empty set. Stated once, loudly.
        Assert.Multiple(() =>
        {
            Assert.That(StarQuestData.Rules.Consumables.Count, Is.EqualTo(33), "consumables");
            Assert.That(StarQuestData.Rules.Injuries.Count, Is.EqualTo(6), "injuries");
            Assert.That(StarQuestData.Rules.Conditions.Count, Is.EqualTo(4), "status conditions");
        });
    }

    private static string Normalise(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace('’', '\'')
            .Replace('“', '"')
            .Replace('”', '"');
}
