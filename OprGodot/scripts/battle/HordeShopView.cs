using FDG;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FdgRaylib.Forge;
using FdgRaylib.Horde;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>Horde Mode's shop between waves: shows <see cref="HordeShopView"/> full screen until the player buys
/// or leaves (Kai, 2026-11-07: the shop stays full-screen).</summary>
public sealed class HordeShopDecision : Decision<HordeShopReply>
{
    public HordeShopRequest Request { get; }
    private HordeShopView? _view;

    public HordeShopDecision(HordeShopRequest r) => Request = r;

    public override string Prompt => $"Horde Shop - wave {Request.Wave} complete";
    public override string Hint => "Spend your points, or press Done - buy nothing to keep them.";
    public override bool HideConfirmBox => true;

    public override void Shown(BattleScreen s)
    {
        _view = new HordeShopView(this, s);
        _view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        s.AddChild(_view);
    }

    public override void Closed(BattleScreen s)
    {
        if (_view != null && GodotObject.IsInstanceValid(_view)) _view.QueueFree();
        _view = null;
    }

    /// <summary>Esc closes the looks / upgrades view inside the shop; it never throws the shop away.</summary>
    public override bool RightClick(BattleScreen s) => _view?.Back() ?? false;

    public override void AutoPlay(BattleScreen s) => Answer(HordeShopReply.Nothing);

    internal void Answer(HordeShopReply reply)
    {
        Complete(reply);
        Version++;
    }

    internal HordeShopView? View => _view;
}

/// <summary>
/// The Horde Shop, from Kai's mockup (2026-11-12, every item kept): top bar with what the wave paid and the purse;
/// the table map on the left (seized objectives with their reach ring, your units lit / dimmed, the enemy); the
/// Recruit / Upgrade / Reinforce tabs in the middle; the cart on the right. Recruits use the Army Forge's cards,
/// upgrade editor and look picker. Over the points: BUY turns into "OVER BY N PTS". Rental drawing code over
/// <see cref="HordeShopSession"/>.
/// </summary>
public partial class HordeShopView : Control
{
    private enum Tab { Recruit, Upgrade, Reinforce }
    private enum Mode { Shop, EditUpgrades, Looks }

    private readonly HordeShopDecision _d;
    private readonly BattleScreen _screen;
    private Task<List<BookFile>>? _loading;
    private HordeShopSession? _s;
    private Tab _tab = Tab.Recruit;
    private Mode _mode = Mode.Shop;
    private string _filter = "";

    // What the middle panel shows on the Recruit tab: a unit picked from the roster (not yet in the cart) or a cart row.
    private ForgeSession? _draft;
    private int _draftObjective;
    private int _cartRow = -1;
    private Guid? _upgradeUnit;
    private Guid? _hot;

    private VBoxContainer _root = null!;
    private Control _body = null!;
    private ShopMap? _map;
    private Label _status = null!;

    public HordeShopView() : this(null!, null!) { }

