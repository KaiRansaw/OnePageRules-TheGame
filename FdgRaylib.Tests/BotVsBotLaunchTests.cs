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
        Assert.DoesNotThrow(() => FdgRaylib.Game.GameSession.Start(game, session, _ => { }), "the table can be opened to watch");
        (session.Vm as FDG.Network.Connection.Lobby.LobbyViewModel_Host)?.StopGame();
    }
}
