using FDG;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib;
using FdgRaylib.Lobby;
using FdgRaylib.Quest;
using FdgRaylib.SaveLoad;
using FDG.ArmyBuilding;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;
using OprGodot.Quest;

namespace OprGodot.Campaign;

/// <summary>
/// Mission Setup (#435, #465; Army Campaign mockup E, approved 2026-11-12): the campaign's step 1 on one screen - the
/// mission plan and the map, your force and theirs as portrait cards (click to bring / leave), the difficulty that sizes
/// their force, and START MISSION. That opens a host quietly with both campaign armies seated and presses LAUNCH for you;
/// if the lobby finds a real reason not to launch, you land in it with the reason on screen.
/// All the rules are <see cref="MissionSetupSession"/> (FdgApp).
/// </summary>
public partial class MissionSetupScreen : Control
{
    public string CampaignPath { get; set; } = "";
    public List<CampaignEntrant> Others { get; set; } = new();

    private MissionSetupSession _s = null!;
    private HBoxContainer _top = null!;
    private HBoxContainer _body = null!;
    private Label _status = null!;
    private Button _start = null!;
    private static readonly Color EnemyRed = new(0.95f, 0.42f, 0.36f);

    public override void _Ready()
    {
        _s = new MissionSetupSession(CampaignPath, Others);
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
        _body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 12);
        root.AddChild(_body);
        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 12);
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        bottom.AddChild(_status);
        _start = Ui.Button("START MISSION", StartMission, "", "CommitButton");
        _start.Name = "StartMission";
        _start.CustomMinimumSize = new Vector2(280, 60);
        _start.AddThemeFontSizeOverride("font_size", 22);
        bottom.AddChild(_start);
        root.AddChild(bottom);
        VisibilityChanged += () => { if (Visible) { _s.Reload(); Rebuild(); } };
        Rebuild();
    }

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    private void Rebuild()
    {
        foreach (Control holder in new Control[] { _top, _body })
            foreach (Node c in holder.GetChildren()) { holder.RemoveChild(c); c.QueueFree(); }
        var b = Ui.Button("Back to camp", App.I.Back, "Back to the Camp. Nothing is started.", "BackButton");
        b.Name = "Back";
        b.CustomMinimumSize = new Vector2(150, 44);
        _top.AddChild(b);
        if (_s.Info is not { } info)
        {
            _top.AddChild(HudKit.Text("Couldn't read this campaign army.", 22, Palette.WoundsRed, HudKit.Bold));
            _start.Disabled = true;
            return;
        }
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -6);
        t.AddChild(HudKit.Text($"MISSION SETUP - {info.Matches + 1}", 30, HudKit.Fg, HudKit.Exo(800)));
        var st = CampaignPreGame.Standing(info, _s.Foe);
        t.AddChild(HudKit.Text($"{info.Name} vs {_s.Foe?.Name ?? "a fresh opponent"} - {GameSystemInfo.DisplayName(_s.System)} - you {st.YourVp} VP, rival {st.RivalVp}", 14, HudKit.Dim, HudKit.Semi));
        _top.AddChild(t);

        _body.AddChild(Column(PlanColumn, 0));
        _body.AddChild(Column(YourColumn, 1));
        _body.AddChild(Column(TheirColumn, 2));

        string? blocker = _s.Blocker();
        _start.Disabled = blocker != null;
        _start.TooltipText = blocker ?? "Seat both armies and start the mission straight away.";
        if (blocker != null) Say(blocker, Palette.HitsYellow);
    }

    private Control Column(Action<VBoxContainer> fill, int which)
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        fill(col);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(col);
        var box = which == 2 ? new ChamferBox { Fill = new Color(0.13f, 0.06f, 0.05f, 0.9f), Border = EnemyRed with { A = 0.6f }, Cut = 12 } : HudKit.PanelBox(12);
        return HudKit.Panel(box, HudKit.Pad(scroll, 14, 10, 14, 10)).Also(p => { p.SizeFlagsHorizontal = SizeFlags.ExpandFill; p.SizeFlagsStretchRatio = 1; });
    }

    // ================================================================== the mission and the map

    private void PlanColumn(VBoxContainer col)
    {
        if (_s.Plan is not { } plan) return;
        var head = new HBoxContainer();
        head.AddChild(HudKit.Heading("The mission", 14).Also(h => h.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        head.AddChild(Ui.Button("Generate all", () => { _s.GenerateAll(); Say("Rolled a new mission.", Palette.Lime); Rebuild(); },
            "Rolls a new deployment, mission type and difficulty from the books. The map and your notes stay.").Also(x => x.Name = "GenerateAll"));
        col.AddChild(head);
        var title = new LineEdit { Name = "Title", Text = plan.Title, PlaceholderText = "Title (optional)", MaxLength = 80 };
        title.TextSubmitted += x => { _s.SetTitle(x); Rebuild(); };
        title.FocusExited += () => _s.SetTitle(title.Text);
        col.AddChild(title);

        if (_s.DeploymentAvailable)
        {
            var styles = Enum.GetValues<EDeploymentStyle>().ToList();
            col.AddChild(PickRow("Deployment", "Deploy", styles.Select(AdvancedDeployment.Name).ToList(), styles.IndexOf(plan.DeploymentStyle),
                i => { _s.SetDeployment(styles[i]); Rebuild(); }, () => { _s.RerollDeployment(); Rebuild(); }));
        }
        if (_s.MissionTypeAvailable)
        {
            var games = Enum.GetValues<FDG.Missions.EMissionCardGame>().ToList();
            col.AddChild(PickRow("Mission", "MissionType", games.Select(MissionSetupSession.MissionTypeName).ToList(), games.IndexOf(plan.MissionCards),
                i => { _s.SetMissionType(games[i]); Rebuild(); }, () => { _s.RerollMissionType(); Rebuild(); }));
            col.AddChild(ForgeWidgets.Wrapped(CampaignMissionGenerator.DescribeMissionType(plan.MissionCards), 13, HudKit.Dim));
        }

        col.AddChild(HudKit.Heading("The map", 14));
        var maps = MapChoices();
        var mapPick = new OptionButton { Name = "Map", TooltipText = "Random: terrain is set out for the mission. Or one of your maps made for this table size." };
        mapPick.AddItem("Random");
        foreach (var (_, name) in maps) mapPick.AddItem(name);
        int sel = _s.CustomMap ? maps.FindIndex(m => m.Path == _s.MapPath) + 1 : 0;
        if (_s.CustomMap && sel == 0) { mapPick.AddItem(_s.MapName); sel = mapPick.ItemCount - 1; }
        mapPick.Selected = sel;
        mapPick.ItemSelected += i => { _s.SetMap(i == 0 ? null : i - 1 < maps.Count ? maps[(int)i - 1].Path : _s.MapPath); Rebuild(); };
        col.AddChild(mapPick);
        if (!_s.CustomMap)
        {
            var modes = MissionSetupSession.RandomTerrainModes.ToList();
            var mode = new OptionButton { Name = "Terrain" };
            foreach (var m in modes) mode.AddItem(MissionSetupSession.TerrainModeLabel(m));
            mode.Selected = Math.Max(0, modes.IndexOf(_s.TerrainMode));
            mode.ItemSelected += i => { _s.SetTerrainMode(modes[(int)i]); Rebuild(); };
            col.AddChild(mode);
        }
        col.AddChild(ForgeWidgets.Wrapped(MissionSetupSession.TerrainModeHelp(_s.TerrainMode), 13, HudKit.Dim));

        col.AddChild(HudKit.Heading("Journal page", 14));
        var notes = new TextEdit { Name = "Notes", Text = _s.Notes, CustomMinimumSize = new Vector2(0, 120), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "Briefing / notes for this mission..." };
        notes.FocusExited += () => _s.SetNotes(notes.Text);
        col.AddChild(notes);
        col.AddChild(Ui.Button("Save campaign as a file", () =>
        {
            string? saved = _s.SaveAsScript();
            Say(saved != null ? $"Saved to {CampaignScriptPaths.DirectoryName} - start a new campaign from it any time." : "Couldn't save the campaign file.", saved != null ? Palette.Lime : Palette.WoundsRed);
        }, "Saves every mission planned so far, with your notes, as a campaign to replay with fresh armies."));
    }

    private List<(string Path, string Name)> MapChoices()
    {
        int biggest = Math.Max(_s.YoursComing, _s.TheirsComing);
        var sizes = new (float W, float H)[] { GameWideConstants.TableSizeFor(_s.System), TableSizing.AutoSize(_s.System, EGameMode.Standard, biggest) };
        var list = new List<(string, string)>();
        foreach (string p in MapPaths.List())
        {
            if (TerrainLayoutLoader.TryLoadFromFile(p, out _) is not { } layout) continue;
            if (!sizes.Any(s => MathF.Abs(layout.TableWidthInches - s.W) < 0.01f && MathF.Abs(layout.TableHeightInches - s.H) < 0.01f)) continue;
            list.Add((p, layout.Name.Length > 0 ? layout.Name : System.IO.Path.GetFileNameWithoutExtension(p)));
        }
        return list;
    }

    private static Control PickRow(string label, string name, List<string> items, int selected, Action<int> pick, Action reroll)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(QuestWidgets.Line(label, 14, HudKit.Dim, 96, HudKit.Semi));
        var ob = new OptionButton { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
        foreach (string s in items) ob.AddItem(s);
        ob.Selected = Math.Max(0, selected);
        ob.ItemSelected += i => pick((int)i);
        row.AddChild(ob);
        row.AddChild(Ui.Button("Reroll", reroll, "Roll it again from the books.").Also(x => x.Name = "Reroll" + name));
        return row;
    }

    // ================================================================== the forces

    private void YourColumn(VBoxContainer col)
    {
        if (_s.Army is not { } army || _s.Info is not { } info) return;
        col.AddChild(HudKit.Heading("Your force - click to bring / leave", 14));
        col.AddChild(ForceCards(army, info, HudKit.Friendly, (u, sh) => { _s.ToggleYours(u, sh); Rebuild(); }, "Y"));
        var (line, problem) = _s.YourForceLine();
        col.AddChild(HudKit.Text(line, 16, HudKit.Fg, HudKit.Bold));
        if (problem != null) col.AddChild(ForgeWidgets.Wrapped(problem, 14, Palette.WoundsRed));
        if (info.SpecialObjective.Length > 0)
        {
            col.AddChild(HudKit.Heading("Your special objective", 13));
            col.AddChild(HudKit.Text(info.SpecialObjective, 16, Palette.HeroGold, HudKit.Bold));
            col.AddChild(ForgeWidgets.Wrapped(CampaignPreGame.ObjectiveText(info.SpecialObjective), 14, HudKit.Fg));
            col.AddChild(HudKit.Text($"Worth +{_s.Numbers.SpecialObjectiveBonus} pts into your treasury.", 13, HudKit.Dim, HudKit.Semi));
        }
        else col.AddChild(ForgeWidgets.Wrapped("No special objective rolled - roll it in the Camp before the mission (book, step 1).", 13, HudKit.Dim));
    }

    private void TheirColumn(VBoxContainer col)
    {
        col.AddChild(HudKit.Heading("Their force", 14));
        if (_s.Enemy is not { } enemy || _s.Foe is not { } foe || _s.Info is not { } info)
        {
            col.AddChild(ForgeWidgets.Wrapped("This campaign has no standing enemy, so the lobby rolls a fresh opponent and nothing carries over on their side.", 14, HudKit.Dim));
            return;
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(QuestWidgets.Line("Difficulty", 14, HudKit.Dim, 96, HudKit.Semi));
        var all = MissionDifficulty.All.ToList();
        var diff = new OptionButton { Name = "Difficulty", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var d in all)
        {
            diff.AddItem(MissionDifficulty.Name(d));
            diff.SetItemTooltip(diff.ItemCount - 1, MissionDifficulty.Describe(d, _s.YoursComing, _s.Numbers) + "\n" + MissionDifficulty.DescribeReward(d, _s.Numbers));
        }
        diff.Selected = Math.Max(0, all.IndexOf(MissionDifficulty.Resolve(info.Difficulty)));
        diff.ItemSelected += i => { _s.SetDifficulty(all[(int)i]); Rebuild(); };
        row.AddChild(diff);
        row.AddChild(Ui.Button("Reroll", () => { _s.RerollDifficulty(); Rebuild(); }, "Roll the difficulty from the book.").Also(x => x.Name = "RerollDifficulty"));
        row.AddChild(Ui.Button("Randomize", () => { _s.FillEnemyForce(randomize: true); Say("Rolled a fresh force for them at the same difficulty.", Palette.Lime); Rebuild(); },
            "Fills their force at random from the units they own, to the same difficulty. Without it they bring their most experienced units first.").Also(x => x.Name = "Randomize"));
        col.AddChild(row);
        col.AddChild(ForgeWidgets.Wrapped(MissionDifficulty.Describe(info.Difficulty, _s.YoursComing, _s.Numbers), 14, HudKit.Fg));
        col.AddChild(ForgeWidgets.Wrapped(MissionDifficulty.DescribeReward(info.Difficulty, _s.Numbers), 13, HudKit.Dim));

        if (_s.ChallengeBonusesApply)
        {
            col.AddChild(HudKit.Heading(ChallengeBonusesText.Title, 13).Also(h => h.TooltipText = ChallengeBonusesText.Intro));
            foreach ((string label, EChallengeBonuses flag, string book) in ChallengeBonusesText.Bonuses)
            {
                var cb = new CheckBox { Text = label, ButtonPressed = (info.ChallengeBonuses & flag) != 0, TooltipText = book, FocusMode = FocusModeEnum.None };
                cb.Toggled += on => _s.SetChallengeBonuses(on ? info.ChallengeBonuses | flag : info.ChallengeBonuses & ~flag);
                col.AddChild(cb);
            }
        }

        col.AddChild(ForceCards(enemy, foe, EnemyRed, (u, sh) => { _s.ToggleTheirs(u, sh); Rebuild(); }, "T"));
        bool over = _s.TheirsComing > _s.HardCap;
        col.AddChild(HudKit.Text(_s.TheirForceLine(), 16, over ? Palette.WoundsRed : HudKit.Fg, HudKit.Bold));
        if (over) col.AddChild(HudKit.Text($"Over the cap by {_s.TheirsComing - _s.HardCap} pts - leave something at home.", 14, Palette.WoundsRed, HudKit.Semi));
        var (under, good) = _s.UnderdogLine();
        col.AddChild(ForgeWidgets.Wrapped(under, 13, good ? Palette.Lime : HudKit.Dim));
        if (_s.CantReachLine() is { } reach) col.AddChild(ForgeWidgets.Wrapped(reach, 13, HudKit.Dim));
    }

    private static Control ForceCards(BuiltArmyFile army, StrikeTeamInfo info, Color team, Action<UnitFileEntry, StrikeUnit> click, string prefix)
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        int i = 0;
        foreach (var (unit, sheet) in MissionSetupSession.Cards(army))
        {
            string tag = MissionSetupSession.ForceTag(info, unit, sheet);
            bool resting = CampaignRules.CannotBringToMission(unit) != null;
            bool coming = !resting && MissionSetupSession.IsComing(info, sheet);
            var u = unit; var sh = sheet;
            var card = ForgeWidgets.Card(army.Faction, $"{prefix}{i++}", unit.Name, unit.PointCost, tag, coming, () => click(u, sh), null, FdgRaylib.Models3D.UnitPortraits.LookOf(unit.ModelLooks));
            card.TooltipText = resting ? CampaignRules.CannotBringToMission(unit)! : coming ? "Coming. Click to leave it at home." : "Staying home. Click to bring it.";
            if (!coming) card.Modulate = new Color(1, 1, 1, 0.5f);
            flow.AddChild(card);
        }
        return flow;
    }

    // ================================================================== start

    private void StartMission()
    {
        if (_s.Start(out string? error) is not { } launch) { Say(error ?? "Can't start the mission.", Palette.WoundsRed); return; }
        if (LobbySession.TryHost(launch.System, EGameMode.Standard, out LobbySession? session, out string hostError) && session != null)
            App.I.Open<LobbyScreen>(Scenes.Lobby, l => l.Session = session);
        else
        {
            MissionSetupSession.Pending = null;
            App.I.Message("Could not start the mission", hostError);
        }
    }

    /// <summary>Drives the screen without a person: "toggle:i" (your card), "their:i", "difficulty:i", "generate", "start".</summary>
    public void Drive(string cmd)
    {
        int c = cmd.IndexOf(':');
        string verb = c < 0 ? cmd : cmd[..c], arg = c < 0 ? "" : cmd[(c + 1)..];
        int.TryParse(arg, out int n);
        switch (verb)
        {
            case "toggle" when _s.Army is { } a && MissionSetupSession.Cards(a) is var yc && n < yc.Count: _s.ToggleYours(yc[n].Unit, yc[n].Sheet); break;
            case "their" when _s.Enemy is { } e && MissionSetupSession.Cards(e) is var tc && n < tc.Count: _s.ToggleTheirs(tc[n].Unit, tc[n].Sheet); break;
            case "difficulty" when n < MissionDifficulty.All.Count(): _s.SetDifficulty(MissionDifficulty.All.ElementAt(n)); break;
            case "generate": _s.GenerateAll(); break;
            case "start": StartMission(); return;
        }
        Rebuild();
    }
}
