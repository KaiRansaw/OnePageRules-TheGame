using FDG;
using FDG.Data;
using FDG.Presentation.Beats;
using FdgRaylib.Game;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FdgRaylib.Tests;

/// <summary>
/// The shared battle report the Godot build draws (FdgApp <see cref="BattleReport"/>): Kai, 2026-11-10 - hovering
/// a line shows where a moved unit stood before, and the rules a line mentions are highlighted with a hover.
/// </summary>
[TestFixture]
public class GodotBattleReportTests
{
    private static BattleReport NewReport() => new(new TableState(GameDataStore.GameDataStoreBuilder.GetDefault()));

    [Test]
    public void AMoveLine_RemembersWhereEachModelStoodBefore_AndWhereItEnded()
    {
        var report = NewReport();
        var unit = new UnitID(Guid.NewGuid());
        var moves = new List<ModelMove>
        {
            new(new ModelID(Guid.NewGuid()), new List<Position> { new(10f, 10f), new(10f, 16f) }),
            new(new ModelID(Guid.NewGuid()), new List<Position> { new(12f, 10f), new(12f, 16f) }),
        };
        report.OnBeat(new UnitMovedBeat(unit, "Warriors", moves, TimeSpan.Zero));

        BattleReport.Line line = report.Snapshot().Single(l => l.Kind == BattleReport.Kind.Move);
        Assert.That(string.Concat(line.Title.Select(p => p.Text)), Is.EqualTo("Warriors moves 6\""));
        Assert.That(line.MovedFrom.Select(s => (s.X, s.Z)), Is.EqualTo(new[] { (10f, 10f), (12f, 10f) }));
        Assert.That(line.MovedTo.Select(s => (s.X, s.Z)), Is.EqualTo(new[] { (10f, 16f), (12f, 16f) }));
        Assert.That(line.Units, Is.EqualTo(new[] { unit }), "the moved unit is ringed on hover");
        Assert.That(line.Title[0], Is.EqualTo(new BattleReport.Piece("Warriors", BattleReport.PieceKind.Unit, unit)),
            "the unit's name is its own hoverable piece");
    }

    [Test]
    public void AStandingStill_IsNotAMoveLine()
    {
        var report = NewReport();
        report.OnBeat(new UnitMovedBeat(new UnitID(Guid.NewGuid()), "Warriors",
            new List<ModelMove> { new(new ModelID(Guid.NewGuid()), new List<Position> { new(10f, 10f), new(10f, 10f) }) }, TimeSpan.Zero));
        Assert.That(report.Snapshot(), Is.Empty);
    }

    [Test]
    public void ARuleTheEngineNamed_IsCutOutOfTheLine_ForItsHover()
    {
        var report = NewReport();
        report.OnBeat(new BannerBeat("Horse Marksmen used Vanguard.", new TextColor(200, 200, 200, 255), EBannerTier.Notice,
            referencedRules: new[] { "Vanguard" }));

        BattleReport.Line line = report.Snapshot().Single();
        Assert.That(line.Title.Select(p => (p.Text, p.Kind)), Is.EqualTo(new[]
        {
            ("Horse Marksmen used ", BattleReport.PieceKind.Plain),
            ("Vanguard", BattleReport.PieceKind.Rule),
            (".", BattleReport.PieceKind.Plain),
        }));
    }

    [Test]
    public void ANameInsideAnotherWord_IsNotMarked()
    {
        var report = NewReport();
        report.OnBeat(new BannerBeat("Scouting party arrives.", new TextColor(200, 200, 200, 255), EBannerTier.Notice,
            referencedRules: new[] { "Scout" }));
        Assert.That(report.Snapshot().Single().Title.All(p => p.Kind == BattleReport.PieceKind.Plain));
    }

    [Test]
    public void TheVersionMoves_WhenALineIsAdded()
    {
        var report = NewReport();
        int before = report.Version;
        report.AddNote("Game saved.", BattleReport.Dim);
        Assert.That(report.Version, Is.GreaterThan(before));
        Assert.That(report.ExportText(), Does.Contain("Game saved."));
    }
}
