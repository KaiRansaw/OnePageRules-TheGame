using Godot;
using OprGodot.Hud;

namespace OprGodot.Mock;

/// <summary>
/// Battle report mockup (Kai, 2026-11-11): "too much text ... I need to look at it and quickly see what
/// happened" - one block per activation, the results only (numbers on hover), click a block = camera to the fight.
/// A: a block per activation in the bottom-right panel. B: the same thing squeezed to one line per activation.
/// Run res://mockups/MockReport.tscn.
/// </summary>
public partial class MockReport : Node
{
    private static readonly Color You = new(1f, 0.66f, 0.2f);
    private static readonly Color Them = new(0.72f, 0.5f, 1f);
    private static readonly Color Red = new(1f, 0.42f, 0.36f);

    private sealed record Result(string Icon, string Target, bool TargetMine, string What, bool Bad, string? Chip = null);
    private sealed record Act(string Unit, bool Mine, string Action, string Icon, Result[] Results, string? Rule = null);

    private static readonly Act[] Round2 =
    {
        new("Marksmen", false, "ADVANCE 6\"", "walk", new[] { new Result("crosshair", "Harlequins", true, "-2 models", true) }),
        new("Automa Guard", true, "HOLD", "shield", new[]
        {
            new Result("crosshair", "Marksmen", false, "-3 models", true),
            new Result("broken-shield", "Marksmen", false, "", true, "SHAKEN"),
        }),
        new("Elite Weapon Masters", false, "CHARGE 9\"", "sword-clash", new[]
        {
            new Result("sword-clash", "Aerea Guard", true, "-1 model", true),
            new Result("shield-reflect", "Aerea Guard", true, "struck back: -2 wounds", false),
            new Result("broken-shield", "Aerea Guard", true, "", true, "LOST - SHAKEN"),
        }),
        new("Vita Aerea", true, "RUSH 12\"", "run", new[] { new Result("flag-objective", "", true, "took an objective", false) }),
        new("Horse Marksmen", false, "ADVANCE 6\"", "walk", Array.Empty<Result>(), Rule: "Vanguard"),
        new("Automa Drones", true, "ADVANCE 4\"", "walk", Array.Empty<Result>()),
    };

