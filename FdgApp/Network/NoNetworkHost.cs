using FDG.Network.Connection;

namespace FdgRaylib.Network;

/// <summary>
/// A lobby host with no network at all: nobody can join and nothing is sent. Battle replays use it to stand up the
/// host lobby a game screen reads its settings from, without opening the game's port.
/// </summary>
public sealed class NoNetworkHost : INetworkHost
{
    public event Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
    public event Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
    public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived { add { } remove { } }
    public Task StartAsync() => Task.CompletedTask;
    public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled) => Task.CompletedTask;
    public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled) => Task.CompletedTask;
    public void DisconnectClient(ConnectionID clientId) { }
    public void MarkClientAuthenticated(ConnectionID clientId) { }
    public void Stop() { }
}
