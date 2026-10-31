using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Quest;
using FDG.Rules.Serialization;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #418 - AoF: Quest's player-side dataset: the hero roster in Assets/AoFQuest and the per-system
// plumbing that lets it exist alongside Star Quest's.
//
// WHAT THIS FIXTURE IS FOR. There are two ways this can be silently wrong, and neither shows up in
// play:
//
//   1. THE WRONG ARMY FORGE ID. AoF: Quest is served as id 7 (the player's heroes) and id 8 (the enemy
//      army books, #417), under one index slug, with indistinguishable payloads. An import pointed at
//      id 8 writes a "hero roster" of enemy profiles that loads, builds and plays - Beastmen would hand
//      you a Ndoli Warrior at 35pts Q4 D4 instead of a Ndoli Beast Lord at 45pts Q6 D6, over a roster
//      of 20 units instead of 5.
//   2. THE WRONG DATASET AT RUNTIME. Until #418 the dataset was a static singleton behind one folder
//      path, so the first screen to touch it decided which system the whole process meant. An AoF:
//      Quest Hero Builder would have shown Star Quest's factions and classes.
//
// So the assertions below are about WHICH data came back, not that files exist.
[TestFixture]
public class AoFQuestHeroDataTests
{
    private static QuestDataSet AoF => QuestDataSet.For(EGameSystem.AoFQuest);
    private static QuestDataSet Sq => QuestDataSet.For(EGameSystem.GFStarQuest);

    // ── The dataset is its own ────────────────────────────────────────────────────────────────────

    [Test]
    public void TheTwoQuestDatasets_AreSeparateObjectsOverSeparateFolders()
    {
        Assert.That(AoF, Is.Not.SameAs(Sq), "one dataset per Quest system, or one system wins the process");
        Assert.That(AoF.FolderName, Is.EqualTo("AoFQuest"));
        Assert.That(Sq.FolderName, Is.EqualTo("StarQuest"));
        Assert.That(AoF.System, Is.EqualTo(EGameSystem.AoFQuest));
        Assert.That(Sq.System, Is.EqualTo(EGameSystem.GFStarQuest));

        // Asked twice, the same instance comes back - the roster is ~1MB of JSON and the Hero Builder
        // touches it every frame.
        Assert.That(QuestDataSet.For(EGameSystem.AoFQuest), Is.SameAs(AoF));
    }

    [Test]
    public void TheRosterLoads_WithNoError()
    {
        Assert.That(AoF.LoadError, Is.Null, AoF.LoadError);
        Assert.That(AoF.Factions, Is.Not.Empty,
            $"no AoF: Quest factions loaded from {AoF.Folder} - re-run --import-starquest with "
            + "--quest-system age-of-fantasy-quest");
    }

    [Test]
    public void TheRosterIsTheHeroDataset_NotTheEnemyBooks()
    {
        // THE discriminating test. The enemy books (id 8) average ~20 units a faction and 46 of them
        // ship in Assets/Books/AoF Quest; the hero roster (id 7) averages ~3.5. Anything above a few
        // means the import was pointed at the enemy id.
        double perFaction = AoF.Factions.Average(f => f.Heroes.Count);

        Assert.That(perFaction, Is.LessThan(10),
            $"the AoF: Quest roster averages {perFaction:0.0} heroes a faction. The HERO roster (Army "
            + "Forge id 7) runs 2-10; the ENEMY books (id 8) run 6-35 - this looks like the enemy "
            + "dataset, which would give every hero an enemy's profile");

        // And the known hero profile, checked against the live API on 2026-10-31.
        SqFaction beastmen = AoF.Factions.Single(f => f.Faction == "Beastmen");
        SqHero lord = beastmen.Heroes.Single(h => h.Name == "Ndoli Beast Lord");
        Assert.That(lord.Cost, Is.EqualTo(45), "the hero profile is 45pts; the enemy book's is not");
        Assert.That(lord.Q, Is.EqualTo(6));
        Assert.That(lord.D, Is.EqualTo(6));
        Assert.That(beastmen.Heroes.Select(h => h.Name), Has.None.EqualTo("Ndoli Warrior"),
            "Ndoli Warrior is an ENEMY unit (id 8) - it has no business in the hero roster");
    }

    [Test]
    public void EveryHeroProfile_IsPricedAndSingleModel()
    {
        var broken = new List<string>();
        foreach (SqFaction f in AoF.Factions)
            foreach (SqHero h in f.Heroes)
            {
                if (h.Cost <= 0) broken.Add($"{f.Faction}/{h.Name}: {h.Cost}pts");
                if (h.Q <= 0 || h.D <= 0) broken.Add($"{f.Faction}/{h.Name}: Q{h.Q} D{h.D}");
            }

        Assert.That(broken, Is.Empty);
    }

    [Test]
    public void TheRosterIsBigEnoughToPlay()
    {
        int heroes = AoF.Factions.Sum(f => f.Heroes.Count);
        Assert.That(AoF.Factions.Count, Is.GreaterThanOrEqualTo(40),
            $"{AoF.Factions.Count} factions; the official index lists 48 and two are empty placeholders");
        Assert.That(heroes, Is.GreaterThanOrEqualTo(140), $"{heroes} hero profiles");
    }

