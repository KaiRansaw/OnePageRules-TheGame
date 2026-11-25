using FDG;
using FDG.StageResolution.Requests;
using FdgRaylib.Game;
using Godot;
using OprGodot.Hud;
using OprGodot.Forge;

namespace OprGodot.Battle;

/// <summary>
/// Command Points: picking your doctrine before deployment (Kai, 2026-11-25, "Doctrines and Stratagems Mockups",
/// screen 1). The six doctrines as cards with their four stratagems and costs, the picked one's stratagems in full on
/// the right with when each can be used, the Universal ones you always have, Roll for it, and Confirm. Replaces the
/// plain list the engine's "Pick your doctrine" question used to show.
/// </summary>
public sealed class DoctrinePickDecision : Decision<string>
{
    public const string DisplayName = "Pick your doctrine";

    private readonly StringSelectionRequest _r;
    private BattleScreen? _s;
    private Control? _screen;
    private int _picked = 3;

    public DoctrinePickDecision(StringSelectionRequest r) => _r = r;

    /// <summary>Is this the engine's doctrine question?</summary>
    public static bool Fits(StringSelectionRequest r) =>
        r.DisplayName == DisplayName && r.ValidOptions.Contains(CommandPoints.RollDoctrine);

    public override string Prompt => "Pick your doctrine";
    public override string Hint => "You always have the Universal stratagems, plus the four of the doctrine you pick.";
    public override string? CancelLabel => null;
    public override string? ConfirmText => $"Use the {CommandPoints.DoctrineName(_picked)}?";
    public override bool HideConfirmBox => true;
    public override void Confirm() => Pick(CommandPoints.DoctrineName(_picked));
    public override void AutoPlay(BattleScreen s) => Pick(_r.Recommended ?? CommandPoints.RollDoctrine);

    private void Pick(string option)
    {
        if (!_r.ValidOptions.Contains(option)) option = CommandPoints.RollDoctrine;
        Complete(option);
        Close();
    }

    public override void Shown(BattleScreen s) { _s = s; Build(); }
    public override void Closed(BattleScreen s) => Close();

    private void Close()
    {
        if (_screen != null && GodotObject.IsInstanceValid(_screen)) _screen.QueueFree();
        _screen = null;
    }

    private void Build()
    {
        if (_s == null) return;
        Close();
        var veil = new PanelContainer { Name = "DoctrinePick", MouseFilter = Control.MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        veil.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.03f, 0.05f, 0.05f, 0.95f) });
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 16);
        veil.AddChild(HudKit.Pad(col, 44, 32, 44, 28));

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 18);
        head.AddChild(HudKit.Text("CHOOSE YOUR DOCTRINE", 32, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        head.AddChild(HudKit.Text("Command Points  -  before deployment", 16, HudKit.Lime, HudKit.Semi).Also(l => l.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd));
        col.AddChild(head);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 22);
        col.AddChild(body);

        // Left: the six doctrines and Roll for it.
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 14);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        for (int d = 1; d <= 6; d++) grid.AddChild(Card(d));
        left.AddChild(grid);
        var roll = Ui.Button("ROLL FOR IT  -  a random doctrine", () => Pick(CommandPoints.RollDoctrine), "Leave it to the dice, as the bots do.", "BackButton");
        roll.Name = "DoctrineRoll";
        roll.CustomMinimumSize = new Vector2(0, 52);
        left.AddChild(roll);
        body.AddChild(left);

        // Right: the picked doctrine in full.
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(470, 0) };
        right.AddThemeConstantOverride("separation", 10);
        right.AddChild(HudKit.Text("YOUR PICK", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        right.AddChild(HudKit.Text(CommandPoints.DoctrineName(_picked), 28, HudKit.Lime, HudKit.Exo(800)));
        foreach (StratagemBook.Entry e in StratagemBook.OfDoctrine(_picked))
        {
            var block = new VBoxContainer();
            block.AddThemeConstantOverride("separation", 1);
            var top = new HBoxContainer();
            top.AddChild(HudKit.Text(e.Name, 19, HudKit.Fg, HudKit.Bold));
            top.AddChild(HudKit.Grow());
            top.AddChild(HudKit.Text($"{e.Cost} CP", 16, HudKit.Lime, HudKit.Bold));
            block.AddChild(top);
            block.AddChild(HudKit.Text("WHEN: " + e.When.ToUpperInvariant(), 12, HudKit.Teal, HudKit.Tracked(HudKit.Bold, 1)));
            block.AddChild(HudKit.Text(e.Text, 15, HudKit.Dim, HudKit.Medium).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(420, 0); }));
            right.AddChild(block);
        }
        right.AddChild(HudKit.Text("ALWAYS YOURS - UNIVERSAL", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 6);
        foreach (StratagemBook.Entry u in StratagemBook.Universal())
        {
            var chip = HudKit.Chip($"{u.Name}  {u.Cost}", HudKit.Dim, false, 13);
            chip.TooltipText = $"{u.Name} ({u.Cost} CP) - {u.When}\n{u.Text}";
            chip.MouseFilter = Control.MouseFilterEnum.Stop;
            chips.AddChild(chip);
        }
        right.AddChild(chips);
        right.AddChild(HudKit.Grow());
        var confirm = Ui.Button($"CONFIRM {StratagemBook.Short(_picked).ToUpperInvariant()}", () => Pick(CommandPoints.DoctrineName(_picked)),
            "Play with this doctrine.", "CommitButton");
        confirm.Name = "DoctrineConfirm";
        confirm.CustomMinimumSize = new Vector2(0, 56);
        right.AddChild(confirm);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(10), HudKit.Pad(right, 22, 18, 22, 18)));

        _s.AddChild(veil);
        _screen = veil;
        Version++;
    }

    private Control Card(int d)
    {
        var b = new Button { Name = $"Doctrine{d}", ThemeTypeVariation = d == _picked ? "CommitButton" : "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 150), TooltipText = StratagemBook.Theme(d) };
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        v.OffsetLeft = 14; v.OffsetRight = -14; v.OffsetTop = 10; v.OffsetBottom = -10;
        v.AddThemeConstantOverride("separation", 3);
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        top.AddChild(HudKit.Text(StratagemBook.Short(d), 21, d == _picked ? HudKit.Lime : HudKit.Fg, HudKit.Exo(800)));
        top.AddChild(HudKit.Grow());
        top.AddChild(HudKit.Text(StratagemBook.Theme(d), 13, HudKit.Dim, HudKit.Semi));
        v.AddChild(top);
        foreach (StratagemBook.Entry e in StratagemBook.OfDoctrine(d))
        {
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddChild(HudKit.Text(e.Name, 16, HudKit.Fg, HudKit.Semi));
            row.AddChild(HudKit.Grow());
            row.AddChild(HudKit.Text($"{e.Cost} CP", 15, HudKit.Lime, HudKit.Bold));
            v.AddChild(row);
        }
        foreach (Node n in v.GetChildren()) if (n is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
        b.AddChild(v);
        b.Pressed += () => { _picked = d; Build(); };
        return b;
    }
}
