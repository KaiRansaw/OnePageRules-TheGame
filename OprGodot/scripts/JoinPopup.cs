using FdgRaylib.Config;
using Godot;

namespace OprGodot;

/// <summary>
/// Join a Game (Kai, 2026-11-07, decision 16): not a screen, a small popup with one box for the host's
/// Tailscale address - remembered from last time - and JOIN / Cancel. No servers, ports or passwords.
/// </summary>
public partial class JoinPopup : Control
{
    public override void _Ready()
    {
        var address = GetNode<LineEdit>("%Address");
        address.Text = UserConfig.Current.LastHostAddress;
        address.GrabFocus();
        address.TextSubmitted += _ => Join();

        GetNode<Button>("%Cancel").Pressed += QueueFree;
        GetNode<Button>("%Join").Pressed += Join;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        QueueFree();
        GetViewport().SetInputAsHandled();
    }

    private void Join()
    {
        string host = GetNode<LineEdit>("%Address").Text.Trim();
        if (host.Length == 0)
        {
            GetNode<Label>("%Status").Text = "Type your friend's Tailscale address first.";
            return;
        }

        UserConfig.Current.LastHostAddress = host;
        UserConfig.Save();
        QueueFree();
        App.I.OpenNotYet($"Lobby - joining {host}",
            "Joining over the network comes with the lobby (port step 2). The address is saved, so it will be filled in next time.");
    }
}
