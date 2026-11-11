using FDG;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// Shows the decision the game is waiting on from the player (<see cref="BattleResolvers.Inbox"/>): the prompt
/// line's text, the actions area (2x2 grid, then any list of choices docked under it, then the way out), the
/// confirm box for a placed move or shot, and where table clicks, hovers, list clicks, right-click, Space and Esc
/// go. Main thread only.
///
/// <para>Kai, 2026-11-10: no banner pop-ups. Choices dock in the actions area; units are picked from the
/// left-hand list or on the table.</para>
/// </summary>
public sealed class BattleDecisions
{
    private readonly BattleScreen _screen;
    private Decision? _shown;
    private int _shownVersion = -1;
    private VBoxContainer _area = null!;
    private Label _areaTitle = null!;
    private HBoxContainer _grid = null!;
    private VBoxContainer _rows = null!;
    private ScrollContainer _rowScroll = null!;
    private Label _rowsTitle = null!, _note = null!;
    private Control _confirm = null!;
    private Label _confirmText = null!;
    private VBoxContainer _confirmChecks = null!;

    public BattleDecisions(BattleScreen screen) => _screen = screen;

    public Decision? Current => BattleResolvers.Inbox.Current;

    /// <summary>Called every HUD refresh: notices a new decision and redraws the actions area when it changes.</summary>
    public (string Prompt, string Hint) Prompt()
    {
        Decision? d = Current;
        for (int guard = 0; guard < 4 && !ReferenceEquals(d, _shown); guard++)
        {
            _shown?.Closed(_screen);
            _shown = d;
            _shownVersion = -1;
            d?.Shown(_screen);
            d = Current;   // Shown may have answered it on the spot (an action pressed on the activation grid)
        }
        if (d == null || d.Version != _shownVersion)
        {
            _shownVersion = d?.Version ?? -1;
            Rebuild(d);
        }
        return d == null ? ("", "") : (d.Prompt, d.Hint);
    }

    /// <summary>Esc: drop a placed move/shot, else step back. True if it did something.</summary>
    public bool Cancel() => Current is { } d && d.RightClick(_screen);
    public bool RightClick() => Current is { } d && d.RightClick(_screen);

    /// <summary>Space / Enter: send the placed move or shot.</summary>
    public bool ConfirmPending()
    {
        if (Current is not { ConfirmText: not null } d) return false;
        d.Confirm();
        return true;
    }

    public bool TableClick(Vector2 at) => Current?.TableClick(_screen, at) ?? false;
    public void TableHover(Vector2 at) => Current?.TableHover(_screen, at);
    public void UnitPicked(IUnit unit) => Current?.UnitPicked(_screen, unit);
    public UnitMark? ListMark(IUnit unit) => Current?.ListMark(unit);

    public bool HotKey(int index)
    {
        if (Current?.Actions is not { } slots || index >= slots.Count || !slots[index].Enabled) return false;
        slots[index].Choose();
        return true;
    }

    // ------------------------------------------------------------------ building

