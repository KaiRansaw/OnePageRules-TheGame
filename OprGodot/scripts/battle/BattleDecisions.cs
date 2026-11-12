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
        // The numbered choice buttons (style A) answer to their number keys.
        int first = Current?.Actions?.Count ?? 0;
        if (Current is { ButtonChoices: true, Rows: { } rows } && Style != RowStyle.List && index >= first)
        {
            var buttons = rows.Where(r => r.Pick != null || !r.Enabled).ToList();
            index -= first;
            if (index >= buttons.Count || !buttons[index].Enabled || buttons[index].Pick is not { } pick) return false;
            pick();
            return true;
        }
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
        if ((any || d?.CancelLabel != null) && Style != RowStyle.List && d!.ButtonChoices)
        {
            // Kai, 2026-11-11 (mockup): the choices as buttons extending to the right of the grid, not a list under it.
            _grid.AddChild(Side(d!, rows ?? Array.Empty<ChoiceRow>()));
            _grid.Visible = true;
            _rowsTitle.Visible = false;
            _areaTitle.Text = slots != null ? "ACTIONS" : (d!.RowsTitle ?? "Choose").ToUpperInvariant() + _titleHint;
            _rowScroll.Visible = false;
            _note.Visible = false;
            return;
        }
        _rowsTitle.Text = any ? (d!.RowsTitle ?? "Choose").ToUpperInvariant() : "";
        _rowsTitle.Visible = any && slots != null;
        _areaTitle.TooltipText = ""; _areaTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
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

    // ------------------------------------------------------------------ choices as buttons (mockup, 2026-11-11)

    public enum RowStyle { List, Square, Tile }

    private string _titleHint = "";

    /// <summary><c>--rows=Square|Tile</c>: how choices are drawn. List = the original rows under the grid.</summary>
    public static RowStyle Style { get; set; } = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--rows=")) is { } a
        && Enum.TryParse(a["--rows=".Length..], out RowStyle st) ? st : RowStyle.Square;   // Kai picked A (square), 2026-11-11

    /// <summary>
    /// The choices to the right of the grid: the buttons, then Back. Any rule text (the spell being helped, Takedown's
    /// wording) is one short gold line above them with the full text on hover - Kai, 2026-11-11: a text column took
    /// too much of the window and left empty space.
    /// </summary>
    private Control Side(Decision d, IReadOnlyList<ChoiceRow> rows)
    {
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 4);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        var info = rows.Where(r => r.Pick == null && r.Enabled).ToList();
        // Rule text named like the area's own title goes on the title (hover it) rather than repeating the name.
        _areaTitle.TooltipText = "";
        _areaTitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (info.FirstOrDefault(r => string.Equals(r.Label, d.RowsTitle, StringComparison.OrdinalIgnoreCase)) is { } same)
        {
            info.Remove(same);
            _areaTitle.TooltipText = same.Detail ?? "";
            _areaTitle.MouseFilter = Control.MouseFilterEnum.Stop;
            _titleHint = same.Detail is { Length: > 0 } ? "  (?)" : "";
        }
        else _titleHint = "";
        if (info.Count > 0)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 14);
            foreach (ChoiceRow r in info)
            {
                var l = HudKit.Text(r.Label.ToUpperInvariant() + (r.Detail is { Length: > 0 } ? "  (?)" : ""), 13, HudKit.Gold, HudKit.Tracked(HudKit.Bold, 1));
                l.TooltipText = r.Detail ?? "";
                l.MouseFilter = Control.MouseFilterEnum.Stop;
                line.AddChild(l);
            }
            outer.AddChild(line);
        }
        outer.AddChild(h);
        var buttons = rows.Where(r => r.Pick != null || !r.Enabled).ToList();
        bool square = Style == RowStyle.Square;
        // Two rows of squares fill the block; with a line of rule text above, one row (it would not fit).
        // Many choices under a line of rule text: two rows of smaller squares instead of one long row.
        bool many = info.Count > 0 && buttons.Count > 5;
        int perColumn = square ? (info.Count > 0 && !many ? 1 : 2) : 3;
        var g = new GridContainer { Columns = Math.Max(1, (buttons.Count + perColumn - 1) / perColumn) };
        g.AddThemeConstantOverride("h_separation", 6);
        g.AddThemeConstantOverride("v_separation", 6);
        // Numbered on from the four action buttons when they are showing (5, 6...), so every key is unique.
        int n = d.Actions?.Count ?? 0;
        float size = d.Actions != null || many ? 74 : 96;
        foreach (ChoiceRow r in buttons)
        {
            Control c = square ? SquareChoice(r, d.RowIcon, ++n, size) : TileChoice(r, d.RowIcon, ++n);
            if (r.SecondaryLabel != null && r.Secondary != null)
            {
                // A companion action on the same choice (#321) - a small button under it.
                var stack = new VBoxContainer();
                stack.AddThemeConstantOverride("separation", 2);
                stack.AddChild(c);
                var sb = Ui.Button(r.SecondaryLabel, r.Secondary, "Don't use this one this time.", "BackButton");
                sb.AddThemeFontSizeOverride("font_size", 12);
                stack.AddChild(sb);
                c = stack;
            }
            g.AddChild(c);
        }
        h.AddChild(g);
        if (d.CancelLabel is { } back)
        {
            var b = square ? SquareChoice(new ChoiceRow(back, "Step back (right-click).", true, null, d.Cancel), "shield-reflect", 0)
                : TileChoice(new ChoiceRow(back, "right-click", true, null, d.Cancel), "shield-reflect", 0);
            b.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            h.AddChild(b);
        }
        return outer;
    }

    /// <summary>A unit's faction, for its Army Forge portrait on a choice button (set by the battle screen).</summary>
    public static Func<IUnit, string>? PortraitFaction { get; set; }

    private static string Tip(ChoiceRow r) => r.Enabled ? (r.Detail ?? "") : (r.Reason ?? "Not available.");

    /// <summary>Like the four action buttons: picture, name, number key; the detail on hover.</summary>
    private static Control SquareChoice(ChoiceRow r, string icon, int n, float size = 96)
    {
        var b = new Button { CustomMinimumSize = new Vector2(size, size + 6), Disabled = !r.Enabled || r.Pick == null, FocusMode = Control.FocusModeEnum.None, TooltipText = Tip(r) };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 12, ContentMarginTop = 6, ContentMarginBottom = 6 };
        // Lit (a weapon that will fire, a ticked unit, a model taking wounds): the same look as a picked action.
        if (r.Selected) box = box.With(x => { x.Border = HudKit.Lime; x.Glow = HudKit.Lime with { A = 0.55f }; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 2);
        Control ic = r.Portrait is { } pu
            ? HudKit.Portrait(PortraitFaction?.Invoke(pu) ?? "", pu.Name, r.Icon ?? icon, size * 0.42f, r.Enabled ? HudKit.Lime : HudKit.Dim, r.Enabled ? 1f : 0.45f)
            : HudKit.Icon(r.Icon ?? icon, 38, !r.Enabled ? HudKit.Dim with { A = 0.4f } : r.Selected ? HudKit.Lime : HudKit.Lime with { A = 0.8f });
        ic.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(ic);
        var lbl = HudKit.Text(r.Label.ToUpperInvariant(), 12, r.Enabled ? HudKit.Fg : HudKit.Dim with { A = 0.6f }, HudKit.Tracked(HudKit.Bold, 1), HorizontalAlignment.Center);
        lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lbl.CustomMinimumSize = new Vector2(size - 10, 0);
        lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(lbl);
        if (r.Pips is { Count: > 0 } pips)
        {
            // Wound pips: grey = taken already, red = taking now, lime outline = still there.
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 3);
            float pip = pips.Count > 6 ? 6 : 9;
            foreach (int p in pips)
                row.AddChild(new Panel
                {
                    CustomMinimumSize = new Vector2(pip, pip), MouseFilter = Control.MouseFilterEnum.Ignore,
                    ThemeTypeVariation = "",
                }.With(pn => pn.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = p == 1 ? HudKit.Dim with { A = 0.6f } : p == 2 ? Palette.WoundsRed : new Color(0, 0, 0, 0),
                    BorderColor = p == 0 ? HudKit.Lime : new Color(0, 0, 0, 0), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
                })));
            col.AddChild(row);
        }
        b.AddChild(col);
        if (n > 0)
        {
            var key = HudKit.Text(n.ToString(), 12, HudKit.Dim, HudKit.Bold);
            key.Position = new Vector2(9, 4);
            key.MouseFilter = Control.MouseFilterEnum.Ignore;
            b.AddChild(key);
        }
        Wire(b, r);
        return b;
    }

    /// <summary>A wider button: picture left, name, and one line of what it does (or why not) - readable without hovering.</summary>
    private static Control TileChoice(ChoiceRow r, string icon, int n)
    {
        var b = new Button { CustomMinimumSize = new Vector2(236, 68), Disabled = !r.Enabled || r.Pick == null, FocusMode = Control.FocusModeEnum.None, TooltipText = Tip(r) };
        HudKit.StyleButton(b, new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 10, ContentMarginLeft = 8, ContentMarginRight = 8 }, HudKit.Fg, 15);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = 10; row.OffsetRight = -8;
        row.AddThemeConstantOverride("separation", 8);
        var ic = HudKit.Icon(r.Icon ?? icon, 34, r.Enabled ? HudKit.Lime : HudKit.Dim with { A = 0.4f });
        ic.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(ic);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 0);
        var name = HudKit.Text((n > 0 ? n + "  " : "") + r.Label, 15, r.Enabled ? HudKit.Fg : HudKit.Dim, HudKit.Bold);
        name.ClipText = true; name.MouseFilter = Control.MouseFilterEnum.Ignore;
        text.AddChild(name);
        string sub = Tip(r);
        if (sub.Length > 0)
        {
            var l = HudKit.Text(sub, 12, r.Enabled ? HudKit.Dim : Palette.WoundsRed with { A = 0.85f }, HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.MaxLinesVisible = 2;
            l.CustomMinimumSize = new Vector2(170, 0);
            l.MouseFilter = Control.MouseFilterEnum.Ignore;
            text.AddChild(l);
        }
        row.AddChild(text);
        b.AddChild(row);
        Wire(b, r);
        return b;
    }

    private static void Wire(Button b, ChoiceRow r)
    {
        if (r.Pick != null) b.Pressed += r.Pick;
        if (r.Hover is { } hover) { b.MouseEntered += () => hover(true); b.MouseExited += () => hover(false); }
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
