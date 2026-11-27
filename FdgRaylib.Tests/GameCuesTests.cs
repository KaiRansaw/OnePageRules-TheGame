using FDG;
using FDG.Data;
using FDG.Presentation.Beats;
using FdgRaylib.Game;
using NUnit.Framework;
using static FdgRaylib.Game.GameCues;

namespace FdgRaylib.Tests;

/// <summary>Lane 4 (2026-11-27): when the battle's own sound cues play.</summary>
[TestFixture]
public class GameCuesTests
{
    [Test]
    public void Beats_GiveTheDiceAndMoraleCues_AndNothingElse()
    {
        Assert.That(FromBeat(new DiceRolledBeat(new float[] { 0, 1, 0, 0, 0, 0 }, 1, 4, ERandomnessType.Probabilistic, "Hit")), Is.EqualTo(ECue.Dice));
        Assert.That(FromBeat(new BannerBeat("Grunts fails morale - Shaken!", new TextColor(255, 0, 0, 255), EBannerTier.Notice)), Is.EqualTo(ECue.MoraleShaken));
        Assert.That(FromBeat(new UnitRoutedBeat(new UnitID(Guid.NewGuid()), "Grunts", Array.Empty<RoutedModel>())), Is.EqualTo(ECue.MoraleRouted));
        Assert.That(FromBeat(new BannerBeat("Round 2", new TextColor(255, 255, 255, 255))), Is.Null, "the round cue comes from the round number, once");
    }

    [Test]
    public void TheTracker_HearsObjectivesChangeHands_TheBattleStart_AndTheEnd()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var table = new TableState(store);
        var me = new PlayerID(Guid.NewGuid());
        var them = new PlayerID(Guid.NewGuid());
        store.Create(new ObjectiveData(new Position(10f, 20f), store));
        var tracker = new Tracker();
        var mine = new[] { me };

        Assert.That(tracker.Update(table, mine, false, null), Is.Empty, "the first look only learns the table");

        var objective = (ObjectiveData)table.Objectives.Objects.Single();
        objective.SetOwner(me);
        Assert.That(tracker.Update(table, mine, false, null), Is.EqualTo(new[] { ECue.ObjectiveSeized }));
        Assert.That(tracker.Update(table, mine, false, null), Is.Empty, "once, not every frame");
        objective.SetOwner(them);
        Assert.That(tracker.Update(table, mine, false, null), Is.EqualTo(new[] { ECue.ObjectiveLost }));

        store.Create(new GameProgressData(EResumeStage.MainPhase, 1, new List<int>(), new List<int>(), 0,
            new Dictionary<int, int>(), new List<DataBinding<UnitData>>(), settings: GameSettings.GetDefault()));
        Assert.That(tracker.Update(table, mine, false, null), Is.EqualTo(new[] { ECue.BattleStart }), "round 0 -> 1");

        Assert.That(tracker.Update(table, mine, true, false), Is.EqualTo(new[] { ECue.Defeat }));
        Assert.That(tracker.Update(table, mine, true, false), Is.Empty);
        Assert.That(new Tracker().Update(table, mine, true, true), Is.EqualTo(new[] { ECue.Victory }), "a game already over when first seen");
    }

    [Test]
    public void TheEndCue_KnowsWhoWonFromTheWinMessage()
    {
        Assert.That(Result("Newbie wins!", new[] { "Newbie" }), Is.True);
        Assert.That(Result("DerpBot 1 wins!", new[] { "Newbie" }), Is.False);
        Assert.That(Result("Alpha and Bravo win!", new[] { "Bravo" }), Is.True);
        Assert.That(Result("The game is a draw.", new[] { "Newbie" }), Is.Null);
        Assert.That(Result(null, new[] { "Newbie" }), Is.Null);
        Assert.That(AmbienceFor(EGameSystem.GrimdarkFuture, "storm"), Is.EqualTo(new[] { "war", "rain" }));
        Assert.That(AmbienceFor(EGameSystem.AoFSkirmish, ""), Is.EqualTo(new[] { "wind" }));
    }

    [Test]
    public void EveryCue_AndEveryAmbienceBed_HasItsFile()
    {
        string sounds = System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "OprGodot", "sounds");
        foreach (ECue cue in Enum.GetValues<ECue>())
            for (int roll = 0; roll < 5; roll++)
                Assert.That(File.Exists(System.IO.Path.Combine(sounds, FileFor(cue, roll) + ".ogg")), Is.True, $"{cue} {FileFor(cue, roll)}");
        foreach (string bed in new[] { "war", "wind", "rain" })
            Assert.That(File.Exists(System.IO.Path.Combine(sounds, "ambience", bed + ".ogg")), Is.True, bed);
        foreach (string music in new[] { "menu_grimdark", "menu_fantasy" })
            Assert.That(File.Exists(System.IO.Path.Combine(sounds, "music", music + ".ogg")), Is.True, music);
    }
}
