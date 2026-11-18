using FDG;
using FDG.Players;
using FdgRaylib.Lobby;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-17: with every seat handed to a bot, LAUNCH did nothing and a second press said "The game has already
/// been launched." The engine made a spectator game, but hooking the battle log up to it threw (no local player), so the
/// table never opened.
/// </summary>
[TestFixture, NonParallelizable]
public class BotVsBotLaunchTests
{
    // A launch sizes the table for its game; put the default back so table-size tests that run later are unaffected.
    [TearDown]
    public void PutTheTableBack() => FDG.GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

    [Test]
    public async Task EverySeatABot_LaunchesAndTheTableOpens()
    {
        Assert.That(LobbySession.TryHost(EGameSystem.AgeOfFantasy, EGameMode.Standard, out var s, out var err), err);
        using var session = s!;
        session.Armies();
        for (int i = 0; i < 100 && !LobbySession.ArmyCatalogReady; i++) await Task.Delay(100);
        session.Tick();
        foreach (var p in session.Vm.PlayerInfos.ToList())
            if (p.PlayerType == EPlayerType.Local) session.ToggleBot(p);
        session.Tick();
        Assume.That(session.Vm.PlayerInfos.All(p => p.PlayerType == EPlayerType.AI && p.ArmyListSummary.IsAssigned),
            "needs two bots with armies");

        Assert.That(session.Launch(), session.LastLaunchError);
        FDG.EngineInterface.IFDGGame? game = null;
        for (int i = 0; i < 100 && game == null; i++) { await Task.Delay(100); game = session.TakeLaunchedGame(); }
        Assert.That(game, Is.Not.Null, "a spectator game reaches the front end");
        Assert.That(game!.LocalPlayerIDs, Is.Empty);
        FdgRaylib.Game.GameSession? watch = null;
        Assert.DoesNotThrow(() => watch = FdgRaylib.Game.GameSession.Start(game, session, _ => { }), "the table can be opened to watch");

        // Kai, 2026-11-18: the watch-only report said only "Held, nothing else" - no beats reached a screen with no seat.
        for (int i = 0; i < 300 && !watch!.Report.Snapshot().Any(l => l.Kind == FdgRaylib.Game.BattleReport.Kind.Move); i++) await Task.Delay(100);
        Assert.That(watch!.Report.Snapshot().Any(l => l.Kind == FdgRaylib.Game.BattleReport.Kind.Move), Is.True,
            "the watching screen hears what the bots do (moves, shots)");

        // Pause: the game stops at its next beat.
        var host = (FDG.Network.Connection.Lobby.LobbyViewModel_Host)session.Vm;
        host.GamePaused = true;
        await Task.Delay(2500);
        int before = watch.Report.Version;
        await Task.Delay(3000);
        Assert.That(watch.Report.Version, Is.EqualTo(before), "nothing new while paused");
        host.GamePaused = false;
        host.StopGame();
    }
}
