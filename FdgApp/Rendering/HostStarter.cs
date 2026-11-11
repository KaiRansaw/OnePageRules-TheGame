using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FdgRaylib.Config;

namespace FdgRaylib.Rendering;

/// <summary>
/// Starts a hosted game with no dialog. Kai, 2026-11-07: "Get rid of it: picking a mode goes straight to
/// the lobby. Your name moves to Settings. No server name, port, password or public listing any more
/// (Tailscale only)."
///
/// <para>What used to be the Host Server dialog's fields: the name comes from Settings
/// (<see cref="UserConfig.PlayerName"/>); the port is the fixed <see cref="NetworkProtocol.DefaultPort"/>
/// friends' Join connects to; there is no password and nothing is ever listed publicly. The UPnP router
/// mapping the dialog used to open is gone with the public listing: friends reach this computer over
/// Tailscale, which needs no router port open, so opening one would only expose the game to the internet
/// for nothing.</para>
///
/// <para>The one failure left is the port being taken (another copy of the game already hosting, or some
/// other program on it). <see cref="TryCreate"/> reports it in plain words; the caller shows a message
/// instead of the old form.</para>
/// </summary>
public sealed class HostStarter
{
    /// <summary>The new lobby. Program.cs owns its lifetime (dispose on lobby close / game end / app exit).</summary>
    public Action<ILobbyViewModel>? OnCreated;

    /// <summary>The port every game is hosted on and every Join connects to.</summary>
    public static int Port => NetworkProtocol.DefaultPort;

    /// <summary>What the lobby header calls this table: whose game it is.</summary>
    public static string TableName(string playerName) =>
        string.IsNullOrWhiteSpace(playerName) ? "The Table" : $"{playerName.Trim()}'s Table";

    /// <summary>
    /// Starts listening and raises <see cref="OnCreated"/> with a fresh host lobby. Returns false, with a
    /// sentence for the player in <paramref name="error"/>, when the port could not be opened.
    /// </summary>
    public bool TryCreate(out string error)
    {
        string name = UserConfig.Current.PlayerName;
        if (string.IsNullOrWhiteSpace(name)) name = UserConfig.DefaultPlayerName;

        FDGHost host = new FDGHost(port: Port);
        Task hostTask = host.StartAsync();

        // A bind failure (port already in use) throws before StartAsync's first await, so the returned
        // task is already faulted here. Without this check the discarded task swallowed the exception
        // and the lobby opened around a server that wasn't listening (#279).
        if (hostTask.IsFaulted)
        {
            // Reading Exception also marks the task's fault observed.
            Console.WriteLine($"Host failed to start: {hostTask.Exception?.GetBaseException().Message}");
            error = $"The game could not start, because network port {Port} is already in use on this " +
                    "computer. Is another copy of the game already open and hosting? Close it and try again.";
            return false;
        }

        error = "";
        var viewModel = new LobbyViewModel_Host(name, TableName(name), password: "", host);
        OnCreated?.Invoke(viewModel);
        return true;
    }
}
