using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib;
using FdgRaylib.Forge;
using FdgRaylib.Quest;
using FdgRaylib.SaveLoad;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;
using OprGodot.Quest;

namespace OprGodot.Campaign;

/// <summary>
/// The army campaign (Campaign Rules v3.6.0 - GF, Firefight, AoF, Skirmish), port step 5. Kai's Army Campaign mockup,
/// approved item by item 2026-11-12, on decision 21. One screen per game with five views:
/// <list type="bullet">
/// <item><b>A. Campaigns</b> - this game's campaigns only, with the scoreboard (you vs the rival).</item>
/// <item><b>B. New Campaign</b> - 4 tabs: Rules, Your army, The enemy, Missions. Armies are built in the Army Forge in
/// campaign mode or taken from a saved list.</item>
/// <item><b>C. The Camp</b> - unit panel, roster cards, next-mission column; the Your army / Enemy army switch tints the
/// screen red while you do the rival's books (house rule B17).</item>
/// <item><b>D. Recruit</b> - the Forge's roster cards and a cart, with the veteran option.</item>
/// <item><b>G. Journal</b> - the campaign's story and a page per mission.</item>
/// </list>
/// PLAY NEXT MISSION opens Mission Setup (E). The rules and bookkeeping are <see cref="ArmyCampaignSession"/> (FdgApp).
/// </summary>
public partial class ArmyCampaignsScreen : Control, IBackGuard
{
    private enum View { List, New, Camp, Recruit, Journal }

    public EGameSystem System { get; set; } = EGameSystem.GrimdarkFuture;

    private ArmyCampaignSession _s = null!;
    private View _view = View.List;
    private int _selected;
    private bool _enemySide;
    private int _unit;
    private int _journalMission = -1;

    private ArmyCampaignSession.Draft _draft = new();
    private int _tab;

    private readonly ArmyCampaignSession.Cart _cart = new();
    private string? _recruitPick;
    private Dictionary<string, (UnitFileEntry Unit, List<ItemEntry> Items)>? _cards;
    private RuleGlossary? _glossary;
    private string? _cardsFor;

    private HBoxContainer _top = null!;
    private MarginContainer _body = null!;
    private Label _status = null!;
    private ColorRect _tint = null!;

    private static readonly Color EnemyRed = new(0.95f, 0.42f, 0.36f);
    private string Game => GameSystemInfo.DisplayName(System);

