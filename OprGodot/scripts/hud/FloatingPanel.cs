using Godot;

namespace OprGodot.Hud;

/// <summary>
/// A small window that floats over the table: drag it by its title bar, minimise it to just the title bar,
/// or close it. Used for pinned enemy info (Kai, 2026-11-10: hover an enemy for a second to see its tooltip;
/// left-click to pin it as one of these). Stays inside the screen while dragged.
/// </summary>
public partial class FloatingPanel : PanelContainer
{
    private readonly Control _body;
    private readonly Button _min;
    private bool _dragging;
    private Vector2 _grab;

    public event Action? Closed;

    public bool Minimised
    {
        get => !_body.Visible;
        set
        {
            _body.Visible = !value;
            _min.Text = value ? "+" : "-";
            _min.TooltipText = value ? "Expand" : "Minimise";
            Size = Vector2.Zero; // shrink to the new content
        }
    }

    public FloatingPanel(string title, Control body, Color accent)
    {
        AddThemeStyleboxOverride("panel", new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.95f }, Border = accent with { A = 0.85f }, Cut = 10, Accent = accent, AccentLength = 36,
            ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 8,
        });
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);

        var bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Move, TooltipText = "Drag to move" };
        bar.AddThemeConstantOverride("separation", 6);
        bar.AddChild(HudKit.Icon("targeting", 16, accent));
        var t = HudKit.Text(title.ToUpperInvariant(), 14, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        t.MouseFilter = MouseFilterEnum.Ignore;
        bar.AddChild(t);
        _min = SmallButton("-", "Minimise");
        _min.Pressed += () => Minimised = !Minimised;
        bar.AddChild(_min);
        var close = SmallButton("x", "Close (Esc)");
        close.Pressed += () => { Closed?.Invoke(); QueueFree(); };
        bar.AddChild(close);
        bar.GuiInput += OnBarInput;
        col.AddChild(bar);

        _body = body;
        col.AddChild(body);
        AddChild(col);
    }

    private static Button SmallButton(string s, string tip)
    {
        var b = new Button { Text = s, TooltipText = tip, CustomMinimumSize = new Vector2(22, 20), FocusMode = FocusModeEnum.None };
        HudKit.StyleButton(b, new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 4, ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 0, ContentMarginBottom = 0 }, HudKit.Fg, 15);
        return b;
    }

    private void OnBarInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _dragging = mb.Pressed;
            _grab = GetGlobalMousePosition() - GlobalPosition;
            if (mb.Pressed) GetParent()?.MoveChild(this, -1); // bring to front
        }
        else if (e is InputEventMouseMotion && _dragging)
        {
            Vector2 screen = GetViewportRect().Size;
            Vector2 p = GetGlobalMousePosition() - _grab;
            GlobalPosition = new Vector2(Mathf.Clamp(p.X, 0, screen.X - Size.X), Mathf.Clamp(p.Y, 0, screen.Y - Size.Y));
        }
    }
}
