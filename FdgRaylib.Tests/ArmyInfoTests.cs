using System.Linq;
using FDG;
using FdgRaylib.Pictures;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Kai, 2026-11-17 ("Choose an Army Mockups" option C): the army picker's info panel - Army Forge's description, the
// army's own rules written out, three signature units. Read from the bundled books, so these run on the real data.
[TestFixture]
public class ArmyInfoTests
{
    [Test]
    public void AlienHives_HasItsDescription_ItsArmyRule_AndOneUnitFromEachBigBox()
    {
        ArmyInfo.Info? info = ArmyInfo.GetAsync(EGameSystem.GrimdarkFuture, "Alien Hives").GetAwaiter().GetResult();
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.About, Does.StartWith("The Alien Hives are a faction of aliens"));
        Assert.That(info.Group, Is.EqualTo("Aliens"));
        Assert.That(info.ArmyRules.Select(r => r.Name), Is.EqualTo(new[] { "Hive Bond" }));
        Assert.That(info.ArmyRules[0].Text, Is.Not.Empty, "the rule is written out, not left to a hover");
        Assert.That(info.Signature.Select(u => u.Category), Is.EqualTo(new[] { "Heroes", "Core Units", "Titans" }).Or
            .EqualTo(new[] { "Heroes", "Core Units", "Vehicles / Monsters" }));
        Assert.That(info.Signature[0].Name, Is.EqualTo("Hive Lord"), "the most expensive (non-narrative) hero");
    }

    [Test]
    public void EveryGameSystem_TextIsAscii_AndMostArmiesHaveARule()
    {
        foreach (EGameSystem system in new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight,
                     EGameSystem.AoFSkirmish, EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
        {
            var names = BookIndex.Books(system).Select(b => b.Name).ToList();
            Assert.That(names, Is.Not.Empty, system.ToString());
            int withRule = 0;
            foreach (string army in names)
            {
                ArmyInfo.Info? info = ArmyInfo.GetAsync(system, army).GetAwaiter().GetResult();
                Assert.That(info, Is.Not.Null, $"{system} {army}");
                Assert.That(info!.About.All(c => c < 128), Is.True, $"{system} {army}: game text is ASCII-only");
                Assert.That(info.Signature, Has.Count.LessThanOrEqualTo(3));
                if (info.ArmyRules.Count > 0) withRule++;
                foreach (ArmyInfo.Rule r in info.ArmyRules)
                    Assert.That(r.Text, Is.Not.Empty, $"{system} {army} {r.Name}: written out on screen, so it needs its text");
            }
            TestContext.Out.WriteLine($"{system}: {withRule}/{names.Count} armies with an army rule");
            Assert.That(withRule, Is.GreaterThan(names.Count * 3 / 4), system.ToString());
        }
    }
}
