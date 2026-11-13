using FDG;
using FDG.ArmyBuilding;
using FDG.StageResolution.Requests;
using FdgRaylib.Horde;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The Godot Horde Shop's non-drawing half (Kai's mockup, 2026-11-12, every item kept): cart, upgrades only
/// once added to the cart, reinforcing, over-budget, and the reply the engine gets.</summary>
[TestFixture]
public class HordeShopSessionTests
{
    private static readonly Guid Warriors = Guid.NewGuid(), Gunners = Guid.NewGuid();

    private static HordeShopSession Shop(int points = 120)
    {
        var request = new HordeShopRequest(new PlayerID(Guid.NewGuid()), 1, points, "Dark Vanguard", "Test",
            new List<HordeShopObjective> { new(1, 18, 16, true), new(3, 54, 32, false) })
        {
            PointsEarned = 100, PointsPerObjective = 40, PriorityBonus = 20,
            Upgradable =
            {
                new HordeShopUnit(Warriors, "Vanguard Warriors", 5, 65, null, "5x Rifle") { ReinforceCost = 33, ReinforceRestores = "up to 2 models" },
                new HordeShopUnit(Gunners, "Heavy Gunners", 3, 120, null, "3x Heavy Rifle")
                    { NotEligibleReason = "It is more than 9\" from every objective your side seized this wave.", NotReinforceableReason = "Not down to half." },
            },
        };
        return new HordeShopSession(request, new[] { DemoBook.Build() });
    }

    [Test]
    public void OpensOnTheArmysBook_AndSaysWhereThePointsCameFrom()
    {
        var s = Shop();
        Assert.That(s.Book!.Name, Is.EqualTo("Dark Vanguard"));
        Assert.That(s.BookMatched);
        Assert.That(s.EarnedLine, Is.EqualTo("2 seized x 40 + priority 20"));
        Assert.That(s.Kept, Is.EqualTo(20));
    }

    [Test]
    public void SameFactionInSeveralGames_OpensTheGameBeingPlayed()
    {
        BookFile gf = DemoBook.Build(), ff = DemoBook.Build();
        gf.GameSystem = GameSystems.GrimdarkFuture;
        ff.GameSystem = GameSystems.GFFirefight;
        var request = new HordeShopRequest(new PlayerID(Guid.NewGuid()), 1, 50, "Dark Vanguard", "Test",
            new List<HordeShopObjective> { new(1, 18, 16, true) }) { GameSystemSlug = GameSystems.GrimdarkFuture };
        var s = new HordeShopSession(request, new[] { ff, gf });
        Assert.That(s.Book, Is.SameAs(gf));
    }

    [Test]
    public void Recruits_GoInTheCart_WithTheirObjective_AndTheReplyCarriesThem()
    {
        var s = Shop();
        var draft = s.Draft("warriors");
        s.AddRecruit(draft.List.Units[0], 3);
        Assert.That(s.RecruitCost, Is.EqualTo(65));
        Assert.That(s.Left, Is.EqualTo(55));
        HordeShopReply reply = s.Reply();
        Assert.That(reply.RecruitsJson, Does.Contain("Vanguard Warriors"));
        Assert.That(reply.ObjectiveNumbers, Is.EqualTo(new[] { 3 }));
        Assert.That(reply.RecruitLoadouts, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnUpgradeCountsOnlyOnceAddedToTheCart()
    {
        var s = Shop();
        var edit = s.Edits.Single(e => e.Info.UnitId == Warriors);
        var section = s.Upgrades.Roster("warriors")!.Sections.First(x => x.Id == "warriors-banner");
        s.Upgrades.Choose(edit.Row, section, "war-banner", 1);
        Assert.That(s.Changed(edit));
        Assert.That(s.PriceOf(edit), Is.EqualTo(10));
        Assert.That(s.UpgradeCost, Is.EqualTo(0), "not in the cart yet");
        edit.InCart = true;
        Assert.That(s.UpgradeCost, Is.EqualTo(10));
        Assert.That(s.Reply().UpgradeUnitIds, Is.EqualTo(new[] { Warriors }));
        s.Reset(edit);
        Assert.That(s.Changed(edit), Is.False);
        Assert.That(s.UpgradeCost, Is.EqualTo(0));
    }

    [Test]
    public void OutOfReachUnits_StayListed_WithWhy()
    {
        var s = Shop();
        var gunners = s.Edits.Single(e => e.Info.UnitId == Gunners);
        Assert.That(gunners.Row, Is.EqualTo(-1));
        Assert.That(gunners.Problem, Does.Contain("9\""));
    }

    [Test]
    public void GoingOverThePoints_StopsTheBuy()
    {
        var s = Shop(points: 90);
        s.AddRecruit(s.Draft("warriors").List.Units[0], 1);
        s.Reinforce.Add(Warriors);
        Assert.That(s.Total, Is.EqualTo(65 + 33));
        Assert.That(s.Left, Is.EqualTo(-8));
        Assert.That(s.CanBuy, Is.False);
        s.Reinforce.Remove(Warriors);
        Assert.That(s.CanBuy);
    }

    [Test]
    public void Reinforce_OnlyForUnitsThatMayBe()
    {
        var s = Shop();
        s.Reinforce.Add(Gunners);
        Assert.That(s.Reinforcing, Is.Empty);
        s.Reinforce.Add(Warriors);
        Assert.That(s.Reply().ReinforceUnitIds, Is.EqualTo(new[] { Warriors }));
    }
}
