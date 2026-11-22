using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Built-in terrain pieces from the Terrain Workshop: saving, and which piece a map's footprint gets.</summary>
[TestFixture, NonParallelizable]
public sealed class TerrainPiecesTests
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdg-pieces-" + Guid.NewGuid().ToString("N"));

    [SetUp] public void Start() => TerrainPieces.Folder = _dir;

    [TearDown] public void Stop()
    {
        TerrainPieces.Folder = null!;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static TerrainPieces.Piece Forest(string name, float w, float d, string style = TerrainPieces.Stylised, string game = TerrainPieces.ForBoth) => new()
    {
        Name = name, Kind = "Forest", Style = style, Game = game, W = w, D = d, Round = true,
        Items = { new TerrainPieces.Item { Model = "stylised/nature/models/CommonTree_1.gltf", X = 1, Z = -1, Turn = 30 } },
    };

    [Test]
    public void SavedPiece_ComesBackWithItsModels()
    {
        Assert.That(TerrainPieces.Save(Forest("Pine grove", 6, 6)), Is.Null);
        Assert.That(TerrainPieces.All, Has.Count.EqualTo(1));
        var back = TerrainPieces.All[0];
        Assert.That(back.Id, Is.EqualTo("pine-grove"));
        Assert.That(back.Items.Single().Turn, Is.EqualTo(30));
        Assert.That(TerrainPieces.ById(TerrainPieces.LookPrefix + "pine-grove"), Is.SameAs(back));
    }

    [Test]
    public void SameName_GetsItsOwnId()
    {
        TerrainPieces.Save(Forest("Grove", 6, 6));
        TerrainPieces.Save(Forest("Grove", 6, 6));
        Assert.That(TerrainPieces.All.Select(p => p.Id).OrderBy(x => x), Is.EqualTo(new[] { "grove", "grove-2" }));
    }

    [Test]
    public void Pick_TakesTheClosestSize_AndNeverOneTooFarOff()
    {
        TerrainPieces.Save(Forest("Small", 4, 4));
        TerrainPieces.Save(Forest("Big", 9, 9));
        Assert.That(TerrainPieces.Pick("Forest", TerrainPieces.Stylised, TerrainLooks.ETheme.Grimdark, 4.5f, 4.5f, true, 0)?.Name, Is.EqualTo("Small"));
        Assert.That(TerrainPieces.Pick("Forest", TerrainPieces.Stylised, TerrainLooks.ETheme.Fantasy, 10, 10, true, 0)?.Name, Is.EqualTo("Big"));
        Assert.That(TerrainPieces.Pick("Forest", TerrainPieces.Stylised, TerrainLooks.ETheme.Grimdark, 20, 20, true, 0), Is.Null);
    }

    [Test]
    public void Pick_KeepsToTheStyleTheKindAndTheGame()
    {
        TerrainPieces.Save(Forest("Real", 6, 6, TerrainPieces.Realistic));
        TerrainPieces.Save(Forest("Fantasy only", 6, 6, game: TerrainPieces.ForAof));
        Assert.That(TerrainPieces.Pick("Forest", TerrainPieces.Stylised, TerrainLooks.ETheme.Grimdark, 6, 6, true, 0), Is.Null);
        Assert.That(TerrainPieces.Pick("Forest", TerrainPieces.Stylised, TerrainLooks.ETheme.Fantasy, 6, 6, true, 0)?.Name, Is.EqualTo("Fantasy only"));
        Assert.That(TerrainPieces.Pick("Forest", TerrainPieces.Realistic, TerrainLooks.ETheme.Grimdark, 6, 6, true, 0)?.Name, Is.EqualTo("Real"));
        Assert.That(TerrainPieces.Pick("Ruins", TerrainPieces.Realistic, TerrainLooks.ETheme.Grimdark, 6, 6, true, 0), Is.Null);
    }

    [Test]
    public void Delete_RemovesIt()
    {
        var p = Forest("Gone", 6, 6);
        TerrainPieces.Save(p);
        Assert.That(TerrainPieces.Delete(p), Is.Null);
        Assert.That(TerrainPieces.All, Is.Empty);
    }
}
