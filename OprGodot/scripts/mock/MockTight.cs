using FdgRaylib;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Mock;

/// <summary>
/// Kai's chosen in-game layout, tightened (2026-11-10): round info top-left, the army list under it with unit
/// portraits (ready first, activated greyed at the bottom), the selected unit + 2x2 actions bottom-left, the
/// clicked enemy docked right. Everything sized down so the table gets the room.
/// </summary>
public partial class MockScene
{
    private const float Pad = 12;

    private void BuildFinal()
    {
        if (State == 2) _table.Look(new Vector3(-2, 74, 0.01f), new Vector3(-2, 0, 0), 40); // T: top-down view
        else _table.Look(new Vector3(6, 50, 46), new Vector3(3, 0, 5), 44);
        TableDressing();
        TableMarks();
        HudKit.Place(_ui, PromptLine(), Control.LayoutPreset.CenterTop, new Vector2(0, Pad));

        var round = TightRound();
        HudKit.Place(_ui, round, Control.LayoutPreset.TopLeft, new Vector2(Pad, Pad));
        HudKit.Place(_ui, TightTopButtons(), Control.LayoutPreset.TopRight, new Vector2(-Pad, Pad));

        var block = TightUnitBlock();
        HudKit.Place(_ui, block, Control.LayoutPreset.BottomLeft, new Vector2(Pad, -Pad));

        // Bottom right: the Mission panel (folds away) stacked on the battle report.
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 6);
        var mission = (CollapsePanel)MissionPanel();
        stack.AddChild(mission);
        if (State == 1) mission.Collapsed = true;
        stack.AddChild(TightReport());
        HudKit.Place(_ui, stack, Control.LayoutPreset.BottomRight, new Vector2(-Pad, -Pad));
        HudKit.Place(_ui, MockTag(), Control.LayoutPreset.CenterBottom, new Vector2(150, -2));

