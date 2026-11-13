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
            left.AddChild(ForgeWidgets.Wrapped("Combined unit: groups marked LINKED change both halves.", 14, Palette.PointsBlue));

        var groups = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        groups.AddThemeConstantOverride("separation", 10);
        foreach (Node g in ForgeWidgets.Groups(s, row, Rebuild).GetChildren().ToList()) { g.GetParent().RemoveChild(g); groups.AddChild(g); }
        groups.AddChild(ForgeWidgets.Wrapped("Hover a rule name to read the book's wording. Options a choice rules out go grey and say why.", 14, HudKit.Dim));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(groups);
        left.AddChild(scroll);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(left, 16, 12, 16, 12)).Also(p => { p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsStretchRatio = 1.4f; }));

        // ---- right: the unit as it will play
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 10);
        right.AddChild(HudKit.Heading("The unit with these upgrades", 16));
        ForgeWidgets.Gear(_s!.Glossary, right, unit, items, _upgradeBefore);
        right.AddChild(ForgeWidgets.Wrapped("Changes show green until you press Done.", 14, HudKit.Dim));
        var rs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        rs.AddChild(right);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(rs, 16, 12, 16, 12)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return body;
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
