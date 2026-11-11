using FDG;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// Shows the decision the game is waiting on from the player (<see cref="BattleResolvers.Inbox"/>): the prompt
/// line's text, the 2x2 action grid, a choice list under the prompt, and where table clicks, hovers, army-list
/// clicks and Esc go. Main thread only.
/// </summary>
public sealed class BattleDecisions
{
    private readonly BattleScreen _screen;
    private Decision? _shown;
    private int _shownVersion = -1;
    private HBoxContainer _grid = null!;
    private Label _gridNote = null!;
    private PanelContainer _choices = null!;
    private VBoxContainer _choiceRows = null!;
    private Label _choiceTitle = null!;

    public BattleDecisions(BattleScreen screen) => _screen = screen;

    public Decision? Current => BattleResolvers.Inbox.Current;

    /// <summary>Called every HUD refresh: notices a new decision and redraws the grid and list when it changes.</summary>
    public (string Prompt, string Hint) Prompt()
    {
        Decision? d = Current;
        if (!ReferenceEquals(d, _shown))
        {
            _shown?.Closed(_screen);
            _shown = d;
            _shownVersion = -1;
            d?.Shown(_screen);
            d = Current;   // Shown may have answered it on the spot (a queued activation)
            if (!ReferenceEquals(d, _shown)) { _shown = d; d?.Shown(_screen); }
        }
        if (d == null || d.Version != _shownVersion)
        {
            _shownVersion = d?.Version ?? -1;
            Rebuild(d);
        }
        return d == null ? ("", "") : (d.Prompt, d.Hint);
    }

    public bool Cancel()
    {
        if (Current is not { } d) return false;
        bool can = d.CancelLabel != null || d is ActionDecision;
        if (can) d.Cancel();
        return can && d.Done;
    }

    public bool TableClick(Vector2 at) => Current?.TableClick(_screen, at) ?? false;
    public void TableHover(Vector2 at) => Current?.TableHover(_screen, at);
    public void UnitPicked(IUnit unit) => Current?.UnitPicked(_screen, unit);

    public bool HotKey(int index)
    {
        if (Current?.Actions is not { } slots || index >= slots.Count || !slots[index].Enabled) return false;
        slots[index].Choose();
        return true;
    }

    // ------------------------------------------------------------------ building

    public Control BuildActionArea()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        v.AddChild(HudKit.Text("ACTIONS", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        _grid = new HBoxContainer();
        v.AddChild(_grid);
        _gridNote = HudKit.Text("", 12, HudKit.Dim, HudKit.Medium);
        _gridNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _gridNote.CustomMinimumSize = new Vector2(150, 0);
        v.AddChild(_gridNote);
        return v;
    }

    /// <summary>The choice list, under the prompt line.</summary>
    public Control BuildChoicePanel()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        col.AddThemeConstantOverride("separation", 4);
        _choiceTitle = HudKit.Text("", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2));
        col.AddChild(_choiceTitle);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 0) };
        _choiceRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _choiceRows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_choiceRows);
        col.AddChild(scroll);
        _choiceScroll = scroll;
        _choices = HudKit.Panel(new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.94f }, Border = HudKit.Lime with { A = 0.5f }, Cut = 10,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        }, col);
        _choices.Visible = false;
        return _choices;
    }

    private ScrollContainer _choiceScroll = null!;

    private static readonly ActionSlot[] Idle =
    {
        new("Move", "walk", "Move", "1", false, "", () => { }),
        new("Shoot", "crosshair", "Shoot", "2", false, "", () => { }),
        new("Charge", "sword-clash", "Charge", "3", false, "", () => { }),
        new("Pass", "stop-sign", "End", "4", false, "", () => { }),
    };

    private void Rebuild(Decision? d)
    {
        // The grid.
        foreach (Node c in _grid.GetChildren()) c.QueueFree();
        IReadOnlyList<ActionSlot> slots = d?.Actions ?? Idle;
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
        _gridNote.Text = d?.Actions == null ? (d == null ? "Lights up when it is your unit's turn." : "") : "";
        _gridNote.Visible = _gridNote.Text.Length > 0;

        // The list.
        foreach (Node c in _choiceRows.GetChildren()) c.QueueFree();
        IReadOnlyList<ChoiceRow>? rows = d?.Rows;
        bool any = rows is { Count: > 0 } || d?.CancelLabel != null && rows != null;
        _choices.Visible = any;
        if (!any) return;
        _choiceTitle.Text = (d!.RowsTitle ?? "Choose").ToUpperInvariant();
        foreach (ChoiceRow r in rows!) _choiceRows.AddChild(Row(r));
        if (d.CancelLabel is { } back)
        {
            var b = Ui.Button($"{back}  (Esc)", d.Cancel, "Leave this choice.", "BackButton");
            b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            _choiceRows.AddChild(b);
        }
        float max = _screen.GetViewportRect().Size.Y * 0.5f;
        _choiceScroll.CustomMinimumSize = new Vector2(0, Mathf.Min(max, rows.Count * 46 + (d.CancelLabel != null ? 48 : 0)));
    }

    private static Control Row(ChoiceRow r)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        var b = new Button { Text = r.Label, Disabled = !r.Enabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left, ClipText = true };
        b.TooltipText = r.Enabled ? r.Detail ?? "" : r.Reason ?? "Not available.";
        b.Pressed += r.Pick;
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        v.AddChild(b);
        string sub = r.Enabled ? r.Detail ?? "" : r.Reason ?? "";
        if (sub.Length > 0)
        {
            var l = HudKit.Text(sub, 13, r.Enabled ? HudKit.Dim : Palette.WoundsRed with { A = 0.8f }, HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(10, 0);
            v.AddChild(l);
        }
        h.AddChild(v);
        if (r.SecondaryLabel != null && r.Secondary != null)
        {
            var s = Ui.Button(r.SecondaryLabel, r.Secondary, "Don't use this weapon this time.", "BackButton");
            s.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            h.AddChild(s);
        }
        return h;
    }
}
