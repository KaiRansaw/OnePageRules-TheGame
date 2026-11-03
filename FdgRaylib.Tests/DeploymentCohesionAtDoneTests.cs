using System;
using System.Collections.Generic;
using FDG;
using FdgRaylib.Placement;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-02: "as long as all of the models in that unit are within coherency range when you click
/// done, that's all that matters."
///
/// <para>
/// Deployment used to refuse every individual click that would leave a model out of cohesion. That has
/// to go, and not as a convenience: it makes the thing he actually asked for - grab part of a placed
/// unit and move it elsewhere - <b>impossible</b>, because every route from "five models here" to
/// "three here, two there" passes through an arrangement the per-click gate refuses. The two changes
/// are one change.
/// </para>
///
/// <para>
/// What has to stay true is that an out-of-cohesion unit cannot be CONFIRMED. These cover the gate that
/// now carries that alone.
/// </para>
/// </summary>
[TestFixture]
public class DeploymentCohesionAtDoneTests
{
    private static readonly Float2 North = new Float2(0f, 1f);

    /// <summary>A 1"-diameter circular base at a spot - roughly a 25mm infantry model.</summary>
    private static PlacementCohesion.Footprint At(float x, float z) =>
        new(new Position(x, z), new CircleBase(0.5f), North);

    private static List<PlacementCohesion.Footprint> Line(int n, float gapBetweenEdges)
    {
        var list = new List<PlacementCohesion.Footprint>();
        float step = 1f + gapBetweenEdges;   // base diameter + the gap between bases
        for (int i = 0; i < n; i++) list.Add(At(20f + i * step, 20f));
        return list;
    }

    [Test]
    public void ATidyLineIsAcceptable()
    {
        PlacementCohesion.Report report = PlacementCohesion.EvaluateStrict(Line(5, 0.2f));

        Assert.That(report.IsAcceptable, Is.True);
        Assert.That(PlacementCohesion.Describe(report), Is.Null);
    }

    [Test]
    public void AModelLeftBehindIsNotAcceptable()
    {
        // Four together and one abandoned across the table - exactly what box-selecting one model and
        // dropping it somewhere silly produces.
        var models = Line(4, 0.2f);
        models.Add(At(45f, 20f));

        PlacementCohesion.Report report = PlacementCohesion.EvaluateStrict(models);

        Assert.That(report.IsAcceptable, Is.False, "Done must not be available with a model stranded.");
        Assert.That(report.StrandedCount, Is.EqualTo(1));
        Assert.That(PlacementCohesion.Describe(report), Does.Contain("cohesion"));
    }

    /// <summary>
    /// The boundary. The rule is 1" between a model and its nearest team-mate, measured base to base,
    /// so 0.9" apart is fine and 1.1" is not - and the difference between those two is the whole
    /// difference between a legal deployment and a refused one.
    /// </summary>
    [Test]
    public void TheLimitIsOneInchBaseToBase()
    {
        Assert.That(PlacementCohesion.EvaluateStrict(Line(3, 0.9f)).IsAcceptable, Is.True,
            "0.9\" between bases is inside the 1\" rule.");
        Assert.That(PlacementCohesion.EvaluateStrict(Line(3, 1.1f)).IsAcceptable, Is.False,
            "1.1\" between bases breaks it.");
    }

    /// <summary>
    /// Strict really is strict. The lenient <c>Evaluate</c> exists so a unit that casualties already
    /// scattered is not trapped - it may not get WORSE. A unit being deployed has no history to be
    /// lenient about, and if the strict path ever picked up that leniency, a player could deploy a
    /// scattered unit and the Done gate would wave it through.
    /// </summary>
    [Test]
    public void StrictDoesNotForgiveAScatteredUnitTheWayTheLenientCheckWould()
    {
        var scattered = new List<PlacementCohesion.Footprint>
        {
            At(20f, 20f), At(26f, 20f), At(32f, 20f),
        };

        // Lenient, comparing the unit against ITSELF: nothing got worse, so it passes.
        Assert.That(PlacementCohesion.Evaluate(scattered, scattered).IsAcceptable, Is.True,
            "the lenient check forgives a unit that was already like this.");

        // Strict: this is a brand-new deployment, and it is simply illegal.
        Assert.That(PlacementCohesion.EvaluateStrict(scattered).IsAcceptable, Is.False,
            "a freshly deployed unit must meet the rule, not merely avoid getting worse.");
    }

    [Test]
    public void TwoClumpsTooFarApartAreBothStranded()
    {
        // Three here, two there - the shape Kai's box-select gesture makes easy to create by accident.
        var models = new List<PlacementCohesion.Footprint>
        {
            At(20f, 20f), At(21.2f, 20f), At(22.4f, 20f),
            At(40f, 20f), At(41.2f, 20f),
        };

        PlacementCohesion.Report report = PlacementCohesion.EvaluateStrict(models);

        Assert.That(report.IsAcceptable, Is.False);

        // Nobody is STRANDED - every model has a neighbour an inch away inside its own clump. It is
        // the all-pairs limit (9") that catches this, and the distinction matters: a check that only
        // looked at nearest neighbours would wave two clumps on opposite table edges straight through.
        Assert.That(report.StrandedCount, Is.Zero, "each clump is internally tight.");
        Assert.That(report.ScatteredCount, Is.GreaterThan(0),
            "the two clumps are far enough apart to break the all-pairs limit.");
    }

    [Test]
    public void ASingleModelUnitIsAlwaysFine()
    {
        Assert.That(PlacementCohesion.EvaluateStrict(new List<PlacementCohesion.Footprint> { At(20f, 20f) })
            .IsAcceptable, Is.True, "a unit of one has nobody to be out of cohesion with.");
        Assert.That(PlacementCohesion.EvaluateStrict(new List<PlacementCohesion.Footprint>())
            .IsAcceptable, Is.True);
    }

    /// <summary>
    /// The message has to name the problem in the player's terms, because it is the only explanation
    /// they get for a greyed-out Done button.
    /// </summary>
    [Test]
    public void TheMessageSaysWhatIsWrongAndByHowMuch()
    {
        var models = Line(4, 0.2f);
        models.Add(At(45f, 20f));

        string? message = PlacementCohesion.Describe(PlacementCohesion.EvaluateStrict(models));

        Assert.That(message, Is.Not.Null);
        Assert.That(message, Does.Contain("1 model is"));
        Assert.That(message, Does.Contain("\""), "it should quote the actual distance.");
    }
}
