using System.Collections.Generic;
using FDG;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-17: deploy as a block, drag single models, and only check coherency on Done. These pin the Done
/// check's coherency half against the core rules' wording - "within 1" of at least one other model, and within 9"
/// of all other models, forming an uninterrupted chain" (GF / AoF Advanced Rulebook 3.6.0, Unit Coherency).
/// </summary>
[TestFixture]
public class ManualPlacementTests
{
    private static readonly Float2 Up = new(0f, 1f);

    // 1" circular bases: centre gap minus 1" = base-to-base gap.
    private static (Position, IBaseShape, Float2) At(float x, float z) => (new Position(x, z), new CircleBase(0.5f), Up);

    [Test]
    public void APackedUnit_IsInCoherency()
    {
        var unit = new List<(Position, IBaseShape, Float2)> { At(10, 10), At(11.5f, 10), At(13, 10), At(10, 11.5f) };
        Assert.That(ManualPlacement.Coherency(unit), Is.Empty);
    }

    [Test]
    public void AModelDraggedAway_IsTheOneFlagged()
    {
        var unit = new List<(Position, IBaseShape, Float2)> { At(10, 10), At(11.5f, 10), At(13, 10), At(18, 10) };
        var bad = ManualPlacement.Coherency(unit);
        Assert.That(bad, Has.Count.EqualTo(1));
        Assert.That(bad[0].Index, Is.EqualTo(3));
        Assert.That(bad[0].Reason, Does.Contain("1\""));
    }

    [Test]
    public void TwoClumps_EachWithANeighbour_StillBreakTheChain()
    {
        // Every model has a team-mate within 1", but the two pairs are 4" apart: not one uninterrupted chain.
        var unit = new List<(Position, IBaseShape, Float2)> { At(10, 10), At(11.5f, 10), At(16.5f, 10), At(18, 10), At(19.5f, 10) };
        var bad = ManualPlacement.Coherency(unit);
        Assert.That(bad.ConvertAll(b => b.Index), Is.EquivalentTo(new[] { 0, 1 }), "the smaller clump is the one out of place");
    }

    [Test]
    public void ALongChain_BreaksTheNineInchRule()
    {
        // Ten models in single file, 0.5" apart: chained, but the ends are 13.5" apart base to base.
        var unit = new List<(Position, IBaseShape, Float2)>();
        for (int i = 0; i < 10; i++) unit.Add(At(10 + i * 1.5f, 10));
        var bad = ManualPlacement.Coherency(unit);
        Assert.That(bad, Is.Not.Empty);
        Assert.That(bad.TrueForAll(b => b.Reason.Contains("9\"")), Is.True);
        Assert.That(bad.Exists(b => b.Index == 0) && bad.Exists(b => b.Index == 9), Is.True);
    }

    [Test]
    public void TheMessage_SaysWhatIsWrong()
    {
        Assert.That(ManualPlacement.Describe(new[] { new ManualPlacement.Fault(default, "outside the deployment zone") }),
            Is.EqualTo("A model is outside the deployment zone."));
    }
}
