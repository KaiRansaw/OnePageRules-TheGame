using FDG;
using FDG.Quest;
using FdgRaylib.Quest;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Quest;

/// <summary>
/// Pieces the Quest screens share (Quest Campaigns mockup, approved 2026-11-12): a hero's portrait card for picking a
/// party, a party card with wounds and power bars, the mission-mark strip, and the hero-sheet hover.
/// </summary>
internal static class QuestWidgets
{
    /// <summary>The 3D look a hero's portrait shows: its picked one, else its base hero's first version.</summary>
    public static string LookOf(QuestHero h) => h.Look ?? QuestArmy.QuestHeroLook(h);

    public static Control Portrait(QuestHero h, float size) =>
        HudKit.Portrait(h.Faction, h.BaseHeroName, "person", size, HudKit.Friendly, look: LookOf(h));

    /// <summary>A hero card to pick for a party: portrait, name, level and class, base hero, and a note line.</summary>
    public static Button PickCard(QuestHero h, string note, Color noteColour, bool picked, bool dimmed, Action click)
    {
        var b = new Button { Name = ForgeWidgets.SafeName("Pick_" + h.Name), CustomMinimumSize = new Vector2(170, 178), FocusMode = Control.FocusModeEnum.None,
            TooltipText = SheetText(h), Disabled = dimmed };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 8, ContentMarginBottom = 8 };
        if (picked) box = box.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        if (dimmed) b.Modulate = new Color(1, 1, 1, 0.5f);
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 2);
        col.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var pic = Portrait(h, 78);
        pic.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(pic);
        foreach (var (text, size, c) in new[] { (h.Name, 16, picked ? HudKit.Lime : HudKit.Fg), ($"Lv {h.Level} {h.Class}", 13, HudKit.Fg), ($"{h.Faction} - {h.BaseHeroName}", 12, HudKit.Dim), (note, 12, noteColour) })
            col.AddChild(Line(text, size, c, 154).Also(l => l.HorizontalAlignment = HorizontalAlignment.Center));
        b.AddChild(col);
        b.Pressed += click;
        return b;
    }

    /// <summary>
    /// A party member as it goes into the next mission: portrait, name, level and class, base hero, wounds (green) and
    /// power (blue) bars, money and items, injuries in red. Hover = the full sheet.
    /// </summary>
    public static Control PartyCard(string file, QuestHero? h, bool inChapter, EGameSystem system)
    {
        var panel = HudKit.Panel(new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8 });
        panel.CustomMinimumSize = new Vector2(230, 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        panel.AddChild(HudKit.Pad(row, 8, 8, 8, 8));
        if (h == null)
        {
            row.AddChild(Line($"Missing: {System.IO.Path.GetFileName(file)}", 13, Palette.WoundsRed, 200));
            return panel;
        }
        panel.TooltipText = SheetText(h);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        row.AddChild(Portrait(h, 62));
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 1);
        col.AddChild(Line(h.Name, 16, HudKit.Fg, 140, HudKit.Bold));
        col.AddChild(Line($"Lv {h.Level} {h.Class}", 13, Palette.HeroGold, 140));
        col.AddChild(Line(h.BaseHeroName, 12, HudKit.Dim, 140));
        int wounds = inChapter ? Math.Max(0, h.Tough - h.WoundsTaken) : h.Tough;
        int power = inChapter && h.PowerLeft >= 0 ? h.PowerLeft : h.Power;
        col.AddChild(Bar(wounds, h.Tough, Palette.Lime, $"Wounds {wounds} / {h.Tough}"));
        col.AddChild(Bar(power, h.Power, Palette.PointsBlue, $"Power {power} / {h.Power}"));
        col.AddChild(Line($"{QuestItemSkin.Money(system, h.Credits)} - {h.Items.Count} item{(h.Items.Count == 1 ? "" : "s")}", 12, HudKit.Fg, 140));
        col.AddChild(Line(h.Injuries.Count > 0 ? string.Join(", ", h.Injuries) : "No injuries", 12, h.Injuries.Count > 0 ? Palette.WoundsRed : HudKit.Dim, 140));
        row.AddChild(col);
        return panel;
    }

    private static Control Bar(int value, int max, Color colour, string tip)
    {
        var back = new Panel { CustomMinimumSize = new Vector2(130, 7), TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Pass };
        back.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.04f, 0.06f, 0.07f), BorderColor = HudKit.Edge, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        var fill = new ColorRect { Color = colour, MouseFilter = Control.MouseFilterEnum.Ignore };
        fill.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        fill.AnchorRight = max <= 0 ? 0 : Math.Clamp((float)value / max, 0, 1);
        back.AddChild(fill);
        return back;
    }

    /// <summary>A row of small boxes, one per mission: teal = won, lime outline = next, empty = still to play.</summary>
    public static Control Marks(IEnumerable<QuestCampaignSession.EMissionMark> marks, float box = 14, float gap = 3)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)gap);
        foreach (var m in marks)
        {
            var p = new Panel { CustomMinimumSize = new Vector2(box, box), MouseFilter = Control.MouseFilterEnum.Ignore };
            var sb = new StyleBoxFlat
            {
                BgColor = m == QuestCampaignSession.EMissionMark.Won ? Palette.AccentHot : new Color(0.04f, 0.06f, 0.07f),
                BorderColor = m == QuestCampaignSession.EMissionMark.Next ? Palette.Lime : m == QuestCampaignSession.EMissionMark.Won ? Palette.AccentHot : HudKit.Edge,
            };
            int w = m == QuestCampaignSession.EMissionMark.Next ? 2 : 1;
            sb.BorderWidthBottom = sb.BorderWidthTop = sb.BorderWidthLeft = sb.BorderWidthRight = w;
            p.AddThemeStyleboxOverride("panel", sb);
            row.AddChild(p);
        }
        return row;
    }

    public static Label Line(string text, int size, Color c, float width, Font? font = null)
    {
        var l = HudKit.Text(text, size, c, font ?? HudKit.Semi);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        l.CustomMinimumSize = new Vector2(width, 0);
        return l;
    }

    /// <summary>The hover: a hero's whole sheet in plain lines.</summary>
    public static string SheetText(QuestHero h)
    {
        var lines = new List<string>
        {
            $"{h.Name} - Level {h.Level} {h.Class}",
            $"{h.Faction} - {h.BaseHeroName} - {h.Points} pts",
            $"XP {h.Xp}" + (h.Level < QuestProgression.MaxLevel ? $" (level {h.Level + 1} at {QuestProgression.XpForLevel[h.Level + 1]})" : ""),
            $"Qua {h.Quality}+  Def {h.Defense}+  Tough {h.Tough}  Pow {h.Power}  Str {h.Strength}+  Dex {h.Dexterity}+  Wil {h.Willpower}+",
            "Skills: " + (h.Skills.Count == 0 ? "-" : string.Join(", ", h.Skills)),
            "Items: " + (h.Items.Count == 0 ? "-" : string.Join(", ", h.Items)),
            "Injuries: " + (h.Injuries.Count == 0 ? "-" : string.Join(", ", h.Injuries)),
        };
        return string.Join("\n", lines);
    }
}

