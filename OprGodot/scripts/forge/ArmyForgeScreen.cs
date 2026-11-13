using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib;
using FdgRaylib.Forge;
using FdgRaylib.Models3D;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Forge;

/// <summary>
/// The Army Forge (Kai, 2026-11-12, from the mockup he reviewed: Total War's army setup). Three views over one
/// <see cref="ForgeSession"/>:
/// <list type="bullet">
/// <item><b>Main</b> - top bar (Back, game, faction, army name, points limit, New / Load / Import Link / Save);
/// the clicked unit on the left (stats, weapons, rules with the book's wording on hover, upgrades taken, Edit
/// upgrades..., its models and Choose looks..., Joins unit, Combined unit, Remove); Your Army (points bar, Legal
/// badge, unit cards) over the Unit Roster in Army Forge's own category boxes. Click a card to see it,
/// double-click or + ADD to add it.</item>
/// <item><b>Looks</b> (<c>ForgeLooks.cs</c>) - every model of the unit as a slot; select one or several (Shift+click,
/// drag across, Select all) and pick one of the unit's downloaded versions for them, with a turning 3D preview
/// beside a 28mm person. Unpicked models use the first version.</item>
/// <item><b>Upgrades</b> (<c>ForgeUpgrades.cs</c>) - the unit's upgrade groups (round = pick one, square = take
/// several, steppers for counts), costs on the right, options a choice rules out greyed with why, and the unit's
/// sheet updating live with what is new in green. Done keeps it; Cancel puts it back.</item>
/// </list>
/// Rules and maths are the engine's and FdgApp's; this file only lays them out (rental drawing code).
/// </summary>
public partial class ArmyForgeScreen : Control, IBackGuard
{
    private enum View { Main, Looks, Upgrades }

    private ForgeSession? _s;
    private Task<List<BookFile>>? _loading;
    private View _view = View.Main;
    private bool _showIssues;

    private VBoxContainer _root = null!;
    private HBoxContainer _top = null!;
    private Control _body = null!;
    private Label _status = null!;

    /// <summary>True once the books are read and the screen is built.</summary>
    public bool IsReady => _s != null;

    /// <summary>Opens straight on this army file (the lobby's "Edit in Army Forge", the scripted runs).</summary>
    public string? OpenPath { get; set; }

    /// <summary>
    /// A campaign army being built (New Campaign, tabs 2 and 3; Army Campaign mockup B): the game is fixed, the points
    /// limit is the campaign treasury, the faction locks once a unit is in, and Save / Load / New give way to one
    /// USE THIS ARMY button gated by the campaign's own start rule (CampaignRules.CannotStart) - the same check the
    /// "use an army you have" path makes, so the two can never disagree.
    /// </summary>
    public sealed record CampaignBuild(FDG.EGameSystem System, string Title, FDG.Campaign.CampaignNumbers Numbers, Action<BuiltArmyFile> OnContinue);

