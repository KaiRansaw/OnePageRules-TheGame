using Godot;

namespace OprGodot;

/// <summary>
/// Stands in for a screen that is not ported yet, so every button on the menus already leads somewhere and
/// Back works from it. Says which screen it will be and which port step brings it.
/// </summary>
public partial class ComingSoonScreen : Control
{
    private string _screen = "";
    private string _detail = "";

    public void Setup(string screen, string detail)
    {
        _screen = screen;
        _detail = detail;
    }

    public override void _Ready()
    {
        GetNode<Button>("%Back").Pressed += App.I.Back;
        GetNode<Label>("%Title").Text = "Not ported yet";
        GetNode<Label>("%Screen").Text = _screen;
        GetNode<Label>("%Detail").Text = _detail
            + "\n\nUntil then this screen works in the Raylib build, which keeps running alongside the port.";
    }
}
