using Godot;

namespace OprGodot;

/// <summary>The main scene: a background and the host every screen is opened into.</summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        AddChild(new Hud.MenuBackdrop { Orbit = !ScriptedRun.IsRequested });
        MoveChild(GetChild(GetChildCount() - 1), 0);
        App.I.Attach(GetNode<Control>("%ScreenHost"));
        App.I.Open<MainMenuScreen>(Scenes.MainMenu);
    }
}
