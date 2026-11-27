using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Quest;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Lane 1, 2026-11-27: a Quest HERO's Spawn upgrade must place something. Army Forge's hero rosters word them in the
// plural - Vlasmyir's "Endless Horde (Spawn(Stitched Zombies [1]))", "Spawn(Lust Warriors [1])", "Spawn(Snake Swarms
// [1])" - and the party army (QuestArmy.Build) never compiled the auxiliary unit specs those name, and the Quest enemy
// books name the unit in the singular ("Stitched Zombie"). So the rule fired at activation and placed nothing.
[TestFixture]
public class QuestHeroSpawnTests
{
    private static BookFile Book(string folder, string file) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books", folder, file)), RuleJson.Options)!;

    private static QuestHero HeroWithEveryUpgrade(EGameSystem system, string faction, string heroName)
    {
        QuestDataSet data = QuestDataSet.For(system);
        SqFaction f = data.Factions.Single(x => x.Faction == faction);
        SqHero baseHero = f.Heroes.Single(h => h.Name == heroName);
        QuestHero hero = data.NewHero(f, baseHero);
        var picks = baseHero.Upgrades
            .Select(uid => f.Package(uid)!)
            .SelectMany(p => p.Sections)
            .Select(s => (s, s.Opts.First(o => o.Label.Contains("Spawn(", StringComparison.Ordinal))))
            .ToList();
        data.ApplyUpgrades(hero, f, baseHero, picks);
        return hero;
    }

    [Test]
    public void Vlasmyir_EndlessHorde_CarriesItsStitchedZombieSpec()
    {
        QuestHero vlasmyir = HeroWithEveryUpgrade(EGameSystem.AoFQuest, "Vampiric Undead", "Vlasmyir");
        BookFile book = Book("AoF Quest", "AoFQ-VampiricUndead.fdgbook");

        BuiltArmyFile party = QuestArmy.Build(new[] { vlasmyir }, _ => book, EGameSystem.AoFQuest);

        UnitFileEntry hero = party.Units.Single();
        Assert.That(hero.SpecialRules.OfType<SpecialRuleEntry_Text>().Select(r => (r.Name, r.TextValue)),
            Does.Contain(("Spawn", "Stitched Zombies [1]")), "the upgrade gives the hero Spawn(Stitched Zombies [1])");

        UnitFileEntry spec = party.AuxiliaryUnits!.Single(u => u.Id == "Stitched Zombies [1]");
        Assert.That(spec.Name, Is.EqualTo("Stitched Zombie"), "the AoF: Quest book's singular unit");
        Assert.That(spec.ModelCount, Is.EqualTo(1));
        Assert.That(spec.PointCost, Is.Zero);
    }

    [Test]
    public void APartyOfTwoFactions_CarriesEachHerosSpecFromItsOwnBook()
    {
        QuestHero vlasmyir = HeroWithEveryUpgrade(EGameSystem.AoFQuest, "Vampiric Undead", "Vlasmyir");
        QuestHero plain = QuestDataSet.For(EGameSystem.AoFQuest) is var data
            ? data.NewHero(data.Factions.Single(f => f.Faction == "Human Empire"),
                data.Factions.Single(f => f.Faction == "Human Empire").Heroes.First())
            : null!;
        BookFile undead = Book("AoF Quest", "AoFQ-VampiricUndead.fdgbook");
        BookFile empire = Book("AoF Quest", "AoFQ-HumanEmpire.fdgbook");

        BuiltArmyFile party = QuestArmy.Build(new[] { plain, vlasmyir },
            faction => faction == "Vampiric Undead" ? undead : empire, EGameSystem.AoFQuest);

        Assert.That(party.AuxiliaryUnits!.Select(u => u.Id), Is.EquivalentTo(new[] { "Stitched Zombies [1]" }));
    }

    [Test]
    public void ASpecNamedInThePlural_FindsTheSingularBookUnit_AndAnExactNameStillWins()
    {
        BookFile book = Book("AoF Quest", "AoFQ-VampiricUndead.fdgbook");
        Assert.That(ListCompiler.FindSpecUnit(book, "Stitched Zombies")?.Name, Is.EqualTo("Stitched Zombie"));
        Assert.That(ListCompiler.FindSpecUnit(book, "Stitched Zombie")?.Name, Is.EqualTo("Stitched Zombie"));
        Assert.That(ListCompiler.FindSpecUnit(book, "Nothing Of The Kind"), Is.Null);

        BookFile aof = Book("Age of Fantasy", "AoF-VampiricUndead.fdgbook");
        Assert.That(ListCompiler.FindSpecUnit(aof, "Stitched Zombies")?.Name, Is.EqualTo("Stitched Zombies"));
    }
}
