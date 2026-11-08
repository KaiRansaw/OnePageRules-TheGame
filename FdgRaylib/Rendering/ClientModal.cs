using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FdgRaylib.Config;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Join a Game. Kai, 2026-11-07: "Not a screen: the main menu's Join a Game opens a small popup with one
/// box, Host's Tailscale address (remembers the last one), and JOIN / Cancel. JOIN goes straight into the
/// host's lobby. No servers, no public list, no ports, no password."
///
/// <para>Drawn over the main menu (the menu stays visible behind a dim). Your name is the one in Settings;
/// the port is the fixed one every host listens on (<see cref="HostStarter.Port"/>).</para>
/// </summary>
public class ClientModal : IAppScreen
{
    public Action<ILobbyViewModel>? OnConnected;
    public Action? OnCancel;

    /// <summary>The screen drawn behind the popup (the main menu), so it reads as a popup, not a screen.</summary>
    public IAppScreen? Behind;

    private string _address = UserConfig.Current.LastHostAddress;
    private string _status = "";
    private volatile bool _isConnecting;

    // The accepted lobby, handed over from the connect task (a background thread) to the next Draw, so the
    // popup is closed and the navigation happens on the main thread.
    private ILobbyViewModel? _joined;
    private readonly object _joinLock = new();

    // Bumped by Reset (Cancel, reopening): a connect that finishes after it belongs to a popup the player
    // has already left, so its connection is closed instead of handed over.
    private int _generation;

    // How long to wait for the host's accept/reject handshake before giving up (#075).
    private const double JoinTimeoutSeconds = 8.0;

    public void OnShown() => Reset();

    public void Draw(int screenW, int screenH)
    {
        Behind?.Draw(screenW, screenH);

        const string id = "Join a Game##joinpopup";
        if (!ImGui.IsPopupOpen(id)) ImGui.OpenPopup(id);
        float w = MathF.Min(560f, screenW * 0.9f);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(w, 0f), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.6f));
        if (ImGui.BeginPopupModal(id, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings))
        {
            ILobbyViewModel? joined;
            lock (_joinLock) { joined = _joined; _joined = null; }
            if (joined != null)
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                ImGui.PopStyleColor();
                Reset();
                OnConnected?.Invoke(joined);
                return;
            }

            ImGui.PushFont(RaylibRenderer.LargeFont);
            UiText.Colored(ImGuiTheme.HeaderAccent, "JOIN A GAME");
            ImGui.PopFont();
            ImGui.Spacing();

            ImGui.TextUnformatted("Host's Tailscale address");
            ImGui.BeginDisabled(_isConnecting);
            ImGui.SetNextItemWidth(-1f);
            bool enter = ImGui.InputText("##hostaddress", ref _address, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            if (ImGui.IsItemHovered())
                UiText.TooltipWrapped("The address your friend's game shows in Settings > Player (for example\n" +
                               "kais-pc.tail1234.ts.net). Both of you need Tailscale running.");
            ImGui.EndDisabled();
            UiText.Disabled($"You join as \"{UserConfig.Current.PlayerName}\" - change your name in Settings.");

            if (_status.Length > 0)
            {
                ImGui.Spacing();
                UiText.Wrapped(_status);
            }
            ImGui.Spacing();

            float bw = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            bool cancel = UiButton.Back("Cancel", new Vector2(bw, 44f))
                || (ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !_isConnecting);
            ImGui.SameLine();
            ImGui.BeginDisabled(_isConnecting);
            bool join = UiButton.Confirm("JOIN", new Vector2(bw, 44f));
            ImGui.EndDisabled();

            if (cancel)
            {
                ImGui.CloseCurrentPopup();
                Reset();
                OnCancel?.Invoke();
            }
            else if ((join || enter) && !_isConnecting)
            {
                _ = AttemptConnect();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor();
    }

    private async Task AttemptConnect()
    {
        string name = UserConfig.Current.PlayerName;
        if (string.IsNullOrWhiteSpace(name))
        {
            _status = "Your name is empty - set it in Settings > Player first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_address))
        {
            _status = "Type your friend's Tailscale address first.";
            return;
        }

        _isConnecting = true;
        _status = "Looking up the address...";
        int generation = _generation;

        try
        {
            IPAddress? ip = await ResolveHostAsync(_address).ConfigureAwait(false);
            if (ip == null)
            {
                _status = "Could not find that address. Check the spelling, and that Tailscale is running.";
                return;
            }

            _status = "Connecting...";
            FDGClient client = new();
            bool connected = await client.ConnectAsync(ip, HostStarter.Port).ConfigureAwait(false);
            if (!connected)
            {
                _status = "Could not connect. Is your friend's game open on a game screen with a lobby?";
                return;
            }

            // Joining the lobby is a two-step handshake (#075): the client greets with its protocol
            // version + store type-map fingerprint, and the host either assigns a PlayerID (accept) or
            // returns a readable rejection (incompatible build).
            _status = "Joining the lobby...";
            var viewModel = new LobbyViewModel_Client(name, client, password: "");

            Task<string?> joinTask = viewModel.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(JoinTimeoutSeconds)))
                .ConfigureAwait(false);

            if (winner != joinTask)
            {
                _status = "Timed out waiting for the host to let you in.";
                viewModel.Dispose();
                client.Disconnect();
                return;
            }

            string? rejectReason = await joinTask.ConfigureAwait(false);
            if (rejectReason != null)
            {
                _status = rejectReason;
                viewModel.Dispose();
                client.Disconnect();
                return;
            }

            // Remembered only after the host accepts, so a typo doesn't replace the last good address.
            UserConfig.Current.LastHostAddress = _address.Trim();
            UserConfig.Save();

            lock (_joinLock)
            {
                if (generation != _generation)
                {
                    viewModel.Dispose();
                    client.Disconnect();
                    return;
                }
                _status = "Joined.";
                _joined = viewModel;
            }
        }
        finally
        {
            _isConnecting = false;
        }
    }

    // Turns whatever was typed into a connectable address: an IPv4/IPv6 literal passes straight through;
    // anything else is resolved as a DNS name. FDGHost listens on IPAddress.Any (IPv4 only), so a resolved
    // name must yield an IPv4 address to actually connect (QF10).
    private static async Task<IPAddress?> ResolveHostAsync(string hostText)
    {
        string trimmed = hostText.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (IPAddress.TryParse(trimmed, out IPAddress? literal))
            return literal;

        try
        {
            IPAddress[] resolved = await Dns.GetHostAddressesAsync(trimmed).ConfigureAwait(false);
            return resolved.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch
        {
            return null;
        }
    }

    private void Reset()
    {
        lock (_joinLock)
        {
            _generation++;
            _joined?.Dispose();
            _joined = null;
        }
        _address = UserConfig.Current.LastHostAddress;
        _status = "";
        _isConnecting = false;
    }
}