    public HordeShopView(HordeShopDecision d, BattleScreen screen)
    {
        _d = d;
        _screen = screen;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        var shade = new ColorRect { Color = new Color(0.03f, 0.05f, 0.06f, 0.97f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        AddChild(margin);
        _root = new VBoxContainer();
        _root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(_root);
        _loading = BookLibrary.LoadAsync();
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (_loading is { IsCompleted: true } t)
        {
            _loading = null;
            try { _s = new HordeShopSession(_d.Request, t.Result); }
            catch (Exception e) { GD.PrintErr("Horde Shop: " + e.Message); }
            if (_s != null && _d.Request.SeizedObjectives.Count > 0) _draftObjective = _d.Request.SeizedObjectives[0].Number;
            Rebuild();
        }
    }

    /// <summary>Esc: close the inner view. True when it did something.</summary>
    public bool Back()
    {
        if (_mode == Mode.Shop) return false;
        _mode = Mode.Shop;
        Rebuild();
        return true;
    }

    // ================================================================== layout

    private void Rebuild()
    {
        foreach (Node c in _root.GetChildren()) { _root.RemoveChild(c); c.QueueFree(); }
        _map = null;
        _root.AddChild(TopBar());
        _body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        ((HBoxContainer)_body).AddThemeConstantOverride("separation", 12);
        _root.AddChild(_body);
        _status = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        _root.AddChild(_status);

        if (_s?.Book == null)
        {
            var wait = HudKit.Text(_loading != null ? "Reading the army books..." : "Your army's book couldn't be found, so there is nothing to buy.", 20, HudKit.Dim, HudKit.Semi);
            _body.AddChild(wait);
            return;
        }

        if (_mode == Mode.Looks && Current() is { } cur)
        {
            var looks = new LooksPanel(cur.Session, cur.Row, () => { _mode = Mode.Shop; Rebuild(); }, (t, c) => Say(t, c))
                { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _body.AddChild(looks);
            return;
        }

        _body.AddChild(Side(MapPanel(), 300));
        var middle = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(Middle(), 14, 10, 14, 10));
        middle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _body.AddChild(middle);
        _body.AddChild(Side(CartPanel(), 320));
    }

    private static Control Side(Control content, float width)
    {
        var p = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(content, 12, 10, 12, 10));
        p.CustomMinimumSize = new Vector2(width, 0);
        return p;
    }

    private Control TopBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 22);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("HORDE SHOP", 30, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text($"Wave {_d.Request.Wave} complete", 14, HudKit.Dim, HudKit.Semi));
        bar.AddChild(t);
        var s = _s;
        if (s != null)
        {
            var earned = new HBoxContainer();
            earned.AddThemeConstantOverride("separation", 6);
            earned.AddChild(HudKit.Text("Earned", 14, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
            earned.AddChild(HudKit.Text($"{s.EarnedLine} =", 16, HudKit.Fg, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
            earned.AddChild(HudKit.Text(_d.Request.PointsEarned.ToString(), 22, Palette.Lime, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
            if (s.Kept > 0) earned.AddChild(HudKit.Text($"+ {s.Kept} kept from before", 14, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
            bar.AddChild(earned);
            bar.AddChild(Purse("AVAILABLE", _d.Request.PointsAvailable.ToString(), HudKit.Fg));
            bar.AddChild(Purse("IN CART", HordeShopSession.Signed(s.Total), HudKit.Fg));
            bar.AddChild(Purse("LEFT", s.Left.ToString(), s.Left < 0 ? Palette.WoundsRed : Palette.Lime));
            if (s.Books.Count > 1 && _mode == Mode.Shop)
            {
                var book = new OptionButton { Name = "ShopBook", FocusMode = FocusModeEnum.None, TooltipText = s.BookMatched
                    ? "The army book recruits and upgrades come from. Changing it empties the cart."
                    : "Your army's book wasn't found - pick it here. Changing it empties the cart." };
                foreach (BookFile b in s.Books) book.AddItem(HordeShopSession.BookLabel(b, _d.Request.AllowedGameSystems));
                book.Selected = Math.Max(0, s.Books.IndexOf(s.Book!));
                book.ItemSelected += i => { s.UseBook(s.Books[(int)i]); _draft = null; _cartRow = -1; _upgradeUnit = null; Rebuild(); };
                bar.AddChild(book);
            }
        }
        bar.AddChild(Ui.Grow());
        var skip = Ui.Button("Done - buy nothing", () => _d.Answer(HordeShopReply.Nothing), "Close the shop and keep your points for later waves.", "BackButton");
        skip.Name = "ShopSkip";
        skip.CustomMinimumSize = new Vector2(190, 44);
        bar.AddChild(skip);
        bar.AddChild(BuyButton("ShopBuyTop"));
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(bar, 16, 8, 16, 8));
    }

    private Button BuyButton(string name)
    {
        var s = _s;
        bool over = s != null && s.Left < 0;
        string text = s == null || s.Items == 0 ? "BUY" : over ? $"OVER BY {-s.Left} PTS" : $"BUY ({HordeShopSession.Signed(s.Total)} pts)";
        var b = Ui.Button(text, Buy, over ? "Not enough points - take something out of the cart." : s?.Items == 0 ? "The cart is empty."
            : "Buy the cart: upgrades and reinforcements apply now, then you place each recruit.", "CommitButton");
        b.Name = name;
        b.Disabled = s == null || !s.CanBuy;
        b.CustomMinimumSize = new Vector2(200, 44);
        if (over) b.AddThemeColorOverride("font_disabled_color", Palette.WoundsRed);
        return b;
    }

    private void Buy()
    {
        if (_s is not { CanBuy: true } s) return;
        _d.Answer(s.Reply());
    }

    private static Control Purse(string label, string value, Color colour)
    {
        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", -4);
        v.AddChild(HudKit.Text(label, 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center));
        v.AddChild(HudKit.Text(value, 24, colour, HudKit.Exo(800), HorizontalAlignment.Center));
        return v;
    }

    private void Say(string text, Color colour)
    {
        if (_status == null || !IsInstanceValid(_status)) return;
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour);
    }

    // ================================================================== map (left)

    private Control MapPanel()
    {
        var s = _s!;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(HudKit.Heading("The table", 15));
        _map = new ShopMap(this) { CustomMinimumSize = new Vector2(276, 184) };
        col.AddChild(_map);
        var legend = new HFlowContainer();
        legend.AddThemeConstantOverride("h_separation", 10);
        legend.AddChild(Key(Palette.Lime, $"Seized ({_d.Request.ShopRangeInches:0}\" ring)"));
        legend.AddChild(Key(HudKit.Dim, "Not seized"));
        legend.AddChild(Key(HudKit.Friendly, "Your unit"));
        legend.AddChild(Key(HudKit.Friendly with { A = 0.35f }, "Out of reach"));
        legend.AddChild(Key(HudKit.Enemy, "Enemy"));
        col.AddChild(legend);
        var pri = _d.Request.SeizedObjectives.FirstOrDefault(o => o.IsPriority);
        if (pri != null) col.AddChild(ForgeWidgets.Wrapped($"Objective {pri.Number} is the priority.", 13, HudKit.Dim));

        col.AddChild(HudKit.Heading("Your units", 14));
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        foreach (HordeShopUnit u in _d.Request.Upgradable.OrderBy(u => u.NotEligibleReason != null))
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
            bool ok = u.NotEligibleReason == null;
            row.AddChild(HudKit.Text(u.Name, 14, ok ? HudKit.Fg : HudKit.Dim, HudKit.Semi).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; l.MouseFilter = MouseFilterEnum.Ignore; }));
            string reach = ok ? (NearestSeized(u.UnitId) is { } o ? $"in reach - obj {o}" : "in reach") : "out of reach";
            row.AddChild(HudKit.Text(reach, 13, ok ? Palette.Lime : HudKit.Dim, HudKit.Semi).Also(l => l.MouseFilter = MouseFilterEnum.Ignore));
            row.TooltipText = u.NotEligibleReason ?? "Within reach of a seized objective.";
            Hover(row, u.UnitId);
            list.AddChild(row);
        }
        if (_d.Request.Upgradable.Count == 0) list.AddChild(HudKit.Text("You have no units on the table.", 13, HudKit.Dim, HudKit.Semi));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        col.AddChild(scroll);
        col.AddChild(ForgeWidgets.Wrapped("Hover a unit here or in a list to light it up on the map.", 12, HudKit.Dim));
        return col;
    }

    private static Control Key(Color c, string text)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 4);
        h.AddChild(new ColorRect { Color = c, CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        h.AddChild(HudKit.Text(text, 12, HudKit.Dim, HudKit.Semi));
        return h;
    }

    private void Hover(Control c, Guid unit)
    {
        c.MouseEntered += () => { _hot = unit; _map?.QueueRedraw(); };
        c.MouseExited += () => { if (_hot == unit) { _hot = null; _map?.QueueRedraw(); } };
    }

    /// <summary>The number of the closest seized objective in reach of a unit, or null.</summary>
    private int? NearestSeized(Guid unitId)
    {
        IUnit? u = FindUnit(unitId);
        if (u == null) return null;
        int? best = null;
        float bestD = float.MaxValue;
        foreach (HordeShopObjective o in _d.Request.SeizedObjectives)
            foreach (IModel m in u.Models.Where(m => m.GetIsAlive()))
            {
                float dx = m.Position.x - o.X, dz = m.Position.z - o.Z, d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < bestD) { bestD = d; best = o.Number; }
            }
        return best;
    }

    internal IUnit? FindUnit(Guid id) =>
        _screen.Session.Table.Units.Objects.FirstOrDefault(u => u.ID.ID == id);

    // ================================================================== middle

    private Control Middle()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        if (_mode == Mode.EditUpgrades && Current() is { } cur)
        {
            col.AddChild(UpgradeEditor(cur.Session, cur.Row, "Done", () => { _mode = Mode.Shop; Rebuild(); }));
            return col;
        }
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        var req = _d.Request;
        tabs.AddChild(TabButton(Tab.Recruit, "Recruit"));
        tabs.AddChild(TabButton(Tab.Upgrade, $"Upgrade ({req.Upgradable.Count(u => u.NotEligibleReason == null)})"));
        tabs.AddChild(TabButton(Tab.Reinforce, $"Reinforce ({req.Upgradable.Count(u => u.NotReinforceableReason == null)})"));
        col.AddChild(tabs);
        Control content = _tab switch
        {
            Tab.Upgrade => UpgradeTab(),
            Tab.Reinforce => ReinforceTab(),
            _ => RecruitTab(),
        };
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(content);
        col.SizeFlagsVertical = SizeFlags.ExpandFill;
        return col;
    }