    public Control BuildActionArea()
    {
        _area = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
        _area.AddThemeConstantOverride("separation", 4);
        _areaTitle = HudKit.Text("ACTIONS", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2));
        _area.AddChild(_areaTitle);
        _grid = new HBoxContainer();
        _area.AddChild(_grid);
        _rowsTitle = HudKit.Text("", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2));
        _area.AddChild(_rowsTitle);
        _rowScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 3);
        _rowScroll.AddChild(_rows);
        _area.AddChild(_rowScroll);
        _note = HudKit.Text("", 12, HudKit.Dim, HudKit.Medium);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(150, 0);
        _area.AddChild(_note);
        return _area;
    }

    /// <summary>The confirm box for a placed move or shot: what will happen, Confirm (Space), Clear (right-click).</summary>
    public Control BuildConfirmBox()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        _confirmText = HudKit.Text("", 17, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center);
        v.AddChild(_confirmText);
        _confirmChecks = new VBoxContainer();
        _confirmChecks.AddThemeConstantOverride("separation", 2);
        v.AddChild(_confirmChecks);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        var ok = Ui.Button("Confirm  (Space)", () => ConfirmPending(), "Do it.", "CommitButton");
        ok.Name = "ConfirmPending";
        var no = Ui.Button("Clear  (right-click)", () => Current?.ClearPending(), "Take it back and choose again.", "BackButton");
        row.AddChild(ok);
        row.AddChild(no);
        v.AddChild(row);
        _confirm = HudKit.Panel(new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.96f }, Border = HudKit.Lime, Cut = 10, Glow = HudKit.Lime with { A = 0.3f },
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 10, ContentMarginBottom = 10,
        }, v);
        _confirm.Visible = false;
        return _confirm;
    }

    private static readonly ActionSlot[] Idle =
    {
        new("Hold", "shield", "Hold", "1", false, "", () => { }),
        new("Advance", "walk", "Advance", "2", false, "", () => { }),
        new("Rush", "run", "Rush", "3", false, "", () => { }),
        new("Charge", "sword-clash", "Charge", "4", false, "", () => { }),
    };

    private void Rebuild(Decision? d)
    {
        // The confirm box.
        string? confirm = d?.ConfirmText;
        _confirm.Visible = confirm != null;
        if (confirm != null) { _confirmText.Text = confirm; _confirm.Size = Vector2.Zero; }
        foreach (Node c in _confirmChecks.GetChildren()) c.QueueFree();
        if (confirm != null && d!.ConfirmChecks is { } checks)
            foreach (ConfirmCheck c in checks)
            {
                var h = new HBoxContainer();
                h.AddThemeConstantOverride("separation", 8);
                var box = new CheckBox { Text = c.Label, ButtonPressed = c.On, TooltipText = "Untick to keep this weapon for another target." };
                box.AddThemeFontSizeOverride("font_size", 16);
                box.Toggled += on => c.Set(on);
                h.AddChild(box);
                h.AddChild(HudKit.Text(c.Detail, 14, HudKit.Dim, HudKit.Medium));
                _confirmChecks.AddChild(h);
            }

        // The grid.
        foreach (Node c in _grid.GetChildren()) c.QueueFree();
        IReadOnlyList<ActionSlot>? slots = d == null ? Idle : d.Actions;
        if (slots != null)
        {
            var g = new GridContainer { Columns = 2 };
            g.AddThemeConstantOverride("h_separation", 6);
            g.AddThemeConstantOverride("v_separation", 6);
            foreach (ActionSlot s in slots)
            {
                var b = HudKit.ActionButton(s.Name, s.Icon, s.Label, s.Key, HudKit.Lime, 74, disabled: !s.Enabled, labelSize: 13);
                b.TooltipText = s.Tooltip;
                b.Pressed += s.Choose;
                g.AddChild(b);
            }
            _grid.AddChild(g);
        }
        _grid.Visible = slots != null;

        // The list, docked under the grid.
        foreach (Node c in _rows.GetChildren()) c.QueueFree();
        IReadOnlyList<ChoiceRow>? rows = d?.Rows;
        bool any = rows is { Count: > 0 };
        _rowsTitle.Text = any ? (d!.RowsTitle ?? "Choose").ToUpperInvariant() : "";
        _rowsTitle.Visible = any && slots != null;
        _areaTitle.Text = slots == null && any ? (d!.RowsTitle ?? "Choose").ToUpperInvariant() : "ACTIONS";
        if (any) foreach (ChoiceRow r in rows!) _rows.AddChild(Row(r));
        if (d?.CancelLabel is { } back)
        {
            var b = Ui.Button($"{back}  (right-click)", d.Cancel, "Step back.", "BackButton");
            b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            _rows.AddChild(b);
        }
        int count = _rows.GetChildCount();
        _rowScroll.Visible = count > 0;
        _rowScroll.CustomMinimumSize = Vector2.Zero;   // the whole actions half scrolls (fixed-size unit block)

        _note.Text = d == null ? "Lights up on your turn." : "";
        _note.Visible = _note.Text.Length > 0;
    }

    private static Control Row(ChoiceRow r)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        if (r.Pick != null)
        {
            var b = new Button { Text = r.Label, Disabled = !r.Enabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left, ClipText = true };
            b.TooltipText = r.Enabled ? r.Detail ?? "" : r.Reason ?? "Not available.";
            b.Pressed += r.Pick;
            if (r.Hover is { } hover) { b.MouseEntered += () => hover(true); b.MouseExited += () => hover(false); }
            v.AddChild(b);
        }
        else
        {
            var l = HudKit.Text(r.Label, 15, r.Enabled ? HudKit.Fg : HudKit.Dim, HudKit.Semi);
            l.TooltipText = r.Enabled ? r.Detail ?? "" : r.Reason ?? "";
            l.MouseFilter = Control.MouseFilterEnum.Stop;
            v.AddChild(l);
        }
        string sub = r.Enabled ? r.Detail ?? "" : r.Reason ?? "";
        if (sub.Length > 0)
        {
            var l = HudKit.Text(sub, 12, r.Enabled ? HudKit.Dim : Palette.WoundsRed with { A = 0.8f }, HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(10, 0);
            v.AddChild(l);
        }
        h.AddChild(v);
        if (r.SecondaryLabel != null && r.Secondary != null)
        {
            var s = Ui.Button(r.SecondaryLabel, r.Secondary, "Don't use this one this time.", "BackButton");
            s.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            h.AddChild(s);
        }
        return h;
    }
}
