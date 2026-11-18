using FDG.Rules.Dispatch;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-17: Weapon Experts get Precision Shooter from a Director's aura, and nothing said what it does.</summary>
[TestFixture]
public class GrantedRuleTextTests
{
    [Test]
    public void PrecisionShooter_ShowsTheAurasBookWording_SaidWhereFrom()
    {
        Assert.That(OfficialRuleText.Text("Precision Shooter", "grimdark-future"), Is.Null, "Army Forge publishes no wording of its own for it in GF");
        Assert.That(OfficialRuleText.TextOrGrantedBy("Precision Shooter", "grimdark-future"),
            Is.EqualTo("Given by Precision Shooter Aura: " + OfficialRuleText.Text("Precision Shooter Aura", "grimdark-future")));
    }

    [Test]
    public void ARuleWithItsOwnWording_KeepsIt()
    {
        Assert.That(OfficialRuleText.TextOrGrantedBy("Precision Shooter", "grimdark-future-star-quest"),
            Is.EqualTo(OfficialRuleText.Text("Precision Shooter", "grimdark-future-star-quest")));
        Assert.That(OfficialRuleText.TextOrGrantedBy("Fearless", "grimdark-future"), Is.EqualTo(OfficialRuleText.Text("Fearless", "grimdark-future")));
    }

    [Test]
    public void ANamedGranter_IsTriedFirst()
    {
        Assert.That(OfficialRuleText.TextOrGrantedBy("Precision Shooter", "grimdark-future", new[] { "Precision Shooter Buff" }),
            Does.StartWith("Given by Precision Shooter Buff: "));
    }

    [Test]
    public void DiceThatSucceeded_AreTheOnesAtOrAboveTheTarget()
    {
        var roll = new BattleReport.Roll("A", null, "To Hit", 4, "2 of 4 succeed", new[] { 6, 4, 3, 1 }, Array.Empty<string>(), 2);
        Assert.That(roll.Succeeded(), Is.EqualTo(new[] { true, true, false, false }));
    }

    [Test]
    public void WhenTheEngineCountedAModifierIn_ItsCountWins()
    {
        // +1 to hit folded into the count, not the target: a 3 hit too.
        var roll = new BattleReport.Roll("A", null, "To Hit", 4, "3 of 4 succeed", new[] { 6, 4, 3, 1 }, new[] { "+1" }, 3);
        Assert.That(roll.Succeeded(), Is.EqualTo(new[] { true, true, true, false }));
    }
}
