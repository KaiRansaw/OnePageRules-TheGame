using Godot;

namespace OprGodot;

/// <summary>The main scene: a background and the host every screen is opened into.</summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        App.I.Attach(GetNode<Control>("%ScreenHost"));
        App.I.Open<MainMenuScreen>(Scenes.MainMenu);
    }
}