    public override void _Ready()
    {
        var table = new MockTable { ShowLabels = false };
        AddChild(table);
        table.Look(new Vector3(0, 46, 40), new Vector3(0, 0, 2));

        var ui = new Control();
        ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.MouseFilter = Control.MouseFilterEnum.Ignore;
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(ui);
        ui.AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.35f), AnchorRight = 1, AnchorBottom = 1 });

        // A - blocks (the proposal), docked bottom-right like the real panel.
        var a = ReportBlocks();
        HudKit.Place(ui, Labelled("A - one block per activation", a), Control.LayoutPreset.BottomRight, new Vector2(-16, -16));

        // B - one line per activation, for comparison.
        var b = ReportLines();
        HudKit.Place(ui, Labelled("B - one line per activation", b), Control.LayoutPreset.BottomRight, new Vector2(-440, -16));

        // The hover card on A's third block (the dice live here, not in the report).
        var tip = HoverCard();
        HudKit.Place(ui, Labelled("Hover a block - the numbers", tip), Control.LayoutPreset.BottomLeft, new Vector2(16, -16));

        var title = HudKit.Text("BATTLE REPORT MOCKUP - click a block = camera goes to that fight", 22, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2));
        HudKit.Place(ui, title, Control.LayoutPreset.CenterTop, new Vector2(0, 16));
    }

    private static Control Labelled(string caption, Control c)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        v.AddChild(HudKit.Text(caption.ToUpperInvariant(), 13, HudKit.Gold, HudKit.Tracked(HudKit.Bold, 2)));
        v.AddChild(c);
        return v;
    }

    private static ChamferBox Panel() => new()
    {
        Fill = HudKit.Glass, Border = HudKit.Edge, Cut = 10,
        ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 8,
    };

    private static Control Header(string extra)
    {
        var h = new HBoxContainer();
        h.AddChild(HudKit.Heading("Battle report", 12));
        h.AddChild(HudKit.Grow());
        h.AddChild(HudKit.Text(extra, 12, HudKit.Dim, HudKit.Semi));
        return h;
    }

    private static Control RoundBar(string text)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        h.AddChild(HudKit.Text(text, 12, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2)));
        h.AddChild(new ColorRect { Color = HudKit.Lime with { A = 0.35f }, CustomMinimumSize = new Vector2(0, 1), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        return h;
    }

    // ---------------------------------------------------------------- A

    private static Control ReportBlocks()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        v.AddThemeConstantOverride("separation", 5);
        v.AddChild(Header("R - full report"));
        v.AddChild(RoundBar("ROUND 2"));
        for (int i = 0; i < Round2.Length; i++) v.AddChild(Block(Round2[i], hovered: i == 2, latest: i == Round2.Length - 1));
        return HudKit.Panel(Panel(), v);
    }

    private static Control Block(Act act, bool hovered, bool latest)
    {
        Color side = act.Mine ? You : Them;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        top.AddChild(HudKit.Portrait("", act.Unit, act.Mine ? "rally-the-troops" : "person", 26, side));
        var name = HudKit.Text(act.Unit.ToUpperInvariant(), 15, side, HudKit.Tracked(HudKit.Bold, 1));
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.VerticalAlignment = VerticalAlignment.Center;
        top.AddChild(name);
        if (act.Rule != null) top.AddChild(HudKit.Chip(act.Rule, Palette.RuleGold, false, 12));
        var chip = new HBoxContainer();
        chip.AddThemeConstantOverride("separation", 3);
        chip.AddChild(HudKit.Icon(act.Icon, 14, HudKit.Fg));
        chip.AddChild(HudKit.Text(act.Action, 13, HudKit.Fg, HudKit.Bold));
        chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        top.AddChild(chip);
        v.AddChild(top);

        foreach (Result r in act.Results)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 5);
            row.AddChild(HudKit.Gap(30));
            row.AddChild(HudKit.Icon(r.Icon, 15, r.Bad ? Red : HudKit.Teal));
            if (r.Target.Length > 0) row.AddChild(HudKit.Text(r.Target, 15, r.TargetMine ? You : Them, HudKit.Semi));
            if (r.What.Length > 0) row.AddChild(HudKit.Text(r.What, 15, r.Bad ? Red : HudKit.Fg, HudKit.Bold));
            if (r.Chip != null) row.AddChild(HudKit.Chip(r.Chip, Red, true, 11));
            v.AddChild(row);
        }
        if (act.Results.Length == 0)
        {
            var row = new HBoxContainer();
            row.AddChild(HudKit.Gap(30));
            row.AddChild(HudKit.Text(act.Rule != null ? "redeployed before the game" : "nothing in range", 13, HudKit.Dim, HudKit.Medium));
            v.AddChild(row);
        }

        var box = new ChamferBox
        {
            Fill = hovered ? new Color(1, 1, 1, 0.1f) : latest ? new Color(HudKit.Lime, 0.07f) : new Color(0, 0, 0, 0.25f),
            Border = hovered ? Colors.White with { A = 0.6f } : HudKit.Edge with { A = 0.35f },
            Cut = 6, Accent = side, AccentLength = 16,
            ContentMarginLeft = 6, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        return HudKit.Panel(box, v);
    }

    // ---------------------------------------------------------------- B

    private static Control ReportLines()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        v.AddThemeConstantOverride("separation", 3);
        v.AddChild(Header("R - full report"));
        v.AddChild(RoundBar("ROUND 2"));
        foreach (Act act in Round2)
        {
            Color side = act.Mine ? You : Them;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 5);
            row.AddChild(new ColorRect { Color = side, CustomMinimumSize = new Vector2(3, 0) });
            row.AddChild(HudKit.Text(act.Unit, 14, side, HudKit.Bold));
            row.AddChild(HudKit.Icon(act.Icon, 13, HudKit.Dim));
            row.AddChild(HudKit.Text(act.Action.ToLowerInvariant(), 13, HudKit.Dim, HudKit.Semi));
            foreach (Result r in act.Results)
            {
                row.AddChild(HudKit.Icon(r.Icon, 13, r.Bad ? Red : HudKit.Teal));
                if (r.Target.Length > 0) row.AddChild(HudKit.Text(r.Target, 14, r.TargetMine ? You : Them, HudKit.Semi));
                row.AddChild(HudKit.Text(r.Chip ?? r.What, 14, r.Bad ? Red : HudKit.Fg, HudKit.Bold));
            }
            var flow = HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.25f), Border = Colors.Transparent, Cut = 4, ContentMarginLeft = 2, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 }, row);
            v.AddChild(flow);
        }
        return HudKit.Panel(Panel(), v);
    }

    // ---------------------------------------------------------------- hover

    private static Control HoverCard()
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        v.AddThemeConstantOverride("separation", 4);
        v.AddChild(HudKit.Text("ELITE WEAPON MASTERS charge AEREA GUARD", 15, Them, HudKit.Tracked(HudKit.Bold, 1)));
        void Line(string who, string what, string dice)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 6);
            var w = HudKit.Text(who, 14, HudKit.Dim, HudKit.Semi);
            w.CustomMinimumSize = new Vector2(150, 0);
            h.AddChild(w);
            h.AddChild(HudKit.Text(what, 14, HudKit.Fg, HudKit.Bold));
            h.AddChild(HudKit.Text(dice, 13, HudKit.Dim, HudKit.Medium));
            v.AddChild(h);
        }
        Line("Great Weapons", "4/8 hit", "(3+)  6 6 5 3 2 2 1 1");
        Line("Aerea Guard saves", "3/4", "(4+, AP2)  5 5 4 2");
        Line("Aerea Guard strike back", "2/3 hit", "(4+)  5 4 1");
        Line("Weapon Masters save", "0/2", "(4+)  3 2");
        Line("Melee", "4 vs 2", "Weapon Masters win");
        Line("Morale", "failed", "(4+)  2 - Shaken");
        var rules = new HBoxContainer();
        rules.AddThemeConstantOverride("separation", 4);
        rules.AddChild(HudKit.Text("Rules:", 13, HudKit.Dim, HudKit.Semi));
        rules.AddChild(HudKit.Chip("Furious", Palette.RuleGold, false, 12));
        rules.AddChild(HudKit.Chip("AP(2)", Palette.RuleGold, false, 12));
        v.AddChild(rules);
        return HudKit.Panel(new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.97f }, Border = Colors.White with { A = 0.6f }, Cut = 8,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        }, v);
    }
}
