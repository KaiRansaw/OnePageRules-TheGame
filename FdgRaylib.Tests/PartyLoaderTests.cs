using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.Quest;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #413 --party: Star Quest's local side is a PARTY of heroes, not an army list. Hand the slot an army
// list and nothing on the table has IsQuestHero set, so StarQuestHordeRules.Heroes finds nothing and the
// first end-of-round check ends the mission with "every hero is down" in round 1 - which is exactly what
// a scripted `--game-system gfsq` run did before this flag existed. These pin the three ways a party can
// be named, and the refusals, because a scripted run that quietly plays a DIFFERENT party than the one
// asked for makes every result it prints afterwards meaningless (#413's standing rule for --army1).
[TestFixture]
public class PartyLoaderTests
{
    private readonly List<string> _temp = new();

    [TearDown]
    public void TearDown()
    {
        foreach (string path in _temp)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
            catch { /* best effort */ }
        }
        _temp.Clear();
    }

    private string HeroFile(string name, int cost = 40)
    {
        string path = Path.Combine(Path.GetTempPath(), $"fdg-413-{Guid.NewGuid():N}{QuestHero.FileExtension}");
        _temp.Add(path);
        File.WriteAllText(path, new QuestHero
        {
            Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = cost, Class = "Soldier",
            Quality = 4, Defense = 4, Tough = 9, Power = 4,
            Weapons = { new QuestWeapon { Name = "Rifle", Range = 24, Attacks = 2 } },
        }.ToJson());
        return path;
    }

    private string HeroFolder(params string[] names)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"fdg-413-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        _temp.Add(folder);
        foreach (string name in names)
            File.WriteAllText(Path.Combine(folder, name + QuestHero.FileExtension),
                new QuestHero { Name = name, Faction = "Test", BaseCost = 40, Tough = 9 }.ToJson());
        return folder;
    }

    // ── The generated party ──────────────────────────────────────────────────────────────────────

    // The default: no files at all, so an automated run is reproducible on any checkout.
    [Test]
    public void NoPaths_GeneratesTheDefaultParty()
    {
        List<QuestHero> party = PartyLoader.Resolve(Array.Empty<string>());

        Assert.That(party, Has.Count.EqualTo(PartyLoader.DefaultGeneratedHeroes));
        Assert.That(party.Select(h => h.Name), Is.Unique);
        Assert.That(party.All(h => h.Points > 0), Is.True, "waves are sized off the party's points.");
    }

    [Test]
    public void AutoKeyword_GeneratesTheDefaultParty()
    {
        Assert.That(PartyLoader.Resolve(new[] { "auto" }),
            Has.Count.EqualTo(PartyLoader.DefaultGeneratedHeroes));
    }

    [Test]
    public void AutoWithACount_GeneratesThatMany()
    {
        Assert.That(PartyLoader.Resolve(new[] { "auto:4" }), Has.Count.EqualTo(4));
        Assert.That(PartyLoader.Resolve(new[] { "auto:2" }), Has.Count.EqualTo(2));
    }

    // Generation runs twice in a suite and in every scripted run; two runs that disagree would make a
    // scripted result incomparable with the last one, which is most of the point of having the flag.
    [Test]
    public void Generate_IsDeterministic()
    {
        string First() => string.Join("|", PartyLoader.Generate(4)
            .Select(h => $"{h.Name}/{h.Class}/{h.BaseHeroName}/{h.Points}/{string.Join("+", h.Skills)}"));

        Assert.That(First(), Is.EqualTo(First()));
    }

    // The generated party exists to exercise Star Quest, so each hero has to be a real classed hero:
    // a class, its free class feature, and its level-1 skills filled to the class's slots. A party of
    // classless statblocks would play, and would test none of the ~120 skills.
    [Test]
    public void Generate_GivesEachHeroAClassWithItsFeatureAndSkills()
    {
        List<QuestHero> party = PartyLoader.Generate(3);

        Assert.That(party.Select(h => h.Class), Is.Unique, "one class each, so the run covers three.");
        foreach (QuestHero hero in party)
        {
            Assert.That(hero.Class, Is.Not.Empty);
            Assert.That(hero.ClassFeat, Is.Not.Empty, $"{hero.Name} has no class feature.");
            Assert.That(hero.Skills, Has.Count.EqualTo(hero.SkillSlots),
                $"{hero.Name} should know as many skills as its class has slots.");
            Assert.That(hero.GameType, Is.EqualTo("GF Star Quest"));
        }
    }

    // Tough(X) is read off the RULES list, not the Tough field, when the party is turned into units.
    [Test]
    public void Generate_KeepsTheToughRuleInStepWithTheStat()
    {
        foreach (QuestHero hero in PartyLoader.Generate(4))
        {
            bool hasRule = hero.Rules.Any(r => r.Equals($"Tough({hero.Tough})", StringComparison.OrdinalIgnoreCase));
            Assert.That(hasRule || hero.Tough <= 1, Is.True,
                $"{hero.Name} is Tough({hero.Tough}) but carries no matching rule.");
        }
    }

    // ── Named files and folders ──────────────────────────────────────────────────────────────────

    [Test]
    public void NamedFiles_LoadInTheOrderGiven()
    {
        List<QuestHero> party = PartyLoader.Resolve(new[] { HeroFile("Vex"), HeroFile("Mara") });

        Assert.That(party.Select(h => h.Name), Is.EqualTo(new[] { "Vex", "Mara" }));
    }

    // One argument holding several comma-separated paths, so a party fits in a single shell token.
    [Test]
    public void NamedFiles_MayBeCommaSeparatedInOneArgument()
    {
        string both = HeroFile("Vex") + "," + HeroFile("Mara");

        Assert.That(PartyLoader.Resolve(new[] { both }).Select(h => h.Name),
            Is.EqualTo(new[] { "Vex", "Mara" }));
    }

    [Test]
    public void AFolder_TakesEveryHeroInIt()
    {
        string folder = HeroFolder("Ada", "Bram", "Cyn");

        Assert.That(PartyLoader.Resolve(new[] { folder }).Select(h => h.Name),
            Is.EquivalentTo(new[] { "Ada", "Bram", "Cyn" }));
    }

    // A headless run exists to crash on purpose; it must never write a hero's XP, injuries or level back
    // over a saved file. QuestHeroSync keys off SourceFile, so clearing it makes that impossible even if
    // some later path starts calling it.
    [Test]
    public void LoadedHeroes_ForgetTheFileTheyCameFrom()
    {
        List<QuestHero> party = PartyLoader.Resolve(new[] { HeroFile("Vex"), HeroFile("Mara") });

        Assert.That(party.Select(h => h.SourceFile), Is.All.Null,
            "a scripted party must not be able to write back over saved heroes.");
    }

    // ── Refusals ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void AMissingFile_IsAnErrorNamingThePath()
    {
        string missing = Path.Combine(Path.GetTempPath(), "fdg-413-not-here.fdghero");

        Assert.That(() => PartyLoader.Resolve(new[] { missing }),
            Throws.ArgumentException.With.Message.Contains("fdg-413-not-here"));
    }

    [Test]
    public void AnUnreadableFile_IsAnErrorNotASilentSkip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fdg-413-{Guid.NewGuid():N}{QuestHero.FileExtension}");
        _temp.Add(path);
        File.WriteAllText(path, "this is not json");

        Assert.That(() => PartyLoader.Resolve(new[] { path, HeroFile("Mara") }),
            Throws.ArgumentException.With.Message.Contains("readable hero file"));
    }

    [Test]
    public void FewerThanTwoHeroes_IsRefused()
    {
        Assert.That(() => PartyLoader.Resolve(new[] { HeroFile("Solo") }),
            Throws.ArgumentException.With.Message.Contains("at least 2"));
    }

    // Truncating to the first four would play a different party than the one named, so the caller is
    // asked to choose instead.
    [Test]
    public void MoreThanFourHeroes_IsRefusedAndListsThem()
    {
        string folder = HeroFolder("Ada", "Bram", "Cyn", "Dex", "Eli");

        Assert.That(() => PartyLoader.Resolve(new[] { folder }),
            Throws.ArgumentException.With.Message.Contains("at most 4").And.Message.Contains("Ada"));
    }

    [Test]
    public void AnEmptyFolder_IsRefused()
    {
        Assert.That(() => PartyLoader.Resolve(new[] { HeroFolder() }),
            Throws.ArgumentException.With.Message.Contains("no .fdghero files"));
    }

    [Test]
    public void MixingAutoWithFiles_IsRefused()
    {
        Assert.That(() => PartyLoader.Resolve(new[] { "auto", HeroFile("Vex") }),
            Throws.ArgumentException.With.Message.Contains("cannot be mixed"));
    }

    [Test]
    public void AnOutOfRangeAutoCount_IsRefused()
    {
        Assert.That(() => PartyLoader.Resolve(new[] { "auto:9" }),
            Throws.ArgumentException.With.Message.Contains("2 to 4"));
        Assert.That(() => PartyLoader.Resolve(new[] { "auto:1" }),
            Throws.ArgumentException.With.Message.Contains("2 to 4"));
    }

    // ── The army the party becomes ───────────────────────────────────────────────────────────────

    // The whole point: every unit must come out as a one-model QUEST HERO, because that flag is what
    // StarQuestHordeRules.Heroes looks for. A party that builds but isn't recognised as heroes is the
    // original bug wearing a disguise.
    [Test]
    public void Build_TurnsThePartyIntoOneModelQuestHeroUnits()
    {
        ArmyListFile party = PartyLoader.Build(new[] { HeroFile("Vex", 40), HeroFile("Mara", 60) });

        Assert.That(party.Units, Has.Count.EqualTo(2));
        Assert.That(party.Units.All(u => u.ModelCount == 1), Is.True);
        Assert.That(party.Units.All(u => !string.IsNullOrEmpty(u.QuestHeroJson)), Is.True,
            "without the hero sheet the engine does not see a quest hero at all.");
        Assert.That(party.TotalPoints, Is.EqualTo(100), "wave budgets are a fraction of this.");
    }

    // ── When a party is wanted at all ────────────────────────────────────────────────────────────

    // A Quest game cannot be played with an army list, so naming the system is enough - the flag itself
    // is optional. Anything else keeps the ordinary army-list path untouched.
    [TestCase(EGameSystem.GFStarQuest, true)]
    [TestCase(EGameSystem.AoFQuest, true)]
    [TestCase(EGameSystem.GrimdarkFuture, false)]
    [TestCase(EGameSystem.GFFirefight, false)]
    [TestCase(EGameSystem.AoFSkirmish, false)]
    public void WantsParty_FollowsTheGameSystem(EGameSystem system, bool expected)
    {
        var app = new CliApp(headless: true) { Scripted = new CliApp.Setup(GameSystem: system) };

        Assert.That(app.WantsParty(), Is.EqualTo(expected));
    }

    [TestCase(EGameMode.StarQuestHorde, true)]
    [TestCase(EGameMode.StarQuestMission, true)]
    [TestCase(EGameMode.Horde, false)]
    [TestCase(EGameMode.Standard, false)]
    public void WantsParty_FollowsTheGameMode(EGameMode mode, bool expected)
    {
        var app = new CliApp(headless: true) { Scripted = new CliApp.Setup(GameMode: mode) };

        Assert.That(app.WantsParty(), Is.EqualTo(expected));
    }

    [Test]
    public void WantsParty_IsTrueWheneverAPartyWasNamed()
    {
        var app = new CliApp(headless: true) { Scripted = new CliApp.Setup(Party: new[] { "auto" }) };

        Assert.That(app.WantsParty(), Is.True);
    }

    // An unscripted run is the interactive CLI, which has always prompted for an army.
    [Test]
    public void WantsParty_IsFalseWithoutAScriptedSetup()
    {
        Assert.That(new CliApp(headless: true).WantsParty(), Is.False);
    }
}