    // ── Classes and skills ───────────────────────────────────────────────────────────────────────

    [Test]
    public void TheClassesAreAoFQuests_NotStarQuests()
    {
        // OPR publishes the same Quest ruleset under two sets of class names - AoF: Quest keeps the
        // fantasy ones, Star Quest renames 12 of the 13. A screen showing "Soldier" and "Psychic" to an
        // AoF: Quest player is the runtime-dataset bug this fixture's header describes.
        var aofNames = AoF.Classes.Select(c => c.Name).ToList();

        Assert.That(aofNames, Contains.Item("Fighter"));
        Assert.That(aofNames, Contains.Item("Wizard"));
        Assert.That(aofNames, Contains.Item("Rogue"));
        Assert.That(aofNames, Contains.Item("Druid"));
        Assert.That(aofNames, Has.None.EqualTo("Soldier"), "Soldier is Star Quest's name for Fighter");
        Assert.That(aofNames, Has.None.EqualTo("Psychic"), "Psychic is Star Quest's name for Wizard");
        Assert.That(aofNames.Count, Is.EqualTo(Sq.Classes.Count()),
            "the two systems are one ruleset with two naming schemes, so the class COUNT must match");
    }

    [Test]
    public void EveryClass_HasAFeatureAndSomeSkillsToLearn()
    {
        var thin = new List<string>();
        foreach (SqClass cls in AoF.Classes)
        {
            if (AoF.ClassFeat(cls) is null) thin.Add($"{cls.Name}: no class feature");
            if (!AoF.ClassSkills(cls, tier: 0).Any()) thin.Add($"{cls.Name}: no tier-0 skills");
        }

        Assert.That(thin, Is.Empty);
        Assert.That(AoF.DefaultSkills(0), Is.Not.Empty, "every class can learn the default skills");
    }

    [Test]
    public void EverySkillAClassOffers_HasAStatOrIsItsFeature()
    {
        var bad = new List<string>();
        foreach (SqClass cls in AoF.Classes)
            foreach (SqSkill s in AoF.ClassSkills(cls, 0).Concat(AoF.ClassSkills(cls, 1)))
                if (QuestDataSet.SkillStat(s) is not ("Str" or "Dex" or "Wil"))
                    bad.Add($"{cls.Name}/{s.Name}: type {s.Type}");

        Assert.That(bad, Is.Empty, "a learnable skill is tested against Str, Dex or Wil");
    }

    // ── The faction picker ───────────────────────────────────────────────────────────────────────

    [Test]
    public void EveryFaction_LandsInAGroupThatThePickerDraws()
    {
        // The picker only draws its own group list, so a faction grouped as anything else disappears
        // from the screen entirely rather than looking wrong.
        var ungrouped = AoF.Factions.Where(f => AoF.GroupOf(f) == "Other").Select(f => f.Faction).ToList();
        Assert.That(ungrouped, Is.Empty,
            "these AoF: Quest factions have no group - add them to QuestDataSet's AoF map: "
            + string.Join(", ", ungrouped));

        var groups = AoF.Factions.Select(f => AoF.GroupOf(f)).Distinct();
        Assert.That(groups, Is.SubsetOf(AoF.FactionGroups));
        Assert.That(AoF.FactionGroups, Is.Not.EqualTo(Sq.FactionGroups),
            "fantasy factions do not sort into Humanity / Chaos / Xenos");
    }

    // ── Building a hero ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void ANewHero_IsStampedWithItsOwnQuestSystem()
    {
        // The stamp is what keeps an AoF hero out of a Star Quest party once both sit on disk, and it
        // is also what decides which dataset reads the hero back.
        SqFaction faction = AoF.Factions.First();
        QuestHero hero = AoF.NewHero(faction, faction.Heroes.First());

        Assert.That(hero.GameType, Is.EqualTo(QuestGameTypes.AoFQuest));
        Assert.That(QuestGameTypes.SystemFor(hero.GameType), Is.EqualTo(EGameSystem.AoFQuest));

        QuestHero sqHero = Sq.NewHero(Sq.Factions.First(), Sq.Factions.First().Heroes.First());
        Assert.That(sqHero.GameType, Is.EqualTo(QuestGameTypes.StarQuest));
    }

    [Test]
    public void ApplyingAnAoFClass_UsesTheAoFDatasetsStatsAndFeature()
    {
        SqFaction faction = AoF.Factions.First();
        QuestHero hero = AoF.NewHero(faction, faction.Heroes.First());
        SqClass fighter = AoF.Classes.Single(c => c.Name == "Fighter");

        AoF.ApplyClass(hero, fighter);

        Assert.That(hero.Class, Is.EqualTo("Fighter"), "the hero wears the AoF: Quest class name");
        Assert.That(hero.ClassId, Is.EqualTo(fighter.Id));
        Assert.That(hero.ClassFeat, Is.Not.Empty, "a class without its feature is a broken hero sheet");
        Assert.That(hero.SkillSlots, Is.GreaterThan(0));
    }

