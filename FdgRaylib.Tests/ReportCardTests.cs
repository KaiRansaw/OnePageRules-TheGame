using FDG;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-15: the battle report's hover says who won first, then each attack as steps (mockup option A).</summary>
[TestFixture]
public class ReportCardTests
{
    private static readonly UnitID Orcs = new(Guid.NewGuid()), Grunts = new(Guid.NewGuid());
    private int _id;

    private BattleReport.Line Line(BattleReport.Kind kind, string text, UnitID? attacker = null, UnitID? target = null,
        BattleReport.Outcome? outcome = null, IReadOnlyList<BattleReport.Roll>? rolls = null, float moved = 0, IReadOnlyList<BattleReport.Piece>? title = null)
    {
        var units = new List<UnitID>();
        if (attacker is { } a) units.Add(a);
        if (target is { } t) units.Add(t);
        return new BattleReport.Line(++_id, kind, 2, title ?? new[] { new BattleReport.Piece(text) }, "", BattleReport.Plain, false,
            attacker, target, null, units, null, null, Array.Empty<BattleReport.Spot>(), Array.Empty<BattleReport.Spot>(),
            rolls ?? Array.Empty<BattleReport.Roll>(), Array.Empty<string>(), outcome, moved,
            target == Grunts ? "Assault Grunts" : target == Orcs ? "Orc Warriors" : null);
    }

    private static BattleReport.Piece U(UnitID id) => new(id == Orcs ? "Orc Warriors" : "Assault Grunts", BattleReport.PieceKind.Unit, id);

    private BattleReport.Line Fight(UnitID by, UnitID at, float attacks, float hits, float saves, int lost, int hitNeed, int saveNeed, params string[] mods) =>
        Line(BattleReport.Kind.Attack, "", by, at,
            new BattleReport.Outcome(true, hits, attacks, hits - saves, lost, false, "CCW", true, saves),
            new[]
            {
                new BattleReport.Roll(by == Orcs ? "Orc Warriors" : "Assault Grunts", by, "Roll to Hit - CCW", hitNeed, $"{hits} hits", new List<int>(), mods),
                new BattleReport.Roll(at == Orcs ? "Orc Warriors" : "Assault Grunts", at, $"CCW: {hits} hits", saveNeed, $"{saves} saved, {hits - saves} wounds", new List<int>(), new List<string>()),
            },
            title: new[] { U(by), new BattleReport.Piece(" fights "), U(at) });

    [Test]
    public void AChargeThatWins_SaysWhoWon_ThenEachAttackAsSteps_ThenTheRout()
    {
        var activation = Line(BattleReport.Kind.Activation, "", title: new[] { U(Orcs) });
        var block = new BattleReport.Block(2, activation with { Units = new[] { Orcs } }, new[]
        {
            Line(BattleReport.Kind.Move, "moves 7\"", moved: 7, title: new[] { U(Orcs) }) with { Units = new[] { Orcs } },
            Fight(Orcs, Grunts, 20, 9, 3, 6, 4, 5),
            Fight(Grunts, Orcs, 8, 4, 1, 3, 4, 5, "Rending"),
            Line(BattleReport.Kind.Line, "", title: new[] { new BattleReport.Piece("Melee won by "), U(Orcs), new BattleReport.Piece(", 6 wounds to 3.") }),
            Line(BattleReport.Kind.Line, "Assault Grunts - Morale Test (5+) - Failed"),
            Line(BattleReport.Kind.Line, "", title: new[] { U(Grunts), new BattleReport.Piece(" fails morale - Routed!") }),
        });

        ReportCard.Card card = ReportCard.For(block)!;

        Assert.That(card.Headline, Is.EqualTo("Orc Warriors won the fight"));
        Assert.That(card.HeadlineUnit, Is.EqualTo(Orcs));
        Assert.That(card.Subline, Is.EqualTo("Charged 7\" into Assault Grunts. Assault Grunts routed."));
        Assert.That(card.Attacks.Select(a => a.Verb), Is.EqualTo(new[] { "attacked", "struck back at" }));
        Assert.That(card.Attacks[0].Steps.Select(s => $"{s.Value} {s.Label}"),
            Is.EqualTo(new[] { "20 ATTACKS", "9 HIT 4+", "3 SAVED 5+", "6 DIED" }));
        Assert.That(card.Attacks[1].Notes, Does.Contain("Rending"));
        Assert.That(card.After.Single().Tag, Is.EqualTo("ROUTED"), "the test and its result are one row");
    }

    [Test]
    public void AShot_WithMixedTargetNumbers_LeavesTheNumberOff()
    {
        var shot = Line(BattleReport.Kind.Attack, "", Orcs, Grunts,
            new BattleReport.Outcome(false, 5, 14, 2, 0, false, "Guns", true, 3),
            new[]
            {
                new BattleReport.Roll("Orc Warriors", Orcs, "Roll to Hit - Rifle", 4, "3 hits", new List<int>(), new List<string>()),
                new BattleReport.Roll("Orc Warriors", Orcs, "Roll to Hit - Rocket", 5, "2 hits", new List<int>(), new List<string> { "Quality 4+", "Bad Shot -1" }),
            },
            title: new[] { U(Orcs), new BattleReport.Piece(" shoots "), U(Grunts) });
        var block = new BattleReport.Block(2, Line(BattleReport.Kind.Activation, "", title: new[] { U(Orcs) }) with { Units = new[] { Orcs } }, new[] { shot });

        ReportCard.Card card = ReportCard.For(block)!;

        Assert.That(card.Headline, Is.EqualTo("Orc Warriors shot Assault Grunts"));
        Assert.That(card.Attacks[0].Steps.Select(s => s.Label), Is.EqualTo(new[] { "ATTACKS", "HIT", "SAVED", "WOUNDS" }));
        Assert.That(card.Attacks[0].Notes, Is.EqualTo(new[] { "Bad Shot -1" }), "the unit's own Quality is not a note");
    }

    [Test]
    public void TheSetupBeforeAnyoneActivates_HasNoCard() =>
        Assert.That(ReportCard.For(new BattleReport.Block(null, null, new[] { Line(BattleReport.Kind.Line, "Map Setup") })), Is.Null);

    [Test]
    public void AMoveOnly_HasNoCard() =>
        Assert.That(ReportCard.For(new BattleReport.Block(2, Line(BattleReport.Kind.Activation, "", title: new[] { U(Orcs) }) with { Units = new[] { Orcs } },
            new[] { Line(BattleReport.Kind.Move, "moves 6\"", moved: 6, title: new[] { U(Orcs) }) with { Units = new[] { Orcs } } })), Is.Null);
}
