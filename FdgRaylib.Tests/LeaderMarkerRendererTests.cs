using System.IO;
using System.Linq;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Fast-Play leader marker (Kai, 2026-11-05: the leader "isnt made clear"). Geometry is pure; the draw
/// call sites are pinned by source so a later edit cannot quietly drop the marker from one view.
/// </summary>
[TestFixture]
public class LeaderMarkerRendererTests
{
    [Test]
    public void TwoChevrons_ApexUp_CentredOnTheBase()
    {
        var c = LeaderMarkerRenderer.Chevrons(100f, 200f, 10f);
        Assert.That(c.Length, Is.EqualTo(2));
        foreach (var stripe in c)
        {
            Assert.That(stripe[1].Y, Is.LessThan(stripe[0].Y), "apex sits above the ends (screen y down)");
            Assert.That(stripe[0].Y, Is.EqualTo(stripe[2].Y).Within(1e-4f));
            Assert.That(stripe[1].X, Is.EqualTo(100f).Within(1e-4f));
        }
        Assert.That(c[0][1].Y, Is.LessThan(c[1][1].Y), "upper stripe first");
        float midY = (c[0][1].Y + c[1][1].Y) / 2f;
        Assert.That(midY, Is.EqualTo(200f - 10f * 0.6f * 0.5f).Within(1e-3f));
    }

    [Test]
    public void Size_IsClampedSoItStaysReadableAndNeverSwampsABigBase()
    {
        Assert.That(LeaderMarkerRenderer.HalfWidthPx(1f), Is.EqualTo(4f));
        Assert.That(LeaderMarkerRenderer.HalfWidthPx(500f), Is.EqualTo(13f));
        Assert.That(LeaderMarkerRenderer.HalfWidthPx(20f), Is.EqualTo(11f).Within(1e-4f));
    }

    [TestCase("RaylibRenderer.cs", "LeaderMarkerRenderer.DrawRaylib")]
    [TestCase("RaylibRenderer.cs", "LeaderMarkerRenderer.IsLeaderModel(unit, model)))")]
    [TestCase("Table3DView.cs", "if (model.IsLeader)")]
    public void BothViews_DrawTheLeader(string file, string call)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "fdg-raylib.sln"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null);
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "FdgRaylib", "Rendering", file));
        Assert.That(src, Does.Contain(call));
    }
}