        // The list fills the space between the round panel and the unit block.
        CallDeferred(nameof(PlaceArmyList), round, block);
    }

    private void PlaceArmyList(Control round, Control block)
    {
        float top = round.GetRect().End.Y + 8;
        float bottom = block.GetRect().Position.Y - 8;
        var list = TightArmyList(bottom - top - 44);
        HudKit.Place(_ui, list, Control.LayoutPreset.TopLeft, new Vector2(Pad, top));
        if (State != 2) EnemyInfoStates();
    }

    // ------------------------------------------------------------------ enemy info: tooltip, then pinned windows
    /// <summary>
    /// Kai, 2026-11-10: hovering an enemy for 1 second shows its info as a tooltip by the pointer; left-clicking
    /// the unit pins that info as a floating window that can be dragged anywhere, minimised or closed. The
    /// screenshot shows all three at once: the hover tooltip (Iron Shooters), a pinned window dragged to the top
    /// (Iron Warriors) and a minimised one (Brute Orc Boss).
    /// </summary>
    private void EnemyInfoStates()
    {
        // Hover tooltip by the pointer.
        Vector2 at = Screen(Target.Centre + new Vector3(1.5f, 0, 0.5f));
        var tip = HudKit.Panel(Tight(10).With(x => { x.Fill = HudKit.Glass with { A = 0.97f }; x.Border = HudKit.Enemy with { A = 0.85f }; x.Glow = HudKit.Enemy with { A = 0.25f }; x.ContentMarginLeft = 10; }),
            EnemyInfo(Target.Unit, 5, "16\" away - in cover (forest)", ("5+", "5+", "~1 model"), "Left-click to pin"));
        _ui.AddChild(tip);
        tip.Position = at + new Vector2(-330, -40); // left of the pointer here, so it doesn't hide the units below
        Pointer(at);

        // Pinned: dragged up out of the way.
        var warriors = _table.Find("Iron Warriors");
        var pinned = new FloatingPanel("Iron Warriors", EnemyInfo(warriors.Unit, 10, "21\" away - out of range (Matchlock Rifle 18\")", null, null), HudKit.Enemy);
        _ui.AddChild(pinned);
        pinned.Position = new Vector2(1240, 96);
        _table.Ring(warriors.Centre, MockTable.Footprint(warriors), HudKit.Enemy, 0.1f, 2.5f);

        // Pinned and minimised: just the title bar.
        var boss = new FloatingPanel("Brute Orc Boss", EnemyInfo(_table.Find("Brute Orc Boss").Unit, 1, "", null, null), HudKit.Enemy);
        _ui.AddChild(boss);
        boss.Position = new Vector2(1240, 470);
        boss.Minimised = true;
    }

    private Control EnemyInfo(SampleUnit u, int left, string where, (string Hit, string Save, string Expect)? shot, string? hint)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 5);
        v.AddChild(TightCard(u, left, "Orcs", HudKit.Enemy, false, 290));
        if (where != "") v.AddChild(HudKit.Text(where, 13, HudKit.Dim, HudKit.Semi));
        if (shot is { } s)
        {
            v.AddChild(new ColorRect { Color = HudKit.Enemy with { A = 0.45f }, CustomMinimumSize = new Vector2(0, 1) });
            v.AddChild(HudKit.Text("IF MARKSMEN SHOOT", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
            var nums = new HBoxContainer();
            nums.AddThemeConstantOverride("separation", 14);
            foreach (var (l, val, c) in new[] { ("HIT ON", s.Hit, Palette.HitsYellow), ("THEY SAVE", s.Save, HudKit.Fg), ("EXPECT", s.Expect, Palette.WoundsRed) })
            {
                var b = new VBoxContainer();
                b.AddThemeConstantOverride("separation", -5);
                b.AddChild(HudKit.Text(l, 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
                b.AddChild(HudKit.Text(val, 20, c, HudKit.Bold));
                nums.AddChild(b);
            }
            v.AddChild(nums);
            v.AddChild(HudKit.Text("Cover +1 to their save, AP(1) -1", 12, HudKit.Dim, HudKit.Medium));
        }
        if (hint != null) v.AddChild(HudKit.Text(hint, 12, HudKit.Dim with { A = 0.7f }, HudKit.Medium));
        return v;
    }

    private static ChamferBox Tight(float cut = 10) => new()
    {
        Fill = HudKit.Glass, Border = HudKit.Edge, Cut = cut,
        ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    // ------------------------------------------------------------------ round + objectives
    private static Control TightRound()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var r = new VBoxContainer();
        r.AddThemeConstantOverride("separation", -6);
        r.AddChild(HudKit.Text("ROUND", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        var n = new HBoxContainer();
        n.AddChild(HudKit.Text("2", 30, HudKit.Lime, HudKit.Bold));
        n.AddChild(HudKit.Text("/4", 18, HudKit.Dim, HudKit.Bold));
        r.AddChild(n);
        row.AddChild(r);
        row.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var o = new VBoxContainer();
        o.AddThemeConstantOverride("separation", 0);
        o.AddChild(HudKit.Text("OBJECTIVES", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        var held = new HBoxContainer();
        held.AddThemeConstantOverride("separation", 10);
        foreach (var (num, c, tip) in new[] { (1, HudKit.Friendly, "You hold 1"), (1, new Color(0.85f, 0.88f, 0.9f), "1 held by nobody"), (1, HudKit.Enemy, "Orcs hold 1") })
        {
            var h = new HBoxContainer { TooltipText = tip };
            h.AddThemeConstantOverride("separation", 2);
            h.AddChild(HudKit.Icon("flag-objective", 18, c));
            h.AddChild(HudKit.Text(num.ToString(), 20, c, HudKit.Bold));
            held.AddChild(h);
        }
        o.AddChild(held);
        row.AddChild(o);
        row.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -2);
        t.AddChild(HudKit.Text("YOUR TURN", 11, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2)));
        t.AddChild(HudKit.Text("Orcs: 6 left to activate", 15, HudKit.Fg, HudKit.Semi));
        row.AddChild(t);
        return HudKit.Panel(Tight(10).With(x => { x.Accent = HudKit.Lime; x.AccentLength = 40; x.ContentMarginLeft = 12; x.ContentMarginRight = 12; }), row);
    }

    private static Control TightTopButtons()
    {
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 6);
        foreach (var (n, icon, label, key) in new[] { ("TopView", "binoculars", "Top view", "T"), ("Report", "scroll-unfurled", "Report", "R"), ("Armies", "stack", "Armies", "L"), ("Menu", "hamburger-menu", "Menu", "Esc") })
            menu.AddChild(HudKit.ActionButton(n, icon, label, key, HudKit.Teal, 56, labelSize: 11));
        return menu;
    }

    // ------------------------------------------------------------------ the army list
    private Control TightArmyList(float height)
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(286, 0) };
        col.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddChild(HudKit.Heading("Your army", 13));
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text($"{BigArmy.Count(c => !c.Done)}/{BigArmy.Length} to go - Tab", 13, HudKit.Dim, HudKit.Semi));
        col.AddChild(head);

        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 3);
        foreach (var c in BigArmy.Where(c => !c.Done)) rows.AddChild(TightRow(c));
        var sep = new HBoxContainer();
        sep.AddThemeConstantOverride("separation", 6);
        sep.AddChild(HudKit.Text("ACTIVATED", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        sep.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(0, 1), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        rows.AddChild(sep);
        foreach (var c in BigArmy.Where(c => c.Done)) rows.AddChild(TightRow(c));

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(rows);
        col.AddChild(scroll);
        return HudKit.Panel(Tight(10).With(x => x.ContentMarginRight = 4), col);
    }

    private Control TightRow(CardState c)
    {
        SampleUnit u = MockData.Unit(He, c.Unit);
        bool single = u.Models == 1;
        int max = single ? u.Tough : u.Models;
        float a = c.Done ? 0.4f : 1f;
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 7);
        h.AddChild(Portrait("Human Empire", u, 36, c.Selected ? HudKit.Lime : HudKit.Friendly, a));
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 3);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 4);
        var name = HudKit.Text(u.Name.ToUpperInvariant(), 14, (c.Selected ? HudKit.Lime : HudKit.Fg) with { A = a }, HudKit.Tracked(HudKit.Bold, 1));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        top.AddChild(name);
        if (c.Shaken) top.AddChild(HudKit.Icon("broken-shield", 15, Palette.WoundsRed));
        if (u.Hero) top.AddChild(HudKit.Icon("flying-flag", 15, HudKit.Gold with { A = a }));
        top.AddChild(HudKit.Text(single ? $"{c.Left}/{max}W" : $"{c.Left}/{max}", 13, HudKit.Dim with { A = a }, HudKit.Semi));
        v.AddChild(top);
        v.AddChild(SegBar(c.Left, max, a, 3));
        h.AddChild(v);
        var box = new ChamferBox
        {
            Fill = c.Selected ? new Color(HudKit.Lime, 0.13f) : new Color(0, 0, 0, c.Done ? 0.12f : 0.25f),
            Border = c.Selected ? HudKit.Lime : c.Shaken ? Palette.WoundsRed with { A = 0.6f } : HudKit.Edge with { A = c.Done ? 0.2f : 0.4f },
            Cut = 6, ContentMarginLeft = 3, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        var p = HudKit.Panel(box, h);
        p.TooltipText = c.Shaken ? "Shaken" : c.Done ? "Already activated this round" : "Click to select - Tab for the next ready unit";
        return p;
    }

    /// <summary>
    /// A unit's picture: Army Forge's portrait from UnitImages/&lt;faction&gt;/Portraits (what the "Download Unit
    /// Pictures" tool fetches), else an icon for its kind, framed in the team colour.
    /// </summary>
    private static Control Portrait(string faction, SampleUnit u, float size, Color team, float alpha)
    {
        Control inner;
        Texture2D? tex = UnitPicture(faction, u.Name);
        if (tex != null)
            inner = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(size, size), SelfModulate = Colors.White with { A = alpha }, ClipContents = true };
        else
            inner = HudKit.Icon(CardIcon(u), size * 0.72f, team with { A = alpha * 0.9f });
        var frame = HudKit.Panel(new ChamferBox
        {
            Fill = new Color(team, 0.12f * alpha), Border = team with { A = 0.55f * alpha }, Cut = 6,
            ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 2, ContentMarginBottom = 2,
        }, inner);
        frame.CustomMinimumSize = new Vector2(size + 4, size + 4);
        inner.SizeFlagsHorizontal = inner.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return frame;
    }

    private static readonly Dictionary<string, Texture2D?> Pictures = new();

    private static Texture2D? UnitPicture(string faction, string unit)
    {
        string key = faction + "/" + unit;
        if (Pictures.TryGetValue(key, out var t)) return t;
        t = null;
        foreach (string root in new[] { Path.Combine(AppAssets.Root, ".."), AppAssets.Root, System.Environment.CurrentDirectory })
        {
            string file = Path.Combine(root, "UnitImages", faction, "Portraits", unit + ".png");
            if (!File.Exists(file)) continue;
            var img = Image.LoadFromFile(file);
            if (img != null) { t = ImageTexture.CreateFromImage(img); break; }
        }
        return Pictures[key] = t;
    }

    // ------------------------------------------------------------------ selected unit + actions
    private Control TightCard(SampleUnit u, int left, string faction, Color team, bool mine, float width)
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        col.AddThemeConstantOverride("separation", 5);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        top.AddChild(Portrait(faction, u, 46, team, 1));
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", -3);
        names.AddChild(HudKit.Text(u.Name.ToUpperInvariant(), 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1)));
        names.AddChild(HudKit.Text($"{faction} - {u.Points} pts", 14, HudKit.Dim, HudKit.Semi));
        top.AddChild(names);
        var tag = HudKit.Chip(mine ? "READY" : "ENEMY", mine ? HudKit.Lime : HudKit.Enemy, true, 12);
        tag.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        top.AddChild(tag);
        col.AddChild(top);

        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 5);
        stats.AddChild(TightStat("QUA", $"{u.Quality}+", "targeting"));
        stats.AddChild(TightStat("DEF", $"{u.Defense}+", "shield"));
        stats.AddChild(TightStat(u.Models == 1 ? "WOUNDS" : "MODELS", u.Models == 1 ? $"{u.Tough}/{u.Tough}" : $"{left}/{u.Models}", "rally-the-troops"));
        col.AddChild(stats);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 0);
        foreach (string h in new[] { "WEAPON", "RNG", "ATK", "AP" })
            grid.AddChild(HudKit.Text(h, 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        foreach (SampleWeapon w in u.Weapons)
        {
            var name = HudKit.Text(w.Name, 15, HudKit.Fg, HudKit.Semi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(name);
            grid.AddChild(HudKit.Text(Range(w), 15, w.Range > 0 ? HudKit.Lime : HudKit.Dim, HudKit.Bold));
            grid.AddChild(HudKit.Text($"A{w.Attacks}", 15, HudKit.Fg, HudKit.Bold));
            grid.AddChild(HudKit.Text(w.Ap > 0 ? w.Ap.ToString() : "-", 15, w.Ap > 0 ? Palette.WoundsRed : HudKit.Dim, HudKit.Bold));
        }
        col.AddChild(grid);

        var rules = new HFlowContainer();
        rules.AddThemeConstantOverride("h_separation", 4);
        rules.AddThemeConstantOverride("v_separation", 4);
        foreach (string r in u.Rules) rules.AddChild(HudKit.Chip(r, Palette.RuleGold, false, 13));
        col.AddChild(rules);
        return col;
    }

    private static Control TightStat(string label, string value, string icon)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        row.AddChild(HudKit.Icon(icon, 18, HudKit.Teal));
        row.AddChild(HudKit.Text(label, 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        var v = HudKit.Text(value, 19, HudKit.Fg, HudKit.Bold);
        v.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        v.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(v);
        var p = HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.3f), Border = HudKit.Edge with { A = 0.5f }, Cut = 5, ContentMarginLeft = 6, ContentMarginRight = 7, ContentMarginTop = 1, ContentMarginBottom = 1 }, row);
        p.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return p;
    }

    private Control TightUnitBlock()
    {
        var block = new HBoxContainer();
        block.AddThemeConstantOverride("separation", 10);
        block.AddChild(TightCard(Active.Unit, 5, "Human Empire", HudKit.Friendly, true, 300));
        block.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 0) });
        var act = new VBoxContainer();
        act.AddThemeConstantOverride("separation", 5);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 5);
        grid.AddThemeConstantOverride("v_separation", 5);
        foreach (var a in Actions) grid.AddChild(HudKit.ActionButton(a.Name, a.Icon, a.Name, a.Key, HudKit.Teal, 70, a.Name == "Advance", a.Off, 12));
        act.AddChild(grid);
        var says = HudKit.Text("Move up to 6\", then may shoot.", 13, HudKit.Lime, HudKit.Semi);
        says.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        says.CustomMinimumSize = new Vector2(145, 0);
        act.AddChild(says);
        block.AddChild(act);
        return HudKit.Panel(Tight(12).With(x => { x.Accent = HudKit.Lime; x.AccentLength = 40; x.ContentMarginLeft = 10; x.ContentMarginTop = 8; x.ContentMarginBottom = 8; }), block);
    }

    // ------------------------------------------------------------------ right side
    private Control TightEnemy()
    {
        var wrap = new VBoxContainer();
        wrap.AddThemeConstantOverride("separation", 3);
        var head = new HBoxContainer();
        head.AddChild(HudKit.Heading("Selected enemy", 12));
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text("Esc - close", 12, HudKit.Dim, HudKit.Semi));
        wrap.AddChild(head);
        wrap.AddChild(TightCard(Target.Unit, 5, "Orcs", HudKit.Enemy, false, 290));
        _table.Area(Target.Centre, MockTable.Footprint(Target) + 0.2f, HudKit.Enemy);
        return HudKit.Panel(Tight(12).With(x => { x.Border = HudKit.Enemy with { A = 0.8f }; x.Accent = HudKit.Enemy; x.AccentLength = 40; x.ContentMarginLeft = 10; }), wrap);
    }

    private static Control TightReport()
    {
        var rep = new VBoxContainer { CustomMinimumSize = new Vector2(350, 0) };
        rep.AddThemeConstantOverride("separation", 2);
        var rh = new HBoxContainer();
        rh.AddChild(HudKit.Heading("Battle report", 12));
        rh.AddChild(HudKit.Grow());
        rh.AddChild(HudKit.Text("R - open", 12, HudKit.Dim, HudKit.Semi));
        rep.AddChild(rh);
        foreach (var (t, c) in Report.Skip(Report.Length - 3))
        {
            var l = HudKit.Text(t, 14, c, HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rep.AddChild(l);
        }
        return HudKit.Panel(Tight(10).With(x => x.Fill = x.Fill with { A = 0.8f }), rep);
    }

    // ------------------------------------------------------------------ mission panel
    /// <summary>
    /// The mission: how the game is won, the score, objectives held, and (with Mission Cards on) your face-up
    /// cards with what each needs and how close you are. Cards and wording from the engine's MissionCards
    /// (AoF Mission Cards v3.6.0, Battle of Wits).
    /// </summary>
    private static Control MissionPanel()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 5);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        var what = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        what.AddThemeConstantOverride("separation", -2);
        what.AddChild(HudKit.Text("Mission Cards - Battle of Wits", 16, HudKit.Fg, HudKit.Bold));
        what.AddChild(HudKit.Text("Ends after round 4. Most victory points wins.", 13, HudKit.Dim, HudKit.Semi));
        what.AddChild(HudKit.Text("+1 VP per objective you control at the end.", 13, HudKit.Dim, HudKit.Semi));
        top.AddChild(what);
        foreach (var (who, vp, c) in new[] { ("YOU", "3", HudKit.Friendly), ("ORCS", "2", HudKit.Enemy) })
        {
            var b = new VBoxContainer();
            b.AddThemeConstantOverride("separation", -6);
            b.AddChild(HudKit.Text(who, 11, c, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center));
            b.AddChild(HudKit.Text(vp + " VP", 22, c, HudKit.Bold, HorizontalAlignment.Center));
            top.AddChild(b);
        }
        v.AddChild(top);

        v.AddChild(HudKit.Text("YOUR CARDS", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        v.AddChild(MissionCard(32, "Hold 2", "Control objective 2 at the beginning and end of the same round.", "2 VP", "Held at the start - keep it", HudKit.Lime));
        v.AddChild(MissionCard(51, "Fire at Will", "Destroy one enemy unit by shooting (1 VP) / three or more (2 VP).", "1-2 VP", "0 destroyed this round", HudKit.Dim));
        v.AddChild(MissionCard(45, "Tactical Precision", "Control any three objectives.", "2 VP", "You control 1 of 3", HudKit.Dim));
        return new CollapsePanel("Mission", "M", "You 3 VP - Orcs 2 VP", v, 350);
    }

    private static Control MissionCard(int number, string name, string needs, string vp, string status, Color statusColour)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        h.AddChild(HudKit.Text(number.ToString(), 12, HudKit.Dim, HudKit.Bold));
        var n = HudKit.Text(name.ToUpperInvariant(), 14, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        h.AddChild(n);
        h.AddChild(HudKit.Chip(vp, Palette.RuleGold, true, 12));
        col.AddChild(h);
        var t = HudKit.Text(needs, 13, HudKit.Dim, HudKit.Medium);
        t.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(t);
        col.AddChild(HudKit.Text(status, 12, statusColour, HudKit.Semi));
        return HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.28f), Border = HudKit.Edge with { A = 0.45f }, Cut = 6, ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 3, ContentMarginBottom = 4 }, col);
    }

    // ------------------------------------------------------------------ prompt line + table marks
    /// <summary>
    /// What the game wants from you right now, top-centre. Changes with every request: "Pick a unit to activate",
    /// "Place an objective", "Choose a target", "Deploy Knights"...
    /// </summary>
    private static Control PromptLine()
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        h.AddChild(HudKit.Icon("walk", 22, HudKit.Lime));
        h.AddChild(HudKit.Text("MARKSMEN - CHOOSE AN ACTION", 20, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2)));
        h.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 22), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        h.AddChild(HudKit.Text("1-4 or click an action  -  Esc to pick another unit", 14, HudKit.Dim, HudKit.Semi));
        return HudKit.Panel(Tight(10).With(x => { x.Fill = HudKit.Glass with { A = 0.9f }; x.Border = HudKit.Lime with { A = 0.55f }; x.ContentMarginLeft = 16; x.ContentMarginRight = 16; x.ContentMarginTop = 5; x.ContentMarginBottom = 5; }), h);
    }

    /// <summary>
    /// Names over your own units always; over an enemy only while hovered or pinned. A red broken shield over a
    /// Shaken unit, a tick over a unit that has already activated this round.
    /// </summary>
    private void TableMarks()
    {
        foreach (var p in _table.Units.Where(p => !p.Mine))
            if (p.Unit.Name is not ("Iron Shooters" or "Iron Warriors"))
                _table.Labels[p.Unit.Name].Visible = false;
        _table.Mark("Infantrymen", "checked-shield", HudKit.Dim);
        _table.Mark("Infantrymen", "broken-shield", Palette.WoundsRed, 1);
        _table.Mark("Battle Master", "checked-shield", HudKit.Dim);
    }
}