    [Test]
    public void TheGeneratedParty_BelongsToTheSystemItWasAskedFor()
    {
        // The headless path (--game-system aof-quest --party generate). It used to read one const, so a
        // scripted AoF: Quest run would have fielded Star Quest heroes.
        List<QuestHero> aof = FdgRaylib.Cli.PartyLoader.Generate(3, EGameSystem.AoFQuest);
        List<QuestHero> sq = FdgRaylib.Cli.PartyLoader.Generate(3, EGameSystem.GFStarQuest);

        Assert.That(aof, Has.Count.EqualTo(3));
        Assert.That(aof.Select(h => h.GameType), Is.All.EqualTo(QuestGameTypes.AoFQuest));
        Assert.That(sq.Select(h => h.GameType), Is.All.EqualTo(QuestGameTypes.StarQuest));

        var aofFactions = AoF.Factions.Select(f => f.Faction).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(aof.Select(h => h.Faction), Is.All.AnyOf(aofFactions.ToArray()));
        Assert.That(aof.Select(h => h.Class), Is.All.Not.EqualTo("Soldier"),
            "an AoF: Quest hero does not have a Star Quest class name");
    }

    // ── Where things are filed ───────────────────────────────────────────────────────────────────

    [Test]
    public void AQuestSystemsHeroFolderAndArmyFolder_AgreeOnItsName()
    {
        // heroes/<game type>/ and armies/<folder> are written by different code and read by the player
        // in the same breath. They are allowed to diverge if a system ever needs it; they must not
        // diverge by accident.
        foreach (EGameSystem system in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
        {
            Assert.That(QuestGameTypes.For(system), Is.EqualTo(ArmyPaths.FolderNameFor(system)),
                $"{system}: the hero folder and the army folder should share one name");
        }
    }

    [Test]
    public void AHeroSavedBeforeThisChange_StillReadsAsStarQuest()
    {
        // Every .fdghero written before #418 has GameType "GF Star Quest" (it was the only value), and
        // a file with the field missing altogether must not become an AoF hero.
        Assert.That(QuestGameTypes.SystemFor(null), Is.EqualTo(EGameSystem.GFStarQuest));
        Assert.That(QuestGameTypes.SystemFor(""), Is.EqualTo(EGameSystem.GFStarQuest));
        Assert.That(QuestGameTypes.SystemFor("GF Star Quest"), Is.EqualTo(EGameSystem.GFStarQuest));
        Assert.That(QuestGameTypes.SystemFor("something else"), Is.EqualTo(EGameSystem.GFStarQuest));
    }

    // ── The rules file's own honesty ─────────────────────────────────────────────────────────────

    [Test]
    public void TheRulesFile_SaysWhatIsMissingRatherThanGuessingIt()
    {
        // AoF: Quest's shop list, injury table and level-1 stat line live in its Campaign Builder, which
        // OPR serves through no API endpoint and which is not among the project's books. They are empty
        // on purpose. This test exists so that "empty" cannot quietly become "invented" later, and so
        // the day the book arrives, it fails and says so.
        Assert.That(AoF.Rules.Consumables, Is.Empty,
            "AoF: Quest's shop items are not available - if they have been added, the shop should read "
            + "them and this pin should be replaced by a real agreement test like Star Quest's");
        // #418, later the same day: Kai added the v2.1.0 Beginners Guide and Campaign Builder, so the
        // injury table IS available now and is typed from the book. The shop list still is not - no OPR
        // book prints it for either Quest system.
        Assert.That(AoF.Rules.Injuries.Select(i => i.Name), Is.EquivalentTo(new[]
        {
            "Chest Wound", "Blinded Eye", "Smashed Leg", "Broken Arm", "Spinal Injury", "Crushed Spirit",
        }), "the six injuries in AoFQ Campaign Builder v2.1.0");

        // What IS available and typed from the AoFQ core rules book, word for word.
        Assert.That(AoF.Rules.Conditions.Select(c => c.Name),
            Is.EquivalentTo(new[] { "Afflicted", "Crippled", "Diseased", "Impaired" }),
            "the four status conditions in AoFQ Core Rules v2.0.1");
        Assert.That(AoF.Rules.BaseHero.Qua, Is.GreaterThan(0), "a hero needs a stat line to start from");
    }

    [Test]
    public void EveryShippedQuestDataset_IsAsciiOnly()
    {
        // CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only, so a stray dash or ellipsis
        // in a class description renders as '?' in game. Checked on the files as shipped.
        foreach (QuestDataSet set in QuestDataSet.All)
        {
            if (!Directory.Exists(set.Folder)) continue;
            foreach (string path in Directory.EnumerateFiles(set.Folder, "*.json"))
            {
                string text = File.ReadAllText(path);
                var bad = text.Where(c => c > 'ÿ').Distinct().ToList();
                Assert.That(bad, Is.Empty,
                    $"{Path.GetFileName(path)} carries characters the font atlas cannot draw: "
                    + string.Join(", ", bad.Select(c => $"U+{(int)c:X4}")));
            }
        }
    }
}