/// <summary>
/// "Choose heroes..." in a one-off Quest mission's lobby: the saved heroes of this Quest game as portrait cards, pick
/// 2 to 4, Done. Heroes in an unfinished campaign are left out (a hero belongs to one campaign).
/// </summary>
public partial class HeroPartyPopup : Control
{
    public EGameSystem System { get; set; } = EGameSystem.GFStarQuest;
    public List<string> Picked { get; set; } = new();
    public event Action<List<string>>? Done;

    private HFlowContainer _cards = null!;
    private Label _count = null!;
    private Button _done = null!;
    private List<(string Path, QuestHero Hero, QuestCampaign? TakenBy)> _heroes = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(900, 560) };
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(HudKit.Text("CHOOSE HEROES", 28, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(HudKit.Text("Pick 2 to 4 heroes for the party. Heroes in a campaign play in that campaign only.", 15, HudKit.Dim, HudKit.Semi));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _cards = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 8);
        _cards.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(_cards);
        col.AddChild(scroll);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _count = HudKit.Text("", 16, HudKit.Fg, HudKit.Semi);
        _count.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_count);
        row.AddChild(Ui.Button("Cancel", QueueFree, "Close, nothing changed.", "BackButton").Also(b => b.Name = "PartyCancel"));
        _done = Ui.Button("DONE", () => { Done?.Invoke(Picked.ToList()); QueueFree(); }, "Use this party.", "CommitButton");
        _done.Name = "PartyDone";
        row.AddChild(_done);
        col.AddChild(row);
        HudKit.Place(this, HudKit.Panel(HudKit.PanelBox(14), HudKit.Pad(col, 20, 16, 20, 16)), LayoutPreset.Center);
        _heroes = new QuestCampaignSession(System).HeroesForNew();
        Build();
    }

    private void Build()
    {
        foreach (Node old in _cards.GetChildren()) { _cards.RemoveChild(old); old.QueueFree(); }
        if (_heroes.Count == 0)
            _cards.AddChild(HudKit.Text($"No heroes yet - make some with Build Heroes under {GameSystemInfo.DisplayName(System)}.", 16, HudKit.Dim, HudKit.Semi));
        foreach (var (path, hero, taken) in _heroes)
        {
            string p = path;
            bool on = Picked.Contains(p, StringComparer.OrdinalIgnoreCase);
            _cards.AddChild(QuestWidgets.PickCard(hero, taken != null ? $"in \"{taken.Name}\"" : on ? "IN PARTY" : $"{hero.Points} pts",
                taken != null ? Palette.HeroGold : on ? Palette.Lime : HudKit.Dim, on, taken != null, () =>
                {
                    if (on) Picked.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
                    else if (Picked.Count < 4) Picked.Add(p);
                    Build();
                }));
        }
        int n = Picked.Count;
        _count.Text = n < 2 ? $"{n} picked - pick at least 2." : $"{n} picked - {_heroes.Where(h => Picked.Contains(h.Path)).Sum(h => h.Hero.Points)} pts";
        _count.AddThemeColorOverride("font_color", n < 2 ? Palette.HeroGold : HudKit.Fg);
        _done.Disabled = n is < 2 or > 4;
    }

    /// <summary>For the screenshot driver: pick the first <paramref name="count"/> free heroes and press Done.</summary>
    public void PickFirst(int count)
    {
        Picked = _heroes.Where(h => h.TakenBy == null).Take(count).Select(h => h.Path).ToList();
        Done?.Invoke(Picked.ToList());
        QueueFree();
    }
}
