using FDG;
using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.TakeBackTools;
using FdgRaylib.Game;
using FdgRaylib.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Battle replays (homebrew, Kai 2026-11-27): a game's activation-start save points are recorded, written to a replay
/// file, and watched back on one table whose store is switched frame to frame - with the report following.
/// </summary>
[TestFixture, NonParallelizable]
public class BattleReplayTests
{
    private readonly List<string> _files = new();

    [TearDown]
    public void TearDown()
    {
        foreach (string f in _files) try { File.Delete(f); } catch { }
        _files.Clear();
        GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
    }

    private static ArmyListFile[] Armies() => Enumerable.Range(0, 2).Select(i => new ArmyListFile
    {
        Name = $"Army {i}",
        Units = new()
        {
            new UnitFileEntry
            {
                Name = "Troops", ModelCount = 1, Quality = 4, Defense = 4,
                Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
            },
        },
    }).ToArray();

    /// <summary>A two-player table in round <paramref name="round"/> with player 0's trooper at x = <paramref name="x"/>.</summary>
    private static GameDataStore Table(int round, float x) => ScenarioCompiler.Compile(new ScenarioFile
    {
        Name = "Replay test",
        Round = round,
        ActivePlayer = 0,
        Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 11 },
        Players = Enumerable.Range(0, 2).Select(i => new ScenarioPlayer
        {
            Army = $"army{i}.fdgarmy",
            Units = new() { new ScenarioUnit { Unit = "Troops", Models = new() { new[] { i == 0 ? x : 50f, 20f } } } },
        }).ToList(),
    }, Armies());

    // The two test frames are compiled separately, so their player ids differ: seat 0's trooper is found per frame.
    private static float TrooperX(ITableState table)
    {
        PlayerID seat0 = table.Players.Objects.OrderBy(p => p.SlotID).First().PlayerID;
        return table.Units.Objects.First(u => u.PlayerID.Equals(seat0)).Models[0].Position.x;
    }

    private string Record()
    {
        GameDataStore first = Table(1, 10f), second = Table(2, 30f);
        PlayerID p0 = first.GetAllValues<PlayerSlotInfo>().OrderBy(s => s.SlotID).First().PlayerID;
        var report = new BattleReport(new TableState(first));
        using var recorder = new BattleReplay.Recorder(report, _ => "Kai");

        TakeBackHistory.Recorder engine = TakeBackHistory.Shared.OnGameStarting();
        engine.Capture(first, 1, p0);
        report.AddNote("The troopers advance.", BattleReport.Plain);
        engine.Capture(second, 2, p0);
        report.AddNote("Kai wins!", BattleReport.Plain);

        string? path = recorder.Finish("Grimdark Future", new[] { "Kai", "Bot" }, "Kai wins!");
        Assert.That(path, Is.Not.Null, "the replay file was written");
        _files.Add(path!);
        return path!;
    }

    [Test]
    public void AGameIsRecordedFrameByFrame()
    {
        string path = Record();
        using BattleReplay.Opened? replay = BattleReplay.Open(path, out string error);

        Assert.That(replay, Is.Not.Null, error);
        Assert.That(replay!.Count, Is.EqualTo(2));
        Assert.That(replay.Manifest.Result, Is.EqualTo("Kai wins!"));
        Assert.That(replay.Manifest.Frames.Select(f => f.Round), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(replay.Manifest.Frames[1].Report.Select(l => l.Text), Does.Contain("The troopers advance."),
            "the lines written between two save points belong to the later one");
        Assert.That(replay.Manifest.Ending.Select(l => l.Text), Does.Contain("Kai wins!"));
        Assert.That(GameProgressUtilities.TryGetProgress(replay.Frame(1))?.RoundCount, Is.EqualTo(2));
    }

    [Test]
    public void AFinishedRecorderTakesNoMoreFrames()
    {
        string path = Record();
        TakeBackHistory.Shared.OnGameStarting().Capture(Table(3, 40f), 3, default);
        using BattleReplay.Opened? replay = BattleReplay.Open(path, out _);
        Assert.That(replay!.Count, Is.EqualTo(2));
    }

    [Test]
    public void WatchingAReplay_MovesTheTableAndTheReport()
    {
        string path = Record();
        using ReplayPlayer? player = ReplayPlayer.Open(path, out string error);
        Assert.That(player, Is.Not.Null, error);
        GameSession session = GameSession.ForReplay(player!);

        Assert.That(session.LocalPlayer, Is.Null, "nobody plays a replay - the screen only watches");
        Assert.That(session.Table.Progress.RoundCount, Is.EqualTo(1));
        Assert.That(TrooperX(session.Table), Is.EqualTo(10f).Within(0.01f));

        Assert.That(player!.Next(), Is.True);
        Assert.That(session.Table.Progress.RoundCount, Is.EqualTo(2), "the same table view now shows frame 2");
        Assert.That(TrooperX(session.Table), Is.EqualTo(30f).Within(0.01f));
        var lines = session.Report.Snapshot().Select(l => string.Concat(l.Title.Select(p => p.Text))).ToList();
        Assert.That(lines, Does.Contain("The troopers advance."));
        Assert.That(lines, Does.Contain("Kai wins!"), "the last frame shows how the game ended");

        Assert.That(player.Previous(), Is.True);
        Assert.That(TrooperX(session.Table), Is.EqualTo(10f).Within(0.01f));
        Assert.That(session.Report.Snapshot().Select(l => string.Concat(l.Title.Select(p => p.Text))), Does.Not.Contain("The troopers advance."));
    }

    [Test]
    public void TheTablesCollectionsFollowTheSwitch()
    {
        // The table view's unit list subscribes to the store once; it must keep answering for the frame on show.
        GameDataStore a = Table(1, 10f), b = Table(2, 30f);
        var store = new SwitchingDataStore(a);
        var table = new TableState(store);
        IDataState<IUnit> units = table.Units;
        var firstIds = units.Objects.Select(u => u.ID).ToList();

        store.Show(b);

        Assert.That(units.Objects.Count(), Is.EqualTo(2));
        Assert.That(units.Objects.Select(u => u.ID), Is.Not.EquivalentTo(firstIds), "frame 2's units, not frame 1's");
    }
}