    private Button TabButton(Tab tab, string text)
    {
        var b = new Button { Name = "Tab" + tab, Text = text, ToggleMode = true, ButtonPressed = _tab == tab, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 38) };
        b.AddThemeFontSizeOverride("font_size", 17);
        if (_tab == tab) b.AddThemeColorOverride("font_color", Palette.Lime);
        b.Pressed += () => { _tab = tab; Rebuild(); };
        return b;
    }

    // ---- recruit

    private Control RecruitTab()
    {
        var s = _s!;
        int left = s.Left;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        var search = new LineEdit { Name = "ShopSearch", PlaceholderText = $"Search {s.Book!.Name}...", Text = _filter };
        search.TextChanged += t => { _filter = t; CallDeferred(nameof(RebuildKeepSearch)); };
        col.AddChild(search);

        var roster = new VBoxContainer();
        roster.AddThemeConstantOverride("separation", 8);
        foreach (var group in ForgeRosterCategories.GroupBook(s.Book))
        {
            var units = group.Units.Where(u => _filter.Length == 0 || u.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (units.Count == 0) continue;
            roster.AddChild(HudKit.Heading(group.Title, 14));
            var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 8);
            flow.AddThemeConstantOverride("v_separation", 8);
            foreach (RosterUnit u in units)
            {
                string id = u.Id;
                bool selected = _draft != null && _draft.List.Units.Count > 0 && _draft.List.Units[0].RosterUnitId == id && _cartRow < 0;
                Control card = ForgeWidgets.Card(s.Book.Faction, "Recruit_" + u.Name, u.Name, u.BasePointCost, u.BasePointCost > left ? "can't afford" : "", selected,
                    () => { _draft = s.Draft(id); _cartRow = -1; Rebuild(); },
                    () => { _draft = s.Draft(id); _cartRow = -1; AddDraft(); });
                if (u.BasePointCost > left) card.Modulate = new Color(1, 1, 1, 0.5f);
                flow.AddChild(card);
            }
            roster.AddChild(flow);
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 230), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        roster.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(roster);
        col.AddChild(HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(scroll, 8, 6, 8, 6)));

        col.AddChild(RecruitDetail());
        return col;
    }

    private void RebuildKeepSearch()
    {
        Rebuild();
        if (FindChild("ShopSearch", true, false) is LineEdit le) { le.GrabFocus(); le.CaretColumn = le.Text.Length; }
    }

    /// <summary>The picked roster unit (a draft) or the clicked cart recruit.</summary>
    private (ForgeSession Session, int Row)? Current()
    {
        if (_s == null) return null;
        if (_tab == Tab.Upgrade && _upgradeUnit is Guid g && _s.Edits.FirstOrDefault(e => e.Info.UnitId == g) is { Row: >= 0 } e)
            return (_s.Upgrades, e.Row);
        if (_cartRow >= 0 && _cartRow < _s.Cart.List.Units.Count) return (_s.Cart, _cartRow);
        if (_draft != null && _draft.List.Units.Count > 0) return (_draft, 0);
        return null;
    }

    private Control RecruitDetail()
    {
        var s = _s!;
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        var inner = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(inner);
        col.AddChild(scroll);

        if (Current() is not { } cur || _tab != Tab.Recruit)
        {
            inner.AddChild(ForgeWidgets.Wrapped("Click a unit above to see it. Double-click puts it straight in the cart.", 15, HudKit.Dim));
            return HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(col, 10, 8, 10, 8)).Also(p => p.SizeFlagsVertical = SizeFlags.ExpandFill);
        }
        bool inCart = cur.Session == s.Cart;
        var (unit, items) = cur.Session.Detail(cur.Row);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(HudKit.Portrait(s.Book!.Faction, unit.Name, "person", 56, HudKit.Friendly));
        var t = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        t.AddThemeConstantOverride("separation", -4);
        t.AddChild(HudKit.Text(inCart ? "IN THE CART" : "NEW RECRUIT", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        t.AddChild(HudKit.Text(unit.Name.ToUpperInvariant(), 21, HudKit.Fg, HudKit.Bold));
        t.AddChild(HudKit.Text($"{unit.ModelCount} model{(unit.ModelCount == 1 ? "" : "s")} - {unit.PointCost} pts", 14, Palette.PointsBlue, HudKit.Semi));
        head.AddChild(t);
        inner.AddChild(head);
        ForgeWidgets.Gear(s.Cart.Glossary, inner, unit, items, null);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        RosterUnit? roster = cur.Session.Roster(cur.Session.List.Units[cur.Row].RosterUnitId);
        if (roster is { Sections.Count: > 0 })
            buttons.AddChild(Ui.Button("Edit upgrades...", () => { _mode = Mode.EditUpgrades; Rebuild(); }, "The Army Forge's upgrade screen for this recruit.").Also(b => b.Name = "RecruitUpgrades"));
        buttons.AddChild(Ui.Button("Choose looks...", () => { _mode = Mode.Looks; Rebuild(); }, "Pick which downloaded version each model uses.").Also(b => b.Name = "RecruitLooks"));
        int taken = cur.Session.List.Units[cur.Row].Choices.Count;
        buttons.AddChild(HudKit.Text(taken == 0 ? "No upgrades taken" : $"{taken} upgrade{(taken == 1 ? "" : "s")} taken", 13, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        inner.AddChild(buttons);

        inner.AddChild(HudKit.Heading("Deploy at", 14));
        var objs = new HFlowContainer();
        objs.AddThemeConstantOverride("h_separation", 8);
        int chosen = inCart ? s.CartObjectives[cur.Row] : _draftObjective;
        foreach (HordeShopObjective o in _d.Request.SeizedObjectives)
        {
            int n = o.Number, row = cur.Row;
            var b = new Button { Name = $"DeployAt{n}", Text = HordeShopSession.ObjectiveLabel(o), ToggleMode = true, ButtonPressed = n == chosen, FocusMode = FocusModeEnum.None,
                TooltipText = $"The recruit is placed fully within {_d.Request.ShopRangeInches:0}\" of this objective after you buy." };
            if (n == chosen) b.AddThemeColorOverride("font_color", Palette.Lime);
            b.Pressed += () => { if (inCart) s.CartObjectives[row] = n; else _draftObjective = n; Rebuild(); };
            objs.AddChild(b);
        }
        inner.AddChild(objs);

        if (inCart)
        {
            var remove = Ui.Button("Remove from cart", () => { s.RemoveRecruit(_cartRow); _cartRow = -1; Rebuild(); }, "Take this recruit out of the cart.", "BackButton");
            remove.Name = "RemoveRecruit";
            col.AddChild(remove);
        }
        else
        {
            var add = Ui.Button($"+ ADD TO CART ({unit.PointCost} pts)", AddDraft, "Put this recruit in the cart.", "CommitButton");
            add.Name = "AddRecruit";
            add.CustomMinimumSize = new Vector2(0, 42);
            add.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            col.AddChild(add);
        }
        return HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(col, 10, 8, 10, 8)).Also(p => p.SizeFlagsVertical = SizeFlags.ExpandFill);
    }

    private void AddDraft()
    {
        if (_s == null || _draft == null || _draft.List.Units.Count == 0) return;
        string name = _draft.Detail(0).Unit.Name;
        _s.AddRecruit(_draft.List.Units[0], _draftObjective);
        Say($"{name} is in the cart - deploys at objective {_draftObjective}.", Palette.Lime);
        _draft = _s.Draft(_draft.List.Units[0].RosterUnitId); // ready for another copy, fresh
        Rebuild();
    }

    /// <summary>The Forge's upgrade groups for a unit, with its sheet and a close button (recruits: mockup "Edit upgrades...").</summary>
    private Control UpgradeEditor(ForgeSession session, int row, string close, Action closeIt)
    {
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        var (unit, items) = session.Detail(row);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(HudKit.Text(unit.Name.ToUpperInvariant(), 22, HudKit.Fg, HudKit.Bold).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        head.AddChild(HudKit.Text($"{unit.PointCost} pts", 18, Palette.PointsBlue, HudKit.Bold));
        head.AddChild(Ui.Button(close, closeIt, "Back to the shop (Esc).", "CommitButton").Also(b => { b.Name = "EditorDone"; b.CustomMinimumSize = new Vector2(110, 40); }));
        col.AddChild(head);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        var groups = ForgeWidgets.Groups(session, row, Rebuild);
        var gs = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        gs.AddChild(groups);
        body.AddChild(gs);
        var sheet = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sheet.AddThemeConstantOverride("separation", 8);
        ForgeWidgets.Gear(session.Glossary, sheet, unit, items, null);
        var ss = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ss.AddChild(sheet);
        body.AddChild(ss);
        col.AddChild(body);
        return col;
    }

    // ---- upgrade

    private Control UpgradeTab()
    {
        var s = _s!;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        if (_d.Request.Upgradable.Count == 0) list.AddChild(HudKit.Text("You have no units in play.", 14, HudKit.Dim, HudKit.Semi));
        foreach (HordeShopUnit info in _d.Request.Upgradable.OrderBy(u => u.NotEligibleReason != null))
        {
            var edit = s.EditFor(info);
            bool ok = edit.Problem == null;
            Guid id = info.UnitId;
            string hero = info.JoinedHeroName != null ? $" + {info.JoinedHeroName}" : "";
            string mark = s.Changed(edit) ? $"   ({HordeShopSession.Signed(s.PriceOf(edit))} pts{(edit.InCart ? ", in cart" : "")})" : "";
            var b = new Button
            {
                Name = ForgeWidgets.SafeName("Up_" + info.Name), Text = $"{info.Name} [{info.ModelCount}]{hero}   -   {info.PointCost} pts{mark}{(ok ? "" : "   -   " + Short(edit.Problem!))}",
                Alignment = HorizontalAlignment.Left, ToggleMode = true, ButtonPressed = _upgradeUnit == id, FocusMode = FocusModeEnum.None,
                TooltipText = ok ? "Click to change this unit's gear." : edit.Problem, Disabled = !ok, CustomMinimumSize = new Vector2(0, 32),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            if (_upgradeUnit == id) b.AddThemeColorOverride("font_color", Palette.Lime);
            b.Pressed += () => { _upgradeUnit = id; Rebuild(); };
            Hover(b, id);
            list.AddChild(b);
        }
        var ls = new ScrollContainer { CustomMinimumSize = new Vector2(0, 150), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ls.AddChild(list);
        col.AddChild(HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(ls, 8, 6, 8, 6)));

        var detail = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", 8);
        var e = _upgradeUnit is Guid g ? s.Edits.FirstOrDefault(x => x.Info.UnitId == g) : null;
        if (e == null || e.Row < 0)
            detail.AddChild(ForgeWidgets.Wrapped($"Pick one of your units within {_d.Request.ShopRangeInches:0}\" of a seized objective to change its gear. Swapping to something cheaper gives points back.", 15, HudKit.Dim));
        else
        {
            var (unit, items) = s.Upgrades.Detail(e.Row);
            int price = s.PriceOf(e);
            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", 10);
            head.AddChild(HudKit.Text(e.Info.Name.ToUpperInvariant(), 20, HudKit.Fg, HudKit.Bold));
            head.AddChild(Ui.Grow());
            head.AddChild(HudKit.Text(s.Changed(e) ? $"{e.Info.PointCost} -> {unit.PointCost} pts ({HordeShopSession.Signed(price)})" : "No changes yet", 16,
                !s.Changed(e) ? HudKit.Dim : price > 0 ? Palette.PointsBlue : Palette.HeroGold, HudKit.Bold));
            detail.AddChild(head);
            detail.AddChild(ForgeWidgets.Wrapped("Has now: " + e.Info.CurrentGear, 13, HudKit.Dim));
            if (e.Info.JoinedHeroName != null)
                detail.AddChild(ForgeWidgets.Wrapped($"Joined by {e.Info.JoinedHeroName}: only the unit's own models are re-equipped; the hero and its points stay as they are.", 13, HudKit.Dim));
            if (!e.FromLoadout)
                detail.AddChild(ForgeWidgets.Wrapped("The shop doesn't know which upgrades this unit started with, so it opens at the book's basic loadout. Pick everything you want it to have (including what it already had) - you only pay the difference.", 13, Palette.HitsYellow));
            if (price < 0 && s.Changed(e)) detail.AddChild(HudKit.Text($"You get {-price} pts back.", 14, Palette.HeroGold, HudKit.Semi));
            var gs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var groups = ForgeWidgets.Groups(s.Upgrades, e.Row, Rebuild);
            gs.AddChild(groups);
            detail.AddChild(gs);
            detail.AddChild(ForgeWidgets.Wrapped("Models keep their place and damage; XP and traits stay.", 13, HudKit.Dim));
            var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            buttons.AddThemeConstantOverride("separation", 8);
            buttons.AddChild(Ui.Button("Reset", () => { s.Reset(e); Rebuild(); }, "Back to what it has now.", "BackButton").Also(b => b.Name = "UpgradeReset"));
            var add = Ui.Button(e.InCart ? "In the cart" : $"+ ADD TO CART ({HordeShopSession.Signed(price)} pts)", () => { e.InCart = true; Rebuild(); },
                "Put this upgrade in the cart (later changes stay in it).", "CommitButton");
            add.Name = "AddUpgrade";
            add.Disabled = e.InCart || !s.Changed(e);
            buttons.AddChild(add);
            detail.AddChild(buttons);
        }
        col.AddChild(HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(detail, 10, 8, 10, 8)).Also(p => p.SizeFlagsVertical = SizeFlags.ExpandFill));
        return col;
    }

    private static string Short(string reason) => reason.Length > 70 ? reason[..67] + "..." : reason;

    // ---- reinforce

    private Control ReinforceTab()
    {
        var s = _s!;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(ForgeWidgets.Wrapped($"A unit down to half its models (or half its Tough, for one model) and within {_d.Request.ReinforceRangeInches:0}\" of a seized objective can be reinforced: pay half its points, rounded up, and get back up to half its models or wounds. It keeps its XP and traits.", 14, HudKit.Dim));
        if (_d.Request.Upgradable.Count == 0) col.AddChild(HudKit.Text("You have no units on the table.", 14, HudKit.Dim, HudKit.Semi));
        else if (!_d.Request.Upgradable.Any(u => u.NotReinforceableReason == null))
            col.AddChild(HudKit.Text("Nothing to reinforce - no unit in reach is down to half strength.", 14, HudKit.Dim, HudKit.Semi));
        foreach (HordeShopUnit info in _d.Request.Upgradable.OrderBy(u => u.NotReinforceableReason != null))
        {
            bool ok = info.NotReinforceableReason == null;
            Guid id = info.UnitId;
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
            row.AddThemeConstantOverride("separation", 10);
            var tick = new CheckBox { Name = ForgeWidgets.SafeName("Rf_" + info.Name), ButtonPressed = s.Reinforce.Contains(id), Disabled = !ok, FocusMode = FocusModeEnum.None };
            tick.Toggled += on => { if (on) s.Reinforce.Add(id); else s.Reinforce.Remove(id); Rebuild(); };
            row.AddChild(tick);
            var name = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            name.AddThemeConstantOverride("separation", -2);
            name.AddChild(HudKit.Text(info.Name, 16, ok ? HudKit.Fg : HudKit.Dim, HudKit.Bold));
            IUnit? unit = FindUnit(id);
            if (unit != null)
            {
                int alive = unit.Models.Count(m => m.GetIsAlive());
                name.AddChild(HudKit.Text($"{alive} of {unit.Models.Count} models left", 13, HudKit.Dim, HudKit.Semi));
            }
            row.AddChild(name);
            var info2 = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            info2.AddThemeConstantOverride("separation", -2);
            if (ok)
            {
                info2.AddChild(HudKit.Text($"brings back {info.ReinforceRestores}", 13, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Right));
                info2.AddChild(HudKit.Text($"{info.ReinforceCost} pts  (half of {info.PointCost})", 15, Palette.Lime, HudKit.Bold, HorizontalAlignment.Right));
            }
            else info2.AddChild(ForgeWidgets.Wrapped(info.NotReinforceableReason!, 13, HudKit.Dim).Also(l => { l.CustomMinimumSize = new Vector2(260, 0); l.HorizontalAlignment = HorizontalAlignment.Right; }));
            row.AddChild(info2);
            Hover(row, id);
            var box = new ChamferBox { Fill = s.Reinforce.Contains(id) ? new Color(0.12f, 0.17f, 0.10f, 0.95f) : HudKit.GlassLight, Border = s.Reinforce.Contains(id) ? HudKit.Lime : HudKit.Edge, Cut = 6,
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 };
            var p = HudKit.Panel(box, row);
            if (!ok) p.Modulate = new Color(1, 1, 1, 0.7f);
            col.AddChild(p);
        }
        col.AddChild(ForgeWidgets.Wrapped("Brought-back models go next to the unit; you place them after BUY.", 13, HudKit.Dim));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(col);
        return scroll;
    }

    // ================================================================== cart (right)

    private Control CartPanel()
    {
        var s = _s!;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        col.AddChild(HudKit.Heading("Cart", 15));
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 2);
        var rows = s.Cart.Rows();
        for (int i = 0; i < rows.Count && i < s.CartObjectives.Count; i++)
        {
            int row = i;
            lines.AddChild(CartLine($"Recruit {rows[i].Name}", $"{rows[i].ModelCount} model{(rows[i].ModelCount == 1 ? "" : "s")} - at objective {s.CartObjectives[i]}", rows[i].PointCost.ToString(),
                () => { s.RemoveRecruit(row); if (_cartRow >= row) _cartRow = -1; Rebuild(); },
                () => { _tab = Tab.Recruit; _cartRow = row; _draft = null; Rebuild(); }, "CartRecruit" + i));
        }
        foreach (var e in s.PendingUpgrades)
        {
            var edit = e;
            lines.AddChild(CartLine($"Upgrade {e.Info.Name}", "", HordeShopSession.Signed(s.PriceOf(e)),
                () => { s.Reset(edit); Rebuild(); }, () => { _tab = Tab.Upgrade; _upgradeUnit = edit.Info.UnitId; Rebuild(); }, ForgeWidgets.SafeName("CartUp_" + e.Info.Name)));
        }
        foreach (var r in s.Reinforcing)
        {
            Guid id = r.UnitId;
            lines.AddChild(CartLine($"Reinforce {r.Name}", r.ReinforceRestores ?? "", r.ReinforceCost.ToString(),
                () => { s.Reinforce.Remove(id); Rebuild(); }, () => { _tab = Tab.Reinforce; Rebuild(); }, ForgeWidgets.SafeName("CartRf_" + r.Name)));
        }
        if (s.Items == 0) lines.AddChild(HudKit.Text("Empty.", 14, HudKit.Dim, HudKit.Semi));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        lines.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(lines);
        col.AddChild(scroll);
        col.AddChild(Sum("In cart", HordeShopSession.Signed(s.Total), HudKit.Fg));
        col.AddChild(Sum("Left after buying", s.Left.ToString(), s.Left < 0 ? Palette.WoundsRed : Palette.Lime));
        if (s.Left < 0) col.AddChild(ForgeWidgets.Wrapped($"{-s.Left} pts too many - take something out.", 14, Palette.WoundsRed));
        else col.AddChild(ForgeWidgets.Wrapped("Points you don't spend are kept for the next shop.", 13, HudKit.Dim));
        if (s.Cart.List.Units.Count > 0)
            col.AddChild(ForgeWidgets.Wrapped($"After BUY you place each recruit on the table, fully inside the {_d.Request.ShopRangeInches:0}\" ring of the objective you picked.", 13, HudKit.Dim));
        col.AddChild(BuyButton("ShopBuy").Also(b => b.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return col;
    }

    private static Control CartLine(string title, string sub, string cost, Action remove, Action open, string name)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var t = new Button { Name = name, Flat = true, Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None,
            Text = sub.Length > 0 ? $"{title}\n{sub}" : title, TooltipText = "Click to see it.", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(120, 0) };
        t.AddThemeFontSizeOverride("font_size", 14);
        t.Pressed += open;
        row.AddChild(t);
        row.AddChild(HudKit.Text(cost, 17, Palette.Lime, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        var x = new Button { Name = name + "_X", Text = "X", FocusMode = FocusModeEnum.None, TooltipText = "Take it out of the cart.", CustomMinimumSize = new Vector2(30, 30), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        x.AddThemeColorOverride("font_color", Palette.WoundsRed);
        x.Pressed += remove;
        row.AddChild(x);
        return row;
    }

    private static Control Sum(string label, string value, Color colour)
    {
        var h = new HBoxContainer();
        h.AddChild(HudKit.Text(label, 15, HudKit.Fg, HudKit.Semi).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        h.AddChild(HudKit.Text(value, 19, colour, HudKit.Bold));
        return h;
    }

    // ================================================================== scripted runs

    /// <summary>"tab:Upgrade", "recruit:Shooter Grunts", "add", "upgrade:<unit>", "pick:Section|Option", "addup",
    /// "reinforce:<unit>", "looks", "editups", "back", "buy", "skip".</summary>
    public void Drive(string cmd)
    {
        if (_s?.Book == null) return;
        var s = _s;
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        // "#n" picks the n-th roster unit / your n-th unit, for runs that don't know the army.
        HordeShopUnit? byIndex = null;
        if (arg.StartsWith('#') && int.TryParse(arg[1..], out int nth))
        {
            byIndex = _d.Request.Upgradable.ElementAtOrDefault(nth);
            arg = verb == "recruit" ? s.Book.Units.ElementAtOrDefault(nth)?.Name ?? "" : byIndex?.Name ?? "";
        }
        switch (verb)
        {
            case "tab" when Enum.TryParse(arg, true, out Tab tab): _tab = tab; break;
            case "recruit" when s.Book.Units.FirstOrDefault(u => u.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } u:
                _draft = s.Draft(u.Id); _cartRow = -1; _tab = Tab.Recruit; break;
            case "add": AddDraft(); return;
            case "upgrade" when (byIndex ?? _d.Request.Upgradable.FirstOrDefault(x => x.Name.Equals(arg, StringComparison.OrdinalIgnoreCase))) is { } up:
                _tab = Tab.Upgrade; _upgradeUnit = up.UnitId; break;
            case "pick" when Current() is { } cur && arg.Split('|') is { Length: >= 2 } parts:
                var view = cur.Session.Sections(cur.Row).FirstOrDefault(v => v.Section.Label.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase));
                var opt = view?.Options.FirstOrDefault(o => o.Option.Label.StartsWith(parts[1], StringComparison.OrdinalIgnoreCase));
                if (view != null && opt != null) cur.Session.Choose(cur.Row, view.Section, opt.Option.Id, 1);
                break;
            case "addup" when _upgradeUnit is Guid g && s.Edits.FirstOrDefault(e => e.Info.UnitId == g) is { } ed: ed.InCart = true; break;
            case "reinforce" when (byIndex ?? _d.Request.Upgradable.FirstOrDefault(x => x.Name.Equals(arg, StringComparison.OrdinalIgnoreCase))) is { } rf:
                _tab = Tab.Reinforce; s.Reinforce.Add(rf.UnitId); break;
            case "looks": _mode = Mode.Looks; break;
            case "editups": _mode = Mode.EditUpgrades; break;
            case "back": _mode = Mode.Shop; break;
            case "buy": Buy(); return;
            case "skip": _d.Answer(HordeShopReply.Nothing); return;
        }
        Rebuild();
    }

    public bool IsReady => _s?.Book != null;

    // ================================================================== the map

    /// <summary>The table seen from above: seized objectives with their reach ring (gold edge = priority), the
    /// others hollow; your units as blue dots (faded when out of reach, lit when hovered); the enemy as red squares.</summary>
    private sealed partial class ShopMap : Control
    {
        private readonly HordeShopView _v;
        public ShopMap() : this(null!) { }
        public ShopMap(HordeShopView v) { _v = v; MouseFilter = MouseFilterEnum.Ignore; }

        public override void _Draw()
        {
            if (_v == null) return;
            BattleTable t = _v._screen.Table;
            float w = t.W, d = t.D;
            float scale = Mathf.Min(Size.X / w, Size.Y / d);
            Vector2 origin = new((Size.X - w * scale) / 2, (Size.Y - d * scale) / 2);
            Vector2 P(float x, float z) => origin + new Vector2(x, d - z) * scale;
            DrawRect(new Rect2(origin, new Vector2(w, d) * scale), new Color(0.07f, 0.1f, 0.11f));
            DrawRect(new Rect2(origin, new Vector2(w, d) * scale), HudKit.Edge, false, 1);

            var req = _v._d.Request;
            foreach (HordeShopObjective o in req.SeizedObjectives)
            {
                DrawCircle(P(o.X, o.Z), req.ShopRangeInches * scale, Palette.Lime with { A = 0.10f });
                DrawArc(P(o.X, o.Z), req.ShopRangeInches * scale, 0, Mathf.Tau, 48, Palette.Lime with { A = 0.7f }, 1);
            }
            ITableState table = _v._screen.Session.Table;
            foreach (IObjective o in table.Objectives.Objects)
            {
                var seized = req.SeizedObjectives.FirstOrDefault(s => MathF.Abs(s.X - o.Position.x) < 0.01f && MathF.Abs(s.Z - o.Position.z) < 0.01f);
                Vector2 p = P(o.Position.x, o.Position.z);
                if (seized != null) DrawCircle(p, 5, Palette.Lime);
                DrawArc(p, 5.5f, 0, Mathf.Tau, 20, o.IsHordePriority || seized?.IsPriority == true ? Palette.HeroGold : HudKit.Dim, o.IsHordePriority ? 2.5f : 1.2f);
                if (seized != null) DrawString(ThemeDB.FallbackFont, p + new Vector2(-3, 4), seized.Number.ToString(), HorizontalAlignment.Left, -1, 10, Colors.Black);
            }
            var mine = req.Upgradable.ToDictionary(u => u.UnitId);
            foreach (IUnit u in table.Units.Objects)
            {
                var alive = u.Models.Where(m => m.GetIsAlive()).ToList();
                if (alive.Count == 0) continue;
                float x = alive.Average(m => m.Position.x), z = alive.Average(m => m.Position.z);
                Vector2 p = P(x, z);
                if (mine.TryGetValue(u.ID.ID, out var info))
                {
                    bool hot = _v._hot == u.ID.ID;
                    Color c = info.NotEligibleReason == null ? HudKit.Friendly : HudKit.Friendly with { A = 0.35f };
                    if (hot) { c = Palette.Lime; DrawArc(p, 7, 0, Mathf.Tau, 16, Colors.White, 1.5f); }
                    DrawCircle(p, hot ? 5.5f : 4f, c);
                }
                else if (!_v._screen.Session.IsMine(u.PlayerID))
                    DrawRect(new Rect2(p - new Vector2(3, 3), new Vector2(6, 6)), HudKit.Enemy);
            }
        }

        public override void _Process(double delta) => QueueRedraw();
    }
}
