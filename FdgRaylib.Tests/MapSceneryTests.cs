using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The scenery round the table, saved with a map (Kai, 2026-11-26, option C).</summary>
[TestFixture]
public class MapSceneryTests
{
    [Test]
    public void Untouched_IsEmptyText_AndGeneratesTheSameEveryTime()
    {
        var s = MapScenery.Parse("");
        Assert.That(s.ToText(), Is.EqualTo(""));
        var a = s.Generate("Hospitable", 72, 48);
        var b = MapScenery.Parse(null).Generate("Hospitable", 72, 48);
        Assert.That(a, Is.Not.Empty);
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Reroll_ChangesThePlaces_DensityChangesTheCount_NoneIsEmpty()
    {
        var s = new MapScenery();
        var first = s.Generate("Generic", 72, 48);
        s.Seed = 1;
        Assert.That(s.Generate("Generic", 72, 48), Is.Not.EqualTo(first));
        s.Density = 2f;
        Assert.That(s.Generate("Generic", 72, 48).Count, Is.GreaterThan(first.Count));
        s.Density = 0f;
        Assert.That(s.Generate("Generic", 72, 48), Is.Empty);
        Assert.That(new MapScenery { Set = "none" }.Generate("Generic", 72, 48), Is.Empty);
    }

    [Test]
    public void NothingStandsOnTheTable()
    {
        foreach (string h in new[] { "Generic", "Hospitable", "Wasteland", "Death" })
            foreach (var p in new MapScenery { Density = 2f }.Generate(h, 72, 48))
                Assert.That(Math.Abs(p.X) > 36 + 30 || Math.Abs(p.Z) > 24 + 30, Is.True, $"{p.Model} at {p.X},{p.Z}");
    }

    [Test]
    public void HandPlacedPieces_SurviveTheRoundTrip()
    {
        var s = new MapScenery { Set = "dead", Density = 1.5f, Seed = 4, Pieces = new() { new("Pine_3", -120.5f, 80f, 45f, 140f), new("Rock_Medium_2", 90f, -70.2f, 0f, 20f) } };
        var back = MapScenery.Parse(s.ToText());
        Assert.That(back.Set, Is.EqualTo("dead"));
        Assert.That(back.Density, Is.EqualTo(1.5f));
        Assert.That(back.Seed, Is.EqualTo(4));
        Assert.That(back.Pieces, Is.EqualTo(s.Pieces));
        Assert.That(back.Resolve("Generic", 72, 48), Is.EqualTo(s.Pieces), "hand pieces win over the set");
    }

    [Test]
    public void UnknownModels_AndNonsense_AreDropped()
    {
        var s = MapScenery.Parse("set=lava;d=9;seed=x;p=Evil_Model,1,2,3,4|Pine_1,1,2,3,99999");
        Assert.That(s.Set, Is.EqualTo(""));
        Assert.That(s.Density, Is.EqualTo(2f));
        Assert.That(s.Pieces!.Select(p => p.Model), Is.EqualTo(new[] { "Pine_1" }));
        Assert.That(s.Pieces![0].Height, Is.EqualTo(400f));
    }
}
