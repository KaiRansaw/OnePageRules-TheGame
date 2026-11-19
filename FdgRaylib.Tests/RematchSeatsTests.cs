using FDG;
using FDG.Ai;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FdgRaylib.Lobby;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Bug hunt, 2026-11-19: Rematch after a game with every seat handed to a bot (a game to watch) brought the first seat
/// back as a Player, so "same seats" quietly became a game you had to play - and a Tactical or Expert bot in that seat
/// came back labelled "Official AI".
/// </summary>
[TestFixture, NonParallelizable]
public class RematchSeatsTests
{
    [Test]
    public async Task RematchOfABotVsBotGame_BringsBothSeatsBackAsBots_WithTheBotThatWasPicked()
    {
        Assert.That(LobbySession.TryHost(EGameSystem.AgeOfFantasy, EGameMode.Standard, out var s, out var err), err);
        LobbySession.RematchPlan plan;
        using (var first = s!)
        {
            first.Tick();   // the empty second seat gets its bot
            foreach (var p in first.Vm.PlayerInfos.ToList())
                if (p.PlayerType == EPlayerType.Local) first.ToggleBot(p);
            first.SetBrain(first.Vm.PlayerInfos[0], EAiProfile.Tactician);
            var host = (LobbyViewModel_Host)first.Vm;
            Assume.That(host.PlayerInfos.Count, Is.EqualTo(2), "a two-seat lobby");
            Assume.That(host.PlayerInfos.All(p => p.PlayerType == EPlayerType.AI), "both seats are bots before the rematch");
            // What LAUNCH records for the Rematch button (LobbySession.LastLaunch).
            plan = new LobbySession.RematchPlan(host.GameSystem, host.GameMode, host.GetSeats());
        }

        // Rematch hosts a fresh lobby and fills it from the plan; the first lobby lets go of the port as it closes.
        LobbySession? again = null;
        for (int i = 0; i < 50 && again == null; i++)
            if (!LobbySession.TryHost(plan.System, plan.Mode, out again, out err)) { again = null; await Task.Delay(100); }
        Assert.That(again, Is.Not.Null, err);
        using var second = again!;
        second.ApplyRematch(plan);
        second.Tick();

        Assert.That(second.Vm.PlayerInfos.Count, Is.EqualTo(2), "the same two seats, no extra one");
        Assert.That(second.Vm.PlayerInfos.Select(p => p.PlayerType), Is.All.EqualTo(EPlayerType.AI),
            "the first seat stays a bot too - a watched game stays a watched game");
        Assert.That(second.BrainOf(second.Vm.PlayerInfos[0]), Is.EqualTo(EAiProfile.Tactician),
            "and the seat card names the bot that was picked, not the default");
    }
}
