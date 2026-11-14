using System;
using System.IO;
using System.Linq;
using FDG;
using FdgRaylib.Cli;
using FdgRaylib.Terrain;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The Godot Map Maker's map model (Map Maker mockup, approved 2026-11-13): pieces keep their book kind and look through a
/// save, undo / redo, the book's generator, and the book check.
/// </summary>
[TestFixture]
public class MapDraftTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string Write(MapDraft d)
    {
        string path = Path.Combine(_dir, "m.fdgterrain");
        File.WriteAllText(path, TerrainLoader.SerializeToJson(d.ToLayout()));
        return path;
    }

    [Test]
    public void Pieces_KeepKindLookAndShape_ThroughASave()
    {
        var d = MapDraft.New(72, 48);
        d.Name = "Round trip";
        var forest = MapPiece.FromKind(TerrainBook.Kind("Forest")!, 20, 30);
        var ruins = MapPiece.FromKind(TerrainBook.Kind("Ruins")!, 50, 12);
        ruins.Angle = 30;
        ruins.Look = "gothic-ruin-2.glb";
        d.Pieces.Add(forest);
        d.Pieces.Add(ruins);
        d.Objectives.Add((36, 24));

        MapDraft back = MapDraft.Load(Write(d), out string? error)!;

        Assert.That(error, Is.Null);
        Assert.That(back.Pieces.Select(p => p.Kind), Is.EqualTo(new[] { "Forest", "Ruins" }));
        Assert.That(back.Pieces[0].Rules, Is.EqualTo(ETerrainType.Difficult | ETerrainType.Cover | ETerrainType.Dense));
        Assert.That(back.Pieces[0].Round, Is.True);
        Assert.That(back.Pieces[1].Look, Is.EqualTo("gothic-ruin-2.glb"));
        Assert.That((back.Pieces[1].X, back.Pieces[1].Z, back.Pieces[1].Angle), Is.EqualTo((50f, 12f, 30f)).Within(0.01f));
        Assert.That(back.Objectives, Is.EqualTo(new[] { (36f, 24f) }));
        Assert.That(back.Dirty, Is.False, "nothing changed since loading");
    }

    [Test]
    public void AnOlderMapsPiece_GetsTheBookKindWithItsRules()
    {
        var d = MapDraft.New(72, 48);
        d.Pieces.Add(new MapPiece { Rules = ETerrainType.Impassible | ETerrainType.Blocking, X = 10, Z = 10, W = 6, D = 4 });
        string path = Write(d);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Kind\": \"\"", "\"Kind\": \"\""));
        Assert.That(MapDraft.Load(path, out _)!.Pieces[0].Kind, Is.EqualTo("Building"));
    }

    [Test]
    public void UndoAndRedo_StepThroughChanges()
    {
        var d = MapDraft.New(72, 48);
        d.Remember();
        d.Pieces.Add(MapPiece.FromKind(TerrainBook.Kind("Barricade")!, 10, 10));
        d.Remember();
        d.Pieces[0].X = 40;

        Assert.That(d.Undo(), Is.True);
        Assert.That(d.Pieces[0].X, Is.EqualTo(10f));
        Assert.That(d.Undo(), Is.True);
        Assert.That(d.Pieces, Is.Empty);
        Assert.That(d.Redo(), Is.True);
        Assert.That(d.Pieces, Has.Count.EqualTo(1));
    }

    [Test]
    public void Generate_RollsTheBooksTable_OnThisTable()
    {
        var d = MapDraft.New(72, 48);
        d.Objectives.Add((36, 24));
        string line = d.Generate(EGameSystem.GrimdarkFuture, TerrainBook.ETheme.Generic, new Random(3));

        Assert.That(line, Does.StartWith("Generic Planets: rolled "));
        Assert.That(d.Pieces.Count, Is.GreaterThanOrEqualTo(9), "9 fixed pieces + 4D3, less any that don't fit");
        Assert.That(d.Pieces.All(p => p.X > 0 && p.X < 72 && p.Z > 0 && p.Z < 48));
        Assert.That(d.Objectives, Has.Count.EqualTo(1), "objectives stay");
        Assert.That(d.CanUndo, Is.True);
    }

    [Test]
    public void BookCheck_UsesTheChosenGamesBook()
    {
        var d = MapDraft.New(72, 48);
        for (int i = 0; i < 4; i++) d.Pieces.Add(MapPiece.FromKind(TerrainBook.Kind("Building")!, 10 + i * 15, 20));
        var gf = d.BookCheck(EGameSystem.GrimdarkFuture);
        Assert.That(gf.First(l => l.Label == "Pieces").Value, Is.EqualTo("4 (book: 15-20)"));
        Assert.That(gf.First(l => l.Label == "Pieces").Ok, Is.False);
        Assert.That(gf.First(l => l.Label == "Block line of sight").Ok, Is.True);
        Assert.That(d.BookCheck(EGameSystem.GFFirefight).First(l => l.Label == "Pieces").Value, Is.EqualTo("4 (book: 20-25)"));
        Assert.That(d.BookCheck(EGameSystem.GFStarQuest).Single().Label, Is.EqualTo("No advice"));
    }

    [Test]
    public void SizeGames_SaysWhoseBookTableItIs()
    {
        Assert.That(MapDraft.SizeGames(72, 48), Does.StartWith("The book table for Grimdark Future, Age of Fantasy"));
        Assert.That(MapDraft.SizeGames(48, 48), Does.Contain("GF: Firefight"));
    }
}
