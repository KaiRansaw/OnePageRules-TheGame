using System.Linq;
using FDG;
using FDG.StageResolution.Requests;
using Godot;
using OprGodot.Hud;
using OprGodot.Forge;

namespace OprGodot.Battle;

/// <summary>
/// Relic Wargear / Magic Items: equipping a Hero before deployment (Kai, 2026-11-25, "Relic Wargear Mockups", boards
/// 2A-2C). Your Heroes down the left with what each carries; the items in their six groups (Free and Limited - a
/// rolled group lights up, the rest are greyed) or the two rolled items as big cards (Randomized); the picked item in
/// full on the right; Equip or No relic. The engine asks one Hero at a time, so the screen is rebuilt for each.
/// </summary>
public sealed class RelicPickDecision : Decision<string>
{
    private readonly StringSelectionRequest _r;
    private BattleScreen? _s;
    private Control? _screen;
    private string _picked;

    public RelicPickDecision(StringSelectionRequest r)
    {
        _r = r;
        _picked = r.Recommended is { } rec && r.ValidOptions.Contains(rec) && rec != RelicWargear.NoRelic ? rec
            : r.ValidOptions.First();
    }

    /// <summary>Is this the engine's relic question?</summary>
    public static bool Fits(StringSelectionRequest r) =>
        r.ValidOptions.Contains(RelicWargear.NoRelic) && (r.DisplayName == "Relic Wargear" || r.DisplayName == "Magic Items");

    private string Hero
    {
        get
        {
            string h = RelicWargear.HeroInQuestion(_r.Instructions);
            int with = h.IndexOf(" (with ", global::System.StringComparison.Ordinal);
            return with > 0 ? h[..with] : h;
        }
    }

    public override string Prompt => $"{_r.DisplayName}: {Hero}";
    public override string Hint => "Pick an item for this Hero, or leave it without one.";
    public override string? CancelLabel => null;
    public override string? ConfirmText => $"Equip the {_picked}?";
    public override bool HideConfirmBox => true;
    public override void Confirm() => Pick(_picked);
    public override void AutoPlay(BattleScreen s) => Pick(_r.Recommended ?? _r.ValidOptions[0]);

    private void Pick(string option)
    {
        if (!_r.ValidOptions.Contains(option)) option = _r.Recommended ?? RelicWargear.NoRelic;
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

    private EGameSystem System => _s?.Session.Lobby.Vm.GameSystem ?? EGameSystem.GrimdarkFuture;

    private void Build()
    {
        if (_s == null) return;
        Close();
        RelicWargearState? state = RelicWargear.State(_s.Session.Table.DataStore);
        int method = state?.Method ?? RelicWargear.Free;
        var table = RelicWargear.Table(System);

        var veil = new PanelContainer { Name = "RelicPick", MouseFilter = Control.MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        veil.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.03f, 0.05f, 0.05f, 0.95f) });
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        veil.AddChild(HudKit.Pad(col, 40, 28, 40, 26));

