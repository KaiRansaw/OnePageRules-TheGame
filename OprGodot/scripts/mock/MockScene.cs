using FdgRaylib;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Mock;

/// <summary>
/// The style mockups Kai asked for on 2026-11-09 ("Nock it up"): the main menu and two in-game HUD layouts
/// in the tactical (XCOM / Halo Wars) look, over a stand-in 3D table. Nothing here is wired to the engine -
/// it is for picking a look. Unit names and stats are read from the bundled AoF books.
/// </summary>
public partial class MockScene : Node
{
    public enum ELayout { Menu, HudColumn, HudXcom, HudXcomCards }
    [Export] public ELayout Layout { get; set; } = ELayout.HudXcom;

    private MockTable _table = null!;
    private Control _ui = null!;

    public override void _Ready()
    {
        _table = new MockTable { ShowLabels = Layout != ELayout.Menu };
        AddChild(_table);
        var layer = new CanvasLayer();
        AddChild(layer);
        _ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_ui);
        CallDeferred(Layout switch { ELayout.Menu => nameof(BuildMenu), ELayout.HudColumn => nameof(BuildColumn), ELayout.HudXcomCards => nameof(BuildXcomCards), _ => nameof(BuildXcom) });
    }

    // ================================================================== shared pieces
    private MockTable.Placed Active => _table.Find("Marksmen");
    private MockTable.Placed Target => _table.Find("Iron Shooters");

    private void TableDressing()
    {
        // The active unit: lime ring, its Advance range, and the line of fire to the target under the cursor.
        var a = Active;
        _table.Area(a.Centre, 6 + MockTable.Footprint(a), HudKit.Lime);
        _table.Ring(a.Centre, MockTable.Footprint(a), HudKit.Lime, 0.12f, 3f);
        _table.Ring(Target.Centre, MockTable.Footprint(Target), HudKit.Enemy, 0.12f, 3f);
        _table.Beam(a.Centre, Target.Centre, HudKit.Enemy);
    }

    private static Control MockTag()
    {
        var l = HudKit.Text("MOCKUP  -  sample units from the AoF Human Empire and Orcs books  -  nothing is wired up", 14, HudKit.Dim with { A = 0.8f }, HudKit.Semi);
        return l;
    }

    private static string Stat(int v) => $"{v}+";

    private static string Range(SampleWeapon w) => w.Range > 0 ? $"{w.Range}\"" : "-";

    /// <summary>The selected unit: portrait, name, stats, weapons and rules.</summary>
    private Control UnitCard(MockTable.Placed p, float width, bool showRulesText)
    {
        SampleUnit u = p.Unit;
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        col.AddThemeConstantOverride("separation", 8);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        var portrait = HudKit.Panel(new ChamferBox { Fill = new Color(HudKit.Friendly, 0.10f), Border = new Color(HudKit.Friendly, 0.7f), Cut = 10, ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4 },
            HudKit.Icon("person", 64, HudKit.Friendly with { A = 0.85f }));
        top.AddChild(portrait);
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", -4);
        names.AddChild(HudKit.Text(u.Name.ToUpperInvariant(), 32, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1)));
        names.AddChild(HudKit.Text($"Human Empire  -  {u.Points} pts  -  {u.Models} models", 17, HudKit.Dim, HudKit.Semi));
        var state = new HBoxContainer();
        state.AddThemeConstantOverride("separation", 6);
        state.AddChild(HudKit.Chip("READY", HudKit.Lime, true, 14));
        state.AddChild(HudKit.Chip("NOT SHAKEN", HudKit.Dim, false, 14));
        names.AddChild(HudKit.Gap(0, 6));
        names.AddChild(state);
        top.AddChild(names);
        col.AddChild(top);

        // Big stat boxes, the numbers a player reads first.
        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 8);
        stats.AddChild(StatBox("QUALITY", Stat(u.Quality), "targeting"));
        stats.AddChild(StatBox("DEFENSE", Stat(u.Defense), "shield"));
        stats.AddChild(StatBox("MODELS", $"{p.Models.Count}/{u.Models}", "rally-the-troops"));
        col.AddChild(stats);

        col.AddChild(HudKit.Heading("Weapons", 15));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (string h in new[] { "", "RANGE", "ATK", "AP" })
            grid.AddChild(HudKit.Text(h, 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        foreach (SampleWeapon w in u.Weapons)
        {
            var name = HudKit.Text(w.Name, 18, HudKit.Fg, HudKit.Semi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(name);
            grid.AddChild(HudKit.Text(Range(w), 18, w.Range > 0 ? HudKit.Lime : HudKit.Dim, HudKit.Bold));
            grid.AddChild(HudKit.Text($"A{w.Attacks}", 18, HudKit.Fg, HudKit.Bold));
            grid.AddChild(HudKit.Text(w.Ap > 0 ? w.Ap.ToString() : "-", 18, w.Ap > 0 ? Palette.WoundsRed : HudKit.Dim, HudKit.Bold));
        }
        col.AddChild(grid);

        col.AddChild(HudKit.Heading("Special rules", 15));
        var rules = new HFlowContainer();
        rules.AddThemeConstantOverride("h_separation", 6);
        foreach (string r in u.Rules) rules.AddChild(HudKit.Chip(r, Palette.RuleGold, false, 15));
        col.AddChild(rules);
        if (showRulesText)
        {
            var hint = HudKit.Text("Hover a rule for its book wording.", 14, HudKit.Dim, HudKit.Medium);
            col.AddChild(hint);
        }
        return HudKit.Panel(HudKit.HeaderBox(), col);
    }

    private static Control StatBox(string label, string value, string icon)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(HudKit.Icon(icon, 26, HudKit.Teal));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", -6);
        v.AddChild(HudKit.Text(label, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        v.AddChild(HudKit.Text(value, 28, HudKit.Fg, HudKit.Bold));
        row.AddChild(v);
        var p = HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.3f), Border = HudKit.Edge with { A = 0.6f }, Cut = 7, ContentMarginLeft = 10, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 }, row);
        p.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return p;
    }

    private static readonly (string Icon, string Name, string Key, string Says, bool Off)[] Actions =
    {
        ("crosshair", "Hold", "1", "HOLD - stay in place, then may shoot.", false),
        ("walk", "Advance", "2", "ADVANCE - move up to 6\", then may shoot.", false),
        ("sprint", "Rush", "3", "RUSH - move up to 12\", no shooting.", false),
        ("crossed-swords", "Charge", "4", "CHARGE - no enemy within 12\".", true),
    };

    /// <summary>Round, and who holds the objectives.</summary>
    private static Control RoundBlock(bool compact)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var round = new VBoxContainer();
        round.AddThemeConstantOverride("separation", -8);
        round.AddChild(HudKit.Text("ROUND", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 3)));
        var num = new HBoxContainer();
        num.AddChild(HudKit.Text("2", 44, HudKit.Lime, HudKit.Bold));
        num.AddChild(HudKit.Text(" / 4", 24, HudKit.Dim, HudKit.Bold));
        round.AddChild(num);
        row.AddChild(round);
        row.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 44), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });

        var obj = new VBoxContainer();
        obj.AddThemeConstantOverride("separation", 0);
        obj.AddChild(HudKit.Text("OBJECTIVES HELD", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 3)));
        var held = new HBoxContainer();
        held.AddThemeConstantOverride("separation", 14);
        foreach (var (who, n, c) in new[] { ("YOU", 1, HudKit.Friendly), ("NEUTRAL", 1, new Color(0.85f, 0.88f, 0.9f)), ("ORCS", 1, HudKit.Enemy) })
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 5);
            h.AddChild(HudKit.Icon("flag-objective", 22, c));
            h.AddChild(HudKit.Text(n.ToString(), 24, c, HudKit.Bold));
            if (!compact) h.AddChild(HudKit.Text(who, 14, HudKit.Dim, HudKit.Semi));
            held.AddChild(h);
        }
        obj.AddChild(held);
        row.AddChild(obj);
        row.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 44), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var turn = new VBoxContainer();
        turn.AddThemeConstantOverride("separation", -2);
        turn.AddChild(HudKit.Text("YOUR TURN", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 3)));
        turn.AddChild(HudKit.Text("3 units left to activate", 18, HudKit.Fg, HudKit.Semi));
        row.AddChild(turn);
        return HudKit.Panel(new ChamferBox { Fill = HudKit.Glass, Border = HudKit.Edge, Cut = 14, Accent = HudKit.Lime, AccentLength = 70, ContentMarginLeft = 18, ContentMarginRight = 20, ContentMarginTop = 8, ContentMarginBottom = 10 }, row);
    }

    private static readonly (string Text, Color Colour)[] Report =
    {
        ("Orcs - Iron Tusk-Beast Riders Rush 12\".", HudKit.Dim),
        ("Empire Artillery shoots Iron Warriors: 6 attacks, 3 hits, 1 wound - 1 model killed.", HudKit.Fg),
        ("Iron Warriors pass their Morale test (rolled 5).", HudKit.Fg),
        ("Orcs - Iron Shooters Hold and shoot Infantrymen: 5 attacks, 1 hit, saved.", HudKit.Fg),
        ("Your turn - pick a unit to activate.", HudKit.Lime),
    };

    private static Control ReportLines(int lines, int size)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 3);
        foreach (var (t, c) in Report.Skip(Report.Length - lines))
        {
            var l = HudKit.Text(t, size, c, HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            v.AddChild(l);
        }
        return v;
    }

    /// <summary>The shot forecast for the unit under the cursor (XCOM's hit-chance panel).</summary>
    private Control Forecast()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(HudKit.Icon("targeting", 22, HudKit.Enemy));
        head.AddChild(HudKit.Text("IRON SHOOTERS", 20, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1)));
        v.AddChild(head);
        v.AddChild(HudKit.Text("16\" away  -  in cover (forest)", 15, HudKit.Dim, HudKit.Semi));
        var nums = new HBoxContainer();
        nums.AddThemeConstantOverride("separation", 14);
        foreach (var (l, val, c) in new[] { ("HIT ON", "5+", Palette.HitsYellow), ("SAVE ON", "5+", HudKit.Fg), ("EXPECT", "~1 wound", Palette.WoundsRed) })
        {
            var b = new VBoxContainer();
            b.AddThemeConstantOverride("separation", -6);
            b.AddChild(HudKit.Text(l, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
            b.AddChild(HudKit.Text(val, 24, c, HudKit.Bold));
            nums.AddChild(b);
        }
        v.AddChild(nums);
        return HudKit.Panel(new ChamferBox { Fill = HudKit.Glass, Border = new Color(HudKit.Enemy, 0.8f), Cut = 10, Glow = HudKit.Enemy with { A = 0.35f }, ContentMarginLeft = 14, ContentMarginRight = 14 }, v);
    }

    // ================================================================== Main menu
    private void BuildMenu()
    {
        _table.Look(new Vector3(-44, 13, 40), new Vector3(2, 0, -4), 36);
        _table.Ring(_table.Find("Knights").Centre, MockTable.Footprint(_table.Find("Knights")), HudKit.Lime, 0.1f, 3f);

        // Darken the left of the picture so the menu reads, and the bottom for the tools.
        var shade = new TextureRect { Texture = Gradient(new Color(0.01f, 0.015f, 0.02f, 0.97f), new Color(0.01f, 0.015f, 0.02f, 0f), true), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.AnchorRight = 0.72f; shade.AnchorBottom = 1;
        _ui.AddChild(shade);
        var floor = new TextureRect { Texture = Gradient(new Color(0.01f, 0.015f, 0.02f, 0f), new Color(0.01f, 0.015f, 0.02f, 0.9f), false), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
        floor.AnchorTop = 0.62f; floor.AnchorRight = 1; floor.AnchorBottom = 1;
        _ui.AddChild(floor);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        col.AddChild(HudKit.Text("ONEPAGERULES", 26, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 12)));
        col.AddChild(HudKit.Text("THE GAME", 104, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(HudKit.Gap(0, 6));
        var rule = new HBoxContainer();
        rule.AddThemeConstantOverride("separation", 6);
        rule.AddChild(new ColorRect { Color = HudKit.Lime, CustomMinimumSize = new Vector2(60, 3) });
        rule.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(420, 1), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        col.AddChild(rule);
        col.AddChild(HudKit.Gap(0, 34));
        col.AddChild(HudKit.Heading("Choose a game", 18));
        col.AddChild(HudKit.Gap(0, 12));
        col.AddChild(GameTile("GrimdarkFuture", "GRIMDARK FUTURE", "Grimdark Future  -  GF: Firefight  -  GF: Star Quest", "fire-ray", false));
        col.AddChild(HudKit.Gap(0, 14));
        col.AddChild(GameTile("AgeOfFantasy", "AGE OF FANTASY", "Age of Fantasy  -  AoF: Skirmish  -  AoF: Quest", "sword-clash", true));
        HudKit.Place(_ui, col, Control.LayoutPreset.CenterLeft, new Vector2(110, -40));

        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 10);
        foreach (var (n, icon, label) in new[] { ("LoadGame", "open-book", "Load Game"), ("JoinGame", "radar-sweep", "Join a Game"), ("ArmyForge", "rally-the-troops", "Army Forge"), ("MapMaker", "position-marker", "Map Maker"), ("Settings", "cog", "Settings"), ("CloseGame", "stop-sign", "Quit") })
        {
            var b = HudKit.ActionButton(n, icon, label, "", n == "CloseGame" ? Palette.WoundsRed : HudKit.Teal, 96);
            b.CustomMinimumSize = new Vector2(118, 96);
            tools.AddChild(b);
        }
        HudKit.Place(_ui, tools, Control.LayoutPreset.BottomLeft, new Vector2(110, -64));

        var build = HudKit.Text($"Godot preview  -  {AppVersion.BuildLabel}", 15, HudKit.Dim, HudKit.Semi);
        HudKit.Place(_ui, build, Control.LayoutPreset.BottomRight, new Vector2(-28, -18));
        HudKit.Place(_ui, MockTag(), Control.LayoutPreset.TopRight, new Vector2(-28, 18));
    }

    private static Button GameTile(string name, string title, string games, string icon, bool hot)
    {
        var b = new Button { Name = name, CustomMinimumSize = new Vector2(620, 118), FocusMode = Control.FocusModeEnum.None };
        var box = new ChamferBox { Fill = new Color(0.05f, 0.08f, 0.09f, 0.82f), Border = HudKit.Edge, Cut = 18, Accent = HudKit.Lime, AccentLength = 80 };
        if (hot) box = box.With(x => { x.Border = HudKit.EdgeHot; x.Glow = HudKit.EdgeHot with { A = 0.45f }; x.LeftBar = HudKit.Lime; x.Fill = new Color(0.07f, 0.12f, 0.12f, 0.9f); });
        HudKit.StyleButton(b, box, HudKit.Fg);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = 26; row.OffsetRight = -22;
        row.AddThemeConstantOverride("separation", 22);
        var ic = HudKit.Icon(icon, 64, hot ? HudKit.Lime : HudKit.Teal);
        ic.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(ic);
        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", -4);
        v.AddChild(HudKit.Text(title, 44, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 3)));
        v.AddChild(HudKit.Text(games, 19, HudKit.Dim, HudKit.Semi));
        row.AddChild(v);
        var arrow = HudKit.Text(">", 40, hot ? HudKit.Lime : HudKit.Edge, HudKit.Bold);
        arrow.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(arrow);
        b.AddChild(row);
        return b;
    }

    private static GradientTexture2D Gradient(Color from, Color to, bool horizontal) => new()
    {
        Gradient = new Gradient { Colors = new[] { from, to }, Offsets = new[] { horizontal ? 0.35f : 0f, 1f } },
        FillFrom = Vector2.Zero, FillTo = horizontal ? new Vector2(1, 0) : new Vector2(0, 1), Width = 256, Height = 256,
    };

    // ================================================================== Layout A: the right column
    private void BuildColumn()
    {
        _table.Look(new Vector3(-8, 52, 46), new Vector3(-6, 0, 3), 42);
        TableDressing();

        const float colW = 470;
        var column = new PanelContainer();
        column.AddThemeStyleboxOverride("panel", new ChamferBox { Fill = new Color(0.035f, 0.055f, 0.065f, 0.94f), Border = HudKit.Edge, Cut = 0, Corners = ChamferBox.ECorners.None, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 14, ContentMarginBottom = 14 });
        column.AnchorLeft = 1; column.AnchorRight = 1; column.AnchorBottom = 1;
        column.OffsetLeft = -colW;
        _ui.AddChild(column);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        v.AddChild(HudKit.Heading("Active unit"));
        v.AddChild(UnitCard(Active, colW - 60, false));
        v.AddChild(HudKit.Heading("Actions"));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        foreach (var a in Actions)
            grid.AddChild(HudKit.ActionButton(a.Name, a.Icon, a.Name, a.Key, HudKit.Teal, 100, a.Name == "Advance", a.Off));
        v.AddChild(grid);
        v.AddChild(HudKit.Text(Actions[1].Says, 16, HudKit.Lime, HudKit.Semi));

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        foreach (var (t, on) in new[] { ("BATTLE REPORT", true), ("MISSION", false), ("GAME MASTER", false) })
        {
            var b = new Button { Text = t, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            HudKit.StyleButton(b, new ChamferBox { Fill = on ? new Color(HudKit.Lime, 0.14f) : HudKit.GlassLight, Border = on ? HudKit.Lime : HudKit.Edge, Cut = 7, Corners = ChamferBox.ECorners.TopRight, ContentMarginTop = 4, ContentMarginBottom = 4 }, on ? HudKit.Lime : HudKit.Dim, 14);
            tabs.AddChild(b);
        }
        v.AddChild(tabs);
        var report = HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.3f), Border = HudKit.Edge with { A = 0.5f }, Cut = 0, Corners = ChamferBox.ECorners.None }, ReportLines(5, 16));
        report.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        v.AddChild(report);
        column.AddChild(v);

        // Round strip over the table, centred on the part of the screen the column leaves.
        var strip = RoundBlock(false);
        HudKit.Place(_ui, strip, Control.LayoutPreset.CenterTop, new Vector2(-colW / 2, 16));
        var fc = Forecast();
        HudKit.Place(_ui, fc, Control.LayoutPreset.TopLeft, new Vector2(0, 0));
        PinNear(fc, Target.Centre, new Vector2(-470, -170));
        HudKit.Place(_ui, MockTag(), Control.LayoutPreset.BottomLeft, new Vector2(20, -14));
    }

    /// <summary>Puts a panel beside a table point (the hover panel that follows the unit under the cursor).</summary>
    private void PinNear(Control c, Vector3 at, Vector2 offset)
    {
        Vector2 p = _table.ToScreen(at) / (GetViewport().GetVisibleRect().Size / _ui.Size);
        c.Position = p + offset;
    }

    // ================================================================== Layout B: XCOM
    private void BuildXcom()
    {
        _table.Look(new Vector3(-6, 46, 44), new Vector3(-4, 0, Layout == ELayout.HudXcomCards ? 11 : 4), 44);
        TableDressing();

        // Top left: round, objectives, whose turn.
        HudKit.Place(_ui, RoundBlock(false), Control.LayoutPreset.TopLeft, new Vector2(24, 20));

        // Top right: the panels that open over the table.
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 8);
        foreach (var (n, icon, label, key) in new[] { ("Report", "scroll-unfurled", "Report", "R"), ("Mission", "flag-objective", "Mission", "M"), ("Armies", "stack", "Armies", "L"), ("Menu", "hamburger-menu", "Menu", "Esc") })
            menu.AddChild(HudKit.ActionButton(n, icon, label, key, HudKit.Teal, 70));
        HudKit.Place(_ui, menu, Control.LayoutPreset.TopRight, new Vector2(-24, 20));

        // Top centre: your units, which have gone this round (the card strip replaces this in B2).
        bool cards = Layout == ELayout.HudXcomCards;
        var squad = new HBoxContainer { Visible = !cards };
        squad.AddThemeConstantOverride("separation", 6);
        foreach (var (n, done, on) in new[] { ("Marksmen", false, true), ("Infantrymen", true, false), ("Knights", false, false), ("Battle Master", true, false), ("Empire Artillery", false, false) })
        {
            var box = new ChamferBox { Fill = on ? new Color(HudKit.Lime, 0.16f) : HudKit.Glass, Border = on ? HudKit.Lime : done ? HudKit.Edge with { A = 0.4f } : HudKit.Edge, Cut = 8, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4 };
            if (on) box.Glow = HudKit.Lime with { A = 0.4f };
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 6);
            h.AddChild(HudKit.Icon(done ? "checked-shield" : "person", 20, done ? HudKit.Dim with { A = 0.6f } : on ? HudKit.Lime : HudKit.Friendly));
            h.AddChild(HudKit.Text(n.ToUpperInvariant(), 15, done ? HudKit.Dim with { A = 0.6f } : on ? HudKit.Lime : HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1)));
            squad.AddChild(HudKit.Panel(box, h));
        }
        HudKit.Place(_ui, squad, Control.LayoutPreset.CenterTop, new Vector2(60, 132));

        // Bottom left: the selected unit.
        HudKit.Place(_ui, UnitCard(Active, 440, false), Control.LayoutPreset.BottomLeft, new Vector2(24, -24));

        // Bottom centre: the action bar, with what the hovered action does above it.
        var bar = new VBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        var says = HudKit.Panel(new ChamferBox { Fill = HudKit.Glass, Border = HudKit.Lime with { A = 0.6f }, Cut = 8, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 3, ContentMarginBottom = 3 },
            HudKit.Text(Actions[1].Says, 19, HudKit.Lime, HudKit.Semi, HorizontalAlignment.Center));
        says.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        bar.AddChild(says);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        foreach (var a in Actions)
            buttons.AddChild(HudKit.ActionButton(a.Name, a.Icon, a.Name, a.Key, HudKit.Teal, 104, a.Name == "Advance", a.Off));
        buttons.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 80), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        buttons.AddChild(HudKit.ActionButton("Measure", "footprint", "Measure", "5", HudKit.Dim, 80));
        buttons.AddChild(HudKit.ActionButton("Cancel", "stop-sign", "Back", "Esc", HudKit.Dim, 80));
        bar.AddChild(buttons);
        if (cards)
        {
            var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            stack.AddThemeConstantOverride("separation", 12);
            stack.AddChild(bar);
            stack.AddChild(CardStrip());
            HudKit.Place(_ui, stack, Control.LayoutPreset.CenterBottom, new Vector2(40, -20));
        }
        else
            HudKit.Place(_ui, bar, Control.LayoutPreset.CenterBottom, new Vector2(60, -24));

        // Bottom right: the last few report lines; R opens the full report.
        var rep = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        rep.AddThemeConstantOverride("separation", 6);
        var rh = new HBoxContainer();
        rh.AddChild(HudKit.Heading("Battle report", 15));
        rh.AddChild(HudKit.Grow());
        rh.AddChild(HudKit.Text("R  -  OPEN", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        rep.AddChild(rh);
        rep.AddChild(ReportLines(4, 16));
        HudKit.Place(_ui, HudKit.Panel(HudKit.PanelBox(12).With(x => x.Fill = x.Fill with { A = 0.78f }), rep), Control.LayoutPreset.BottomRight, new Vector2(-24, -24));

        // The forecast follows the enemy under the cursor.
        var fc = Forecast();
        HudKit.Place(_ui, fc, Control.LayoutPreset.TopLeft);
        PinNear(fc, Target.Centre, new Vector2(-470, -170));

        if (Layout == ELayout.HudXcomCards) HudKit.Place(_ui, MockTag(), Control.LayoutPreset.CenterTop, new Vector2(0, 4));
        else HudKit.Place(_ui, MockTag(), Control.LayoutPreset.CenterBottom, new Vector2(60, -4));
    }

    private void BuildXcomCards() => BuildXcom();

    // ================================================================== Layout B2: TWW3 unit cards
    private sealed record CardState(string Unit, int Left, bool Done, bool Shaken, bool Selected, string Key);

    /// <summary>
    /// Total War: Warhammer 3-style army strip: one portrait card per unit, in army order. Shows what is left
    /// of the unit (models, or wounds for a single Tough model), whether it has gone this round, and its state.
    /// Click a card = select that unit (same as clicking it on the table).
    /// </summary>
    private Control CardStrip()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        foreach (var c in new[]
        {
            new CardState("Marksmen", 5, false, false, true, "F1"),
            new CardState("Infantrymen", 8, true, true, false, "F2"),
            new CardState("Knights", 5, false, false, false, "F3"),
            new CardState("Battle Master", 3, true, false, false, "F4"),
            new CardState("Empire Artillery", 2, false, false, false, "F5"),
        })
            row.AddChild(UnitPortraitCard(c));
        return row;
    }

    private Control UnitPortraitCard(CardState c)
    {
        SampleUnit u = _table.Find(c.Unit).Unit;
        bool single = u.Models == 1;
        int max = single ? u.Tough : u.Models;
        Color frame = c.Selected ? HudKit.Lime : c.Shaken ? Palette.WoundsRed : HudKit.Edge;
        float dimA = c.Done ? 0.45f : 1f;

        var b = new Button { Name = "Card_" + c.Unit.Replace(" ", ""), CustomMinimumSize = new Vector2(118, 158), FocusMode = Control.FocusModeEnum.None };
        var box = new ChamferBox { Fill = c.Selected ? new Color(0.10f, 0.15f, 0.09f, 0.95f) : new Color(0.05f, 0.075f, 0.085f, c.Done ? 0.75f : 0.92f), Border = frame with { A = c.Done ? 0.5f : 1f }, Cut = 12, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6 };
        if (c.Selected) { box.Glow = HudKit.Lime with { A = 0.5f }; box.Accent = HudKit.Lime; box.AccentLength = 40; }
        HudKit.StyleButton(b, box, HudKit.Fg);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.OffsetLeft = 7; col.OffsetRight = -7; col.OffsetTop = 7; col.OffsetBottom = -7;
        col.AddThemeConstantOverride("separation", 3);

        // Portrait: Army Forge unit art when downloaded, else an icon for the unit's type.
        string icon = u.Name.Contains("Artillery") ? "fire-ray" : u.Name.Contains("Knight") ? "running-shoe" : u.Hero ? "rally-the-troops" : u.Weapons.Any(w => w.Range > 0) ? "targeting" : "person";
        var pic = HudKit.Panel(new ChamferBox { Fill = new Color(HudKit.Friendly, c.Selected ? 0.16f : 0.08f), Border = Colors.Transparent, BorderWidth = 0, Cut = 8, ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 6, ContentMarginBottom = 6 },
            HudKit.Icon(icon, 54, (c.Selected ? HudKit.Lime : HudKit.Friendly) with { A = dimA }));
        pic.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(pic);

        var name = HudKit.Text(u.Name.ToUpperInvariant(), 13, (c.Selected ? HudKit.Lime : HudKit.Fg) with { A = dimA }, HudKit.Tracked(HudKit.Bold, 1), HorizontalAlignment.Center);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.CustomMinimumSize = new Vector2(0, 36);
        name.VerticalAlignment = VerticalAlignment.Center;
        col.AddChild(name);

        // What is left: one segment per model (or per wound for a single Tough model).
        var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 2);
        for (int i = 0; i < max; i++)
        {
            Color seg = i < c.Left ? (c.Left * 2 <= max ? Palette.WoundsRed : HudKit.Lime) : new Color(1, 1, 1, 0.12f);
            bar.AddChild(new ColorRect { Color = seg with { A = seg.A * dimA }, CustomMinimumSize = new Vector2(0, 5), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        col.AddChild(bar);
        col.AddChild(HudKit.Text(single ? $"{c.Left}/{max} wounds" : $"{c.Left}/{max} models", 12, HudKit.Dim with { A = dimA }, HudKit.Semi, HorizontalAlignment.Center));
        b.AddChild(col);

        // Corner marks: hotkey top-left, state icons top-right.
        var key = HudKit.Text(c.Key, 12, HudKit.Dim, HudKit.Bold);
        key.Position = new Vector2(8, 4);
        key.MouseFilter = Control.MouseFilterEnum.Ignore;
        b.AddChild(key);
        var marks = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(70, 4) };
        marks.AddThemeConstantOverride("separation", 2);
        if (c.Shaken) marks.AddChild(HudKit.Icon("broken-shield", 20, Palette.WoundsRed));
        if (c.Done) marks.AddChild(HudKit.Icon("checked-shield", 20, HudKit.Dim));
        if (u.Hero) marks.AddChild(HudKit.Icon("flying-flag", 20, HudKit.Gold));
        b.AddChild(marks);
        b.TooltipText = c.Shaken ? "Shaken - must spend its next activation idle to recover." : c.Done ? "Already activated this round." : "Ready to activate.";
        return b;
    }
}
