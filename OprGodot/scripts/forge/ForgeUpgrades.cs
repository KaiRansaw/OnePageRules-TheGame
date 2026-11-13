using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Forge;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Forge;

/// <summary>
/// The upgrade editor (mockup screen C): the unit's upgrade groups on the left - round boxes pick one, square boxes
/// take several, - / + for counts - each option's cost on the right and, when a choice rules it out, greyed with the
/// reason under it. On the right the unit's sheet as it will play, new weapons, rules and changed stats in green.
/// Done keeps the picks; Cancel (or Esc) puts the unit back the way it was.
/// </summary>
public partial class ArmyForgeScreen
{
    private int _upgradeRow;
    private List<List<UpgradeChoice>>? _upgradeUndo;
    private UnitFileEntry? _upgradeBefore;

    private void OpenUpgrades(int row)
    {
        _upgradeRow = row;
        _upgradeUndo = _s!.Snapshot();
        _upgradeBefore = _s.Detail(row).Unit;
        _view = View.Upgrades;
        Rebuild();
    }

    private void CancelUpgrades()
    {
        if (_upgradeUndo != null) _s!.Restore(_upgradeUndo);
        _upgradeUndo = null;
        _view = View.Main;
        Rebuild();
    }

    private Control BuildUpgrades()
    {
        var s = _s!;
        int row = _upgradeRow;
        BuilderUnit bu = s.List.Units[row];
        RosterUnit roster = s.Roster(bu.RosterUnitId)!;
        var (unit, items) = s.Detail(row);
        string armyName = string.IsNullOrWhiteSpace(s.List.Name) ? s.Book.Name : $"{s.Book.Name} - {s.List.Name}";
        TopTitle(armyName);
        _top.AddChild(Ui.Grow());
        _top.AddChild(TopButton("Save", "Save", "Name the army and save it.", OpenSave));

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 16);

        // ---- left: the groups
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f };
        left.AddThemeConstantOverride("separation", 10);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        var title = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeConstantOverride("separation", -4);
        title.AddChild(HudKit.Text(unit.Name.ToUpperInvariant(), 26, HudKit.Fg, HudKit.Bold));
        int basePts = roster.BasePointCost, extra = unit.PointCost - basePts;
        title.AddChild(HudKit.Text($"{basePts} pts {(extra >= 0 ? "+" : "-")} {Math.Abs(extra)} upgrades = {unit.PointCost} pts", 16, Palette.PointsBlue, HudKit.Bold));
        head.AddChild(title);
        var cancel = Ui.Button("Cancel", CancelUpgrades, "Put the upgrades back how they were (Esc).", "BackButton");
        cancel.Name = "CancelUpgrades";
        cancel.CustomMinimumSize = new Vector2(110, 42);
        head.AddChild(cancel);
        var done = Ui.Button("Done", () => { _upgradeUndo = null; _view = View.Main; Rebuild(); }, "Keep these upgrades.", "CommitButton");
        done.Name = "Done";
        done.CustomMinimumSize = new Vector2(130, 42);
        head.AddChild(done);
        left.AddChild(head);
        if (s.PartnerOf(row) >= 0)
            left.AddChild(Wrapped("Combined unit: groups marked LINKED change both halves.", 14, Palette.PointsBlue));

        var groups = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        groups.AddThemeConstantOverride("separation", 10);
        foreach (ForgeRules.SectionView view in s.Sections(row)) groups.AddChild(Group(row, view));
        groups.AddChild(Wrapped("Hover a rule name to read the book's wording. Options a choice rules out go grey and say why.", 14, HudKit.Dim));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(groups);
        left.AddChild(scroll);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(left, 16, 12, 16, 12)).Also(p => { p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsStretchRatio = 1.4f; }));

        // ---- right: the unit as it will play
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 10);
        right.AddChild(HudKit.Heading("The unit with these upgrades", 16));
        Gear(right, unit, items, _upgradeBefore);
        right.AddChild(Wrapped("Changes show green until you press Done.", 14, HudKit.Dim));
        var rs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        rs.AddChild(right);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(rs, 16, 12, 16, 12)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return body;
    }

    private Control Group(int row, ForgeRules.SectionView view)
    {
        var s = _s!;
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
        foreach (ForgeRules.OptionView o in view.Options) col.AddChild(OptionRow(row, view, o, radio));
        return HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(col, 12, 8, 12, 8));
    }

    private Control OptionRow(int row, ForgeRules.SectionView view, ForgeRules.OptionView o, ButtonGroup? radio)
    {
        var s = _s!;
        var wrap = new VBoxContainer();
        wrap.AddThemeConstantOverride("separation", 0);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        string tip = OptionTip(o.Option);
        string name = SafeName($"Opt_{view.Section.Label}_{o.Option.Label}");

        if (view.Control == ForgeRules.EControl.Stepper)
        {
            var minus = new Button { Text = "-", Disabled = o.Count <= 0, CustomMinimumSize = new Vector2(32, 30), FocusMode = FocusModeEnum.None };
            minus.Pressed += () => { s.Choose(row, view.Section, o.Option.Id, o.Count - 1); Rebuild(); };
            line.AddChild(minus);
            line.AddChild(HudKit.Text(o.Count.ToString(), 17, o.Count > 0 ? Palette.Lime : HudKit.Fg, HudKit.Bold).Also(l => { l.CustomMinimumSize = new Vector2(26, 0); l.HorizontalAlignment = HorizontalAlignment.Center; }));
            var plus = new Button { Name = name, Text = "+", Disabled = o.Count >= o.Max, CustomMinimumSize = new Vector2(32, 30), FocusMode = FocusModeEnum.None };
            plus.Pressed += () => { s.Choose(row, view.Section, o.Option.Id, o.Count + 1); Rebuild(); };
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
                Rebuild();
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
    private string OptionTip(UpgradeOption option)
    {
        var rules = new List<SpecialRuleEntry>(option.RulesGained);
        foreach (WeaponFileEntry w in option.WeaponsGained) rules.AddRange(w.SpecialRules);
        foreach (ItemEntry i in option.ItemsGained) rules.AddRange(i.Rules);
        var distinct = rules.GroupBy(r => r.PrintableName).Select(g => g.First()).ToList();
        string head = ForgeRules.OptionSummary(option);
        return distinct.Count == 0 ? head : head + "\n\n" + string.Join("\n\n", distinct.Select(r => _s!.Glossary.Tooltip(r)));
    }
}

internal static class ControlExtensions
{
    /// <summary>Configure a control inline and keep building.</summary>
    public static T Also<T>(this T c, Action<T> configure) where T : Control
    {
        configure(c);
        return c;
    }
}
