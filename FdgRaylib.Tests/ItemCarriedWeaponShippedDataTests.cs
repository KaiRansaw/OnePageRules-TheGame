using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Bug hunt 2026-11-27: units whose weapons Army Forge nests inside an item came out of the book import with
// none of them (GF Jackals' Charger Beast had no attacks at all). The shipped books were patched in place from
// Army Forge; these pin a few of them, each compiled the way an army is built.
[TestFixture]
public class ItemCarriedWeaponShippedDataTests
{
    private static BookFile Book(string folder, string file) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", folder, file)), RuleJson.Options)!;

    private static UnitFileEntry Compile(BookFile book, string unit)
    {
        RosterUnit roster = book.Units.First(u => u.Name == unit);
        return ListCompiler.Compile(book, new BuilderList
        {
            PointsLimit = 100000, Units = { new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount } },
        }).Units.Single();
    }

    [TestCase("Grimdark Future", "Jackals.fdgbook", "Charger Beast", "Explosive Spear", 1, 4)]
    [TestCase("GF Star Quest", "GFSQ-Jackals.fdgbook", "Charger Beast", "Explosive Spear", 1, 12)]
    [TestCase("Age of Fantasy", "AoF-Goblins.fdgbook", "Pump Plane", "Bombing Run", 1, 2)]
    [TestCase("Age of Fantasy", "AoF-Goblins.fdgbook", "Beast Riders", "Poison Fangs", 5, 1)]
    [TestCase("Grimdark Future", "RobotLegions.fdgbook", "Great Wraith-Shard", "Dual Seer Swords", 1, -1)]
    public void ItemWeapon_IsInTheCompiledUnit(string folder, string file, string unit, string weapon, int quantity, int attacks)
    {
        UnitFileEntry compiled = Compile(Book(folder, file), unit);
        WeaponFileEntry w = compiled.Weapons.Single(x => x.Name == weapon);
        Assert.That(w.Quantity, Is.EqualTo(quantity));
        if (attacks > 0) Assert.That(w.Attacks, Is.EqualTo(attacks));
    }

    // The Quest games' enemy collections are built from those books, so they carried the same hole.
    [TestCase(EGameSystem.GFStarQuest, "Jackals - enemy collection.fdgarmy", "Charger Beast", "Explosive Spear")]
    [TestCase(EGameSystem.AoFQuest, "Goblins - enemy collection.fdgarmy", "Pump Plane", "Bombing Run")]
    public void QuestEnemyCollection_CarriesTheItemWeapon(EGameSystem system, string file, string unit, string weapon)
    {
        string path = Path.Combine(FdgRaylib.ArmyPaths.FolderPath!, FdgRaylib.ArmyPaths.FolderNameFor(system), file);
        BuiltArmyFile army = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)!;
        Assert.That(army.Units.First(u => u.Name == unit).Weapons.Select(w => w.Name), Does.Contain(weapon));
    }
}
