using FDG;
using FDG.Campaign;
using FDG.Campaign.Map;
using FdgRaylib.Quest;
using Godot;
using OprGodot.Forge;
using OprGodot.Hud;

namespace OprGodot.Campaign;

/// <summary>
/// #467 step 3 - the campaign map (Map Campaign Screens mockup, approved 2026-11-15): the armies down the left with last
/// turn's report, the map in the middle, the selected region's card and this turn's order on the right. END TURN plays the
/// turn; each of the player's battles gets a card and then goes through Mission Setup; a finished turn shows its report.
/// All the rules are <see cref="MapCampaignSession"/> (FdgApp) and <c>FDG.Campaign.Map</c> (engine).
/// </summary>
public partial class MapCampaignScreen : Control, Hud.ILooked
{
    public string CampaignPath { get; set; } = "";
    public Hud.Looks.ELook Look => Hud.Looks.For(System);
    public EGameSystem System { get; set; } = EGameSystem.GrimdarkFuture;

    private MapCampaignSession? _s;
    private string? _selected;
    private MapOrder? _order;
    private MapBattle? _waiting;
    private List<string>? _lastReport;

    private HBoxContainer _top = null!;
    private HBoxContainer _body = null!;
    private Label _status = null!;
    private Control? _overlay;
    private MapView _map = null!;

    internal static readonly Color[] ArmyColours =
    {
        new("4ccbf2"), new("ed5c4d"), new("f0a03c"), new("b07ae8"),
    };

    internal static Color ColourOf(CampaignMap map, string? faction) =>
        map.Faction(faction) is { } f ? ArmyColours[Math.Clamp(f.Colour, 0, ArmyColours.Length - 1)] : new Color("6f7f83");

    internal static Color TypeColour(ERegionType t) => t switch
    {
        ERegionType.Hive => new Color("c9b37a"),
        ERegionType.Forge => new Color("e08a4a"),
        ERegionType.Shrine => new Color("c8a8e8"),
        ERegionType.Fortress => new Color("9fb4bc"),
        ERegionType.Medical => new Color("7fd6c2"),
        ERegionType.Port => new Color("6fb0e8"),
        _ => new Color("6cbf5a"),
    };

    private bool Realm => _s?.Map.Style == EMapStyle.Realm;
    private string Place => Realm ? "region" : "planet";
    private string Places => Realm ? "regions" : "planets";

