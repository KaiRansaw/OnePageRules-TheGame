using Godot;

namespace OprGodot.Hud;

/// <summary>
/// A docked panel with a clickable header that folds the body away (header stays, with a one-line summary).
/// Used for the in-game Mission panel above the battle report (Kai, 2026-11-10).
/// </summary>
public partial class CollapsePanel : PanelContainer
{
    private readonly Control _body;
    private readonly Label _arrow, _summary;

    public bool Collapsed
    {
        get => !_body.Visible;
        set { _body.Visible = !value; _arrow.Text = value ? "+" : "-"; _summary.Visible = value; Size = new Vector2(Size.X, 0); }
    }

    public CollapsePanel(string title, string hotkey, string summary, Control body, float width)
    {
        AddThemeStyleboxOverride("panel", new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.86f }, Border = HudKit.Edge, Cut = 10,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        col.AddThemeConstantOverride("separation", 5);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = $"Click or press {hotkey} to fold / unfold" };
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(HudKit.Heading(title, 12));
        _summary = HudKit.Text(summary, 13, HudKit.Fg, HudKit.Semi);
        _summary.MouseFilter = MouseFilterEnum.Ignore;
        head.AddChild(_summary);
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text(hotkey, 12, HudKit.Dim, HudKit.Semi));
        _arrow = HudKit.Text("-", 18, HudKit.Lime, HudKit.Bold);
        head.AddChild(_arrow);
        head.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Collapsed = !Collapsed; };
        col.AddChild(head);

        _body = body;
        col.AddChild(body);
        AddChild(col);
        _summary.Visible = false;
    }
}
