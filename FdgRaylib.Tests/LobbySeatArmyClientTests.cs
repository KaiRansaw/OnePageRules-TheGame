using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FDG;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The lobby's seat window on a JOINED player's machine (wish list "Lobby": click a seat = that player's
/// army list shows under the map, any player's, enemies included). Batch B shipped it working on the host
/// only - a client saw "Only the host's screen can show other players' lists." for every seat but its own.
/// The engine now sends every seat's full list to every joined player; these pin that the lobby screen
/// reads it, through the real host and client view models.
/// </summary>
[TestFixture]
public class LobbySeatArmyClientTests
{
    [Test]
    public async Task AClientsSeatWindow_ShowsTheHostsList()
    {
        var (host, client) = await StandUpJoinedLobby();
        PlayerID hostSeat = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;
        host.UpdateArmyListFile(hostSeat, Army("Host Guard"));

        (ArmyListFile? army, string why) = LobbyScreen.SeatArmy(client, hostSeat);

        Assert.That(army?.Name, Is.EqualTo("Host Guard"), "the friend sees the host's cards");
        Assert.That(army!.Units.Single().Name, Is.EqualTo("Riflemen"));
        Assert.That(why, Is.Empty);
    }

    [Test]
    public async Task AClientsSeatWindow_ShowsABotsList()
    {
        var (host, client) = await StandUpJoinedLobby();
        host.AddAiPlayer(FDG.Ai.EAiProfile.SoloRules);
        PlayerID bot = host.PlayerInfos.Single(p => p.PlayerName == "DerpBot 1").PlayerID;
        host.UpdateArmyListFile(bot, Army("Bot Horde"));

        Assert.That(LobbyScreen.SeatArmy(client, bot).Army?.Name, Is.EqualTo("Bot Horde"));
    }

    [Test]
    public async Task AnEmptySeat_SaysSo_AndNeverBlamesTheHost()
    {
        var (host, client) = await StandUpJoinedLobby();
        PlayerID hostSeat = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;

        (ArmyListFile? army, string why) = LobbyScreen.SeatArmy(client, hostSeat);

        Assert.That(army, Is.Null);
        Assert.That(why, Is.EqualTo(LobbyScreen.NoSeatArmyText));
        Assert.That(why, Does.Not.Contain("Only the host"));
    }

    [Test]
    public async Task AListStillOnItsWay_ReadsLoading()
    {
        var (host, client, wire) = await StandUpJoinedLobbyWithWire();
        PlayerID hostSeat = host.PlayerInfos.Single(p => p.PlayerName == "Host").PlayerID;
        wire.Client.HoldSeatArmies = true; // the roster says "picked"; the list itself has not landed yet

        host.UpdateArmyListFile(hostSeat, Army("In Flight"));

        (ArmyListFile? army, string why) = LobbyScreen.SeatArmy(client, hostSeat);
        Assert.That(army, Is.Null);
        Assert.That(why, Is.EqualTo("Loading army..."));
    }

    [Test]
    public void TheSeatWindowsWordsAreAsciiOnly()
    {
        foreach (string text in new[] { LobbyScreen.NoSeatArmyText, LobbyScreen.LoadingSeatArmyText,
                     LobbyScreen.UnreadableSeatArmyText, LobbyScreen.ResumeSeatArmyText })
            Assert.That(text.All(c => c < 128), Is.True, text);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static ArmyListFile Army(string name) => new()
    {
        Name = name,
        Faction = name,
        GameSystem = FDG.ArmyBuilding.GameSystems.GrimdarkFuture,
        Units = { new UnitFileEntry { Name = "Riflemen", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 100 } },
    };

    private static async Task<(LobbyViewModel_Host host, LobbyViewModel_Client client)> StandUpJoinedLobby()
    {
        var (host, client, _) = await StandUpJoinedLobbyWithWire();
        return (host, client);
    }

    private static async Task<(LobbyViewModel_Host host, LobbyViewModel_Client client, Loopback wire)> StandUpJoinedLobbyWithWire()
    {
        var wireHost = new Loopback();
        var host = new LobbyViewModel_Host("Host", "The Table", "", wireHost);
        var client = new LobbyViewModel_Client("Friend", wireHost.Client, "");
        Task<string?> join = client.JoinResultTask;
        Assert.That(await Task.WhenAny(join, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(join), "join hung");
        Assert.That(await join, Is.Null);
        return (host, client, wireHost);
    }

    /// <summary>One host, one client, delivered synchronously in-process (the engine suite has the
    /// multi-client version; it is internal to that assembly).</summary>
    private sealed class Loopback : INetworkHost
    {
        public readonly LoopbackClient Client;
        public Loopback() => Client = new LoopbackClient(this);

        public event Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
        public event Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
        public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived;

        public Task StartAsync() => Task.CompletedTask;
        public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled) { Client.Deliver(data); return Task.CompletedTask; }
        public Task SendCommandToSingleClientAsync(ConnectionID client, ArraySegment<byte> data, bool isPooled)
        {
            if (client == Client.ConnectionID) Client.Deliver(data);
            return Task.CompletedTask;
        }
        public void DisconnectClient(ConnectionID client) { }
        public void MarkClientAuthenticated(ConnectionID client) { }
        public void Stop() { }

        internal void FromClient(ArraySegment<byte> data) =>
            OnMessageReceived?.Invoke(new ArraySegment<byte>(data.ToArray()), Client.ConnectionID);
    }

    private sealed class LoopbackClient : INetworkClient
    {
        private readonly Loopback _host;
        public readonly ConnectionID ConnectionID = new(Guid.NewGuid());
        public LoopbackClient(Loopback host) => _host = host;

        public event Action<ArraySegment<byte>>? OnMessageReceived;
        public event Action? OnDisconnected { add { } remove { } }

        public Task<bool> ConnectAsync(IPAddress serverIP, int port = NetworkProtocol.DefaultPort) => Task.FromResult(true);
        public Task SendCommandToHost(ArraySegment<byte> command, bool isPooled) { _host.FromClient(command); return Task.CompletedTask; }
        public void Disconnect() { }

        /// <summary>Swallow seat-army frames - the moment between the roster and the list on a real network.</summary>
        public bool HoldSeatArmies;

        internal void Deliver(ArraySegment<byte> data)
        {
            // Wire frame: [4-byte type-name length][UTF-8 type name][JSON].
            string type = System.Text.Encoding.UTF8.GetString(data.AsSpan(4, BitConverter.ToInt32(data.AsSpan(0, 4))));
            if (HoldSeatArmies && type.EndsWith("LobbySeatArmyMessage", StringComparison.Ordinal)) return;
            OnMessageReceived?.Invoke(new ArraySegment<byte>(data.ToArray()));
        }
    }
}
