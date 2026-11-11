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
        _table.Look(new Vector3(6, 50, 46), new Vector3(3, 0, 5), 44);
        TableDressing();

        var round = TightRound();
        HudKit.Place(_ui, round, Control.LayoutPreset.TopLeft, new Vector2(Pad, Pad));
        HudKit.Place(_ui, TightTopButtons(), Control.LayoutPreset.TopRight, new Vector2(-Pad, Pad));

        var block = TightUnitBlock();
        HudKit.Place(_ui, block, Control.LayoutPreset.BottomLeft, new Vector2(Pad, -Pad));

        HudKit.Place(_ui, TightReport(), Control.LayoutPreset.BottomRight, new Vector2(-Pad, -Pad));
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
        // The clicked enemy sits right beside the selected unit's actions, bottom-left, so the two compare at a glance.
        HudKit.Place(_ui, TightEnemy(), Control.LayoutPreset.BottomLeft, new Vector2(block.GetRect().End.X + 8, -Pad));
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
        t.AddChild(HudKit.Text("vs Orcs (bot)", 15, HudKit.Fg, HudKit.Semi));
        row.AddChild(t);
        return HudKit.Panel(Tight(10).With(x => { x.Accent = HudKit.Lime; x.AccentLength = 40; x.ContentMarginLeft = 12; x.ContentMarginRight = 12; }), row);
    }

    private static Control TightTopButtons()
    {
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 6);
        foreach (var (n, icon, label, key) in new[] { ("Report", "scroll-unfurled", "Report", "R"), ("Mission", "flag-objective", "Mission", "M"), ("Armies", "stack", "Armies", "L"), ("Menu", "hamburger-menu", "Menu", "Esc") })
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
}
