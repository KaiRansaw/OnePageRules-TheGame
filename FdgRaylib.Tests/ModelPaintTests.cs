using FdgRaylib.Models3D;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The colour painter's stored text (Kai, 2026-11-15): swaps parse, replace, drop and cap at four.</summary>
[TestFixture]
public class ModelPaintTests
{
    [Test]
    public void Swaps_RoundTrip_ReplaceByColour_AndCapAtFour()
    {
        string? p = ModelPaint.With(null, 0xC02020, 0x2050D0);
        Assert.That(p, Is.EqualTo("C02020>2050D0"));
        p = ModelPaint.With(p, 0xC02020, 0x20D050);
        Assert.That(ModelPaint.Parse(p), Has.Count.EqualTo(1), "the same colour is replaced, not added");
        for (int i = 1; i <= 5; i++) p = ModelPaint.With(p, i, i * 16);
        Assert.That(ModelPaint.Parse(p), Has.Count.EqualTo(ModelPaint.MaxSwaps));
        Assert.That(ModelPaint.Parse(p).Last().From, Is.EqualTo(5), "the newest swap stays");
        Assert.That(ModelPaint.Without("C02020>2050D0", 0xC02020), Is.Null);
        Assert.That(ModelPaint.Parse("junk;#ABCDEF>#123456"), Has.Count.EqualTo(1), "bad parts are skipped");
    }

    [Test]
    public void Areas_LiveBesideSwaps_AndEachEditKeepsTheOther()
    {
        string? p = ModelPaint.With(null, 0xFF0000, 0x0000FF);
        p = ModelPaint.WithArea(p, 3, 0x22AA44);
        p = ModelPaint.WithArea(p, 1, 0xFFFFFF);
        Assert.That(p, Is.EqualTo("FF0000>0000FF;A1=FFFFFF;A3=22AA44"));
        Assert.That(ModelPaint.Parse(p), Has.Count.EqualTo(1), "area colours are not swaps");
        p = ModelPaint.Without(p, 0xFF0000);
        Assert.That(p, Is.EqualTo("A1=FFFFFF;A3=22AA44"), "taking a swap off keeps the areas");
        p = ModelPaint.WithArea(p, 1, null);
        Assert.That(ModelPaint.Areas(p).Keys, Is.EquivalentTo(new[] { 3 }));
        p = ModelPaint.WithArea(p, 3, null);
        Assert.That(p, Is.Null);
    }
}
