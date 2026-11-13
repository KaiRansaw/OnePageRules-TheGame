using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Forge;
using FdgRaylib.Models3D;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;
using static Godot.Control;

namespace OprGodot.Forge;

/// <summary>
/// Army Forge pieces shared by the Forge and the Horde Shop (Kai's shop mockup, 2026-11-12: "same cards, same
/// upgrade editor as the Forge"): unit cards, the unit sheet, upgrade groups, and the downloaded-model library.
/// </summary>
internal static class ForgeWidgets
{
    private static UnitModelLibrary? _models;
    private static bool _modelsScanned;

    /// <summary>The downloaded unit models, scanned once per run (Rescan after a download).</summary>
    public static UnitModelLibrary? Models()
    {
        if (_modelsScanned) return _models;
        _modelsScanned = true;
        try
        {
            UnitModelMap? map = null;
            try { map = UnitModelMap.Load(); } catch (Exception) { }
            _models = UnitModelLibrary.Scan(UnitModelPaths.Root, map);
        }
        catch (Exception e) { GD.PrintErr("Army Forge: model folder unreadable: " + e.Message); }
        return _models;
    }

    public static Control Card(string faction, string name, string unit, int pts, string tag, bool selected, Action click, Action? doubleClick, string? look = null)
    {
        var b = new Button { Name = SafeName(name), CustomMinimumSize = new Vector2(150, 0), FocusMode = FocusModeEnum.None, ToggleMode = false, TooltipText = unit };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6 };
        if (selected) box = box.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(HudKit.Portrait(faction, unit, "person", 34, HudKit.Friendly, look: look));
        var txt = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        txt.AddThemeConstantOverride("separation", -4);
        var n = HudKit.Text(unit, 14, selected ? HudKit.Lime : HudKit.Fg, HudKit.Bold);
        n.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        n.CustomMinimumSize = new Vector2(96, 0);
        n.MouseFilter = MouseFilterEnum.Ignore;
        txt.AddChild(n);
        var p = HudKit.Text(tag.Length > 0 ? $"{pts} pts - {tag}" : $"{pts} pts", 13, tag == "!" ? Palette.WoundsRed : HudKit.Dim, HudKit.Semi);
        p.MouseFilter = MouseFilterEnum.Ignore;
        txt.AddChild(p);
        row.AddChild(txt);
        var pad = HudKit.Pad(row, 0, 0, 0, 0);
        pad.MouseFilter = MouseFilterEnum.Ignore;
        pad.SetAnchorsPreset(LayoutPreset.FullRect);
        b.AddChild(pad);
        b.CustomMinimumSize = new Vector2(160, 50);
        b.Pressed += click;
        if (doubleClick != null)
            b.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) doubleClick(); };
        return b;
    }

    public static string SafeName(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

    /// <summary>Stat chips, the weapon table, wargear and rules (hover = the book's wording). Weapons and rules not
    /// in <paramref name="before"/> are drawn green with "(new)" - the upgrade editor's live sheet.</summary>
    public static void Gear(RuleGlossary glossary, VBoxContainer col, UnitFileEntry unit, List<ItemEntry> items, UnitFileEntry? before)
    {
        var stats = new HFlowContainer();
        stats.AddThemeConstantOverride("h_separation", 6);
        stats.AddChild(StatChip("QUA", $"{unit.Quality}+", before != null && before.Quality != unit.Quality));
        stats.AddChild(StatChip("DEF", $"{unit.Defense}+", before != null && before.Defense != unit.Defense));
        if (ForgeRules.ToughValue(unit) is { } tough) stats.AddChild(StatChip("TOUGH", tough, before != null && ForgeRules.ToughValue(before) != tough));
        stats.AddChild(StatChip("MODELS", unit.ModelCount.ToString(), before != null && before.ModelCount != unit.ModelCount));
        if (before != null) stats.AddChild(StatChip("PTS", unit.PointCost.ToString(), before.PointCost != unit.PointCost));
        col.AddChild(stats);

        if (unit.Weapons.Count > 0)
        {
            var grid = new GridContainer { Columns = 5 };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 2);
            foreach (string h in new[] { "WEAPON", "RNG", "ATK", "AP", "SPECIAL" })
                grid.AddChild(HudKit.Text(h, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
            foreach (WeaponFileEntry w in unit.Weapons)
            {
                bool isNew = before != null && !before.Weapons.Any(o => o.Name == w.Name);
                Color c = isNew ? Palette.Lime : HudKit.Fg;
                var name = HudKit.Text(ForgeRules.CountedName(w.Quantity, w.Name) + (isNew ? " (new)" : ""), 14, c, HudKit.Semi);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                name.CustomMinimumSize = new Vector2(120, 0);
                grid.AddChild(name);
                grid.AddChild(HudKit.Text(ForgeRules.RangeText(w.RangeInches), 14, c, HudKit.Semi));
                grid.AddChild(HudKit.Text(ForgeRules.AttacksText(w.Attacks), 14, c, HudKit.Semi));
                grid.AddChild(HudKit.Text(ForgeRules.ApText(w.ArmorPenetration), 14, c, HudKit.Semi));
                var spe = HudKit.Text(w.SpecialRules.Count == 0 ? "-" : string.Join(", ", w.SpecialRules.Select(r => r.PrintableName)), 13, isNew ? Palette.Lime : Palette.RuleGold, HudKit.Semi);
                spe.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                spe.CustomMinimumSize = new Vector2(90, 0);
                if (w.SpecialRules.Count > 0)
                {
                    spe.MouseFilter = MouseFilterEnum.Stop;
                    spe.TooltipText = string.Join("\n\n", w.SpecialRules.Select(r => glossary.Tooltip(r)));
                }
                grid.AddChild(spe);
            }
            col.AddChild(grid);
        }

        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        foreach (ItemEntry item in items)
        {
            var chip = HudKit.Chip(ForgeRules.ItemSummary(item), HudKit.Fg, size: 14);
            chip.TooltipText = item.Rules.Count == 0 ? item.Name : string.Join("\n\n", item.Rules.Select(r => glossary.Tooltip(r)));
            chips.AddChild(chip);
        }
        foreach (SpecialRuleEntry r in unit.SpecialRules)
        {
            bool isNew = before != null && !before.SpecialRules.Any(o => o.PrintableName == r.PrintableName);
            var chip = HudKit.Chip(r.PrintableName + (isNew ? " (new)" : ""), isNew ? Palette.Lime : Palette.RuleGold, size: 14);
            chip.TooltipText = glossary.Tooltip(r);
            chips.AddChild(chip);
        }
        col.AddChild(chips);
    }

    public static Control StatChip(string label, string value, bool changed)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(HudKit.Text(label, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        row.AddChild(HudKit.Text(value, 17, changed ? Palette.Lime : HudKit.Fg, HudKit.Bold));
        var box = new ChamferBox { Fill = new Color(0, 0, 0, 0.3f), Border = changed ? Palette.Lime : HudKit.Edge, Cut = 5, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 2, ContentMarginBottom = 2 };
        return HudKit.Panel(box, row);
    }

    public static Label Wrapped(string text, int size, Color colour)
    {
        var l = HudKit.Text(text, size, colour, HudKit.Semi);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(10, 0);
        return l;
    }

    /// <summary>A unit's upgrade groups (mockup C): round boxes pick one, square boxes take several, - / + for
    /// counts, costs on the right, ruled-out options greyed with why. <paramref name="changed"/> runs after each pick.</summary>
    public static VBoxContainer Groups(ForgeSession s, int row, Action changed)
    {
        var groups = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        groups.AddThemeConstantOverride("separation", 10);
        foreach (ForgeRules.SectionView view in s.Sections(row)) groups.AddChild(Group(s, row, view, changed));
        return groups;
    }

    private static Control Group(ForgeSession s, int row, ForgeRules.SectionView view, Action changed)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        var label = HudKit.Text(view.Section.Label, 17, Palette.RuleGold, HudKit.Bold);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(label);
        string kind = view.Control switch
        {
            ForgeRules.EControl.PickOne => "pick one",
            ForgeRules.EControl.Stepper => view.Section.MaxApplications > 0 ? $"up to {view.Section.MaxApplications}" : "how many",
            _ => view.Options.Count == 1 ? "take it or not" : "pick any",
        };
        head.AddChild(HudKit.Text(kind, 13, HudKit.Dim, HudKit.Semi));
        if (view.Linked) head.AddChild(HudKit.Chip("LINKED", Palette.PointsBlue, size: 12));
        col.AddChild(head);
        if (view.NothingToReplace) col.AddChild(HudKit.Text("Nothing left to replace.", 13, HudKit.Dim, HudKit.Semi));

        ButtonGroup? radio = view.Control == ForgeRules.EControl.PickOne ? new ButtonGroup { AllowUnpress = true } : null;
        foreach (ForgeRules.OptionView o in view.Options) col.AddChild(OptionRow(s, row, view, o, radio, changed));
        return HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(col, 12, 8, 12, 8));
    }

    private static Control OptionRow(ForgeSession s, int row, ForgeRules.SectionView view, ForgeRules.OptionView o, ButtonGroup? radio, Action changed)
    {
        var wrap = new VBoxContainer();
        wrap.AddThemeConstantOverride("separation", 0);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        string tip = OptionTip(s, o.Option);
        string name = SafeName($"Opt_{view.Section.Label}_{o.Option.Label}");

        if (view.Control == ForgeRules.EControl.Stepper)
        {
            var minus = new Button { Text = "-", Disabled = o.Count <= 0, CustomMinimumSize = new Vector2(32, 30), FocusMode = FocusModeEnum.None };
            minus.Pressed += () => { s.Choose(row, view.Section, o.Option.Id, o.Count - 1); changed(); };
            line.AddChild(minus);
            line.AddChild(HudKit.Text(o.Count.ToString(), 17, o.Count > 0 ? Palette.Lime : HudKit.Fg, HudKit.Bold).Also(l => { l.CustomMinimumSize = new Vector2(26, 0); l.HorizontalAlignment = HorizontalAlignment.Center; }));
            var plus = new Button { Name = name, Text = "+", Disabled = o.Count >= o.Max, CustomMinimumSize = new Vector2(32, 30), FocusMode = FocusModeEnum.None };
            plus.Pressed += () => { s.Choose(row, view.Section, o.Option.Id, o.Count + 1); changed(); };
            line.AddChild(plus);
            var text = HudKit.Text(o.Option.Label, 15, o.Enabled ? HudKit.Fg : HudKit.Dim with { A = 0.6f }, HudKit.Semi);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.MouseFilter = MouseFilterEnum.Stop;
            text.TooltipText = tip;
            line.AddChild(text);
        }
        else
        {
            var box = new CheckBox
            {
                Name = name, Text = o.Option.Label, ButtonPressed = o.Chosen, Disabled = !o.Enabled, ButtonGroup = radio,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, TooltipText = tip,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            box.AddThemeFontSizeOverride("font_size", 15);
            if (o.Chosen) box.AddThemeColorOverride("font_color", Palette.Lime);
            // Clicking a picked round box again takes it back off (the "keep what it has" choice).
            box.Pressed += () =>
            {
                bool on = !o.Chosen;
                s.Choose(row, view.Section, on ? o.Option.Id : (view.Control == ForgeRules.EControl.PickOne ? string.Empty : o.Option.Id), on ? 1 : 0);
                changed();
            };
            line.AddChild(box);
        }

        int cost = o.Option.Cost;
        var price = HudKit.Text(ForgeRules.CostText(cost), 15, cost == 0 ? HudKit.Dim : Palette.PointsBlue, HudKit.Bold);
        price.CustomMinimumSize = new Vector2(44, 0);
        price.HorizontalAlignment = HorizontalAlignment.Right;
        line.AddChild(price);
        wrap.AddChild(line);
        if (!o.Enabled && o.WhyNot != null)
            wrap.AddChild(HudKit.Text("   " + o.WhyNot, 13, HudKit.Dim with { A = 0.8f }, HudKit.Semi));
        return wrap;
    }

    /// <summary>The option's own rules in the book's words, for its hover.</summary>
    private static string OptionTip(ForgeSession s, UpgradeOption option)
    {
        var rules = new List<SpecialRuleEntry>(option.RulesGained);
        foreach (WeaponFileEntry w in option.WeaponsGained) rules.AddRange(w.SpecialRules);
        foreach (ItemEntry i in option.ItemsGained) rules.AddRange(i.Rules);
        var distinct = rules.GroupBy(r => r.PrintableName).Select(g => g.First()).ToList();
        string head = ForgeRules.OptionSummary(option);
        return distinct.Count == 0 ? head : head + "\n\n" + string.Join("\n\n", distinct.Select(r => s.Glossary.Tooltip(r)));
    }
}
