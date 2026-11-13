using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Forge;
using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Godot Army Forge's non-drawing half (<see cref="ForgeSession"/>, <see cref="ForgeRules"/>) and per-model looks
/// (Kai, 2026-11-12: "choose each individual model's look"; unpicked models use the first version).
/// </summary>
[TestFixture]
public class ArmyForgeLooksTests
{
    private static ForgeSession Session() => new(new List<BookFile> { DemoBook.Build() });

    [Test]
    public void Slots_OnePerModel_AndLooksSaveIntoTheList()
    {
        var s = Session();
        int row = s.Add("warriors");
        var slots = s.Slots(row);
        Assert.That(slots, Has.Count.EqualTo(s.Detail(row).Unit.ModelCount));
        Assert.That(slots.All(x => x.Look == null), "a new unit uses the first version everywhere");

        s.SetLook(new[] { slots[1], slots[3] }, "v2.glb");
        Assert.That(s.List.Units[row].ModelLooks, Is.EqualTo(new List<string?> { null, "v2.glb", null, "v2.glb" }));
        Assert.That(s.LooksSummary(row), Does.Contain("2 picked"));

        s.SetLook(s.Slots(row), null);
        Assert.That(s.List.Units[row].ModelLooks, Is.Null, "reset to first version leaves nothing behind");
    }

    [Test]
    public void CombinedUnit_ShowsBothHalves_AndWritesEachHalf()
    {
        var s = Session();
        int row = s.Add("warriors");
        int n = s.Detail(row).Unit.ModelCount;
        s.SetCombined(row, true);
        int first = s.List.Units.FindIndex(u => u.CombinedWithId == null);
        var slots = s.Slots(first);
        Assert.That(slots, Has.Count.EqualTo(n * 2));
        s.SetLook(new[] { slots[n] }, "z.glb");
        BuiltArmyFile army = s.Compile();
        Assert.That(army.Units.Single(u => u.ModelCount == n * 2).ModelLooks![n], Is.EqualTo("z.glb"),
            "the second half's first model is model n+1 of the merged unit");
    }

    [Test]
    public void TakingBackAModelUpgrade_DropsLooksForModelsThatAreGone()
    {
        var s = Session();
        int row = s.Add("warriors");
        RosterUnit roster = s.Roster("warriors")!;
        UpgradeSection grow = roster.Sections.First(x => x.Variant == UpgradeVariant.AddModels);
        s.Choose(row, grow, grow.Options[0].Id, 2);
        var slots = s.Slots(row);
        s.SetLook(new[] { slots[^1] }, "big.glb");
        s.Choose(row, grow, grow.Options[0].Id, 0);
        Assert.That(s.List.Units[row].ModelLooks, Is.Null.Or.Count.EqualTo(s.Detail(row).Unit.ModelCount));
    }

    [Test]
    public void ModelLoadouts_MatchHowTheEngineDealsWeapons()
    {
        var unit = new UnitFileEntry
        {
            ModelCount = 3,
            Weapons =
            {
                new WeaponFileEntry { Name = "Rifle", Quantity = 2 },
                new WeaponFileEntry { Name = "Plasma", Quantity = 1 },
                new WeaponFileEntry { Name = "Claws", Quantity = 3 },
            },
        };
        var loadouts = ForgeRules.ModelLoadouts(unit);
        // Fewest first: Plasma, Rifle, Rifle, Claws x3 -> dealt round the 3 models.
        Assert.That(loadouts[0], Is.EqualTo(new[] { "Plasma", "Claws" }));
        Assert.That(loadouts[1], Is.EqualTo(new[] { "Rifle", "Claws" }));
        Assert.That(ForgeRules.SpecialKit(unit)[0], Is.EqualTo(new[] { "Plasma" }));
    }

    [Test]
    public void Pick_UsesTheLook_ElseTheFirstVersion()
    {
        var files = new[] { "/m/U/a.glb", "/m/U/b.glb" };
        Assert.That(ModelLooks.Pick(files, "B.GLB"), Is.EqualTo("/m/U/b.glb"));
        Assert.That(ModelLooks.Pick(files, null), Is.EqualTo("/m/U/a.glb"));
        Assert.That(ModelLooks.Pick(files, "gone.glb"), Is.EqualTo("/m/U/a.glb"), "a friend without that file sees the first version");
        Assert.That(ModelLooks.Pick(Array.Empty<string>(), "a.glb"), Is.Null);
    }

    [Test]
    public void UpgradeSections_GreyOutWithAReason()
    {
        var s = Session();
        int row = s.Add("warriors");
        var views = s.Sections(row);
        Assert.That(views, Is.Not.Empty);
        Assert.That(views.SelectMany(v => v.Options).Where(o => !o.Enabled).All(o => o.WhyNot != null));
    }
}
