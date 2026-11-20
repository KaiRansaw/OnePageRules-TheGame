using FDG;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-19: only a model's solid parts are Impassable. The parts a model reports (fractions of its
/// box) land on the right side of a piece's footprint, turned with it, and are saved with the map.</summary>
[TestFixture]
public class TerrainSolidsTests
{
    private static MapPiece Piece(float angle) => new()
    {
        Kind = "Wall", Name = "L", Look = "wh:7", Rules = ETerrainType.Impassible | ETerrainType.Blocking,
        X = 36, Z = 24, W = 8, D = 4, Angle = angle,
    };

    // A strip along the model's +X end (u 0.9-1) and one along its first row (v 0-0.1).
    private static readonly IReadOnlyList<(float, float, float, float)> Parts = new[] { (0.9f, 0f, 1f, 1f), (0f, 0f, 1f, 0.1f) };

    [Test]
    public void Unturned_UIsAlongTheWidth_AndVStartsAtTheFarEdge()
    {
        IZone z = TerrainSolids.FromParts(Piece(0), Parts);
        Assert.That(z.IsPointWithinZone(new Float2(39.7f, 24f)), Is.True, "u 0.9-1 = the +x end");
        Assert.That(z.IsPointWithinZone(new Float2(34f, 25.9f)), Is.True, "v 0-0.1 = the +z (far) edge");
        Assert.That(z.IsPointWithinZone(new Float2(34f, 22.1f)), Is.False, "the near edge is open");
        Assert.That(z.IsPointWithinZone(new Float2(32.3f, 24f)), Is.False, "the -x end is open");
    }

    [Test]
    public void Turned_ThePartsTurnWithTheFootprint()
    {
        MapPiece p = Piece(90);
        IZone z = TerrainSolids.FromParts(p, Parts);
        // Turned 90 degrees the piece's +x end is where its corners put it.
        var c = p.Corners(); // (-hw,-hd), (hw,-hd), (hw,hd), (-hw,hd), turned
        var plusX = ((c[1].X + c[2].X) / 2, (c[1].Z + c[2].Z) / 2);
        var minusX = ((c[0].X + c[3].X) / 2, (c[0].Z + c[3].Z) / 2);
        Assert.That(z.IsPointWithinZone(new Float2(plusX.Item1 * 0.95f + 36 * 0.05f, plusX.Item2 * 0.95f + 24 * 0.05f)), Is.True);
        Assert.That(z.IsPointWithinZone(new Float2(minusX.Item1 * 0.9f + 36 * 0.1f, minusX.Item2 * 0.9f + 24 * 0.1f)), Is.False);
    }

    [Test]
    public void SavedMaps_CarryTheParts_AndEditingDoesNotSeeThem()
    {
        var saved = TerrainSolids.Provider;
        try
        {
            TerrainSolids.Provider = look => look == "wh:7" ? Parts : null;
            var draft = MapDraft.New(72, 48);
            draft.Pieces.Add(Piece(30));
            var plain = Piece(0); plain.Look = ""; draft.Pieces.Add(plain);
            var cover = Piece(0); cover.Rules = ETerrainType.Cover; draft.Pieces.Add(cover);

            var layout = draft.ToLayout();
            Assert.That(layout.Pieces[0].Solid, Is.Not.Null, "a model that stops models plays on its parts");
            Assert.That(layout.Pieces[1].Solid, Is.Null, "a plain block plays its whole footprint");
            Assert.That(layout.Pieces[2].Solid, Is.Null, "area terrain (cover only) keeps its whole footprint");
            Assert.That(draft.ToLayout(withSolids: false).Pieces.All(p => p.Solid == null), Is.True);
        }
        finally { TerrainSolids.Provider = saved; }
    }
}