    public override void _Ready()
    {
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
        _status = HudKit.Text("", 15, HudKit.Dim, HudKit.Semi);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_status);
        _map = new MapView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Name = "Map" };
        _map.Clicked += id => { _selected = id; Rebuild(); };
        VisibilityChanged += () => { if (Visible) Refresh(); };
        Refresh();
    }

    private void Say(string text, Color? c = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", c ?? HudKit.Dim);
    }

    // ------------------------------------------------------------------ state

    /// <summary>Re-reads everything (a mission or the Camp may have changed the files) and carries the turn on.</summary>
    private void Refresh()
    {
        _s = MapCampaignSession.Open(CampaignPath, out string? error);
        if (_s == null) { CloseOverlay(); Say(error ?? "Couldn't open the campaign.", Palette.WoundsRed); Rebuild(); return; }
        _selected ??= _s.Map.Regions.FirstOrDefault(r => r.Owner == CampaignMap.PlayerId && r.Capital)?.Id;
        if (_s.Map.Pending != null)
        {
            // A battle was set up: if its mission has been played, record it; if not (backed out of Mission Setup), its card again.
            if (_s.BattleUnderWay && !_s.CollectBattle())
            {
                Rebuild();
                if (_waiting == null) _waiting = _s.Step().WaitingFor;
                if (_waiting != null) ShowBattle(_waiting);
                return;
            }
            Carry();
            return;
        }
        CloseOverlay();
        Rebuild();
    }

    /// <summary>Plays the turn on: to the next battle of the player's (its card), or to the end (the report).</summary>
    private void Carry()
    {
        if (_s == null) return;
        MapTurnResult r = _s.Step();
        _waiting = r.WaitingFor;
        Rebuild();
        if (_waiting != null) { ShowBattle(_waiting); return; }
        _order = null;
        _lastReport = r.Lines.ToList();
        ShowReport(r.Map.History.Count > 0 ? r.Map.History[^1] : new MapTurnRecord { Turn = r.Map.Turn - 1, Lines = r.Lines });
    }

    /// <summary>Screenshot runs (ScriptedRun "mapdo:garrison" / "mapdo:claim" / "mapdo:rest"): gives that order on the first
    /// region it fits and ends the turn.</summary>
    internal void DemoTurn(string what)
    {
        if (_s == null) return;
        EOrderKind kind = what switch { "garrison" => EOrderKind.Garrison, "claim" => EOrderKind.Claim, "attack" => EOrderKind.Attack, _ => EOrderKind.Rest };
        MapRegion? r = kind == EOrderKind.Rest ? null
            : CampaignMapRules.Reachable(_s.Map, CampaignMap.PlayerId).FirstOrDefault(x => _s.WhyNot(new MapOrder(CampaignMap.PlayerId, kind, x.Id)) == null);
        _order = r == null ? null : new MapOrder(CampaignMap.PlayerId, kind, r.Id);
        _selected = r?.Id ?? _selected;
        EndTurn();
    }

    private void EndTurn()
    {
        if (_s == null) return;
        MapOrder order = _order ?? new MapOrder(CampaignMap.PlayerId, EOrderKind.Rest);
        if (_s.BeginTurn(order) is { } why) { Say(why, Palette.WoundsRed); return; }
        Say("");
        Carry();
    }

    // ------------------------------------------------------------------ layout

    private void Rebuild()
    {
        // The map view is kept between rebuilds (it holds nothing but what it's told to draw): lift it out of its old panel
        // before that panel is freed, or it goes with it.
        _map.GetParent()?.RemoveChild(_map);
        foreach (Node n in _top.GetChildren()) n.QueueFree();
        foreach (Node n in _body.GetChildren()) n.QueueFree();

        _top.AddChild(Ui.Button("< Campaign armies", App.I.Back, "Back to the campaign list.", "BackButton").Also(x => x.Name = "Back"));
        if (_s == null) return;
        CampaignMap map = _s.Map;
        var titles = new VBoxContainer();
        titles.AddChild(HudKit.Text(map.Player.Name.ToUpperInvariant(), 30, HudKit.Fg, HudKit.Exo(800)));
        titles.AddChild(HudKit.Text($"Map campaign - {GameSystemInfo.DisplayName(map.System)} - " +
                                    (map.VpTarget > 0 ? $"first to {map.VpTarget} VP, or take every rival's capital" : "Endless - take every rival's capital"),
            14, HudKit.Dim, HudKit.Semi));
        _top.AddChild(titles);
        _top.AddChild(Ui.Grow());
        EMapOutcome outcome = _s.Outcome;
        _top.AddChild(HudKit.Text(outcome switch
        {
            EMapOutcome.YouWon => "YOU WON THE CAMPAIGN",
            EMapOutcome.YouLost => "CAMPAIGN LOST",
            EMapOutcome.RivalWon => "A RIVAL WON",
            EMapOutcome.Draw => "A DRAW",
            _ => $"TURN {map.Turn}",
        }, 20, outcome is EMapOutcome.InProgress or EMapOutcome.YouWon ? HudKit.Lime : Palette.WoundsRed, HudKit.Bold).Also(l => l.SizeFlagsVertical = SizeFlags.ShrinkCenter));
        _top.AddChild(Ui.Button("Camp (your army)", OpenCamp, "Your units: traits, upgrades, recruits, injuries - the campaign's camp screen.").Also(x => x.Name = "Camp"));
        _top.AddChild(Ui.Button("Journal", ShowJournal, "Every turn's report.").Also(x => x.Name = "Journal"));

        _body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(ArmiesColumn(), 12, 10, 12, 10)).Also(p => p.CustomMinimumSize = new Vector2(300, 0)));
        _map.Bind(map, _selected, _order, outcome == EMapOutcome.InProgress && map.Pending == null);
        _body.AddChild(HudKit.Panel(HudKit.PanelBox(12), _map).Also(p => p.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        _body.AddChild(HudKit.Panel(HudKit.PanelBox(12), HudKit.Pad(CardColumn(), 14, 10, 14, 10)).Also(p => p.CustomMinimumSize = new Vector2(400, 0)));
    }

    private Control ArmiesColumn()
    {
        CampaignMap map = _s!.Map;
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(HudKit.Heading("Armies", 13));
        foreach (MapFaction f in map.Factions)
        {
            bool met = CampaignMapRules.HasMet(map, CampaignMap.PlayerId, f.Id);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 1);
            var name = new HBoxContainer();
            name.AddThemeConstantOverride("separation", 8);
            name.AddChild(new ColorRect { Color = ColourOf(map, f.Id), CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            name.AddChild(HudKit.Text(f.Name + (f.IsPlayer ? " (you)" : ""), 17, f.Alive ? HudKit.Fg : HudKit.Dim, HudKit.Bold));
            box.AddChild(name);
            if (!f.Alive) box.AddChild(HudKit.Text("Knocked out - lost its capital.", 13, Palette.WoundsRed, HudKit.Semi));
            else if (!met) box.AddChild(ForgeWidgets.Wrapped($"Not met yet - you'll see its numbers once it holds a {Place} next to yours.", 13, HudKit.Dim));
            else
            {
                MapArmyState st = _s.StateOf(f.Id);
                box.AddChild(HudKit.Text($"{_s.Vp(f.Id)} VP   {st.Treasury} pts   {CampaignMapRules.HeldCount(map, f.Id)} {Places}", 14, HudKit.Fg, HudKit.Semi));
                string siege = string.Join(", ", map.Regions.Where(r => r.Siege == f.Id).Select(r => r.Name));
                box.AddChild(HudKit.Text($"Army {st.Points} pts" + (siege.Length > 0 ? $" - besieging {siege}" : ""), 13, HudKit.Dim, HudKit.Semi));
            }
            col.AddChild(HudKit.Panel(new ChamferBox { Fill = HudKit.GlassLight, Border = met || f.IsPlayer ? HudKit.Edge : HudKit.Edge with { A = 0.4f }, Cut = 6 }, HudKit.Pad(box, 10, 6, 10, 6)));
        }
        col.AddChild(HudKit.Grow());
        col.AddChild(HudKit.Heading("Last turn", 13));
        var lines = map.History.Count > 0 ? map.History[^1].Lines : new List<string>();
        if (lines.Count == 0) col.AddChild(HudKit.Text("Nothing yet.", 14, HudKit.Dim, HudKit.Semi));
        foreach (string line in lines.Take(5)) col.AddChild(ForgeWidgets.Wrapped(line, 13, line.StartsWith("Your battle") ? HudKit.Lime : HudKit.Fg));
        if (lines.Count > 5) col.AddChild(Ui.Button("Full report", () => ShowReport(map.History[^1]), "Everything that happened last turn."));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(col);
        return scroll;
    }

    private Control CardColumn()
    {
        CampaignMap map = _s!.Map;
        var col = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 7);
        MapRegion? r = map.Region(_selected);
        col.AddChild(HudKit.Heading(Realm ? "Region" : "Planet", 13));
        if (r == null) col.AddChild(ForgeWidgets.Wrapped($"Click a {Place} on the map. Gold dashed rings are the ones you can act on this turn.", 15, HudKit.Dim));
        else
        {
            col.AddChild(HudKit.Text(r.Name + (r.Capital ? " - capital" : ""), 26, HudKit.Fg, HudKit.Exo(800)));
            col.AddChild(HudKit.Text(CampaignMapRules.TypeName(r.Type, map.Style) + (r.Starred ? " - starred, 1 VP while held" : ""), 15, HudKit.Dim, HudKit.Semi));
            if (r.Siege != null) col.AddChild(HudKit.Text($"Under siege by {map.Faction(r.Siege)?.Name} - one more win takes it.", 15, ColourOf(map, r.Siege), HudKit.Bold));
            Fact(col, "Held by", r.Owner == null ? "Nobody - a local garrison" : map.Faction(r.Owner)!.Name + (r.Owner == CampaignMap.PlayerId ? " (you)" : ""), r.Owner == null ? HudKit.Dim : ColourOf(map, r.Owner));
            Fact(col, "Perk", CampaignMapRules.PerkText(r.Type, map.Style, _s.Numbers));
            Fact(col, "Battlefield", CampaignMapRules.BattlefieldText(r.Type, map.System));
            Fact(col, "Next to", string.Join(", ", CampaignMapRules.Neighbours(map, r.Id).Select(id => map.Region(id)!.Name)));
            Actions(col, r);
        }
        col.AddChild(HudKit.Grow());
        col.AddChild(HudKit.Heading("Your order this turn", 13));
        string orderText = _order == null ? $"Rest: units recover, +{CampaignMapRules.RestIncome(_s.Numbers)} pts" : OrderText(_order);
        var orderRow = new HBoxContainer();
        orderRow.AddThemeConstantOverride("separation", 8);
        orderRow.AddChild(ForgeWidgets.Wrapped(orderText, 16, _order == null ? HudKit.Dim : HudKit.Lime).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        if (_order != null && map.Pending == null) orderRow.AddChild(Ui.Button("Rest instead", () => { _order = null; Rebuild(); }, "Rest this turn instead.").Also(x => x.Name = "RestInstead"));
        col.AddChild(orderRow);
        bool over = _s.Outcome != EMapOutcome.InProgress;
        bool mid = map.Pending != null;
        var end = Ui.Button(mid ? "YOUR BATTLE IS WAITING" : "END TURN", mid ? () => { if (_waiting != null) ShowBattle(_waiting); } : EndTurn,
            over ? "The campaign is over." : mid ? "This turn has a battle of yours still to play." : "Everyone acts: your order first, then the rivals'. You play every battle you're in.", "CommitButton");
        end.Name = "EndTurn";
        end.Disabled = over;
        end.CustomMinimumSize = new Vector2(0, 58);
        end.AddThemeFontSizeOverride("font_size", 22);
        col.AddChild(end);
        return col;
    }

    private void Actions(VBoxContainer col, MapRegion r)
    {
        CampaignMap map = _s!.Map;
        if (_s.Outcome != EMapOutcome.InProgress || map.Pending != null) return;
        if (r.Owner == CampaignMap.PlayerId) { col.AddChild(HudKit.Text("You hold it.", 15, HudKit.Lime, HudKit.Semi)); return; }
        if (!CampaignMapRules.CanReach(map, CampaignMap.PlayerId, r)) { col.AddChild(HudKit.Text("It isn't next to your land.", 15, HudKit.Dim, HudKit.Semi)); return; }
        if (r.Owner != null)
        {
            AddOrder(col, new MapOrder(CampaignMap.PlayerId, EOrderKind.Attack, r.Id), $"Attack {map.Faction(r.Owner)!.Name} here",
                r.Capital ? (r.Siege == CampaignMap.PlayerId ? "You are besieging it: win here and it falls." : "A capital takes two wins: this one besieges it.") : "Win and it's yours.");
            return;
        }
        int cost = CampaignMapRules.ClaimCost(map, CampaignMap.PlayerId, _s.Numbers);
        AddOrder(col, new MapOrder(CampaignMap.PlayerId, EOrderKind.Claim, r.Id), $"Pay {cost} pts to claim it",
            $"No battle. The price is {_s.Numbers.SpecialObjectiveBonus} pts per {Place} you hold. You have {_s.StateOf(CampaignMap.PlayerId).Treasury} pts.");
        AddOrder(col, new MapOrder(CampaignMap.PlayerId, EOrderKind.Garrison, r.Id), "Fight the garrison (free if you win)",
            "A small local force, about half yours. Pays no treasury or VP - the land is the prize.");
    }

    private void AddOrder(VBoxContainer col, MapOrder order, string label, string note)
    {
        string? why = _s!.WhyNot(order);
        bool chosen = _order == order;
        var b = Ui.Button(label, () => { _order = order; Say($"Order: {OrderText(order)}. Press END TURN when you're ready.", Palette.Lime); Rebuild(); }, why ?? note, "CommitButton");
        b.Name = "Order" + order.Kind;
        b.Disabled = why != null;
        b.ToggleMode = true;
        b.ButtonPressed = chosen;
        b.CustomMinimumSize = new Vector2(0, 44);
        col.AddChild(b);
        col.AddChild(ForgeWidgets.Wrapped(why ?? note, 13, why != null ? Palette.HitsYellow : HudKit.Dim));
    }

    private string OrderText(MapOrder o)
    {
        string where = _s!.Map.Region(o.Region)?.Name ?? "";
        return o.Kind switch
        {
            EOrderKind.Attack => $"Attack {where}",
            EOrderKind.Claim => $"Pay for {where}",
            EOrderKind.Garrison => $"Fight the garrison of {where}",
            _ => "Rest",
        };
    }

    private static void Fact(VBoxContainer col, string label, string value, Color? colour = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(HudKit.Text(label, 14, HudKit.Dim, HudKit.Semi).Also(l => l.CustomMinimumSize = new Vector2(90, 0)));
        row.AddChild(ForgeWidgets.Wrapped(value, 14, colour ?? HudKit.Fg).Also(l => l.SizeFlagsHorizontal = SizeFlags.ExpandFill));
        col.AddChild(row);
    }

    // ------------------------------------------------------------------ overlays

    private void CloseOverlay()
    {
        _overlay?.QueueFree();
        _overlay = null;
    }

    private VBoxContainer Overlay(Color border, float width = 760)
    {
        CloseOverlay();
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.04f, 0.7f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        _overlay = dim;
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        v.AddThemeConstantOverride("separation", 9);
        var panel = HudKit.Panel(new ChamferBox { Fill = HudKit.Glass with { A = 0.97f }, Solid = true, Border = border, Cut = 16, ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 22, ContentMarginBottom = 22 }, v);
        HudKit.Place(dim, panel, LayoutPreset.Center);
        return v;
    }

    /// <summary>The card before each of the player's battles (mockup screen 3).</summary>
    private void ShowBattle(MapBattle b)
    {
        if (_s == null) return;
        CampaignMap map = _s.Map;
        MapRegion r = map.Region(b.Region)!;
        string? themId = b.OpponentOf(CampaignMap.PlayerId);
        string them = themId == null ? "the local garrison" : map.Faction(themId)!.Name;
        bool garrison = themId == null;
        var v = Overlay(HudKit.Lime);
        v.AddChild(HudKit.Heading($"Turn {map.Turn} - your battle", 13));
        string head = b.Kind switch
        {
            EMapBattleKind.Garrison => $"You fight the garrison of {r.Name}",
            EMapBattleKind.Clash => $"You meet {them} at {r.Name}",
            _ when b.Attacker == CampaignMap.PlayerId => $"You attack {them} at {r.Name}",
            _ => $"{them} attacks you at {r.Name}",
        };
        v.AddChild(HudKit.Text(head, 30, HudKit.Fg, HudKit.Exo(800)));
        v.AddChild(HudKit.Text(CampaignMapRules.TypeName(r.Type, map.Style) + (r.Starred ? " - starred" : "") + (r.Capital ? " - a capital" : "")
                               + (b.Kind == EMapBattleKind.Clash ? " - you both went for it this turn" : ""), 15, HudKit.Dim, HudKit.Semi));
        Fact(v, "Battlefield", CampaignMapRules.BattlefieldText(r.Type, map.System) + ". Mission Setup opens on it; you can still pick another map there.");
        int under = _s.PlayerUnderdogBonus(b);
        Fact(v, "Your land", under > 0 ? $"+{under} underdog point(s) from the {Places} you hold" + (r.Owner == CampaignMap.PlayerId && r.Capital ? " (defending your capital: +2)" : "") : "No underdog points from land.");
        if (garrison)
        {
            Fact(v, "If you win", $"{r.Name} is yours. No treasury or VP - the land is the prize.");
            Fact(v, "If you lose", "Nothing changes. No treasury or VP either way.");
        }
        else if (b.Kind == EMapBattleKind.Clash)
        {
            Fact(v, "If you win", r.Owner == null ? $"{r.Name} is yours, and the book's reward for a win." : $"You beat {them}, but {map.Faction(r.Owner)!.Name} keeps {r.Name} this turn.");
            Fact(v, "If you lose", $"The book's loss reward ({_s.Numbers.LosePoints} pts).");
        }
        else if (b.Attacker == CampaignMap.PlayerId)
        {
            Fact(v, "If you win", (r.Capital && r.Siege != CampaignMap.PlayerId ? $"{r.Name} is under siege - one more win takes it." : $"{r.Name} is yours" + (r.Capital ? $", and {them} is knocked out" : "") + ".")
                                  + $" The book's reward: {CampaignNumbers.WinVp} VP + {_s.Numbers.WinPoints} pts (at Normal).");
            Fact(v, "If you lose", $"They keep it" + (r.Siege == CampaignMap.PlayerId ? " and the siege is lifted" : "") + $". The book's loss reward: {_s.Numbers.LosePoints} pts.");
        }
        else
        {
            Fact(v, "If you win", $"You keep {r.Name}" + (r.Siege != null ? " and the siege is lifted" : "") + $". The book's reward: {CampaignNumbers.WinVp} VP + {_s.Numbers.WinPoints} pts (at Normal).");
            Fact(v, "If you lose", (r.Capital && r.Siege != themId ? $"Your capital is under siege - lose here again and the campaign is over." : r.Capital ? "Your capital falls - the campaign is lost." : $"{them} takes {r.Name}.")
                                   + $" The book's loss reward: {_s.Numbers.LosePoints} pts.");
        }
        var later = _s.LaterPlayerBattles(b);
        if (later.Count > 0)
            Fact(v, "Still to come", string.Join("; ", later.Select(x => $"{map.Faction(x.Attacker)?.Name} attacks you at {map.Region(x.Region)?.Name}")));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(Ui.Button("Back to the map", () => { CloseOverlay(); Rebuild(); }, "Look at the map first. The battle waits: YOUR BATTLE IS WAITING (bottom right) brings this back.", "BackButton").Also(x => x.Name = "BattleBack"));
        row.AddChild(Ui.Button("SET UP THIS MISSION", () => SetUpMission(b), "Opens Mission Setup with this battle filled in: who's coming on both sides, the difficulty, the map.", "CommitButton")
            .Also(x => { x.Name = "SetUpMission"; x.CustomMinimumSize = new Vector2(280, 52); x.AddThemeFontSizeOverride("font_size", 19); }));
        v.AddChild(row);
    }

    private void SetUpMission(MapBattle b)
    {
        if (_s == null) return;
        if (_s.PrepareBattle(b) is { } why) { Say(why, Palette.WoundsRed); return; }
        var others = new List<CampaignEntrant>();
        if (_s.InfoOf(CampaignMap.PlayerId).EnemyArmyFile is { } enemy && FdgRaylib.Quest.StrikeTeamFiles.Load(enemy) is { StrikeTeam: { } info } army)
            others.Add(new CampaignEntrant(info.Name, CampaignRules.ArmyValue(army), false));
        CloseOverlay();
        App.I.Open<MissionSetupScreen>(Scenes.MissionSetup, m => { m.CampaignPath = CampaignPath; m.Others = others; });
    }

    /// <summary>A finished turn's report and the standing (mockup screen 4).</summary>
    private void ShowReport(MapTurnRecord record)
    {
        if (_s == null) return;
        CampaignMap map = _s.Map;
        var v = Overlay(HudKit.Edge, 820);
        v.AddChild(HudKit.Heading($"Turn {record.Turn} report", 13));
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 3);
        foreach (string line in record.Lines) lines.AddChild(ForgeWidgets.Wrapped(line, 15, line.StartsWith("Your battle") ? HudKit.Lime : HudKit.Fg));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 280), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        lines.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(lines);
        v.AddChild(scroll);
        v.AddChild(HudKit.Heading("Standing", 13));
        foreach (MapFaction f in map.Factions)
        {
            bool met = CampaignMapRules.HasMet(map, CampaignMap.PlayerId, f.Id);
            string text = !f.Alive ? $"{f.Name} - knocked out" : !met ? $"{f.Name} - not met yet" : $"{f.Name}{(f.IsPlayer ? " (you)" : "")} - {_s.Vp(f.Id)} VP, {CampaignMapRules.HeldCount(map, f.Id)} {Places}";
            v.AddChild(HudKit.Text(text, 16, f.Alive ? ColourOf(map, f.Id) : HudKit.Dim, HudKit.Semi));
        }
        EMapOutcome outcome = _s.Outcome;
        if (outcome != EMapOutcome.InProgress)
            v.AddChild(HudKit.Text(outcome switch
            {
                EMapOutcome.YouWon => "YOU WON THE CAMPAIGN!",
                EMapOutcome.YouLost => "YOU LOST YOUR CAPITAL - THE CAMPAIGN IS LOST",
                EMapOutcome.RivalWon => "A RIVAL REACHED THE VP TARGET - THE CAMPAIGN IS OVER",
                _ => "THE CAMPAIGN ENDS IN A DRAW",
            }, 24, outcome == EMapOutcome.YouWon ? Palette.HeroGold : Palette.WoundsRed, HudKit.Bold));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(Ui.Button("Camp (spend your points)", () => { CloseOverlay(); OpenCamp(); }, "Upgrades, recruits and trait picks before the next turn."));
        row.AddChild(Ui.Button("BACK TO THE MAP", () => { CloseOverlay(); Rebuild(); }, "", "CommitButton").Also(x => { x.Name = "ReportClose"; x.CustomMinimumSize = new Vector2(240, 50); }));
        v.AddChild(row);
    }

    /// <summary>Every turn's report, newest first (Kai: "the Journal keeps every turn's report").</summary>
    private void ShowJournal()
    {
        if (_s == null) return;
        var v = Overlay(HudKit.Edge, 860);
        v.AddChild(HudKit.Heading("Journal - every turn", 13));
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 4);
        if (_s.Map.History.Count == 0) lines.AddChild(HudKit.Text("No turns played yet.", 15, HudKit.Dim, HudKit.Semi));
        foreach (MapTurnRecord t in Enumerable.Reverse(_s.Map.History))
        {
            lines.AddChild(HudKit.Text($"Turn {t.Turn}", 18, HudKit.Lime, HudKit.Bold));
            foreach (string line in t.Lines) lines.AddChild(ForgeWidgets.Wrapped(line, 14, HudKit.Fg));
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        lines.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(lines);
        v.AddChild(scroll);
        v.AddChild(ForgeWidgets.Wrapped("Each mission's own page (casualties, XP, your notes) is in the Camp's Journal.", 13, HudKit.Dim));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(Ui.Button("Close", () => { CloseOverlay(); Rebuild(); }, "", "CommitButton").Also(x => x.Name = "JournalClose"));
        v.AddChild(row);
    }

    private void OpenCamp() =>
        App.I.Open<ArmyCampaignsScreen>(Scenes.ArmyCampaigns, a => { a.System = _s?.System ?? System; a.CampOf = CampaignPath; });
}

