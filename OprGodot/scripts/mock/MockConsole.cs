using Godot;
using OprGodot.Hud;

namespace OprGodot.Mock;

/// <summary>
/// Round-2 mockups (Kai, 2026-11-10): where the army cards go, what a 15-unit army does to them, and what
/// hovering / clicking an enemy, a rule and a piece of terrain look like.
/// </summary>
public partial class MockScene
{
    private const string He = "AoF-HumanEmpire", Orc = "AoF-Orcs";

    /// <summary>A 15-unit Human Empire list: what the card strip has to cope with.</summary>
    private static readonly CardState[] BigArmy =
    {
        new("Marksmen", 5, false, false, true, "F1"),
        new("Infantrymen", 8, true, true, false, "F2"),
        new("Knights", 5, false, false, false, "F3"),
        new("Battle Master", 3, true, false, false, "F4"),
        new("Empire Artillery", 2, false, false, false, "F5"),
        new("Infantrymen", 10, false, false, false, "F6"),
        new("Champion", 3, true, false, false, "F7"),
        new("Fanatics", 6, true, false, false, "F8"),
        new("Mercenaries", 5, false, false, false, "F9"),
        new("Hunting Hounds", 3, false, true, false, ""),
        new("Mercenary Hunters", 5, false, false, false, ""),
        new("Horse Marksmen", 4, true, false, false, ""),
        new("Guard Knights", 5, false, false, false, ""),
        new("Empire Artillery", 3, false, false, false, ""),
        new("Steam Tank of Water", 7, false, false, false, ""),
    };

    // Book wording (Army Forge text, BuiltInAssets/OfficialRuleText.json) and AoF Advanced Rulebook v3.6.0 p12.
    private const string FerociousText = "When this model shoots/charges enemies over 9\"/6\" away, unmodified result of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).";
    private const string CoverText = "If the majority of models in a unit are fully inside a piece of Cover terrain or behind a sight blocker, they get +1 to Defense rolls when blocking hits from shooting attacks.";
    private const string DifficultText = "If any model in a unit moves in or through Difficult terrain at any point of its move, then all models in the unit may not move more than 6\" for that movement.";