        // Heading.
        var heroes = RelicWargear.HeroesOf(_s.Session.Table.DataStore, _r.TargetPlayerID);
        var mine = state?.Picks.Where(p => p.Owner == _r.TargetPlayerID.ToString()).ToList() ?? new();
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 18);
        head.AddChild(HudKit.Text(_r.DisplayName.ToUpperInvariant(), 32, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 2)));
        head.AddChild(HudKit.Text($"before deployment  -  {RelicWargear.MethodName(method)}: {RelicWargear.MethodText(method)}", 15,
            HudKit.Lime, HudKit.Semi).Also(l => { l.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                l.AutowrapMode = TextServer.AutowrapMode.WordSmart; }));
        head.AddChild(HudKit.Text($"{mine.Count} of {heroes.Count} Heroes equipped", 16, HudKit.Dim, HudKit.Semi).Also(l => l.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd));
        col.AddChild(head);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 18);
        col.AddChild(body);

        // Left: your Heroes.
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        left.AddThemeConstantOverride("separation", 8);
        left.AddChild(HudKit.Text("YOUR HEROES", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        foreach (RelicWargear.HeroSlot h in heroes)
        {
            RelicPick? has = mine.FirstOrDefault(p => p.Model == h.Model.ID.ToString());
            bool now = Hero == h.Name && has == null;
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", 1);
            card.AddChild(HudKit.Text(h.Name, 19, now ? HudKit.Lime : HudKit.Fg, HudKit.Exo(800)));
            card.AddChild(HudKit.Text(h.Unit.HeroAttachment != null ? $"joined to {h.Unit.Name}" : "on its own", 14, HudKit.Dim, HudKit.Medium));
            card.AddChild(HudKit.Text(has != null ? has.Item + (has.Weapon != null ? $" ({has.Weapon})" : "") : now ? "picking now" : "-",
                15, has != null ? HudKit.Gold : HudKit.Dim, HudKit.Bold));
            var box = HudKit.PanelBox(8);
            if (now) box.Border = HudKit.Lime;
            left.AddChild(HudKit.Panel(box, HudKit.Pad(card, 12, 8, 12, 8)));
        }
        left.AddChild(HudKit.Grow());
        left.AddChild(HudKit.Text("A Hero can go without a relic.", 14, HudKit.Dim, HudKit.Medium).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        body.AddChild(left);

        // Middle: what was rolled, then the items.
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", 10);
        string rolled = _r.Instructions.Contains(": ") ? _r.Instructions[(_r.Instructions.IndexOf(": ", global::System.StringComparison.Ordinal) + 2)..] : _r.Instructions;
        mid.AddChild(HudKit.Panel(HudKit.PanelBox(8), HudKit.Pad(HudKit.Text($"{Hero}: {rolled}", 18, HudKit.Fg, HudKit.Semi)
            .Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart), 14, 10, 14, 10)));
        var offered = _r.ValidOptions.Where(o => o != RelicWargear.NoRelic).ToList();
        if (method == RelicWargear.Randomized)
        {
            var two = new HBoxContainer();
            two.AddThemeConstantOverride("separation", 14);
            foreach (string o in offered) two.AddChild(BigCard(table.First(d => d.Name == o)));
            mid.AddChild(two);
            mid.AddChild(HudKit.Text("A die that lands on an item your army already has is rolled again.", 14, HudKit.Dim, HudKit.Medium));
        }
        else
        {
            var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 10);
            grid.AddThemeConstantOverride("v_separation", 10);
            for (int c = 0; c < 6; c++) grid.AddChild(Group(c, table, offered));
            mid.AddChild(grid);
        }
        body.AddChild(mid);

        // Right: the picked item in full.
        RelicWargear.Def picked = table.First(d => d.Name == _picked);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        right.AddThemeConstantOverride("separation", 10);
        right.AddChild(HudKit.Text($"{Hero.ToUpperInvariant()}  -  SELECTED", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        right.AddChild(HudKit.Text(picked.Name, 28, HudKit.Gold, HudKit.Exo(800)).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        right.AddChild(HudKit.Text($"{RelicWargear.Categories[picked.Category].ToUpperInvariant()} {picked.Number}  -  "
            + (picked.Kind == RelicWargear.EKind.OncePerGame ? "ONCE PER GAME" : "ALWAYS ON"), 12, HudKit.Teal, HudKit.Tracked(HudKit.Bold, 2)));
        right.AddChild(HudKit.Text(picked.Text, 17, HudKit.Fg, HudKit.Medium).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(356, 0); }));
        if (picked.Kind is RelicWargear.EKind.Weapon or RelicWargear.EKind.MeleeWeapon)
            right.AddChild(HudKit.Text("You pick the weapon next.", 14, HudKit.Dim, HudKit.Medium));
        if (picked.Kind == RelicWargear.EKind.Ranger)
            right.AddChild(HudKit.Text("You pick Ambush or Scout next.", 14, HudKit.Dim, HudKit.Medium));
        right.AddChild(HudKit.Grow());
        right.AddChild(HudKit.Text("YOUR ARMY SO FAR", 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        foreach (RelicPick p in mine)
        {
            var row = new HBoxContainer();
            row.AddChild(HudKit.Text(p.Hero, 15, HudKit.Fg, HudKit.Bold));
            row.AddChild(HudKit.Grow());
            row.AddChild(HudKit.Text(p.Item, 15, HudKit.Gold, HudKit.Semi));
            right.AddChild(row);
        }
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        var equip = Ui.Button($"EQUIP ON {Hero.ToUpperInvariant()}", () => Pick(_picked), $"Give {Hero} the {_picked}.", "CommitButton");
        equip.Name = "RelicEquip";
        equip.CustomMinimumSize = new Vector2(0, 54);
        equip.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttons.AddChild(equip);
        var none = Ui.Button("NO RELIC", () => Pick(RelicWargear.NoRelic), "This Hero goes without.", "BackButton");
        none.Name = "RelicNone";
        none.CustomMinimumSize = new Vector2(120, 54);
        buttons.AddChild(none);
        right.AddChild(buttons);
        body.AddChild(HudKit.Panel(HudKit.PanelBox(10), HudKit.Pad(right, 22, 18, 22, 18)));

        _s.AddChild(veil);
        _screen = veil;
        Version++;
    }

    /// <summary>One group of six: the items you can take are buttons; the rest are greyed with why.</summary>
    private Control Group(int c, System.Collections.Generic.IReadOnlyList<RelicWargear.Def> table, System.Collections.Generic.List<string> offered)
    {
        bool open = table.Any(d => d.Category == c && offered.Contains(d.Name));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        v.AddChild(HudKit.Text($"{c + 1}  {RelicWargear.Categories[c].ToUpperInvariant()}", 12, open ? HudKit.Teal : HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        foreach (RelicWargear.Def d in table.Where(d => d.Category == c).OrderBy(d => d.Number))
        {
            bool can = offered.Contains(d.Name);
            string why = _r.InvalidOptions.FirstOrDefault(i => i.Option == d.Name)?.Reason ?? "";
            var b = new Button
            {
                Name = "Relic" + new string(d.Name.Where(char.IsLetterOrDigit).ToArray()),
                Text = $"{d.Number}. {d.Name}" + (can ? (d.Kind == RelicWargear.EKind.OncePerGame ? "   once" : "") : why == "not rolled" ? "" : $"   {why}"),
                Disabled = !can, Flat = d.Name != _picked, Alignment = HorizontalAlignment.Left,
                TooltipText = d.Text, ThemeTypeVariation = d.Name == _picked ? "CommitButton" : "",
            };
            b.AddThemeFontSizeOverride("font_size", 15);
            b.Pressed += () => { _picked = d.Name; Build(); };
            v.AddChild(b);
        }
        var box = HudKit.PanelBox(8);
        if (open && offered.Count < 30) box.Border = HudKit.Lime;
        var panel = HudKit.Panel(box, HudKit.Pad(v, 10, 8, 10, 8));
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.Modulate = open ? Colors.White : new Color(1, 1, 1, 0.45f);
        return panel;
    }

    /// <summary>Randomized: each of the two rolled items as a big card.</summary>
    private Control BigCard(RelicWargear.Def d)
    {
        var b = new Button { Name = "Relic" + new string(d.Name.Where(char.IsLetterOrDigit).ToArray()), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200), ThemeTypeVariation = d.Name == _picked ? "CommitButton" : "", TooltipText = d.Text };
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        v.OffsetLeft = 18; v.OffsetRight = -18; v.OffsetTop = 14; v.OffsetBottom = -14;
        v.AddThemeConstantOverride("separation", 6);
        v.AddChild(HudKit.Text($"ROLLED {d.Number}  -  {RelicWargear.Categories[d.Category].ToUpperInvariant()}", 13, HudKit.Teal, HudKit.Tracked(HudKit.Bold, 2)));
        v.AddChild(HudKit.Text(d.Name, 26, d.Name == _picked ? HudKit.Gold : HudKit.Fg, HudKit.Exo(800)));
        v.AddChild(HudKit.Text(d.Text, 16, HudKit.Fg, HudKit.Medium).Also(l => l.AutowrapMode = TextServer.AutowrapMode.WordSmart));
        v.AddChild(HudKit.Text(d.Kind == RelicWargear.EKind.OncePerGame ? "Once per game" : "Always on", 14, HudKit.Dim, HudKit.Medium));
        foreach (Node n in v.GetChildren()) if (n is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
        b.AddChild(v);
        b.Pressed += () => { _picked = d.Name; Build(); };
        return b;
    }
}