/// <summary>
/// The map itself (mockup screen 2): a star system (planets on lanes) or a realm (regions on roads). Drawn plainly - the
/// look is rental; what's on it is the design: owner rings, capitals, stars, sieges, what you can act on, your order.
/// </summary>
public partial class MapView : Control
{
    public event Action<string>? Clicked;

    private CampaignMap? _map;
    private string? _selected;
    private MapOrder? _order;
    private bool _canAct;

    public void Bind(CampaignMap map, string? selected, MapOrder? order, bool canAct)
    {
        _map = map;
        _selected = selected;
        _order = order;
        _canAct = canAct;
        MouseFilter = MouseFilterEnum.Stop;
        QueueRedraw();
    }

    private float Radius => Mathf.Clamp(Mathf.Min(Size.X, Size.Y) / 22f, 16f, 30f);

    private Vector2 At(MapRegion r) => new(r.X * Size.X, r.Y * Size.Y);

    public override void _GuiInput(InputEvent e)
    {
        if (_map == null || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        MapRegion? hit = _map.Regions.OrderBy(r => At(r).DistanceTo(mb.Position)).FirstOrDefault();
        if (hit != null && At(hit).DistanceTo(mb.Position) <= Radius * 1.6f) Clicked?.Invoke(hit.Id);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map == null) return;
        bool realm = _map.Style == EMapStyle.Realm;
        DrawRect(new Rect2(Vector2.Zero, Size), realm ? new Color("1b1a14") : new Color("070c11"));
        var rng = new Random(_map.Seed);
        if (realm)
            for (int i = 0; i < 60; i++)
                DrawCircle(new Vector2((float)rng.NextDouble() * Size.X, (float)rng.NextDouble() * Size.Y), 30 + (float)rng.NextDouble() * 90,
                    new Color(0.5f + (float)rng.NextDouble() * 0.15f, 0.45f, 0.3f, 0.035f));
        else
            for (int i = 0; i < 220; i++)
                DrawCircle(new Vector2((float)rng.NextDouble() * Size.X, (float)rng.NextDouble() * Size.Y), 0.5f + (float)rng.NextDouble() * 1.1f,
                    new Color(1, 1, 1, 0.15f + (float)rng.NextDouble() * 0.5f));

        foreach (MapLink l in _map.Links)
        {
            Vector2 a = At(_map.Region(l.A)!), b = At(_map.Region(l.B)!);
            DrawDashedLine(a, b, realm ? new Color("8a7a55") : new Color("4b6b78"), realm ? 3 : 1.5f, realm ? 6 : 8);
        }

        float rad = Radius;
        Font font = HudKit.Bold;
        foreach (MapRegion r in _map.Regions)
        {
            Vector2 p = At(r);
            Color own = MapCampaignScreen.ColourOf(_map, r.Owner);
            if (r.Id == _selected) DrawArc(p, rad + 11, 0, Mathf.Tau, 48, HudKit.Lime, 3);
            else if (_canAct && CampaignMapRules.CanReach(_map, CampaignMap.PlayerId, r)) DashedRing(p, rad + 9, Palette.HeroGold, 1.5f);
            if (realm)
            {
                var pts = new Vector2[8];
                for (int i = 0; i < 8; i++)
                {
                    float ang = i / 8f * Mathf.Tau + r.X * 3;
                    float wob = rad * (1.1f + 0.18f * Mathf.Sin(i * 2.3f + r.Y * 10));
                    pts[i] = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.85f) * wob;
                }
                DrawColoredPolygon(pts, MapCampaignScreen.TypeColour(r.Type) with { A = 0.45f });
                DrawPolyline(pts.Append(pts[0]).ToArray(), own, 4);
            }
            else
            {
                DrawCircle(p, rad, MapCampaignScreen.TypeColour(r.Type));
                DrawCircle(p + new Vector2(rad * 0.25f, rad * 0.25f), rad * 0.8f, new Color(0, 0, 0, 0.22f));
                DrawArc(p, rad + 4, 0, Mathf.Tau, 48, own, 4);
            }
            if (r.Siege != null) DashedRing(p, rad + 15, MapCampaignScreen.ColourOf(_map, r.Siege), 3);
            if (r.Capital) DrawColoredPolygon(new[]
            {
                p + new Vector2(-11, -rad - 8), p + new Vector2(-11, -rad - 20), p + new Vector2(-5, -rad - 13), p + new Vector2(0, -rad - 22),
                p + new Vector2(5, -rad - 13), p + new Vector2(11, -rad - 20), p + new Vector2(11, -rad - 8),
            }, Palette.HeroGold);
            if (r.Starred) Star(p + new Vector2(rad + 4, -rad + 2), 7, Palette.HeroGold);
            if (_order?.Region == r.Id)
            {
                DrawLine(p + new Vector2(-rad - 14, -rad + 4), p + new Vector2(-rad + 2, -rad - 12), HudKit.Lime, 3);
                DrawLine(p + new Vector2(-rad - 14, -rad - 12), p + new Vector2(-rad + 2, -rad + 4), HudKit.Lime, 3);
            }
            Vector2 size = font.GetStringSize(r.Name, HorizontalAlignment.Left, -1, 15);
            Vector2 at = p + new Vector2(-size.X / 2, rad + 22);
            DrawStringOutline(font, at, r.Name, HorizontalAlignment.Left, -1, 15, 4, new Color(0, 0, 0, 0.85f));
            DrawString(font, at, r.Name, HorizontalAlignment.Left, -1, 15, HudKit.Fg);
        }
    }

    private void DashedRing(Vector2 c, float r, Color colour, float width)
    {
        const int segments = 24;
        for (int i = 0; i < segments; i += 2)
            DrawArc(c, r, i / (float)segments * Mathf.Tau, (i + 1) / (float)segments * Mathf.Tau, 4, colour, width);
    }

    private void Star(Vector2 c, float r, Color colour)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
        }
        DrawColoredPolygon(pts, colour);
    }
}
