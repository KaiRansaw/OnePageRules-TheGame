using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FdgRaylib;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #414 - the Star Quest enemy collections shipped in armies/GF Star Quest.
//
// Star Quest's enemy side is not an army in the ordinary sense. StarQuestHorde.DeployWave treats it as a
// POOL: each wave draws from the units still in reserve or already destroyed - they come back - until the
// wave's points budget is spent. So the collection's job is to be a varied pool, and a collection too
// small for a late wave does not fail loudly, it logs "the enemy collection is empty, nobody arrives"
// and the wave simply does not happen.
//
// Three separate things had to be true before a GUI Star Quest game could start at all, and only the
// third is data:
//
//   1. the party must be stamped as a Star Quest army (QuestArmyGameSystemTests, engine side);
//   2. the lobby's army dropdown must read this folder rather than armies/GDF (asserted below);
//   3. this folder must contain something.
//
// All three were false at once, which is why the symptom was a grey LAUNCH button blaming the player's
// army. Each is pinned where it lives.
[TestFixture]
public class StarQuestCollectionShippedDataTests
{
    /// <summary>
    /// The shipped armies folder for a game type, or null when this checkout has no armies folder.
    /// <see cref="ArmyPaths.FolderPath"/> walks up from the test binary, so it finds the repo's folder.
    /// </summary>
    private static string? FolderFor(EGameSystem system) =>
        ArmyPaths.FolderPath is { } root
            ? Path.Combine(root, ArmyPaths.FolderNameFor(system))
            : null;

    private static List<string> CollectionPaths()
    {
        string? dir = FolderFor(EGameSystem.GFStarQuest);
        Assert.That(dir, Is.Not.Null, "no armies folder found from the test binary");
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir!, "*.fdgarmy").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    private static BuiltArmyFile Load(string path) =>
        JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)!;

    [Test]
    public void StarQuestHasItsOwnArmiesFolder_NotGrimdarkFutures()
    {
        // The lobby used to redirect every Quest system to armies/GDF, so it listed Grimdark Future
        // armies that the launch gate then refused while this folder went unread. The mapping is what
        // the dropdown uses, so it is asserted on its own rather than only through a file count.
        Assert.That(ArmyPaths.FolderNameFor(EGameSystem.GFStarQuest), Is.EqualTo("GF Star Quest"));
        Assert.That(ArmyPaths.FolderNameFor(EGameSystem.GFStarQuest),
            Is.Not.EqualTo(ArmyPaths.FolderNameFor(EGameSystem.GrimdarkFuture)));
    }

    [Test]
    public void TheFolderIsNotEmpty()
    {
        // The plainest statement of the blocker: with nothing here there is nothing to pick as the
        // enemy, so no Star Quest game can be set up however correct the rest of the path is.
        Assert.That(CollectionPaths(), Is.Not.Empty,
            "armies/GF Star Quest is empty - re-run --make-collections against the bundled books");
    }

    [Test]
    public void EveryCollection_IsAStarQuestArmy_AndPassesAStarQuestLobbysFilter()
    {
        // A collection stamped with the wrong system is refused by the launch gate exactly as the party
        // was, so this is the same seam from the other side. Asserted per file, because one bad file
        // would otherwise just be a list that never works and nobody knows why.
        var wrong = new List<string>();

        foreach (string path in CollectionPaths())
        {
            BuiltArmyFile army = Load(path);
            if (army.GameSystem != GameSystems.GFStarQuest
                || !GameSystems.IsAllowed(GameSystemInfo.ArmySource(EGameSystem.GFStarQuest), army.GameSystem))
            {
                wrong.Add($"{Path.GetFileName(path)}: '{army.GameSystem ?? "(none)"}'");
            }
        }

        Assert.That(wrong, Is.Empty,
            "every list in armies/GF Star Quest must be a Star Quest army, or the lobby refuses it");
    }

    [Test]
    public void EveryCollection_HasUnitsThatCostPoints()
    {
        // The pool is drawn by POINTS. A unit costing 0 would be drawn for free and could fill a wave on
        // its own, so a book that compiled to free units would quietly break the wave budget rather than
        // fail to load.
        var broken = new List<string>();

        foreach (string path in CollectionPaths())
        {
            BuiltArmyFile army = Load(path);
            if (army.Units.Count == 0) broken.Add($"{Path.GetFileName(path)}: no units");
            else if (army.Units.Any(u => u.PointCost <= 0))
                broken.Add($"{Path.GetFileName(path)}: {army.Units.Count(u => u.PointCost <= 0)} free unit(s)");
        }

        Assert.That(broken, Is.Empty);
    }

    [Test]
    public void MostCollections_AreBigEnoughForALateWave()
    {
        // Wave 10 of Horde draws wave * partyPoints / 4, doubled for a boss fight: about 1500 pts
        // against a 300 pt party. A collection below that runs dry in the late waves. This is a floor on
        // the CORPUS rather than on every file, because a few Star Quest books genuinely have small
        // rosters (Worker Unions compiles to 880 pts over 11 units) and that is the book's shape, not a
        // bake fault - padding it would mean inventing units. The test's job is to notice if the bake
        // starts producing thin collections across the board.
        List<BuiltArmyFile> all = CollectionPaths().Select(Load).ToList();
        if (all.Count == 0) Assert.Pass("no collections shipped; TheFolderIsNotEmpty is the test for that");

        int deep = all.Count(a => a.Units.Sum(u => u.PointCost) >= 1500);

        Assert.That(deep, Is.GreaterThan(all.Count / 2),
            $"only {deep} of {all.Count} collections hold 1500+ pts, so most would run dry in the late "
            + "waves - the bake should be the whole roster at base model count");
    }

    [Test]
    public void NoCollection_CarriesAnEmbeddedBookSnapshot()
    {
        // Deliberate, and worth pinning because it is the difference between 2.6MB and 17MB in the repo.
        // ListCompiler attaches the picks plus a full snapshot of the source book so a Forge re-open can
        // never be re-pointed at a newer catalog; a generated collection does not need that, and the
        // books are already bundled right beside it. BuiltArmyFile documents both fields as null on an
        // army not authored by the Forge, and such a file still plays.
        var fat = CollectionPaths()
            .Where(p => Load(p) is { } a && (a.Book != null || a.Selections != null))
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(fat, Is.Empty,
            "a collection is generated from a bundled book, so it should not embed a copy of it");
    }

    [Test]
    public void EveryCollection_BringsItsOwnRuleDefinitions()
    {
        // What replaces the embedded book at play time. Units, RuleDefinitions and Spells are what the
        // engine reads; dropping the book snapshot must not have taken the definitions with it, or every
        // enemy special rule in Star Quest would silently do nothing.
        var ruleless = new List<string>();

        foreach (string path in CollectionPaths())
        {
            BuiltArmyFile army = Load(path);
            bool anyRules = army.Units.Any(u => u.SpecialRules.Count > 0);
            if (anyRules && (army.RuleDefinitions == null || army.RuleDefinitions.Count == 0))
                ruleless.Add(Path.GetFileName(path));
        }

        Assert.That(ruleless, Is.Empty,
            "these collections have units with special rules and no definitions to resolve them against");
    }
}
