using Godot;

namespace OprGodot;

/// <summary>The main scene: a background and the host every screen is opened into.</summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        // "--no-backdrop" (test runs): no 3D table behind the menus - the software renderer of a cloud test screen draws
        // menu screens several times faster without it.
        if (!OS.GetCmdlineUserArgs().Contains("--no-backdrop"))
        {
            AddChild(new Hud.MenuBackdrop { Orbit = !ScriptedRun.IsRequested });
            MoveChild(GetChild(GetChildCount() - 1), 0);
        }
        App.I.Attach(GetNode<Control>("%ScreenHost"));
        App.I.Open<MainMenuScreen>(Scenes.MainMenu);
    }
}
