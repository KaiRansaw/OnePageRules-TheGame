using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The battle effects' weapon reading (Kai, 2026-11-26). Looks only - no rule reads it.</summary>
[TestFixture]
public class AttackLookTests
{
    [TestCase("Rifle", false, EAttackLook.Bullet)]
    [TestCase("Heavy Machinegun", false, EAttackLook.Heavy)]
    [TestCase("Lasgun", false, EAttackLook.Laser)]
    [TestCase("Plasma Rifle", false, EAttackLook.Plasma)]
    [TestCase("Flamer", false, EAttackLook.Flame)]
    [TestCase("Dragon Breath", false, EAttackLook.Flame)]
    [TestCase("Rocket Launcher", false, EAttackLook.Explosive)]
    [TestCase("Longbow", false, EAttackLook.Thrown)]
    [TestCase("Crossbow", false, EAttackLook.Thrown)]
    [TestCase("Javelins", false, EAttackLook.Thrown)]
    [TestCase("Legion Rifle", false, EAttackLook.Bullet)]
    [TestCase("Hand Weapon", true, EAttackLook.Blade)]
    [TestCase("Great Axe", true, EAttackLook.Blade)]
    [TestCase("Spear", true, EAttackLook.Blade)]
    [TestCase("Thunder Hammer", true, EAttackLook.Blunt)]
    [TestCase("Razor Claws", true, EAttackLook.Claw)]
    [TestCase("CCW", true, EAttackLook.Strike)]
    [TestCase(null, false, EAttackLook.Bullet)]
    public void ReadsTheWeaponName(string? name, bool melee, EAttackLook want) =>
        Assert.That(AttackLook.For(name, melee), Is.EqualTo(want));

    [Test]
    public void EveryLook_HasASoundThatShips_AndMeleeHasNoProjectile()
    {
        string sounds = System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "OprGodot", "sounds");
        foreach (EAttackLook l in System.Enum.GetValues<EAttackLook>())
        {
            Assert.That(System.IO.Directory.GetFiles(System.IO.Path.Combine(sounds, AttackLook.FireSound(l)), "*.ogg"), Is.Not.Empty, l.ToString());
            if (AttackLook.HitSound(l) is { } hit)
                Assert.That(System.IO.Directory.GetFiles(System.IO.Path.Combine(sounds, hit), "*.ogg"), Is.Not.Empty, l + " hit");
            if (AttackLook.IsMelee(l)) Assert.That(AttackLook.Speed(l), Is.Zero);
        }
    }

    [Test]
    public void EverySoundFolder_ShipsItsLicence()
    {
        string sounds = System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "OprGodot", "sounds");
        foreach (string d in System.IO.Directory.GetDirectories(sounds))
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(d, "License.txt")), d);
    }
}

/// <summary>Which attacks get the big explosion (Kai, 2026-11-26: big models, Blast(3)+, heavy names).</summary>
[TestFixture]
public class AttackLookBigTests
{
    private static FDG.IUnit Shooter(string weapon, int blast)
    {
        var store = FDG.Data.GameDataStore.GameDataStoreBuilder.GetDefault();
        var w = new FDG.Weapon(weapon, rangeInches: 24f, attacks: 1, armorPenetration: 0);
        if (blast > 0)
            w.AttachRuleDefinition(new FDG.Rules.Dispatch.ResolvedRule("Blast", FDG.Rules.Dispatch.CoreRuleCatalog.Blast,
                new FDG.Rules.Foundation.RuleArgument[] { new FDG.Rules.Foundation.RuleArgument.Int(blast) }));
        var model = new FDG.ModelData(baseRadiusInches: 0.5f, weapons: new List<FDG.Weapon> { w }, initialPosition: new FDG.Position(), gameDataStore: store);
        return new FDG.UnitData(new FDG.PlayerID(Guid.NewGuid()), "P1", quality: 4, defense: 4,
            modelBindings: new List<FDG.Data.DataBinding<FDG.ModelData>> { store.GetDataBinding<FDG.ModelData>(store.Create(model)) });
    }

    [Test] public void APlainRifle_IsNotBig() => Assert.That(AttackLook.IsBig(Shooter("Rifle", 0), "Rifle", false), Is.False);
    [Test] public void Blast3_IsBig() => Assert.That(AttackLook.IsBig(Shooter("Spore Gun", 3), "Spore Gun", false), Is.True);
    [Test] public void Blast2_IsNot() => Assert.That(AttackLook.IsBig(Shooter("Spore Gun", 2), "Spore Gun", false), Is.False);
    [Test] public void AHeavyName_IsBig_WhoeverFiresIt() => Assert.That(AttackLook.IsBig(null, "Battle Cannon", false), Is.True);
}
