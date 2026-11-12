using FdgRaylib.Config;
using FdgRaylib.Lobby;
using Godot;

namespace OprGodot;

/// <summary>
/// Join a Game (Kai, 2026-11-07, decision 16): not a screen, a small popup with one box for the host's
/// Tailscale address - remembered from last time - and JOIN / Cancel. No servers, ports or passwords.
/// JOIN looks the address up, connects and asks the host for a seat (LobbySession.JoinAsync), saying each step
/// under the box; once in, the friend's lobby opens (its Back returns to the main menu).
/// </summary>
public partial class JoinPopup : Control
{
    private bool _busy;
    private int _attempt;
    private LobbySession? _joined;
    private string? _status, _error;
    private readonly object _gate = new();

    public override void _Ready()
    {
        var address = GetNode<LineEdit>("%Address");
        address.Text = UserConfig.Current.LastHostAddress;
        address.GrabFocus();
        address.TextSubmitted += _ => Join();

        GetNode<Button>("%Cancel").Pressed += Cancel;
        GetNode<Button>("%Join").Pressed += Join;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        Cancel();
        GetViewport().SetInputAsHandled();
    }

    private void Cancel()
    {
        _attempt++;   // a join still on its way is let go when it arrives
        QueueFree();
    }

    /// <summary>Test driver: join this address now.</summary>
    public void JoinAddress(string address)
    {
        GetNode<LineEdit>("%Address").Text = address;
        Join();
    }

    private void Join()
    {
        if (_busy) return;
        string host = GetNode<LineEdit>("%Address").Text.Trim();
        _busy = true;
        int attempt = ++_attempt;
        GetNode<Button>("%Join").Disabled = true;
        Task.Run(async () =>
        {
            var (session, error) = await LobbySession.JoinAsync(host, s => { lock (_gate) _status = s; });
            lock (_gate)
            {
                if (attempt != _attempt) { session?.Dispose(); return; }
                _joined = session;
                _error = session == null ? error : null;
            }
        });
    }

    public override void _Process(double delta)
    {
        LobbySession? joined;
        string? status, error;
        lock (_gate) { joined = _joined; _joined = null; status = _status; error = _error; _error = null; }
        if (status != null) GetNode<Label>("%Status").Text = status;
        if (error != null)
        {
            GetNode<Label>("%Status").Text = error;
            _busy = false;
            GetNode<Button>("%Join").Disabled = false;
            lock (_gate) _status = null;
        }
        if (joined == null) return;
        QueueFree();
        App.I.Open<LobbyScreen>(Scenes.Lobby, l => l.Session = joined);
    }
}
