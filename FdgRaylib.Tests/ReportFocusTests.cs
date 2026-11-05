using System.IO;
using System.Numerics;
using FDG;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-05: hovering a battle-report card draws a line from the shooter to the target and rings
/// every unit the card names.
/// </summary>
[TestFixture]
public class ReportFocusTests
{
    [Test]
    public void Centroid_IsTheMiddleOfTheLivingModels_AndNullForNone()
    {
        Assert.That(ReportFocusGeometry.Centroid(new[] { new Position(0f, 0f), new Position(4f, 2f) }),
            Is.EqualTo(new Position(2f, 1f)));
        Assert.That(ReportFocusGeometry.Centroid(System.Array.Empty<Position>()), Is.Null);
    }

    [Test]
    public void ArrowHead_PointsAtTheTarget()
    {
        var head = ReportFocusGeometry.ArrowHead(new Vector2(0f, 0f), new Vector2(100f, 0f), 16f);
        Assert.That(head, Is.Not.Null);
        Assert.That(head!.Value.Tip, Is.EqualTo(new Vector2(100f, 0f)));
        Assert.That(head.Value.Base.X, Is.EqualTo(84f).Within(1e-3f));
        Assert.That(head.Value.Left.Y, Is.Not.EqualTo(head.Value.Right.Y), "two back corners either side of the line");
    }

    [Test]
    public void ArrowHead_UnitsInBaseContact_DrawNoArrow_OnlyRings()
    {
        Assert.That(ReportFocusGeometry.ArrowHead(new Vector2(0f, 0f), new Vector2(10f, 0f), 16f), Is.Null);
    }

    [TestCase("Presentation/DiceTray.cs", "a.AttackerUnit is { } aid ? UnitById(aid)")]
    [TestCase("Presentation/DiceTray.cs", "if (hovered) SetHover(e);")]
    [TestCase("Presentation/DiceTray.cs", "lossLine.Units.Add(died.Unit);")]
    [TestCase("Presentation/DiceTray.cs", "routLine.Units.Add(routed.Unit);")]
    [TestCase("RaylibRenderer.cs", "Presentation.ReportFocusOverlay.Draw(")]
    public void TheHoverIsWiredThrough(string file, string snippet)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "fdg-raylib.sln"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "FdgRaylib", "Rendering", file));
        Assert.That(src, Does.Contain(snippet));
    }
}