    public CampaignBuild? Campaign { get; set; }

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);
        _root = new VBoxContainer();
        _root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(_root);
        _top = new HBoxContainer();
        _top.AddThemeConstantOverride("separation", 10);
        _root.AddChild(_top);
        _body = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddChild(_body);
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _root.AddChild(_status);

        var wait = HudKit.Text("Reading the army books...", 22, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
        wait.SetAnchorsPreset(LayoutPreset.Center);
        _body.AddChild(wait);
        _loading = ForgeSession.LoadLibraryAsync();
    }

    public override void _Process(double delta)
    {
        if (_loading is { IsCompleted: true } t)
        {
            _loading = null;
            try
            {
                _s = new ForgeSession(new List<BookFile>(t.Result));
                if (OpenPath != null) OpenFile(OpenPath);
                if (Campaign is { } build && !BeginCampaign(build)) return;
            }
            catch (Exception e) { Status("Could not read the army books: " + e.Message, Palette.WoundsRed); return; }
            Rebuild();
        }
            }

    /// <summary>Esc / Back inside Looks or Upgrades returns to the Forge; leaving with unsaved changes asks first.</summary>
    public bool AllowBack()
    {
        if (_s == null) return true;
        if (_view == View.Upgrades) { CancelUpgrades(); return false; }
        if (_view == View.Looks) { _view = View.Main; Rebuild(); return false; }
        if (Campaign != null) return true;
        if (!_s.Dirty || _s.List.Units.Count == 0) return true;
        Confirm("Leave the Army Forge?", "This list has changes that are not saved. Leave anyway and lose them?", "Leave", () => { _s.NewList(); App.I.Back(); });
        return false;
    }

    // ================================================================== layout

    private void Rebuild()
    {
        if (_s == null) return;
        foreach (Node c in _top.GetChildren()) { _top.RemoveChild(c); c.QueueFree(); }
        foreach (Node c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
        _looks = null;
        Control content = _view switch
        {
            View.Looks => BuildLooks(),
            View.Upgrades => BuildUpgrades(),
            _ => BuildMain(),
        };
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _body.AddChild(content);
    }

    private int _looksRow;
    private LooksPanel? _looks;

    private Control BuildLooks()
    {
        var s = _s!;
        string armyName = string.IsNullOrWhiteSpace(s.List.Name) ? s.Book.Name : $"{s.Book.Name} - {s.List.Name}";
        TopTitle(armyName);
        _top.AddChild(Ui.Grow());
        _top.AddChild(TopButton("Save", "Save", "Name the army and save it.", OpenSave));
        _looks = new LooksPanel(s, _looksRow, () => { _view = View.Main; Rebuild(); }, (t, c) => Status(t, c));
        return _looks;
    }

    private void TopTitle(string sub)
    {
        var back = Ui.Button("Back", App.I.Back, "Back (Esc).", "BackButton");
        back.Name = "Back";
        back.CustomMinimumSize = new Vector2(110, 44);
        _top.AddChild(back);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text("ARMY FORGE", 30, HudKit.Fg, HudKit.Exo(800)));
        t.AddChild(HudKit.Text(sub, 14, HudKit.Dim, HudKit.Semi));
        _top.AddChild(t);
    }

    /// <summary>Locks the Forge to the campaign's game. False (with a message) when that game has no books - never another game's.</summary>
    private bool BeginCampaign(CampaignBuild build)
    {
        string? slug = GameSystems.CampaignBookSlug(build.System);
        string? match = slug == null ? null : _s!.Systems.FirstOrDefault(x => GameSystems.SameSystem(x, slug));
        if (match == null)
        {
            Status($"No {FDG.GameSystemInfo.DisplayName(build.System)} army books are installed, so a campaign army can't be built here.", Palette.WoundsRed);
            Campaign = null;
            return false;
        }
        _s!.ChooseSystem(match);
        _s.NewList();
        _s.List.PointsLimit = build.Numbers.Treasury;
        return true;
    }

    /// <summary>"640 / 750 pts spent - minimum 600 - Heroes 1/1". Game text, so ASCII only.</summary>
    public static string CampaignHeader(CampaignBuild build, BuiltArmyFile compiled) =>
        $"{FDG.GameSystemInfo.DisplayName(build.System)}  -  {compiled.TotalPoints} / {build.Numbers.Treasury} pts spent  -  "
        + $"minimum {build.Numbers.MinimumSpend}  -  Heroes {compiled.Units.Count(FDG.Campaign.CampaignRules.IsHero)}/1";

    private void CampaignTop(CampaignBuild build, BuiltArmyFile compiled)
    {
        var s = _s!;
        TopTitle(build.Title + "  -  " + CampaignHeader(build, compiled));
        var books = s.BooksInSystem;
        if (s.List.Units.Count == 0)
        {
            var faction = new OptionButton { Name = "Faction", FocusMode = FocusModeEnum.None, TooltipText = "The army book to build from. Locked once a unit is in." };
            foreach (BookFile b in books) faction.AddItem(b.Name);
            faction.Selected = Math.Max(0, books.FindIndex(b => b.Name == s.Book.Name));
            faction.ItemSelected += i => { s.ChooseBook(books[(int)i]); s.List.PointsLimit = build.Numbers.Treasury; Status(""); Rebuild(); };
            _top.AddChild(faction);
        }
        else _top.AddChild(HudKit.Text(s.Book.Name, 17, HudKit.Fg, HudKit.Bold).Also(l => l.TooltipText = "Locked while the army has units - remove them to pick another faction."));
        var name = new LineEdit { Name = "ArmyName", PlaceholderText = "army name", Text = s.List.Name ?? "", CustomMinimumSize = new Vector2(220, 0) };
        name.TextChanged += txt => s.List.Name = txt;
        _top.AddChild(name);
        _top.AddChild(Ui.Grow());
        string? why = FDG.Campaign.CampaignRules.CannotStart(compiled, build.Numbers);
        var use = TopButton("UseArmy", "USE THIS ARMY", why ?? "Take this army into the campaign.", () =>
        {
            BuiltArmyFile army = s.Compile();
            if (FDG.Campaign.CampaignRules.CannotStart(army, build.Numbers) is { } no) { Status(no, Palette.WoundsRed); return; }
            if (!string.IsNullOrWhiteSpace(s.List.Name)) army.Name = s.List.Name.Trim();
            App.I.Back();      // Campaign is still set, so leaving never asks about unsaved changes
            build.OnContinue(army);
        }, "CommitButton");
        use.Disabled = why != null;
        _top.AddChild(use);
        if (why != null) Status(why, Palette.HitsYellow);
    }

    private Control BuildMain()
    {
        var s = _s!;
        BuiltArmyFile compiled = s.Compile();
        if (Campaign is { } build) CampaignTop(build, compiled);
        else MainTop(s);
        IReadOnlyList<ListIssue> issues = s.Issues(compiled);
        List<UnitFileEntry> rows = s.Rows();

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 16);

        var left = HudKit.Panel(HudKit.PanelBox(12));
        left.CustomMinimumSize = new Vector2(460, 0);
        left.AddChild(HudKit.Pad(UnitPanel(rows, issues), 14, 12, 14, 12));
        body.AddChild(left);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 12);
        right.AddChild(ArmyBox(compiled, rows, issues));
        right.AddChild(RosterBox());
        body.AddChild(right);
        return body;
    }

    private void MainTop(ForgeSession s)
    {
        TopTitle(ForgeSession.SystemName(s.System));

        var game = new OptionButton { Name = "Game", FocusMode = FocusModeEnum.None, TooltipText = "Which game. Each game has its own army books and unit stats." };
        foreach (string slug in s.Systems) game.AddItem(ForgeSession.SystemName(slug));
        game.Selected = Math.Max(0, s.Systems.IndexOf(s.System));
        game.ItemSelected += i => Guarded(() => { s.ChooseSystem(s.Systems[(int)i]); Status(""); Rebuild(); }, () => game.Selected = s.Systems.IndexOf(s.System));
        _top.AddChild(game);

        var books = s.BooksInSystem;
        var faction = new OptionButton { Name = "Faction", FocusMode = FocusModeEnum.None, TooltipText = "The army book to build from." };
        foreach (BookFile b in books) faction.AddItem(b.Name);
        faction.Selected = Math.Max(0, books.FindIndex(b => b.Name == s.Book.Name));
        faction.ItemSelected += i => Guarded(() => { s.ChooseBook(books[(int)i]); Status(""); Rebuild(); }, () => faction.Selected = books.FindIndex(b => b.Name == s.Book.Name));
        _top.AddChild(faction);

        var name = new LineEdit { Name = "ArmyName", PlaceholderText = "army name", Text = s.List.Name ?? "", CustomMinimumSize = new Vector2(220, 0) };
        name.TextChanged += txt => s.List.Name = txt;
        _top.AddChild(name);

        _top.AddChild(HudKit.Text("Limit", 15, HudKit.Dim, HudKit.Semi));
        var limit = new SpinBox { Name = "Limit", MinValue = 0, MaxValue = 20000, Step = 50, Value = s.List.PointsLimit, Suffix = "pts", CustomMinimumSize = new Vector2(120, 0), TooltipText = "The points limit for this list." };
        limit.ValueChanged += v => { s.List.PointsLimit = (int)v; Rebuild(); };
        _top.AddChild(limit);

        _top.AddChild(Ui.Grow());
        _top.AddChild(TopButton("New", "New", "Start an empty list for this faction.", () => Guarded(() => { s.NewList(); Status(""); Rebuild(); }, null)));
        _top.AddChild(TopButton("Load", "Load", "Open one of your saved armies for this game.", OpenLoad));
        _top.AddChild(TopButton("ImportLink", "Import Link", "Paste a share link from the Army Forge website.", OpenImport));
        _top.AddChild(TopButton("Save", "Save", "Name the army and save it into this game's armies folder.", OpenSave, "CommitButton"));
    }

    private static Button TopButton(string name, string text, string tip, Action a, string? variation = null)
    {
        var b = Ui.Button(text, a, tip, variation);
        b.Name = name;
        b.CustomMinimumSize = new Vector2(Math.Max(90, text.Length * 11 + 30), 40);
        return b;
    }

    // ================================================================== your army

    private Control ArmyBox(BuiltArmyFile compiled, List<UnitFileEntry> rows, IReadOnlyList<ListIssue> issues)
    {
        var s = _s!;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        int units = compiled.Units.Count;
        head.AddChild(HudKit.Text($"YOUR ARMY - {units} UNIT{(units == 1 ? "" : "S")}", 18, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        var bar = ForgeRules.PointsBar(compiled.TotalPoints, s.List.PointsLimit);
        var pb = new ProgressBar { MinValue = 0, MaxValue = 1, Value = bar.Fraction, ShowPercentage = false, CustomMinimumSize = new Vector2(260, 22), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        pb.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = bar.Over ? Palette.WoundsRed : Palette.Lime });
        pb.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.4f), BorderColor = HudKit.Edge, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        head.AddChild(pb);
        head.AddChild(HudKit.Text(bar.Label, 17, bar.Over ? Palette.WoundsRed : HudKit.Fg, HudKit.Bold));
        head.AddChild(Ui.Grow());
        string badge = ForgeRules.LegalityBadge(issues);
        bool bad = issues.Any(i => i.Severity == ListIssueSeverity.Error);
        var legal = new Button { Name = "Legal", Text = badge.ToUpperInvariant(), FocusMode = FocusModeEnum.None, Flat = true, TooltipText = issues.Count == 0 ? "Nothing wrong with this list." : "Click to see what's wrong." };
        legal.AddThemeColorOverride("font_color", issues.Count == 0 ? Palette.Lime : bad ? Palette.WoundsRed : Palette.HitsYellow);
        legal.Pressed += () => { _showIssues = !_showIssues; Rebuild(); };
        head.AddChild(legal);
        col.AddChild(head);

        if (_showIssues && issues.Count > 0)
            foreach (ListIssue i in issues)
                col.AddChild(ForgeWidgets.Wrapped(i.Message, 15, i.Severity == ListIssueSeverity.Error ? Palette.WoundsRed : Palette.HitsYellow));

        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        for (int i = 0; i < s.List.Units.Count && i < rows.Count; i++)
        {
            int idx = i;
            int partner = s.PartnerOf(i);
            string tag = partner >= 0 ? "Combined" : s.List.Units[i].JoinsUnitId != null ? "Joined" : "";
            if (issues.Any(x => x.UnitIndex == i && x.Severity == ListIssueSeverity.Error)) tag = "!";
            flow.AddChild(ForgeWidgets.Card(_s!.Book.Faction, $"Army{i}", rows[i].Name, rows[i].PointCost, tag, s.SelectedIndex == i, () => { s.SelectRow(idx); Rebuild(); }, null,
                FdgRaylib.Models3D.UnitPortraits.LookOf(s.List.Units[i].ModelLooks)));
        }
        if (s.List.Units.Count == 0)
            flow.AddChild(HudKit.Text("Empty - click a unit in the roster below, then + ADD (or double-click it).", 15, HudKit.Dim, HudKit.Semi));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 150), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        flow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(flow);
        col.AddChild(scroll);
        return HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(col, 14, 10, 14, 10));
    }

    // ================================================================== roster

    private Control RosterBox()
    {
        var s = _s!;
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(HudKit.Text($"UNIT ROSTER - {s.Book.Name.ToUpperInvariant()}", 18, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        head.AddChild(HudKit.Text("Click to see - double-click or + ADD to add", 14, HudKit.Dim, HudKit.Semi));
        col.AddChild(head);

        var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        var a = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var b = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        a.AddThemeConstantOverride("separation", 10);
        b.AddThemeConstantOverride("separation", 10);
        columns.AddChild(a);
        columns.AddChild(b);
        // Two columns, each box to the shorter one (Army Forge's boxes, Raylib's PackColumns idea).
        int ha = 0, hb = 0;
        foreach (var group in s.RosterGroups)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 6);
            box.AddChild(HudKit.Heading(group.Title, 15));
            var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 8);
            flow.AddThemeConstantOverride("v_separation", 8);
            foreach (RosterUnit u in group.Units)
            {
                string id = u.Id;
                int pts = s.RosterCards.TryGetValue(u.Id, out var card) ? card.Unit.PointCost : u.BasePointCost;
                flow.AddChild(ForgeWidgets.Card(_s!.Book.Faction, "Roster_" + u.Name, u.Name, pts, "", s.SelectedRosterId == u.Id,
                    () => { s.SelectRoster(id); Rebuild(); }, () => { s.Add(id); Rebuild(); }));
            }
            box.AddChild(flow);
            int weight = 2 + (group.Units.Count + 2) / 3;
            if (ha <= hb) { a.AddChild(box); ha += weight; } else { b.AddChild(box); hb += weight; }
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(columns);
        col.AddChild(scroll);
        var panel = HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(col, 14, 10, 14, 10));
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        return panel;
    }

    /// <summary>A unit card: picture, name, points, an optional tag. Click selects; double-click adds.</summary>
    // ================================================================== the clicked unit

    private Control UnitPanel(List<UnitFileEntry> rows, IReadOnlyList<ListIssue> issues)
    {
        var s = _s!;
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(col);
        outer.AddChild(scroll);

        if (s.SelectedIndex is int idx && idx < s.List.Units.Count)
        {
            BuilderUnit bu = s.List.Units[idx];
            var (unit, items) = s.Detail(idx);
            RosterUnit? roster = s.Roster(bu.RosterUnitId);
            Title(col, "IN YOUR ARMY", unit, roster);
            ForgeWidgets.Gear(_s!.Glossary, col, unit, items, null);

            foreach (ListIssue i in issues.Where(x => x.UnitIndex == idx))
                col.AddChild(ForgeWidgets.Wrapped(i.Message, 14, i.Severity == ListIssueSeverity.Error ? Palette.WoundsRed : Palette.HitsYellow));

            if (roster is { Sections.Count: > 0 })
            {
                col.AddChild(HudKit.Heading("Upgrades taken", 15));
                var taken = TakenLines(roster, bu);
                if (taken.Count == 0) col.AddChild(HudKit.Text("None yet.", 14, HudKit.Dim, HudKit.Semi));
                foreach (string line in taken) col.AddChild(ForgeWidgets.Wrapped(line, 14, HudKit.Fg));
                var edit = Ui.Button("Edit upgrades...", () => OpenUpgrades(idx), "Choose this unit's upgrades, with the unit sheet updating as you pick.");
                edit.Name = "EditUpgrades";
                col.AddChild(edit);
            }

            col.AddChild(HudKit.Heading("Models", 15));
            col.AddChild(ForgeWidgets.Wrapped(s.LooksSummary(idx), 14, HudKit.Dim));
            int versions = s.Versions(ForgeWidgets.Models(), unit.Name).Count;
            var looks = Ui.Button("Choose looks...", () => { _view = View.Looks; _looksRow = idx; Rebuild(); },
                versions == 0 ? "No models downloaded for this unit yet - you can still open it to see." : $"Pick which of the {versions} downloaded versions each model uses on the table.");
            looks.Name = "ChooseLooks";
            col.AddChild(looks);
            if (versions == 0) col.AddChild(ForgeWidgets.Wrapped("No 3D models downloaded for this unit (Asset Lab > Download models).", 13, HudKit.Dim));

            if (ForceOrgValidator.IsHero(unit))
            {
                if (!s.HeroesMayJoin)
                {
                    bu.JoinsUnitId = null;
                    col.AddChild(ForgeWidgets.Wrapped("Heroes can't join units in this game.", 14, Palette.HitsYellow));
                }
                else
                {
                    var hosts = s.HostsFor(idx, rows);
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 8);
                    row.AddChild(HudKit.Text("Joins unit:", 15, HudKit.Fg, HudKit.Semi));
                    var join = new OptionButton { Name = "JoinsUnit", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    join.AddItem("none - deploys on its own");
                    foreach (int h in hosts) join.AddItem(ForgeSession.RowLabel(rows, h));
                    int sel = bu.JoinsUnitId == null ? 0 : hosts.FindIndex(h => s.List.Units[h].Id == bu.JoinsUnitId) + 1;
                    join.Selected = Math.Max(0, sel);
                    join.ItemSelected += i => { s.Join(idx, i == 0 ? null : hosts[(int)i - 1]); Rebuild(); };
                    row.AddChild(join);
                    col.AddChild(row);
                }
            }
            else if (bu.Id != null)
            {
                var joiners = s.List.Units.Select((h, i) => (h, i)).Where(p => p.i != idx && p.h.JoinsUnitId == bu.Id && p.i < rows.Count).Select(p => rows[p.i].Name).ToList();
                if (joiners.Count > 0) col.AddChild(ForgeWidgets.Wrapped("Joined by: " + string.Join(", ", joiners), 14, HudKit.Dim));
            }

            if (unit.ModelCount > 1 && !ForceOrgValidator.IsHero(unit) && (s.CanCombine(idx) || s.PartnerOf(idx) >= 0))
            {
                var comb = new CheckBox { Name = "Combined", Text = "Combined unit", ButtonPressed = s.PartnerOf(idx) >= 0, FocusMode = FocusModeEnum.None,
                    TooltipText = "Two copies of this unit fight as one big unit (each pays for its own upgrades)." };
                comb.Toggled += on => { s.SetCombined(idx, on); Rebuild(); };
                col.AddChild(comb);
            }

            var remove = Ui.Button("Remove from army", () => { s.Remove(idx); Rebuild(); }, "Take this unit out of the list.", "BackButton");
            remove.Name = "Remove";
            remove.CustomMinimumSize = new Vector2(0, 42);
            outer.AddChild(remove);
        }
        else if (s.SelectedRosterId is { } rid && s.Roster(rid) is { } roster)
        {
            var (card, items) = s.RosterCards[rid];
            Title(col, "FROM THE ROSTER", card, roster);
            ForgeWidgets.Gear(_s!.Glossary, col, card, items, null);
            col.AddChild(HudKit.Text("Base: " + ForgeRules.BaseSummary(roster.Base), 14, HudKit.Dim, HudKit.Semi));
            if (ForgeRules.IsCaster(roster) && s.Book.Spells.Count > 0) Spells(col);
            if (roster.Sections.Count > 0)
            {
                col.AddChild(HudKit.Heading("Upgrades it can take", 15));
                foreach (UpgradeSection sec in roster.Sections)
                {
                    col.AddChild(HudKit.Text(sec.Label, 14, Palette.RuleGold, HudKit.Semi));
                    foreach (UpgradeOption o in sec.Options)
                        col.AddChild(ForgeWidgets.Wrapped("   " + ForgeRules.OptionSummary(o), 13, HudKit.Dim));
                }
            }
            var add = Ui.Button("+ ADD TO ARMY", () => { s.Add(rid); Rebuild(); }, "Put this unit in your army (or double-click its card).", "CommitButton");
            add.Name = "Add";
            add.CustomMinimumSize = new Vector2(0, 46);
            outer.AddChild(add);
        }
        else
        {
            col.AddChild(ForgeWidgets.Wrapped("Click a unit card to see it here.", 16, HudKit.Dim));
        }
        return outer;
    }

    private void Title(VBoxContainer col, string where, UnitFileEntry unit, RosterUnit? roster)
    {
        var s = _s!;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(HudKit.Portrait(s.Book.Faction, unit.Name, "person", 76, HudKit.Friendly, look: FdgRaylib.Models3D.UnitPortraits.LookOf(unit.ModelLooks)));
        var t = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        t.AddThemeConstantOverride("separation", -2);
        t.AddChild(HudKit.Text(where, 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        var n = ForgeWidgets.Wrapped(unit.Name.ToUpperInvariant(), 22, HudKit.Fg);
        n.AddThemeFontOverride("font", HudKit.Bold);
        t.AddChild(n);
        string cat = roster == null ? "" : s.CategoryOf(roster) + " - ";
        t.AddChild(HudKit.Text($"{cat}{unit.ModelCount} model{(unit.ModelCount == 1 ? "" : "s")}", 14, HudKit.Dim, HudKit.Semi));
        t.AddChild(HudKit.Text($"{unit.PointCost} pts", 18, Palette.PointsBlue, HudKit.Bold));
        row.AddChild(t);
        col.AddChild(row);
    }

    private void Spells(VBoxContainer col)
    {
        col.AddChild(HudKit.Heading("Spells", 15));
        foreach (var sp in _s!.Book.Spells)
            col.AddChild(ForgeWidgets.Wrapped($"{sp.Name} ({sp.Threshold}): {FDG.Stages.SpellText.Describe(sp)}", 13, HudKit.Dim));
    }

    /// <summary>"Replace one Bio-Ravager: Bio-Plasma (+10)" for every choice, in the book's order.</summary>
    private static List<string> TakenLines(RosterUnit roster, BuilderUnit bu)
    {
        var lines = new List<string>();
        foreach (UpgradeSection sec in roster.Sections)
            foreach (UpgradeChoice c in bu.Choices.Where(c => c.SectionId == sec.Id))
                if (sec.Options.FirstOrDefault(o => o.Id == c.OptionId) is { } o)
                    lines.Add($"{sec.Label}: {(c.Count > 1 ? c.Count + "x " : "")}{o.Label} ({ForgeRules.CostText(o.Cost * Math.Max(1, c.Count))})");
        return lines;
    }

    // ================================================================== small things

    private void Status(string text, Color? colour = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour ?? HudKit.Dim);
    }

    /// <summary>Runs a change that throws the list away - after a yes when it has unsaved units.</summary>
    private void Guarded(Action change, Action? undoUi)
    {
        if (_s == null) return;
        if (!_s.Dirty || _s.List.Units.Count == 0) { change(); return; }
        Confirm("Start a new list?", "This throws away the list on screen, which has changes that are not saved.", "Throw it away", change, undoUi);
    }

    private void Confirm(string title, string text, string ok, Action yes, Action? no = null)
    {
        var d = new ConfirmationDialog { Title = title, DialogText = text, OkButtonText = ok, CancelButtonText = "Cancel" };
        d.Confirmed += () => { d.QueueFree(); yes(); };
        d.Canceled += () => { d.QueueFree(); no?.Invoke(); };
        AddChild(d);
        d.PopupCentered();
    }

    // ================================================================== save / load / import

    private void OpenSave()
    {
        var s = _s!;
        var d = new ConfirmationDialog { Title = "Name your army", OkButtonText = "Save", CancelButtonText = "Cancel" };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        var box = new LineEdit { Name = "SaveName", Text = s.SuggestedName(), CustomMinimumSize = new Vector2(420, 0) };
        col.AddChild(box);
        var where = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        col.AddChild(where);
        void Where()
        {
            string? p = s.PathFor(box.Text);
            where.Text = p == null ? "No armies folder found." : (File.Exists(p) ? "Replaces " : "Saves as ") + Path.GetFileName(p) + $" in armies\\{ArmyPaths.FolderNameFor(s.GameSystemEnum)}";
        }
        box.TextChanged += _ => Where();
        Where();
        d.AddChild(col);
        void Do()
        {
            if (box.Text.Trim().Length == 0) return;
            try
            {
                string path = s.Save(box.Text.Trim());
                Status($"Saved {Path.GetFileName(path)} - it's in the {ForgeSession.SystemName(s.System)} lobby's army list.", Palette.Lime);
            }
            catch (Exception e) { Status("SAVE FAILED - " + e.Message, Palette.WoundsRed); }
            d.QueueFree();
            Rebuild();
        }
        d.Confirmed += Do;
        box.TextSubmitted += _ => { d.Hide(); Do(); };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
        box.GrabFocus();
        box.SelectAll();
    }

    private void OpenLoad()
    {
        var s = _s!;
        var armies = s.SavedArmies();
        var popup = new PopupPanel { Size = new Vector2I(700, 560) };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        popup.AddChild(col);
        col.AddChild(HudKit.Text($"LOAD AN ARMY - {ForgeSession.SystemName(s.System).ToUpperInvariant()}", 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        col.AddChild(HudKit.Text($"From armies\\{ArmyPaths.FolderNameFor(s.GameSystemEnum)}, newest first.", 14, HudKit.Dim, HudKit.Semi));
        var list = new ItemList { Name = "Armies", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 400) };
        foreach (var a in armies) list.AddItem($"{a.Name}   -   {a.Faction}, {a.Points} pts");
        if (armies.Count == 0) list.AddItem("(no saved armies for this game yet)");
        col.AddChild(list);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Ui.Button("Cancel", () => popup.QueueFree(), variation: "BackButton"));
        void Open()
        {
            int[] sel = list.GetSelectedItems();
            if (sel.Length == 0 || sel[0] >= armies.Count) return;
            popup.QueueFree();
            Guarded(() => { OpenFile(armies[sel[0]].Path); Rebuild(); }, null);
        }
        buttons.AddChild(Ui.Button("Open", Open, variation: "CommitButton"));
        list.ItemActivated += _ => Open();
        col.AddChild(buttons);
        AddChild(popup);
        popup.PopupCentered();
    }

    /// <summary>Opens an army file for editing (after a yes when rebuilding it would change it).</summary>
    private void OpenFile(string path)
    {
        var s = _s!;
        var r = ForgeSession.Read(path);
        string file = Path.GetFileName(path);
        if (r.Problem != null) { Status($"{file} was not opened: {r.Problem}", Palette.WoundsRed); App.I.Message("Can't open that army", r.Problem); return; }
        void Take() { s.Adopt(r.File!); _view = View.Main; Status($"Opened {file}", Palette.Lime); Rebuild(); }
        if (r.DriftNote != null) Confirm("Open for editing?", r.DriftNote, "Open it", Take);
        else Take();
    }

    private void OpenImport()
    {
        ImportArmyPopup.Open(this, p =>
        {
            p.Saved = (path, _) => { p.QueueFree(); Guarded(() => { OpenFile(path); Rebuild(); }, null); };
        });
    }

    // ================================================================== scripted runs

    /// <summary>"game:Grimdark Future", "book:Alien Hives", "add:Shooter Grunts", "row:0", "upgrades", "pick:Section|Option",
    /// "count:Section|Option|n", "done", "cancel", "looks", "sel:0,2", "selall", "version:1", "reset", "save:Name", "issues".</summary>
    public void Drive(string cmd)
    {
        if (_s == null) return;
        var s = _s;
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        switch (verb)
        {
            case "game" when s.Systems.FirstOrDefault(x => ForgeSession.SystemName(x).Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } sys:
                s.ChooseSystem(sys); break;
            case "book" when s.BooksInSystem.FirstOrDefault(b => b.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } book:
                s.ChooseBook(book); break;
            case "add" when s.Book.Units.FirstOrDefault(u => u.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } u:
                s.Add(u.Id); break;
            case "see" when s.Book.Units.FirstOrDefault(u => u.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) is { } u2:
                s.SelectRoster(u2.Id); break;
            case "row" when int.TryParse(arg, out int r): s.SelectRow(r); break;
            case "combine": if (s.SelectedIndex is int ci) s.SetCombined(ci, true); break;
            case "upgrades": if (s.SelectedIndex is int ui) { OpenUpgrades(ui); return; } break;
            case "pick" or "count":
                if (_view == View.Upgrades && arg.Split('|') is { Length: >= 2 } parts)
                {
                    var view = s.Sections(_upgradeRow).FirstOrDefault(v => v.Section.Label.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase));
                    var opt = view?.Options.FirstOrDefault(o => o.Option.Label.StartsWith(parts[1], StringComparison.OrdinalIgnoreCase));
                    if (view != null && opt != null)
                        s.Choose(_upgradeRow, view.Section, opt.Option.Id, verb == "count" && parts.Length > 2 && int.TryParse(parts[2], out int n) ? n : opt.Chosen && view.Control != ForgeRules.EControl.PickOne ? 0 : 1);
                }
                break;
            case "done": if (_view == View.Upgrades) { _view = View.Main; } else if (_view == View.Looks) _view = View.Main; break;
            case "cancel": if (_view == View.Upgrades) { CancelUpgrades(); return; } break;
            case "looks": if (s.SelectedIndex is int li) { _view = View.Looks; _looksRow = li; } break;
            case "sel": _looks?.Select(arg.Split(';', ',').Select(p => int.TryParse(p, out int k) ? k : -1).Where(k => k >= 0)); return;
            case "selall": _looks?.SelectAll(); _looks?.Refresh(); return;
            case "version" when int.TryParse(arg, out int v): _looks?.PickVersion(v); return;
            case "reset": _looks?.Reset(); return;
            case "save":
                try { Status("Saved " + Path.GetFileName(s.Save(arg)), Palette.Lime); } catch (Exception e) { Status("SAVE FAILED - " + e.Message, Palette.WoundsRed); }
                break;
            case "open": OpenFile(arg); return;
            case "issues": _showIssues = true; break;
        }
        Rebuild();
    }
}
