using System.Collections.Generic;
using System.Linq;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Map Maker option A (Kai, 2026-11-17): editing many pieces at once - rotate together, duplicate, mirror copy, line up,
/// spread evenly, and lock (a locked piece stays put and the lock survives undo and saving).
/// </summary>
[TestFixture]
public class MapDraftGroupTests
{
    private static MapPiece Box(float x, float z, float w = 4, float d = 2) => new() { Kind = "Ruins", Name = "Ruins", X = x, Z = z, W = w, D = d };

    [Test]
    public void RotateGroup_90_TurnsThePiecesAboutTheirMiddle_AndTurnsEachPiece()
    {
        var d = MapDraft.New(72, 48);
        MapPiece a = Box(30, 24), b = Box(40, 24);
        d.Pieces.AddRange(new[] { a, b });
        d.RotateGroup(new List<MapPiece> { a, b }, 90);
        Assert.That(a.X, Is.EqualTo(35).Within(0.001));
        Assert.That(a.Z, Is.EqualTo(19).Within(0.001));
        Assert.That(b.X, Is.EqualTo(35).Within(0.001));
        Assert.That(b.Z, Is.EqualTo(29).Within(0.001));
        Assert.That(a.Angle, Is.EqualTo(90).Within(0.001));
    }

    [Test]
    public void LockedPieces_DoNotMove_Turn_OrLineUp()
    {
        var d = MapDraft.New(72, 48);
        MapPiece a = Box(10, 10), b = Box(30, 20);
        b.Locked = true;
        d.Pieces.AddRange(new[] { a, b });
        var both = new List<MapPiece> { a, b };
        d.MoveBy(both, 5, 5);
        d.RotateGroup(both, 90);
        Assert.That((b.X, b.Z, b.Angle), Is.EqualTo((30f, 20f, 0f)));
        Assert.That(a.X, Is.Not.EqualTo(10f));
    }

    [Test]
    public void MirrorCopy_PutsAMatchingPieceOnTheOtherSide()
    {
        var d = MapDraft.New(72, 48);
        MapPiece a = Box(10, 8);
        a.Angle = 30;
        d.Pieces.Add(a);
        MapPiece tb = d.MirrorCopy(new[] { a }, MapDraft.EMirror.TopBottom).Single();
        Assert.That((tb.X, tb.Z, tb.Angle), Is.EqualTo((10f, 40f, -30f)));
        MapPiece lr = d.MirrorCopy(new[] { a }, MapDraft.EMirror.LeftRight).Single();
        Assert.That((lr.X, lr.Z), Is.EqualTo((62f, 8f)));
        MapPiece turned = d.MirrorCopy(new[] { a }, MapDraft.EMirror.Turn180).Single();
        Assert.That((turned.X, turned.Z, turned.Angle), Is.EqualTo((62f, 40f, -150f)));
        Assert.That(d.Pieces, Has.Count.EqualTo(4));
    }

    [Test]
    public void Duplicate_AddsUnlockedCopies_OffsetBy3()
    {
        var d = MapDraft.New(72, 48);
        MapPiece a = Box(10, 10);
        a.Locked = true;
        d.Pieces.Add(a);
        MapPiece c = d.Duplicate(new[] { a }).Single();
        Assert.That((c.X, c.Z, c.Locked), Is.EqualTo((13f, 13f, false)));
        Assert.That(d.Pieces, Has.Count.EqualTo(2));
    }

    [Test]
    public void LineUp_LeftEdges_AndSpreadEvenly()
    {
        var d = MapDraft.New(72, 48);
        MapPiece a = Box(10, 5), b = Box(20, 15, w: 8), c = Box(50, 30);
        d.Pieces.AddRange(new[] { a, b, c });
        var all = new List<MapPiece> { a, b, c };
        d.LineUp(all, true);
        Assert.That(MapDraft.Box(b).L, Is.EqualTo(8).Within(0.001));
        Assert.That(MapDraft.Box(c).L, Is.EqualTo(8).Within(0.001));

        MapPiece p = Box(0, 10), q = Box(5, 10), r = Box(40, 10);
        d.SpreadEvenly(new List<MapPiece> { p, q, r });
        Assert.That(q.X, Is.EqualTo(20).Within(0.001));
    }

    [Test]
    public void TheLock_SurvivesUndo_AndTheSavedMap()
    {
        var d = MapDraft.New(72, 48);
        d.Pieces.Add(Box(10, 10));
        d.Pieces[0].Locked = true;
        d.Remember();
        d.Pieces[0].X = 20;
        Assert.That(d.Undo(), Is.True);
        Assert.That(d.Pieces[0].Locked, Is.True);
        Assert.That(d.ToLayout().Pieces[0].Locked, Is.True);
    }

    [Test]
    public void SnapTo_HalfInch_AndOff()
    {
        Assert.That(MapDraft.SnapTo(3.3f, 1), Is.EqualTo(3f));
        Assert.That(MapDraft.SnapTo(3.3f, 0.5f), Is.EqualTo(3.5f));
        Assert.That(MapDraft.SnapTo(3.3f, 0), Is.EqualTo(3.25f));
    }
}