    // ================================================================== Option 1: one console along the bottom
    private void BuildConsole()
    {
        _table.Look(new Vector3(-6, 50, 44), new Vector3(-4, 0, 10), 44);
        TableDressing();
        HudKit.Place(_ui, RoundBlock(false), Control.LayoutPreset.TopLeft, new Vector2(24, 20));
        HudKit.Place(_ui, TopRightButtons(), Control.LayoutPreset.TopRight, new Vector2(-24, 20));

        // One connected panel: the selected unit on the left; its actions and the army's cards on the right,
        // so the cards sit right under the buttons they lead to.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(UnitCardContent(Active.Unit, 5, "Human Empire", HudKit.Friendly, 400, false, null, true));
        row.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 0) });

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 10);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        foreach (var a in Actions)
            actions.AddChild(HudKit.ActionButton(a.Name, a.Icon, a.Name, a.Key, HudKit.Teal, 86, a.Name == "Advance", a.Off));
        actions.AddChild(HudKit.ActionButton("Measure", "footprint", "Measure", "5", HudKit.Dim, 70));
        var says = HudKit.Text(Actions[1].Says, 18, HudKit.Lime, HudKit.Semi);
        says.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        actions.AddChild(HudKit.Gap(8));
        actions.AddChild(says);
        right.AddChild(actions);
        right.AddChild(ScrollingStrip(BigArmy, 7));
        row.AddChild(right);

        var console = HudKit.Panel(new ChamferBox { Fill = new Color(0.035f, 0.055f, 0.065f, 0.93f), Border = HudKit.Edge, Cut = 16, Corners = ChamferBox.ECorners.TopLeft | ChamferBox.ECorners.TopRight, Accent = HudKit.Lime, AccentLength = 90, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14 }, row);
        HudKit.Place(_ui, console, Control.LayoutPreset.BottomLeft, new Vector2(24, -16));

        // Report: collapsed to a tab in the corner above the console's right end.
        HudKit.Place(_ui, ReportBox(3, 380), Control.LayoutPreset.BottomRight, new Vector2(-24, -16));

        switch (State)
        {
            case 0: HoverEnemy(); break;
            case 1: ClickedEnemy(); break;
            case 2: HoverTerrain(); break;
        }
        HudKit.Place(_ui, MockTag(), Control.LayoutPreset.CenterTop, new Vector2(0, 4));
    }

    private static Control TopRightButtons()
    {
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 8);
        foreach (var (n, icon, label, key) in new[] { ("Report", "scroll-unfurled", "Report", "R"), ("Mission", "flag-objective", "Mission", "M"), ("Armies", "stack", "Armies", "L"), ("Menu", "hamburger-menu", "Menu", "Esc") })
            menu.AddChild(HudKit.ActionButton(n, icon, label, key, HudKit.Teal, 70));
        return menu;
    }

    private static Control ReportBox(int lines, float width)
    {
        var rep = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        rep.AddThemeConstantOverride("separation", 6);
        var rh = new HBoxContainer();
        rh.AddChild(HudKit.Heading("Battle report", 15));
        rh.AddChild(HudKit.Grow());
        rh.AddChild(HudKit.Text("R  -  OPEN", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        rep.AddChild(rh);
        rep.AddChild(ReportLines(lines, 15));
        return HudKit.Panel(HudKit.PanelBox(12).With(x => x.Fill = x.Fill with { A = 0.8f }), rep);
    }

    /// <summary>
    /// The army strip for a big army: as many cards as fit, arrows at both ends, the mouse wheel scrolls it
    /// while the pointer is over it, and a count of who is still to go. Tab jumps to the next ready unit.
    /// </summary>
    private Control ScrollingStrip(CardState[] army, int visible)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        head.AddChild(HudKit.Heading("Your army", 14));
        int ready = army.Count(c => !c.Done);
        head.AddChild(HudKit.Text($"{army.Length} units  -  {ready} still to activate", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text($"1-{visible} of {army.Length}   WHEEL / ARROWS SCROLL   TAB = NEXT READY", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        col.AddChild(head);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(StripArrow("<", false));
        foreach (var c in army.Take(visible)) row.AddChild(UnitPortraitCard(c, small: true));
        row.AddChild(StripArrow(">", true));
        col.AddChild(row);

        // Position bar: where the visible window sits in the whole army.
        var track = new Control { CustomMinimumSize = new Vector2(0, 4) };
        track.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.08f), AnchorRight = 1, AnchorBottom = 1 });
        track.AddChild(new ColorRect { Color = HudKit.Teal, AnchorRight = visible / (float)army.Length, AnchorBottom = 1 });
        col.AddChild(track);
        return col;
    }

    private static Button StripArrow(string s, bool on)
    {
        var b = new Button { Text = s, CustomMinimumSize = new Vector2(30, 0), FocusMode = Control.FocusModeEnum.None, Disabled = !on };
        HudKit.StyleButton(b, new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 6, ContentMarginLeft = 4, ContentMarginRight = 4 }, HudKit.Fg, 22);
        return b;
    }

    // ------------------------------------------------------------------ the three pointer states
    private Vector2 Screen(Vector3 at) => _table.ToScreen(at) / (GetViewport().GetVisibleRect().Size / _ui.Size);

    /// <summary>A drawn mouse pointer so the screenshot shows where the mouse is.</summary>
    private void Pointer(Vector2 at)
    {
        var poly = new Polygon2D
        {
            Polygon = new[] { new Vector2(0, 0), new Vector2(0, 26), new Vector2(7, 20), new Vector2(12, 31), new Vector2(17, 29), new Vector2(12, 18), new Vector2(20, 18) },
            Color = Colors.White, Position = at,
        };
        var outline = new Line2D { Points = poly.Polygon.Append(Vector2.Zero).ToArray(), Width = 1.5f, DefaultColor = Colors.Black, Position = at };
        _ui.AddChild(poly);
        _ui.AddChild(outline);
    }

    /// <summary>Hover an enemy: a compact card by the pointer - who they are, and what your shot would do.</summary>
    private void HoverEnemy()
    {
        Vector2 at = Screen(Target.Centre + new Vector3(1.5f, 0, 0.5f));
        SampleUnit e = Target.Unit;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(HudKit.Icon("targeting", 22, HudKit.Enemy));
        head.AddChild(HudKit.Text(e.Name.ToUpperInvariant(), 21, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1)));
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text("ORCS", 14, HudKit.Enemy, HudKit.Tracked(HudKit.Bold, 2)));
        v.AddChild(head);
        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 6);
        stats.AddChild(HudKit.Chip($"Q{e.Quality}+", HudKit.Fg, true));
        stats.AddChild(HudKit.Chip($"D{e.Defense}+", HudKit.Fg, true));
        stats.AddChild(HudKit.Chip($"{e.Models}/{e.Models} models", HudKit.Fg, true));
        foreach (string r in e.Rules) stats.AddChild(HudKit.Chip(r, Palette.RuleGold));
        v.AddChild(stats);
        foreach (var w in e.Weapons)
            v.AddChild(HudKit.Text($"{w.Name}   {(w.Range > 0 ? w.Range + "\"  " : "")}A{w.Attacks}{(w.Ap > 0 ? $"  AP({w.Ap})" : "")}", 16, HudKit.Dim, HudKit.Semi));
        v.AddChild(new ColorRect { Color = HudKit.Enemy with { A = 0.5f }, CustomMinimumSize = new Vector2(0, 1) });
        v.AddChild(HudKit.Text("IF MARKSMEN SHOOT  (Matchlock Rifle, 16\")", 13, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        var nums = new HBoxContainer();
        nums.AddThemeConstantOverride("separation", 16);
        foreach (var (l, val, c) in new[] { ("HIT ON", "5+", Palette.HitsYellow), ("THEY SAVE ON", "5+", HudKit.Fg), ("EXPECT", "~1 model", Palette.WoundsRed) })
        {
            var b = new VBoxContainer();
            b.AddThemeConstantOverride("separation", -6);
            b.AddChild(HudKit.Text(l, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
            b.AddChild(HudKit.Text(val, 24, c, HudKit.Bold));
            nums.AddChild(b);
        }
        v.AddChild(nums);
        v.AddChild(HudKit.Text("Cover: +1 to their save (forest)  -  AP(1): -1", 14, HudKit.Dim, HudKit.Medium));
        v.AddChild(HudKit.Text("Click to see their full card", 13, HudKit.Dim with { A = 0.7f }, HudKit.Medium));
        var card = HudKit.Panel(new ChamferBox { Fill = HudKit.Glass with { A = 0.94f }, Border = HudKit.Enemy with { A = 0.85f }, Cut = 10, Glow = HudKit.Enemy with { A = 0.3f }, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 }, v);
        _ui.AddChild(card);
        card.Position = at + new Vector2(28, -40);
        Pointer(at);
    }

    /// <summary>Click an enemy: its full card docks on the right; hovering a rule chip shows the book text.</summary>
    private async void ClickedEnemy()
    {
        var chips = new Dictionary<string, Control>();
        var content = UnitCardContent(Target.Unit, 5, "Orcs", HudKit.Enemy, 400, false, chips, false);
        var card = HudKit.Panel(new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Enemy with { A = 0.85f }, Cut = 12, Accent = HudKit.Enemy, AccentLength = 56, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 }, content);
        var wrap = new VBoxContainer();
        wrap.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        head.AddChild(HudKit.Heading("Selected enemy", 15));
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text("ESC / RIGHT-CLICK  -  CLOSE", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        wrap.AddChild(head);
        wrap.AddChild(card);
        HudKit.Place(_ui, wrap, Control.LayoutPreset.CenterRight, new Vector2(-24, -150));
        _table.Area(Target.Centre, MockTable.Footprint(Target) + 0.2f, HudKit.Enemy);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Control chip = chips["Ferocious"];
        Vector2 at = chip.GlobalPosition + chip.Size * 0.6f;
        var tip = RuleTip("FEROCIOUS", "Special rule  -  Iron Shooters", new[] { ("", FerociousText) }, "Army Forge wording");
        _ui.AddChild(tip);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        tip.Position = new Vector2(at.X - tip.Size.X - 75, at.Y - tip.Size.Y / 2);
        Pointer(at);
    }

    /// <summary>Hover terrain: what the piece is and what its rules do, plus who it affects right now.</summary>
    private void HoverTerrain()
    {
        Vector2 at = Screen(new Vector3(-14.5f, 0, 1.5f));
        var tip = RuleTip("FOREST", "Terrain  -  Cover + Difficult", new[] { ("COVER", CoverText), ("DIFFICULT", DifficultText) }, "AoF Advanced Rulebook v3.6.0, p12",
            "Iron Shooters (Orcs) are in it: +1 to their saves against your shooting.");
        _ui.AddChild(tip);
        tip.Position = at + new Vector2(26, -60);
        _table.Ring(new Vector3(-11, 0, -3), 5.9f, HudKit.Gold, 0.1f, 2.5f);
        Pointer(at);
    }

    private static Control RuleTip(string title, string sub, (string Head, string Text)[] parts, string source, string? now = null)
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        v.AddThemeConstantOverride("separation", 6);
        v.AddChild(HudKit.Text(title, 22, Palette.RuleGold, HudKit.Tracked(HudKit.Bold, 2)));
        v.AddChild(HudKit.Text(sub, 14, HudKit.Dim, HudKit.Semi));
        foreach (var (h, t) in parts)
        {
            if (h != "") v.AddChild(HudKit.Text(h, 13, HudKit.Teal, HudKit.Tracked(HudKit.Bold, 2)));
            var l = HudKit.Text(t, 16, HudKit.Fg, HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(440, 0);
            v.AddChild(l);
        }
        if (now != null)
        {
            v.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(0, 1) });
            var n = HudKit.Text(now, 15, HudKit.Lime, HudKit.Semi);
            n.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            n.CustomMinimumSize = new Vector2(440, 0);
            v.AddChild(n);
        }
        v.AddChild(HudKit.Text(source, 12, HudKit.Dim with { A = 0.7f }, HudKit.Medium));
        return HudKit.Panel(new ChamferBox { Fill = new Color(0.03f, 0.04f, 0.045f, 0.97f), Border = Palette.RuleGold with { A = 0.7f }, Cut = 8, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 }, v);
    }

    // ================================================================== Option 2: army list down the left side
    private void BuildSideList()
    {
        _table.Look(new Vector3(4, 48, 44), new Vector3(2, 0, 9), 44);
        TableDressing();
        HudKit.Place(_ui, RoundBlock(true), Control.LayoutPreset.CenterTop, new Vector2(0, 16));
        HudKit.Place(_ui, TopRightButtons(), Control.LayoutPreset.TopRight, new Vector2(-24, 20));

        // Left: every unit as a slim row; scrolls when the army is longer than the screen.
        var list = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) };
        list.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddChild(HudKit.Heading("Your army", 15));
        head.AddChild(HudKit.Grow());
        head.AddChild(HudKit.Text($"{BigArmy.Count(c => !c.Done)}/{BigArmy.Length} to go", 14, HudKit.Dim, HudKit.Semi));
        list.AddChild(head);
        foreach (var c in BigArmy) list.AddChild(UnitRow(c));
        HudKit.Place(_ui, HudKit.Panel(HudKit.PanelBox(12).With(x => { x.ContentMarginLeft = 10; x.ContentMarginRight = 10; }), list), Control.LayoutPreset.TopLeft, new Vector2(16, 20));

        // Bottom centre: the selected unit and its actions as one block.
        var block = new HBoxContainer();
        block.AddThemeConstantOverride("separation", 16);
        block.AddChild(UnitCardContent(Active.Unit, 5, "Human Empire", HudKit.Friendly, 400, false, null, true));
        block.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 0) });
        var act = new VBoxContainer();
        act.AddThemeConstantOverride("separation", 8);
        act.AddChild(HudKit.Heading("Actions", 15));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var a in Actions) grid.AddChild(HudKit.ActionButton(a.Name, a.Icon, a.Name, a.Key, HudKit.Teal, 96, a.Name == "Advance", a.Off));
        act.AddChild(grid);
        var says = HudKit.Text(Actions[1].Says, 16, HudKit.Lime, HudKit.Semi);
        says.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        says.CustomMinimumSize = new Vector2(200, 0);
        act.AddChild(says);
        block.AddChild(act);
        HudKit.Place(_ui, HudKit.Panel(HudKit.HeaderBox(), block), Control.LayoutPreset.CenterBottom, new Vector2(80, -16));
        HudKit.Place(_ui, ReportBox(4, 400), Control.LayoutPreset.BottomRight, new Vector2(-24, -16));
        HudKit.Place(_ui, MockTag(), Control.LayoutPreset.BottomLeft, new Vector2(330, -4));
    }

    private Control UnitRow(CardState c)
    {
        SampleUnit u = MockData.Unit(He, c.Unit);
        bool single = u.Models == 1;
        int max = single ? u.Tough : u.Models;
        float a = c.Done ? 0.45f : 1f;
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 8);
        h.AddChild(HudKit.Icon(CardIcon(u), 26, (c.Selected ? HudKit.Lime : HudKit.Friendly) with { A = a }));
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 2);
        var name = HudKit.Text(u.Name.ToUpperInvariant(), 15, (c.Selected ? HudKit.Lime : HudKit.Fg) with { A = a }, HudKit.Tracked(HudKit.Bold, 1));
        v.AddChild(name);
        v.AddChild(SegBar(c.Left, max, a, 4));
        h.AddChild(v);
        if (c.Shaken) h.AddChild(HudKit.Icon("broken-shield", 18, Palette.WoundsRed));
        if (c.Done) h.AddChild(HudKit.Icon("checked-shield", 18, HudKit.Dim));
        h.AddChild(HudKit.Text(single ? $"{c.Left}/{max}W" : $"{c.Left}/{max}", 14, HudKit.Dim with { A = a }, HudKit.Semi));
        var box = new ChamferBox { Fill = c.Selected ? new Color(HudKit.Lime, 0.14f) : new Color(0, 0, 0, 0.25f), Border = c.Selected ? HudKit.Lime : c.Shaken ? Palette.WoundsRed with { A = 0.7f } : HudKit.Edge with { A = 0.4f }, Cut = 6, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 5 };
        return HudKit.Panel(box, h);
    }

    private static string CardIcon(SampleUnit u) =>
        u.Name.Contains("Artillery") ? "fire-ray" : u.Name.Contains("Tank") ? "gears" : u.Name.Contains("Knight") || u.Name.Contains("Horse") || u.Name.Contains("Hound") ? "running-shoe" : u.Hero ? "rally-the-troops" : u.Weapons.Any(w => w.Range > 0) ? "targeting" : "person";

    private static Control SegBar(int left, int max, float alpha, float height)
    {
        var bar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 2);
        for (int i = 0; i < max; i++)
        {
            Color seg = i < left ? (left * 2 <= max ? Palette.WoundsRed : HudKit.Lime) : new Color(1, 1, 1, 0.12f);
            bar.AddChild(new ColorRect { Color = seg with { A = seg.A * alpha }, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        return bar;
    }
}
