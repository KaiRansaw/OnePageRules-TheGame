using System;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Recruit's 5 XP option (Campaign Rules v3.6.0: "One of the new recruited units may also start with 5 XP if
/// the player pays for it") goes to the unit the player picks, not always the first unit in the cart.
/// </summary>
[TestFixture]
public class RecruitVeteranPickTests
{
    private const string TrooperId = "trooper";
    private const string ScoutId = "scout";
    private string? _path;

    [TearDown]
    public void Cleanup()
    {
        try { if (_path != null && File.Exists(_path)) File.Delete(_path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static BookFile Book() => new()
    {
        Name = "Test Legion",
        Faction = "Test Legion",
        Units =
        {
            new RosterUnit
            {
                Id = TrooperId, Name = "Troopers", Quality = 4, Defense = 4,
                BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
            },
            new RosterUnit
            {
                Id = ScoutId, Name = "Scouts", Quality = 4, Defense = 5,
                BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 60,
                Weapons = { new WeaponFileEntry { Name = "Carbine", RangeInches = 18, Attacks = 1 } },
            },
        },
    };

    private (BuiltArmyFile Army, BookFile Book) Started()
    {
        BookFile book = Book();
        var list = new BuilderList { Name = "Test", BookName = book.Name };
        list.Units.Add(new BuilderUnit { RosterUnitId = TrooperId, ModelCount = 5 });
        BuiltArmyFile army = ListCompiler.Compile(book, list);
        _path = Path.Combine(Path.GetTempPath(), $"recruit-veteran-{Guid.NewGuid():N}.fdgarmy");
        CampaignRules.Start(army, "Test", _path, CampaignNumbers.Firefight, EGameSystem.GFFirefight);
        army.StrikeTeam!.BankedPoints = 1000;
        return (army, book);
    }

    private static int XpOf(UnitFileEntry unit) => CampaignRules.Sheet(unit)?.Xp ?? -1;

    [Test]
    public void TheSecondUnitInTheCart_CanBeTheFiveXpRecruit()
    {
        (BuiltArmyFile army, BookFile book) = Started();
        var screen = new StrikeTeamScreen();
        screen.CartForTests.AddRange(new[] { TrooperId, ScoutId });
        screen.CartVeteranForTests = 1;

        screen.ConfirmRecruits(_path!, army, book, CampaignNumbers.Firefight);

        UnitFileEntry troopers = army.Units.Skip(1).First(u => u.Name.StartsWith("Troopers"));
        UnitFileEntry scouts = army.Units.First(u => u.Name.StartsWith("Scouts"));
        Assert.That(XpOf(scouts), Is.EqualTo(5), "the picked unit starts at 5 XP");
        Assert.That(XpOf(troopers), Is.EqualTo(0), "the first unit in the cart is no longer the automatic choice");
    }

    [Test]
    public void Nobody_MeansNoRecruitStartsAtFiveXp()
    {
        (BuiltArmyFile army, BookFile book) = Started();
        var screen = new StrikeTeamScreen();
        screen.CartForTests.AddRange(new[] { TrooperId, ScoutId });
        screen.CartVeteranForTests = -1;

        screen.ConfirmRecruits(_path!, army, book, CampaignNumbers.Firefight);

        Assert.That(army.Units.Skip(1).All(u => XpOf(u) == 0), Is.True);
        Assert.That(CampaignEconomy.VeteranRecruitUsed(army), Is.False);
    }
}