    public override void _Ready()
    {
        _s = new ArmyCampaignSession(System);
        _tint = new ColorRect { Color = new Color(0.6f, 0.05f, 0.03f, 0.10f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _tint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_tint);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        foreach (string side in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);
        _top = new HBoxContainer();
        _top.AddThemeConstantOverride("separation", 12);
        root.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(_top, 14, 8, 14, 8)));
        _body = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_body);
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_status);
        VisibilityChanged += () => { if (Visible) { Reload(); Say(""); Rebuild(); } };
        Rebuild();
    }

    private void Reload()
    {
        string? path = Current?.Path;
        _s.Reload();
        if (path != null) Reselect(path);
    }

    private void Reselect(string path)
    {
        int i = _s.Campaigns.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _selected = i;
    }

    public bool AllowBack()
    {
        switch (_view)
        {
            case View.List: return true;
            case View.Recruit: _view = View.Camp; _cart.Clear(); break;
            case View.Journal: _view = Current != null ? View.Camp : View.List; break;
            case View.Camp: _view = View.List; _enemySide = false; break;
            default: _view = View.List; break;
        }
        Say("");
        Rebuild();
        return false;
    }

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    private ArmyCampaignSession.Entry? Current =>
        _s.Campaigns.Count == 0 ? null : _s.Campaigns[Math.Clamp(_selected, 0, _s.Campaigns.Count - 1)];

    /// <summary>The army being looked at in the Camp: yours, or (switched) the rival's.</summary>
    private ArmyCampaignSession.Entry? Side => Current is { } c ? (_enemySide ? _s.EnemyOf(c.Info) : c) : null;

    private void Rebuild()
    {
        foreach (Control holder in new Control[] { _top, _body })
            foreach (Node c in holder.GetChildren()) { holder.RemoveChild(c); c.QueueFree(); }
        bool red = _enemySide && _view is View.Camp or View.Recruit;
        _tint.Visible = red;
        TopBar();
        _body.AddChild(_view switch
        {
            View.New => NewView(),
            View.Camp => CampView(),
            View.Recruit => RecruitView(),
            View.Journal => JournalView(),
            _ => ListView(),
        });
    }

    // ================================================================== top bar

    private void TopBar()
    {
        string back = _view switch { View.New => "Cancel", View.Recruit => "Back to camp", _ => "Back" };
        var b = Ui.Button(back, App.I.Back, _view == View.List ? "Back to the game screen (Esc)." : "Back (Esc).", "BackButton");
        b.Name = "Back";
        b.CustomMinimumSize = new Vector2(130, 44);
        _top.AddChild(b);
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        var side = Side;
        string title = _view switch
        {
            View.New => "NEW CAMPAIGN",
            View.Camp => (side?.Info.Name ?? "").ToUpperInvariant() + (_enemySide ? "  (THE ENEMY)" : ""),
            View.Recruit => "RECRUIT",
            View.Journal => "JOURNAL - " + (Current?.Info.Name ?? "").ToUpperInvariant(),
            _ => $"{Game.ToUpperInvariant()} CAMPAIGNS",
        };
        t.AddChild(HudKit.Text(title, 30, _enemySide && _view is View.Camp or View.Recruit ? EnemyRed : HudKit.Fg, HudKit.Exo(800)));
        string sub = _view switch
        {
            View.New => Game,
            View.Camp when Current is { } c => CampSubtitle(c),
            View.Recruit when side is { } sd => $"{sd.Info.Name} - {sd.Army.Faction} - treasury {sd.Info.BankedPoints} pts",
            View.Journal => "The campaign's story, and a page per mission",
            _ => $"{Game} - {ArmyCampaignSession.Plural(_s.Campaigns.Count, "campaign")} (only this game's)",
        };
        t.AddChild(HudKit.Text(sub, 14, HudKit.Dim, HudKit.Semi));
        _top.AddChild(t);
        _top.AddChild(Ui.Grow());

        switch (_view)
        {
            case View.List:
                _top.AddChild(Ui.Button("+ NEW CAMPAIGN", StartNew, "Start a campaign: the rules, your army, the enemy, the missions.", "CommitButton")
                    .Also(x => { x.Name = "NewCampaign"; x.CustomMinimumSize = new Vector2(190, 44); }));
                break;
            case View.New:
            {
                if (_tab < 3)
                    _top.AddChild(Ui.Button("NEXT", () => { _tab++; Rebuild(); }, "The next tab.", "CommitButton")
                        .Also(x => { x.Name = "NextTab"; x.CustomMinimumSize = new Vector2(140, 44); }));
                string? problem = _s.NewProblem(_draft);
                _top.AddChild(Ui.Button("START CAMPAIGN", FinishNew, problem ?? "Save both armies and open the Camp.", _tab == 3 ? "CommitButton" : null)
                    .Also(x => { x.Name = "StartCampaign"; x.Disabled = problem != null; x.CustomMinimumSize = new Vector2(200, 44); }));
                break;
            }
            case View.Camp:
            {
                var you = Ui.Button("Your army", () => { _enemySide = false; _unit = 0; Rebuild(); }, "Your army's books.");
                you.Name = "YourArmy";
                you.ToggleMode = true; you.ButtonPressed = !_enemySide;
                _top.AddChild(you);
                var foe = Ui.Button("Enemy army", () => { _enemySide = true; _unit = 0; Rebuild(); },
                    Current is { } c2 && _s.EnemyOf(c2.Info) != null ? "Do the rival's books: its trait picks, upgrades and recruits (house rule B17). The screen tints red." : "This campaign has no standing enemy.");
                foe.Name = "EnemyArmy";
                foe.ToggleMode = true; foe.ButtonPressed = _enemySide;
                foe.Disabled = Current is not { } c3 || _s.EnemyOf(c3.Info) == null;
                _top.AddChild(foe);
                _top.AddChild(Ui.Button("Journal", OpenJournal, "The campaign's story, and what happened in every mission.").Also(x => x.Name = "Journal"));
                break;
            }
            case View.Recruit:
            {
                string? why = RecruitProblem();
                _top.AddChild(Ui.Button("CONFIRM RECRUITS", ConfirmRecruits, why ?? "Buy everything in the cart from the treasury.", "CommitButton")
                    .Also(x => { x.Name = "ConfirmRecruits"; x.Disabled = why != null; x.CustomMinimumSize = new Vector2(210, 44); }));
                break;
            }
            case View.Journal:
                _top.AddChild(Ui.Button("Export journal", ExportJournal, "Write the whole journal (story and every mission) to a text file beside the campaign.")
                    .Also(x => x.Name = "ExportJournal"));
                break;
        }
    }

    private string CampSubtitle(ArmyCampaignSession.Entry c)
    {
        var st = _s.StandingOf(c);
        return $"{Game} - {st.Describe()} - you {st.YourVp}, rival {st.RivalVp} - {ArmyCampaignSession.Plural(c.Info.Matches, "mission")} played"
               + (c.Info.NoPermadeath ? " - No Permadeath" : "");
    }

    // ================================================================== A. the list

    private Control ListView()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 6);
        left.AddChild(HudKit.Heading("Campaigns", 14));
        if (_s.Campaigns.Count == 0) left.AddChild(ForgeWidgets.Wrapped("No campaigns for this game yet. Press + NEW CAMPAIGN.", 15, HudKit.Dim));
        for (int i = 0; i < _s.Campaigns.Count; i++)
        {
            int idx = i;
            left.AddChild(ListCard(_s.Campaigns[i], i == _selected, () => { _selected = idx; Rebuild(); }));
        }
        left.AddChild(HudKit.Grow());
        left.AddChild(ForgeWidgets.Wrapped("The bar is your VP toward the target.", 12, HudKit.Dim));
        var ls = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ls.AddChild(left);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ls, 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(360, 0)));

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        if (Current is { } cur) ListDetail(right, cur);
        else right.AddChild(ForgeWidgets.Wrapped("Start a campaign with + NEW CAMPAIGN (top right). You and a rival army play mission after mission; " +
                                                 "units earn experience, get hurt, and the treasury buys upgrades and recruits - the Campaign Rules.", 16, HudKit.Dim));
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(right, 16, 12, 16, 12)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return row;
    }

    private Control ListCard(ArmyCampaignSession.Entry e, bool selected, Action click)
    {
        var (title, line) = _s.ListRow(e);
        var st = _s.StandingOf(e);
        var b = new Button { Name = ForgeWidgets.SafeName("Campaign_" + e.Info.Name), CustomMinimumSize = new Vector2(0, 86), FocusMode = FocusModeEnum.None };
        var box = new ChamferBox { Fill = HudKit.GlassLight, Border = selected ? HudKit.Lime : HudKit.Edge, Cut = 8 };
        if (selected) box.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f);
        HudKit.StyleButton(b, box, HudKit.Fg, 15);
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = 10; col.OffsetRight = -10; col.OffsetTop = 6; col.OffsetBottom = -6;
        col.AddChild(QuestWidgets.Line(title, 17, selected ? HudKit.Lime : HudKit.Fg, 310, HudKit.Bold));
        col.AddChild(QuestWidgets.Line(line, 13, st.IsOver ? Palette.Lime : HudKit.Dim, 310));
        col.AddChild(VpBar(st, 300, 10));
        b.AddChild(col);
        b.Pressed += click;
        return b;
    }

    private static Control VpBar(CampaignPreGame.CampaignStanding st, float width, float height)
    {
        var pb = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, st.HasTarget ? st.VpTarget : 1), Value = st.HasTarget ? st.YourVp : 0, ShowPercentage = false, CustomMinimumSize = new Vector2(width, height), MouseFilter = MouseFilterEnum.Ignore };
        pb.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Palette.Lime });
        pb.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.4f) });
        if (!st.HasTarget) pb.Visible = false;
        return pb;
    }

    private void ListDetail(VBoxContainer col, ArmyCampaignSession.Entry c)
    {
        var foe = _s.EnemyOf(c.Info);
        var st = _s.StandingOf(c);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(HudKit.Text((c.Info.Name + (foe != null ? " vs " + foe.Info.Name : "")).ToUpperInvariant(), 26, HudKit.Fg, HudKit.Exo(800)));
        string flags = st.Describe() + (c.Info.NoPermadeath ? " - No Permadeath" : "");
        head.AddChild(HudKit.Text(flags, 16, Palette.HeroGold, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        col.AddChild(head);
        if (st.IsOver)
            col.AddChild(HudKit.Text(st.Outcome switch { ECampaignOutcome.YouWon => "YOU WON THE CAMPAIGN", ECampaignOutcome.RivalWon => "THE RIVAL WON THE CAMPAIGN", _ => "THE CAMPAIGN IS A DRAW" },
                20, st.Outcome == ECampaignOutcome.YouWon ? Palette.Lime : Palette.WoundsRed, HudKit.Bold));

        var score = new HBoxContainer();
        score.AddThemeConstantOverride("separation", 12);
        score.AddChild(ScoreBox("You", c, st, st.YourVp, HudKit.Lime));
        if (foe != null) score.AddChild(ScoreBox("Rival", foe, st, st.RivalVp, EnemyRed));
        col.AddChild(score);

        col.AddChild(HudKit.Heading("Where it stands", 13));
        Fact(col, "Next up", st.IsOver ? "Nothing - the campaign is decided." : $"Mission {c.Info.Matches + 1}");
        Fact(col, "Sitting out injured", ArmyCampaignSession.SittingOut(c.Army));
        Fact(col, "Waiting on you", WaitingLine(c.Army));
        if (foe != null) Fact(col, "The rival", $"{ArmyCampaignSession.Plural(ArmyCampaignSession.Waiting(foe.Army), "choice")} waiting - {foe.Info.BankedPoints} pts to spend (you do its books)");

        col.AddChild(HudKit.Heading("Lately", 13));
        var hist = new VBoxContainer();
        hist.AddThemeConstantOverride("separation", 2);
        if (c.Info.History.Count == 0) hist.AddChild(HudKit.Text("Nothing played yet.", 14, HudKit.Dim, HudKit.Semi));
        foreach (string line in Enumerable.Reverse(c.Info.History).Take(5)) hist.AddChild(ForgeWidgets.Wrapped(line, 13, HudKit.Dim));
        var hs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        hist.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hs.AddChild(hist);
        col.AddChild(hs);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Ui.Button("Delete", () => AskDelete(c), "Delete this campaign (asks first). Removes both armies.", "BackButton").Also(x => x.Name = "Delete"));
        row.AddChild(HudKit.Text("Delete asks first and removes both armies.", 13, HudKit.Dim, HudKit.Semi).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.SizeFlagsVertical = SizeFlags.ShrinkCenter; }));
        row.AddChild(Ui.Button("OPEN CAMP", () => { _view = View.Camp; _enemySide = false; _unit = 0; Say(""); Rebuild(); },
            "Your units, their traits and upgrades, recruiting, and the next mission.", "CommitButton")
            .Also(x => { x.Name = "OpenCamp"; x.CustomMinimumSize = new Vector2(220, 52); x.AddThemeFontSizeOverride("font_size", 19); }));
        col.AddChild(row);
    }

    private static string WaitingLine(BuiltArmyFile army)
    {
        var lines = army.Units.Select(u => (u, CampaignRules.Sheet(u))).Where(p => p.Item2 is { Pending.Count: > 0 })
            .Select(p => $"{p.u.Name}: {string.Join(", ", p.Item2!.Pending.Select(x => ArmyCampaignSession.ChoiceText(x).Heading))}").ToList();
        return lines.Count == 0 ? "Nothing" : string.Join(" - ", lines);
    }

    private static Control ScoreBox(string who, ArmyCampaignSession.Entry e, CampaignPreGame.CampaignStanding st, int vp, Color colour)
    {
        var (w, l) = CampaignRules.RecordOf(e.Info);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 2);
        col.AddChild(HudKit.Text($"{who} - {e.Army.Faction}" + (e.Info.IsBot ? " (bot)" : ""), 14, colour, HudKit.Bold));
        col.AddChild(HudKit.Text(st.HasTarget ? $"{vp} VP of {st.VpTarget}" : $"{vp} VP", 24, HudKit.Fg, HudKit.Exo(800)));
        col.AddChild(HudKit.Text($"{w} W / {l} L", 14, HudKit.Dim, HudKit.Semi));
        col.AddChild(HudKit.Text($"Army {e.Army.TotalPoints} pts - treasury {e.Info.BankedPoints} pts unspent", 14, HudKit.Dim, HudKit.Semi));
        var p = HudKit.Panel(new ChamferBox { Fill = HudKit.GlassLight, Border = colour with { A = 0.6f }, Cut = 8 }, HudKit.Pad(col, 12, 8, 12, 8));
        p.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return p;
    }

    private static void Fact(VBoxContainer col, string label, string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(QuestWidgets.Line(label, 14, HudKit.Dim, 170, HudKit.Semi));
        row.AddChild(ForgeWidgets.Wrapped(text, 14, HudKit.Fg).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        col.AddChild(row);
    }

    private void AskDelete(ArmyCampaignSession.Entry c)
    {
        string foe = _s.EnemyOf(c.Info)?.Info.Name ?? "";
        var dlg = new ConfirmationDialog
        {
            Title = "Delete campaign?",
            DialogText = $"Delete the campaign \"{c.Info.Name}\"? Both armies go with it" + (foe.Length > 0 ? $" ({c.Info.Name} and {foe})" : "") + ", with their journal. This can't be undone.",
            OkButtonText = "Delete",
        };
        dlg.Confirmed += () =>
        {
            string? error = _s.Delete(c);
            Say(error ?? $"Deleted \"{c.Info.Name}\".", error != null ? Palette.WoundsRed : null);
            _selected = 0;
            Rebuild();
            dlg.QueueFree();
        };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    // ================================================================== B. new campaign

    private void StartNew()
    {
        _draft = new ArmyCampaignSession.Draft { VpTarget = _s.RollVpTarget() };
        _tab = 0;
        _view = View.New;
        Say("");
        Rebuild();
    }

    private Control NewView()
    {
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 10);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        string[] names = { "1  Rules", "2  Your army", "3  The enemy", "4  Missions" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            bool done = _s.TabDone(_draft, i);
            var b = new Button { Name = $"Tab{i}", Text = names[i] + (done && i != _tab ? "  (done)" : ""), ToggleMode = true, ButtonPressed = i == _tab, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(200, 40) };
            b.AddThemeColorOverride("font_color", done ? Palette.Lime : HudKit.Fg);
            b.Pressed += () => { _tab = idx; Rebuild(); };
            tabs.AddChild(b);
        }
        col.AddChild(tabs);
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 10);
        switch (_tab)
        {
            case 0: RulesTab(page); break;
            case 1: ArmyTab(page, enemy: false); break;
            case 2: ArmyTab(page, enemy: true); break;
            default: MissionsTab(page); break;
        }
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(page);
        col.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(scroll, 18, 14, 18, 14)).Also(p => p.SizeFlagsVertical = SizeFlags.ExpandFill));
        if (_s.NewProblem(_draft) is { } problem) col.AddChild(HudKit.Text("Still to do: " + problem, 14, Palette.HitsYellow, HudKit.Semi));
        else col.AddChild(HudKit.Text("Ready - START CAMPAIGN (top right).", 14, Palette.Lime, HudKit.Semi));
        return col;
    }

    private static Button Radio(string name, string text, bool on, Action pick, string tip)
    {
        var b = new CheckBox { Name = name, Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None, TooltipText = tip };
        b.Pressed += pick;
        return b;
    }

    private void RulesTab(VBoxContainer page)
    {
        page.AddChild(HudKit.Heading("How the campaign is won", 14));
        var race = new HBoxContainer();
        race.AddThemeConstantOverride("separation", 10);
        race.AddChild(Radio("Race", "Race to", _draft.Type == ECampaignType.PointLimited, () => { _draft.Type = ECampaignType.PointLimited; Rebuild(); },
            "The first army to this many campaign VP wins the campaign."));
        var vp = new SpinBox { Name = "VpTarget", MinValue = 1, MaxValue = 40, Value = _draft.VpTarget, Suffix = "VP", Editable = _draft.Type == ECampaignType.PointLimited };
        vp.ValueChanged += v => _draft.VpTarget = (int)v;
        race.AddChild(vp);
        race.AddChild(Ui.Button("Roll it (D3+4)", () => { _draft.VpTarget = _s.RollVpTarget(); _draft.Type = ECampaignType.PointLimited; Rebuild(); }, "The book's campaign length: D3+4 VP.").Also(x => x.Name = "RollVp"));
        page.AddChild(race);
        page.AddChild(Radio("Endless", "Endless - play as long as you like", _draft.Type == ECampaignType.Endless, () => { _draft.Type = ECampaignType.Endless; Rebuild(); },
            "No target: VP add up on the scoreboard and the campaign never ends."));
        var perma = new CheckBox { Name = "NoPermadeath", Text = "No Permadeath - destroyed units come back injured", ButtonPressed = _draft.NoPermadeath, FocusMode = FocusModeEnum.None,
            TooltipText = "A unit that would be lost for good sits out a mission injured instead." };
        perma.Toggled += on => _draft.NoPermadeath = on;
        page.AddChild(perma);
        page.AddChild(HudKit.Text("Story (optional)", 14, HudKit.Dim, HudKit.Semi));
        var story = new TextEdit { Name = "Story", Text = _draft.Story, CustomMinimumSize = new Vector2(0, 140), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Who is fighting, and why..." };
        story.TextChanged += () => _draft.Story = story.Text;
        page.AddChild(story);
        page.AddChild(ForgeWidgets.Wrapped("Book: the VP target is D3+4. Picking your own number is house rule B15. Game-limited and time-limited campaigns are left out (B15).", 13, HudKit.Dim));
    }

    private void ArmyTab(VBoxContainer page, bool enemy)
    {
        CampaignNumbers n = _s.Numbers;
        BuiltArmyFile? army = enemy ? _draft.Enemy : _draft.Yours;
        page.AddChild(HudKit.Heading(enemy ? "The enemy" : "Your army", 14));
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 10);
        nameRow.AddChild(HudKit.Text("Name", 15, HudKit.Dim, HudKit.Semi));
        var name = new LineEdit { Name = enemy ? "EnemyName" : "YourName", Text = enemy ? _draft.EnemyName : _draft.Name, CustomMinimumSize = new Vector2(320, 0), MaxLength = CampaignRules.MaxUnitNameLength * 2, PlaceholderText = enemy ? "e.g. The Red Brood" : "e.g. Iron Wardens" };
        name.TextChanged += t => { if (enemy) _draft.EnemyName = t; else _draft.Name = t; };
        name.FocusExited += Rebuild;
        nameRow.AddChild(name);
        string? nameProblem = enemy ? ArmyCampaignSession.EnemyNameProblem(_draft) : _s.YourNameProblem(_draft);
        if (nameProblem != null) nameRow.AddChild(HudKit.Text(nameProblem, 14, Palette.HitsYellow, HudKit.Semi).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        page.AddChild(nameRow);

        var choices = new HBoxContainer();
        choices.AddThemeConstantOverride("separation", 12);
        choices.AddChild(Ui.Button(army == null ? "Build it in the Army Forge" : "Edit... (rebuild in the Forge)", () => OpenForge(enemy),
            $"The Forge in campaign mode: {Game} books only, the {n.Treasury} pt treasury in the points bar, no Save / Load. Comes back here.", "CommitButton")
            .Also(x => x.Name = enemy ? "BuildEnemy" : "BuildYours"));
        var saved = enemy ? _s.EnemyCandidates(_draft) : _s.Armies;
        var pick = new OptionButton { Name = enemy ? "PickEnemy" : "PickYours", CustomMinimumSize = new Vector2(320, 0), TooltipText = $"A saved {Game} list. It must fit the {n.Treasury} pt treasury." };
        pick.AddItem(enemy ? "Use a saved army..." : "Use an army I already have...");
        foreach (var a in saved)
        {
            string? why = _s.CannotStart(a.Army);
            pick.AddItem($"{a.Army.Name} - {a.Army.Faction} - {a.Army.TotalPoints} pts" + (why != null ? "  (won't fit)" : ""));
            if (why != null) pick.SetItemDisabled(pick.ItemCount - 1, true);
        }
        pick.ItemSelected += i =>
        {
            if (i <= 0) return;
            var src = saved[(int)i - 1];
            string? error;
            if (enemy)
            {
                error = _s.CannotStart(src.Army) ?? (StrikeTeamFiles.Load(src.Path) is { } copy ? null : "Couldn't read that army file.");
                if (error == null) { _draft.Enemy = StrikeTeamFiles.Load(src.Path); if (_draft.EnemyName.Trim().Length == 0) _draft.EnemyName = _draft.Enemy!.Name; }
            }
            else error = _s.UseExisting(_draft, src);
            Say(error ?? "", error != null ? Palette.WoundsRed : null);
            Rebuild();
        };
        choices.AddChild(pick);
        if (enemy)
            choices.AddChild(Ui.Button("Pick one for me", () => { string? e = _s.PickEnemyForMe(_draft); Say(e ?? $"Picked {_draft.Enemy?.Name}.", e != null ? Palette.WoundsRed : Palette.Lime); Rebuild(); },
                "A random saved list of this game that fits the treasury - never your own.").Also(x => x.Name = "PickForMe"));
        page.AddChild(choices);

        if (army == null)
            page.AddChild(ForgeWidgets.Wrapped(enemy
                ? "Build the enemy in the Forge, or let the game pick a saved list. You do the enemy's bookkeeping too: its upgrades, recruits and trait picks (house rule B17)."
                : $"Spend at least {n.MinimumSpend} of the {n.Treasury} pts. Whatever is left goes into the treasury. One Hero to start (book).", 15, HudKit.Dim));
        else
        {
            string? why = _s.CannotStart(army);
            page.AddChild(HudKit.Text($"{(why == null ? "Built" : "Not ready")} - {army.Faction}, {army.TotalPoints} of {n.Treasury} pts (at least {n.MinimumSpend})", 17, why == null ? Palette.Lime : Palette.WoundsRed, HudKit.Bold));
            if (why != null) page.AddChild(ForgeWidgets.Wrapped(why, 14, Palette.WoundsRed));
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 8);
            flow.AddThemeConstantOverride("v_separation", 8);
            foreach (UnitFileEntry u in army.Units)
                flow.AddChild(ForgeWidgets.Card(army.Faction, "U_" + u.Name, u.Name, u.PointCost, CampaignRules.IsHero(u) ? "Hero" : "", false, () => { }, null, FdgRaylib.Models3D.UnitPortraits.LookOf(u.ModelLooks)));
            page.AddChild(flow);
            int left = n.Treasury - army.TotalPoints;
            if (left > 0) page.AddChild(HudKit.Text($"{left} pts left over go into the treasury.", 14, HudKit.Dim, HudKit.Semi));
        }
    }

    private void OpenForge(bool enemy)
    {
        string title = enemy ? "The enemy" : "Your army";
        var build = new ArmyForgeScreen.CampaignBuild(System, title, _s.Numbers, army =>
        {
            if (enemy)
            {
                _draft.Enemy = army;
                if (_draft.EnemyName.Trim().Length == 0 && army.Name.Trim().Length > 0) _draft.EnemyName = army.Name.Trim();
            }
            else
            {
                _draft.Yours = army;
                _draft.YoursSource = null;
                if (_draft.Name.Trim().Length == 0 && army.Name.Trim().Length > 0) _draft.Name = army.Name.Trim();
            }
            Say($"{title}: {army.Faction}, {army.TotalPoints} pts.", Palette.Lime);
            Rebuild();
        });
        App.I.Open<ArmyForgeScreen>(Scenes.ArmyForge, f => f.Campaign = build);
    }

    private void MissionsTab(VBoxContainer page)
    {
        page.AddChild(HudKit.Heading("The missions", 14));
        page.AddChild(Radio("AsIGo", "Plan each mission as I go", _draft.Planning == ArmyCampaignSession.EPlanning.AsIGo, () => { _draft.Planning = ArmyCampaignSession.EPlanning.AsIGo; Rebuild(); },
            "Each mission is rolled on Mission Setup just before you play it (and can be changed there)."));
        page.AddChild(Radio("Ahead", "Plan missions ahead (deployment, mission type, difficulty, map, notes)", _draft.Planning == ArmyCampaignSession.EPlanning.Ahead,
            () => { _draft.Planning = ArmyCampaignSession.EPlanning.Ahead; if (_draft.Plans.Count == 0) _s.AddPlan(_draft); Rebuild(); }, "Roll a run of missions now. Any of them can still be changed on Mission Setup."));
        page.AddChild(Radio("FromSaved", "Start from a saved campaign file", _draft.Planning == ArmyCampaignSession.EPlanning.FromSaved, () => { _draft.Planning = ArmyCampaignSession.EPlanning.FromSaved; Rebuild(); },
            "Replay a campaign you (or a friend) saved: its missions and notes, with these armies."));

        if (_draft.Planning == ArmyCampaignSession.EPlanning.Ahead)
        {
            for (int i = 0; i < _draft.Plans.Count; i++)
            {
                int idx = i;
                CampaignMissionPlan p = _draft.Plans[i];
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 10);
                row.AddChild(QuestWidgets.Line($"Mission {p.Mission}", 15, HudKit.Fg, 100, HudKit.Bold));
                string deploy = AdvancedDeployment.Available(System) ? AdvancedDeployment.Name(p.DeploymentStyle) + " - " : "";
                row.AddChild(HudKit.Text($"{deploy}{MissionSetupSession.MissionTypeName(p.MissionCards)} - {MissionDifficulty.Name(p.Difficulty)}", 14, HudKit.Dim, HudKit.Semi).Also(l => { l.SizeFlagsHorizontal = SizeFlags.ExpandFill; l.SizeFlagsVertical = SizeFlags.ShrinkCenter; }));
                var notes = new LineEdit { Text = p.Notes, PlaceholderText = "Notes...", CustomMinimumSize = new Vector2(300, 0) };
                notes.TextChanged += t => p.Notes = CampaignJournal.Clean(t);
                row.AddChild(notes);
                row.AddChild(Ui.Button("Reroll", () => { _s.RerollPlan(_draft, idx); Rebuild(); }, "Roll this mission again from the books.").Also(x => x.Name = $"Reroll{i}"));
                row.AddChild(Ui.Button("X", () => { ArmyCampaignSession.RemovePlan(_draft, idx); Rebuild(); }, "Remove this mission.", "BackButton"));
                page.AddChild(row);
            }
            var add = new HBoxContainer();
            add.AddThemeConstantOverride("separation", 10);
            add.AddChild(Ui.Button("+ Add mission", () => { _s.AddPlan(_draft); Rebuild(); }, "Roll another mission onto the end.").Also(x => x.Name = "AddPlan"));
            add.AddChild(Ui.Button("Save as a file", () =>
            {
                string? saved = _s.SavePlansAsScript(_draft);
                Say(saved != null ? $"Saved to {CampaignScriptPaths.DirectoryName} - start a new campaign from it any time." : "Couldn't save the campaign file.", saved != null ? Palette.Lime : Palette.WoundsRed);
            }, "Save these missions to replay them, or give them to a friend."));
            page.AddChild(add);
        }
        else if (_draft.Planning == ArmyCampaignSession.EPlanning.FromSaved)
        {
            var scripts = _s.SavedScripts();
            if (scripts.Count == 0) page.AddChild(ForgeWidgets.Wrapped($"No saved {Game} campaigns yet. Save one from Mission Setup (\"Save campaign as a file\").", 15, HudKit.Dim));
            else
            {
                var ob = new OptionButton { Name = "Script", CustomMinimumSize = new Vector2(360, 0) };
                ob.AddItem("Pick a saved campaign...");
                foreach (var (_, sc) in scripts) ob.AddItem($"{sc.Name} - {ArmyCampaignSession.Plural(sc.Missions.Count, "mission")}");
                int sel = scripts.ToList().FindIndex(x => x.Path == _draft.ScriptPath);
                ob.Selected = sel + 1;
                ob.ItemSelected += i => { if (i > 0) ArmyCampaignSession.LoadScript(_draft, scripts[(int)i - 1].Path, scripts[(int)i - 1].Script); Rebuild(); };
                page.AddChild(ob);
                if (_draft.Premise.Length > 0) page.AddChild(ForgeWidgets.Wrapped(_draft.Premise, 14, HudKit.Dim));
            }
        }
    }

    private void FinishNew()
    {
        string? path = _s.Finish(_draft, out string? error);
        if (path == null) { Say(error ?? "Couldn't start the campaign.", Palette.WoundsRed); return; }
        Reselect(path);
        _view = View.Camp;
        _enemySide = false;
        _unit = 0;
        Say($"Campaign started: {_draft.Name.Trim()} vs {_draft.EnemyName.Trim()}.", Palette.Lime);
        _draft = new ArmyCampaignSession.Draft();
        Rebuild();
    }

    // ================================================================== C. the camp

    private Control CampView()
    {
        if (Current is not { } cur || Side is not { } side) return new Control();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var units = side.Army.Units;
        _unit = Math.Clamp(_unit, 0, Math.Max(0, units.Count - 1));

        var panel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeConstantOverride("separation", 6);
        if (units.Count > 0) UnitPanel(panel, side, units[_unit]);
        var ps = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ps.AddChild(panel);
        row.AddChild(HudKit.Panel(SideBox(), HudKit.Pad(ps, 14, 10, 14, 10)).Also(p => p.CustomMinimumSize = new Vector2(500, 0)));

        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mid.AddThemeConstantOverride("separation", 8);
        mid.AddChild(HudKit.Heading($"Roster - {ArmyCampaignSession.Plural(units.Count, "unit")}, {side.Army.TotalPoints} pts", 14));
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        for (int i = 0; i < units.Count; i++)
        {
            int idx = i;
            UnitFileEntry u = units[i];
            StrikeUnit? sh = CampaignRules.Sheet(u);
            string tag = (sh is { Level: > 0 } ? $"Lv {sh.Level} - " : "") + (ArmyCampaignSession.RosterTag(sh) ?? "ready");
            flow.AddChild(ForgeWidgets.Card(side.Army.Faction, $"Unit{i}", u.Name, u.PointCost, tag, i == _unit, () => { _unit = idx; Rebuild(); }, null,
                FdgRaylib.Models3D.UnitPortraits.LookOf(u.ModelLooks)));
        }
        var fs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        flow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fs.AddChild(flow);
        mid.AddChild(fs);
        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 12);
        bottom.AddChild(Ui.Button("+ Recruit units", OpenRecruit, "Buy new units from the treasury (the Forge's roster and a cart).", "CommitButton").Also(x => x.Name = "Recruit"));
        bottom.AddChild(HudKit.Text($"Treasury {side.Info.BankedPoints} pts", 18, Palette.HeroGold, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        mid.AddChild(bottom);
        if (!_enemySide && _s.EnemyOf(cur.Info) != null)
            mid.AddChild(HudKit.Text("Switch to Enemy army (top) to do the rival's books - the screen tints red while you edit it.", 13, HudKit.Dim, HudKit.Semi));
        row.AddChild(HudKit.Panel(SideBox(), HudKit.Pad(mid, 14, 10, 14, 10)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));

        var next = new VBoxContainer();
        next.AddThemeConstantOverride("separation", 6);
        NextColumn(next, cur);
        var ns = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        next.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ns.AddChild(next);
        var nextCol = new VBoxContainer();
        nextCol.AddThemeConstantOverride("separation", 8);
        nextCol.AddChild(ns);
        string? cant = PlayProblem(cur);
        nextCol.AddChild(Ui.Button("PLAY NEXT MISSION", () => OpenMissionSetup(cur), cant ?? "Opens Mission Setup: the mission, who's coming on both sides, the difficulty, START MISSION.", "CommitButton")
            .Also(x => { x.Name = "PlayNext"; x.Disabled = cant != null; x.CustomMinimumSize = new Vector2(0, 56); x.AddThemeFontSizeOverride("font_size", 20); }));
        if (cant != null) nextCol.AddChild(ForgeWidgets.Wrapped(cant, 13, Palette.HitsYellow));
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(nextCol, 14, 10, 14, 10)).Also(p => p.CustomMinimumSize = new Vector2(380, 0)));
        return row;
    }

    private ChamferBox SideBox() => _enemySide ? new ChamferBox { Fill = new Color(0.16f, 0.06f, 0.05f, 0.92f), Border = EnemyRed with { A = 0.7f }, Cut = 12 } : HudKit.PanelBox(12);

    /// <summary>Why PLAY NEXT MISSION is greyed, or null.</summary>
    private string? PlayProblem(ArmyCampaignSession.Entry c)
    {
        if (_s.StandingOf(c).IsOver) return "This campaign is over.";
        int mine = ArmyCampaignSession.Waiting(c.Army);
        if (mine > 0) return $"{ArmyCampaignSession.Plural(mine, "choice")} waiting in your army - make them first (the cards say \"pick a trait\").";
        if (_s.EnemyOf(c.Info) is { } foe && ArmyCampaignSession.Waiting(foe.Army) is > 0 and int theirs)
            return $"{ArmyCampaignSession.Plural(theirs, "choice")} waiting in the enemy's army - switch to Enemy army and make them.";
        return null;
    }

    private void UnitPanel(VBoxContainer col, ArmyCampaignSession.Entry side, UnitFileEntry unit)
    {
        StrikeUnit? sheet = CampaignRules.Sheet(unit);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        string type = CampaignRules.UnitTypeOf(unit, side.Army.Book);
        head.AddChild(HudKit.Portrait(side.Army.Faction, type, "person", 76, _enemySide ? EnemyRed : HudKit.Friendly, look: FdgRaylib.Models3D.UnitPortraits.LookOf(unit.ModelLooks)));
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", 2);
        var rename = new LineEdit { Name = "Rename", Text = unit.Name, MaxLength = CampaignRules.MaxUnitNameLength, TooltipText = "Rename the unit (press Enter)." };
        rename.AddThemeFontSizeOverride("font_size", 20);
        rename.TextSubmitted += t =>
        {
            string? why = ArmyCampaignSession.Rename(side, unit, t);
            Say(why ?? $"Renamed to {unit.Name}.", why != null ? Palette.WoundsRed : Palette.Lime);
            Rebuild();
        };
        names.AddChild(rename);
        names.AddChild(HudKit.Text($"{type} x{unit.ModelCount}", 14, HudKit.Dim, HudKit.Semi));
        head.AddChild(names);
        col.AddChild(head);
        if (sheet == null) { col.AddChild(HudKit.Text("Not a campaign unit.", 14, HudKit.Dim, HudKit.Semi)); return; }

        int per = StrikeTeamRules.XpPerLevel;
        Fact(col, "Level", $"{sheet.Level} - {sheet.Xp} / {(sheet.Level + 1) * per} XP - {ArmyCampaignSession.Plural(sheet.Games, "game")}");
        Fact(col, "Points", $"{unit.PointCost}" + (sheet.CostChange != 0 ? $" (book {unit.PointCost - sheet.CostChange}, campaign {(sheet.CostChange > 0 ? "+" : "")}{sheet.CostChange})" : ""));
        Fact(col, sheet.IsHero ? "Traits" : "Traits", sheet.Traits.Count > 0 ? string.Join(", ", sheet.Traits) : "none");
        Fact(col, "Injuries", sheet.Injuries.Count > 0 ? string.Join(", ", sheet.Injuries) : "none");
        if (sheet.Talents.Count > 0) Fact(col, "Talents", string.Join(", ", sheet.Talents));
        if (sheet.SkillSets.Count > 0) Fact(col, "Skill sets", string.Join(", ", sheet.SkillSets));
        if (sheet.Skills.Count > 0) Fact(col, "Skills", string.Join(", ", sheet.Skills));
        Fact(col, "Kills", ArmyCampaignSession.Plural(sheet.Kills, "unit") + (sheet.HeroKills > 0 ? $" ({sheet.HeroKills} heroes)" : ""));
        if (sheet.MissionsOut > 0) Fact(col, "Recovering", $"sits out {ArmyCampaignSession.Plural(sheet.MissionsOut, "more mission")}");

        foreach (string pending in sheet.Pending.ToList())
        {
            var (heading, ask) = ArmyCampaignSession.ChoiceText(pending);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 4);
            box.AddChild(HudKit.Text($"WAITING ON YOU: {heading}", 15, Palette.HeroGold, HudKit.Bold));
            box.AddChild(ForgeWidgets.Wrapped(ask, 14, HudKit.Fg));
            var opts = new HFlowContainer();
            opts.AddThemeConstantOverride("h_separation", 6);
            opts.AddThemeConstantOverride("v_separation", 6);
            foreach (string option in CampaignRules.Options(sheet, pending))
            {
                string o = option;
                opts.AddChild(Ui.Button(o, () => { Say(_s.Choose(side, unit, pending, o), Palette.Lime); Rebuild(); }, "Pick this.").Also(x => x.Name = ForgeWidgets.SafeName("Choose_" + o)));
            }
            box.AddChild(opts);
            col.AddChild(HudKit.Panel(new ChamferBox { Fill = new Color(0.16f, 0.13f, 0.04f, 0.9f), Border = Palette.HeroGold, Cut = 6 }, HudKit.Pad(box, 10, 6, 10, 6)));
        }

        col.AddChild(HudKit.Heading($"Upgrades - campaign prices, from the treasury ({side.Info.BankedPoints} pts)", 13));
        var (lines, why) = ArmyCampaignSession.Upgrades(side, unit);
        if (lines == null) col.AddChild(HudKit.Text(why ?? "", 14, HudKit.Dim, HudKit.Semi));
        else
        {
            string? section = null;
            foreach (var line in lines)
            {
                if (line.Section.Label != section) { section = line.Section.Label; col.AddChild(HudKit.Text(section, 13, HudKit.Dim, HudKit.Semi)); }
                var r = new HBoxContainer();
                r.AddThemeConstantOverride("separation", 8);
                r.AddChild(ForgeWidgets.Wrapped(line.Option.Label + (line.Owned ? "  (owned)" : ""), 14, line.Owned ? Palette.Lime : HudKit.Fg).Also(x => x.SizeFlagsHorizontal = SizeFlags.ExpandFill));
                var l2 = line;
                r.AddChild(Ui.Button(line.Label, () =>
                {
                    var (ok, msg) = ArmyCampaignSession.ChangeUpgrade(side, unit, l2);
                    Say(msg, ok ? Palette.Lime : Palette.WoundsRed);
                    Reload();
                    Rebuild();
                }, line.Affordable ? "Change it." : "The treasury can't pay for it.").Also(x => { x.Disabled = !line.Affordable; x.CustomMinimumSize = new Vector2(130, 0); }));
                col.AddChild(r);
            }
        }
        col.AddChild(Ui.Button("Disband unit", () => AskDisband(side, unit), "Remove the unit from the army for good: its XP and upgrades are gone.", "BackButton").Also(x => x.Name = "Disband"));
    }

    private void AskDisband(ArmyCampaignSession.Entry side, UnitFileEntry unit)
    {
        var dlg = new ConfirmationDialog { Title = "Disband unit?", DialogText = $"Disband {unit.Name}? It leaves the army for good, with its experience and upgrades. This can't be undone.", OkButtonText = "Disband" };
        dlg.Confirmed += () => { Say(ArmyCampaignSession.Disband(side, unit)); _unit = 0; Reload(); Rebuild(); dlg.QueueFree(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void NextColumn(VBoxContainer col, ArmyCampaignSession.Entry c)
    {
        var st = _s.StandingOf(c);
        CampaignNumbers n = _s.Numbers;
        col.AddChild(HudKit.Heading("Campaign", 13));
        col.AddChild(HudKit.Text(st.Describe(), 16, HudKit.Fg, HudKit.Bold));
        col.AddChild(HudKit.Text($"You {st.YourVp} - Rival {st.RivalVp}", 16, HudKit.Fg, HudKit.Semi));
        if (st.HasTarget) col.AddChild(VpBar(st, 300, 10));

        col.AddChild(HudKit.Heading($"Next mission - {c.Info.Matches + 1}", 13));
        if (CampaignPlans.Next(c.Info) is { } plan && plan.Title.Length > 0) col.AddChild(ForgeWidgets.Wrapped(plan.Title, 15, HudKit.Fg));
        col.AddChild(HudKit.Text("Your special objective", 13, HudKit.Dim, HudKit.Semi));
        if (c.Info.SpecialObjective.Length == 0)
        {
            col.AddChild(HudKit.Text("Not rolled yet.", 14, HudKit.Dim, HudKit.Semi));
            col.AddChild(Ui.Button("Roll it", () => { Say("Special objective: " + _s.RollSpecialObjective(c), Palette.Lime); Rebuild(); }, "Rolled before the mission (book, step 1).").Also(x => x.Name = "RollObjective"));
        }
        else
        {
            col.AddChild(HudKit.Text(c.Info.SpecialObjective, 16, Palette.HeroGold, HudKit.Bold));
            col.AddChild(ForgeWidgets.Wrapped(CampaignPreGame.ObjectiveText(c.Info.SpecialObjective), 14, HudKit.Fg));
            col.AddChild(HudKit.Text($"Worth +{n.SpecialObjectiveBonus} pts into your treasury.", 13, HudKit.Dim, HudKit.Semi));
        }

        if (_s.EnemyOf(c.Info) is { } foe)
        {
            col.AddChild(HudKit.Heading("The rival", 13));
            col.AddChild(HudKit.Text(foe.Info.Name, 15, EnemyRed, HudKit.Bold));
            col.AddChild(ForgeWidgets.Wrapped($"{ArmyCampaignSession.Plural(foe.Army.Units.Count, "unit")}, {foe.Army.TotalPoints} pts, treasury {foe.Info.BankedPoints} pts", 14, HudKit.Fg));
            col.AddChild(ForgeWidgets.Wrapped("How much of it comes is set by the difficulty on Mission Setup.", 13, HudKit.Dim));
        }

        if (c.Info.Matches > 0)
        {
            col.AddChild(HudKit.Heading("Last mission", 13));
            foreach (string line in CampaignJournal.RecordFor(c.Info, c.Info.Matches).Take(8)) col.AddChild(ForgeWidgets.Wrapped(line, 13, HudKit.Fg));
        }
    }

    private void OpenMissionSetup(ArmyCampaignSession.Entry c)
    {
        var others = _s.OthersThan(c);
        App.I.Open<MissionSetupScreen>(Scenes.MissionSetup, m => { m.CampaignPath = c.Path; m.Others = others; });
    }

    // ================================================================== D. recruit

    private void OpenRecruit()
    {
        _cart.Clear();
        _recruitPick = null;
        _view = View.Recruit;
        Say("");
        Rebuild();
    }

    private bool EnsureCards(BookFile book)
    {
        if (_cards != null && _cardsFor == book.Name) return true;
        try
        {
            _cards = ForgeSession.BuildRosterCards(book);
            _glossary = RuleGlossary.Build(book);
            _cardsFor = book.Name;
            return true;
        }
        catch (Exception e) { Say("Couldn't read the army book: " + e.Message, Palette.WoundsRed); return false; }
    }

    private string? RecruitProblem()
    {
        if (Side is not { Army.Book: { } book } side) return "No book attached to this army.";
        if (_cart.Ids.Count == 0) return "The cart is empty - click a unit, then + ADD TO CART.";
        if (!EnsureCards(book)) return "Couldn't read the army book.";
        int total = _s.CartTotal(side, book, _cart, _cards!);
        return total > side.Info.BankedPoints ? $"{total - side.Info.BankedPoints} pts short - take the veteran off, or remove a unit." : null;
    }

    private Control RecruitView()
    {
        if (Side is not { } side) return new Control();
        if (side.Army.Book is not { } book || !EnsureCards(book))
            return ForgeWidgets.Wrapped("This army has no book attached, so it can't recruit.", 16, Palette.WoundsRed);
        var cards = _cards!;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 8);
        left.AddChild(HudKit.Heading($"Unit roster - {book.Name}", 14));
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        foreach (RosterUnit r in book.Units)
        {
            string id = r.Id;
            int price = cards.TryGetValue(id, out var cd) ? cd.Unit.PointCost : r.BasePointCost;
            string? why = _s.WhyNot(side, book, r, _cart, cards);
            int inCart = _cart.Ids.Count(x => x == id);
            string tag = inCart > 0 ? $"in cart x{inCart}" : ArmyCampaignSession.IsHeroRoster(r) ? "Hero" : why != null ? "can't" : "";
            var card = ForgeWidgets.Card(book.Faction, "R_" + id, r.Name, price, tag, _recruitPick == id, () => { _recruitPick = id; Rebuild(); }, () => AddToCart(side, book, r), null);
            if (why != null && inCart == 0) { card.Modulate = new Color(1, 1, 1, 0.5f); card.TooltipText = why; }
            flow.AddChild(card);
        }
        var fs = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        flow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fs.AddChild(flow);
        left.AddChild(fs);
        left.AddChild(ForgeWidgets.Wrapped($"Book: one new Hero after each mission, at most {CampaignNumbers.MaxHeroes} Heroes - the others grey out with why. Double-click a card to add it.", 13, HudKit.Dim));
        row.AddChild(HudKit.Panel(SideBox(), HudKit.Pad(left, 14, 10, 14, 10)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));

        // The picked unit's sheet, and the cart.
        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 8);
        if (_recruitPick is { } pick && book.Units.FirstOrDefault(r => r.Id == pick) is { } picked)
        {
            right.AddChild(HudKit.Text(picked.Name, 20, HudKit.Fg, HudKit.Bold));
            if (cards.TryGetValue(pick, out var c) && _glossary != null)
            {
                var gear = new VBoxContainer();
                gear.AddThemeConstantOverride("separation", 4);
                ForgeWidgets.Gear(_glossary, gear, c.Unit, c.Items, null);
                right.AddChild(gear);
            }
            string? why = _s.WhyNot(side, book, picked, _cart, cards);
            right.AddChild(Ui.Button("+ ADD TO CART", () => AddToCart(side, book, picked), why ?? "Add it to the cart.", "CommitButton").Also(x => { x.Name = "AddToCart"; x.Disabled = why != null; }));
            if (why != null) right.AddChild(ForgeWidgets.Wrapped(why, 13, Palette.HitsYellow));
        }
        else right.AddChild(ForgeWidgets.Wrapped("Click a unit for its stats, then + ADD TO CART.", 14, HudKit.Dim));

        right.AddChild(HudKit.Heading("Cart", 14));
        bool vetUsed = CampaignEconomy.VeteranRecruitUsed(side.Army);
        for (int i = 0; i < _cart.Ids.Count; i++)
        {
            int idx = i;
            string id = _cart.Ids[i];
            RosterUnit? r = book.Units.FirstOrDefault(x => x.Id == id);
            int price = cards.TryGetValue(id, out var cd) ? cd.Unit.PointCost : r?.BasePointCost ?? 0;
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(QuestWidgets.Line($"{r?.Name ?? id}  {price}", 15, HudKit.Fg, 200, HudKit.Semi));
            var vet = new CheckBox { Name = $"Veteran{i}", Text = $"5 XP (+{_s.Numbers.VeteranRecruitCost})", ButtonPressed = _cart.Veteran == i, Disabled = vetUsed, FocusMode = FocusModeEnum.None,
                TooltipText = vetUsed ? "A veteran has already been recruited since the last mission (once per mission)." : "Book: a recruit may start with 5 XP for the veteran price. Once per mission." };
            vet.Toggled += on => { _cart.Veteran = on ? idx : -1; Rebuild(); };
            line.AddChild(vet);
            line.AddChild(Ui.Button("Remove", () => { _cart.Remove(idx); Rebuild(); }, "Take it out of the cart.", "BackButton"));
            right.AddChild(line);
        }
        if (_cart.Ids.Count == 0) right.AddChild(HudKit.Text("Empty.", 14, HudKit.Dim, HudKit.Semi));
        int total = _s.CartTotal(side, book, _cart, cards);
        right.AddChild(HudKit.Text($"Total {total}   -   Treasury {side.Info.BankedPoints}", 17, total > side.Info.BankedPoints ? Palette.WoundsRed : HudKit.Fg, HudKit.Bold));
        if (RecruitProblem() is { } problem && _cart.Ids.Count > 0) right.AddChild(ForgeWidgets.Wrapped(problem, 13, Palette.WoundsRed));
        var rs = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rs.AddChild(right);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(rs, 14, 10, 14, 10)).Also(p => p.CustomMinimumSize = new Vector2(480, 0)));
        return row;
    }

    private void AddToCart(ArmyCampaignSession.Entry side, BookFile book, RosterUnit roster)
    {
        if (_s.WhyNot(side, book, roster, _cart, _cards!) is { } why) { Say(why, Palette.HitsYellow); return; }
        _cart.Ids.Add(roster.Id);
        Say($"{roster.Name} is in the cart.");
        Rebuild();
    }

    private void ConfirmRecruits()
    {
        if (Side is not { Army.Book: { } book } side || RecruitProblem() != null) return;
        List<string> lines = _s.ConfirmRecruits(side, book, _cart);
        Say(string.Join(" ", lines), Palette.Lime);
        _view = View.Camp;
        Rebuild();
    }

    // ================================================================== G. journal

    private void OpenJournal()
    {
        _view = View.Journal;
        _journalMission = Current?.Info.Matches is > 0 and int m ? m : 1;
        Say("");
        Rebuild();
    }

    private Control JournalView()
    {
        if (Current is not { } cur) return new Control();
        StrikeTeamInfo info = cur.Info;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var story = new VBoxContainer();
        story.AddThemeConstantOverride("separation", 6);
        story.AddChild(HudKit.Heading("The campaign", 14));
        var storyText = new TextEdit { Name = "Story", Text = info.Journal, SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Write the campaign's story..." };
        storyText.FocusExited += () => { CampaignJournal.SetCampaignNote(info, storyText.Text); ArmyCampaignSession.Save(cur.Path, cur.Army); };
        story.AddChild(storyText);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(story, 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(420, 0)));

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        list.AddChild(HudKit.Heading("Missions", 14));
        var missions = CampaignJournal.Missions(info).ToList();
        if (!missions.Contains(info.Matches + 1)) missions.Add(info.Matches + 1);
        foreach (int m in missions.OrderBy(x => x))
        {
            int mm = m;
            bool played = CampaignJournal.IsPlayed(info, m);
            string mark = played ? (CampaignJournal.RecordFor(info, m).Any(l => l.Contains("won", StringComparison.OrdinalIgnoreCase)) ? "" : "") : m == info.Matches + 1 ? " - next" : "";
            var b = new Button { Name = $"J{m}", Text = $"Mission {m}{(played ? " - played" : mark)}", Flat = true, Alignment = HorizontalAlignment.Left, ToggleMode = true, ButtonPressed = m == _journalMission, FocusMode = FocusModeEnum.None };
            b.AddThemeColorOverride("font_color", played ? HudKit.Fg : HudKit.Dim);
            b.Pressed += () => { _journalMission = mm; Rebuild(); };
            list.AddChild(b);
        }
        var ls = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ls.AddChild(list);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ls, 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(240, 0)));

        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 6);
        int mission = Math.Max(1, _journalMission);
        var plan = info.Plans.FirstOrDefault(p => p.Mission == mission);
        page.AddChild(HudKit.Heading($"Mission {mission}" + (plan is { Title.Length: > 0 } ? $" - {plan.Title}" : "") + (plan != null ? $" - {MissionSetupSession.MissionTypeName(plan.MissionCards)}" : ""), 14));
        page.AddChild(HudKit.Text("What happened - written by the game", 13, HudKit.Dim, HudKit.Semi));
        var rec = new VBoxContainer();
        rec.AddThemeConstantOverride("separation", 2);
        var record = CampaignJournal.RecordFor(info, mission);
        if (record.Count == 0) rec.AddChild(HudKit.Text("Not played yet.", 15, HudKit.Dim, HudKit.Semi));
        foreach (string line in record) rec.AddChild(ForgeWidgets.Wrapped(line, 15, HudKit.Fg));
        page.AddChild(HudKit.Panel(new ChamferBox { Fill = new Color(0.03f, 0.05f, 0.06f, 0.9f), Border = HudKit.Edge, Cut = 6 }, HudKit.Pad(rec, 12, 8, 12, 8)));
        page.AddChild(HudKit.Text(record.Count > 0 ? "Your account of the mission" : "The briefing (it becomes the start of your account)", 13, HudKit.Dim, HudKit.Semi));
        var notes = new TextEdit { Name = "MissionNotes", Text = CampaignJournal.NoteFor(info, mission), SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Write what happened..." };
        notes.FocusExited += () => { CampaignJournal.SetNote(info, mission, notes.Text); ArmyCampaignSession.Save(cur.Path, cur.Army); };
        page.AddChild(notes);
        row.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(page, 14, 10, 14, 10)).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        return row;
    }

    private void ExportJournal()
    {
        if (Current is not { } cur) return;
        string? file = _s.ExportJournal(cur, out string? error);
        Say(file != null ? "Journal saved to " + file : "Couldn't write the journal file: " + error, file != null ? Palette.Lime : Palette.WoundsRed);
    }

    // ================================================================== screenshot driver

    /// <summary>
    /// Drives the screen without a person: "new", "tab:N", "name:X", "enemyname:X", "useyours:i", "enemyforme", "plan:ahead",
    /// "start", "select:i", "camp", "enemy", "you", "unit:i", "choose" (first option of the first waiting choice), "roll",
    /// "recruit", "cart:i" (roster index), "vet", "confirm", "journal", "play".
    /// </summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        int.TryParse(arg, out int n);
        switch (verb)
        {
            case "new": StartNew(); return;
            case "tab": _tab = n; break;
            case "name": _draft.Name = arg; break;
            case "enemyname": _draft.EnemyName = arg; break;
            case "useyours":
            {
                var ok = _s.Armies.Where(a => _s.CannotStart(a.Army) == null).ToList();
                if (n < ok.Count) Say(_s.UseExisting(_draft, ok[n]) ?? "", Palette.WoundsRed);
                else Say("No saved army fits the treasury.", Palette.WoundsRed);
                break;
            }
            case "enemyforme": Say(_s.PickEnemyForMe(_draft) ?? ""); break;
            case "plan" when arg == "ahead": _draft.Planning = ArmyCampaignSession.EPlanning.Ahead; if (_draft.Plans.Count == 0) { _s.AddPlan(_draft); _s.AddPlan(_draft); } break;
            case "start": FinishNew(); return;
            case "select": _selected = n; _view = View.List; break;
            case "camp": _view = View.Camp; break;
            case "enemy": _enemySide = true; _unit = 0; break;
            case "you": _enemySide = false; _unit = 0; break;
            case "unit": _unit = n; break;
            case "choose" when Side is { } sd:
                foreach (UnitFileEntry u in sd.Army.Units)
                    if (CampaignRules.Sheet(u) is { Pending.Count: > 0 } sh) { Say(_s.Choose(sd, u, sh.Pending[0], CampaignRules.Options(sh, sh.Pending[0])[0])); break; }
                break;
            case "roll" when Current is { } cr: _s.RollSpecialObjective(cr); break;
            case "recruit": OpenRecruit(); return;
            case "cart" when Side is { Army.Book: { } bk } s2 && EnsureCards(bk) && n < bk.Units.Count: _recruitPick = bk.Units[n].Id; AddToCart(s2, bk, bk.Units[n]); return;
            case "vet": _cart.Veteran = _cart.Ids.Count > 0 ? 0 : -1; break;
            case "confirm": ConfirmRecruits(); return;
            case "journal": OpenJournal(); return;
            case "play" when Current is { } cp: OpenMissionSetup(cp); return;
        }
        Rebuild();
    }
}
