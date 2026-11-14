using Godot;

namespace OprGodot;

/// <summary>
/// A test driver for checking screens without a person at the keyboard. Started by the user arguments after
/// <c>--</c> on the Godot command line, and does nothing otherwise:
/// <list type="bullet">
/// <item><c>--press=NameA,NameB</c> - presses the buttons with those node names, one after another, a few frames apart.</item>
/// <item><c>--shot=path.png</c> - saves a screenshot of the window once the presses are done, then quits.</item>
/// <item><c>--size=1920x1080</c> - window size for the screenshot.</item>
/// </list>
/// Example: <c>godot --path OprGodot -- --press=GrimdarkFuture --shot=/tmp/gf.png</c>.
/// </summary>
public partial class ScriptedRun : Node
{
    private Queue<string> _presses = new();
    private string? _shot;
    private int _wait = 20;

    /// <summary>True when the command line asks for a scripted run (no node made).</summary>
    public static bool IsRequested => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--press=") || a.StartsWith("--shot="));

    public static bool Requested(out ScriptedRun run)
    {
        run = new ScriptedRun();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--press="))
                foreach (string name in arg["--press=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    run._presses.Enqueue(name);
            else if (arg.StartsWith("--shot=")) run._shot = arg["--shot=".Length..];
            else if (arg.StartsWith("--size=") && arg["--size=".Length..].Split('x') is [var w, var h]
                     && int.TryParse(w, out int width) && int.TryParse(h, out int height))
                DisplayServer.WindowSetSize(new Vector2I(width, height));
        }
        return run._presses.Count > 0 || run._shot != null;
    }

    public override void _Process(double delta)
    {
        if (--_wait > 0) return;
        _wait = 15;

        if (_presses.TryDequeue(out string? name))
        {
            // "wait:N" waits N more frames (a lobby filling its seats, a scan finishing).
            if (name.StartsWith("wait:") && int.TryParse(name[5..], out int frames)) { _wait = frames; return; }
            // "portraits:<model folder>|<out folder>" photographs every model there at every portrait angle, then quits.
            if (name.StartsWith("portraits:") && name[10..].Split('|') is [var pin, var pout])
            {
                var batch = new Lab.PortraitShot.Batch(pin, pout);
                batch.Done += () => GetTree().Quit();
                AddChild(batch);
                _wait = int.MaxValue;
                return;
            }
            // "tab:Name" opens the tab called Name in whatever TabContainer has one.
            if (name.StartsWith("tab:")) { OpenTab(GetTree().Root, name[4..]); return; }
            // "army:<file>" gives the first (host) seat that army in the lobby; "confirm" presses OK on an open dialog.
            if (name.StartsWith("army:") && Find<LobbyScreen>(GetTree().Root) is { } lobby)
            {
                var vm = lobby.Session.Vm;
                // The host's own seat is row 0; a guest's is the one seat it may edit.
                var seat = vm.HasHostPrivileges ? vm.PlayerInfos.First().PlayerID
                    : vm.PlayerInfos.First(p => vm.CheckCanModifyPlayerIDInfo(p.PlayerID)).PlayerID;
                if (FdgRaylib.Lobby.LobbySession.LoadArmyFile(name[5..]) is { } army) lobby.Session.Vm.UpdateArmyListFile(seat, army);
                else GD.PrintErr($"ScriptedRun: no army at {name[5..]}");
                return;
            }
            // "join:127.0.0.1" types that address into the open Join a Game popup and joins.
            // "friend:Marcus|marcus-pc.tail1.ts.net|fav" adds a friend in the open Join a Game popup ("fav" stars them).
            if (name.StartsWith("friend:") && Find<JoinPopup>(GetTree().Root) is { } fp && name[7..].Split('|') is { Length: >= 2 } fa)
            { fp.AddFriendForTest(fa[0], fa[1], fa.Length > 2 && fa[2] == "fav"); return; }
            if (name.StartsWith("join:") && Find<JoinPopup>(GetTree().Root) is { } joinPopup) { joinPopup.JoinAddress(name[5..]); _wait = 240; return; }
            // "waitplayers:2" holds the host until that many seats are filled (a friend has joined).
            if (name.StartsWith("waitplayers:") && int.TryParse(name[12..], out int players))
            {
                bool full = Find<LobbyScreen>(GetTree().Root) is { } wl && wl.Session.Vm.PlayerInfos.Count >= players
                    && wl.Session.Vm.PlayerInfos.Any(p => p.PlayerType == FDG.Players.EPlayerType.Network);
                if (!full && ++_waitedFor < 3000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} {(full ? "a friend joined" : "timed out")}");
                return;
            }
            // "import:<share link>" pastes a link into the open Import from Army Forge box and imports it.
            if (name.StartsWith("import:") && Find<ImportArmyPopup>(GetTree().Root) is { } import) { import.ImportLink(name[7..]); _wait = 240; return; }
            // "vm:SetFastPlay=true;true;true" calls that lobby setter (test runs of settings with no button press path).
            if (name.StartsWith("vm:") && Find<LobbyScreen>(GetTree().Root) is { } vmLobby) { CallVm(vmLobby.Session.Vm, name[3..]); return; }
            // "snapall" photographs the first of each --hold decision kind (to <shot dir>/<Kind>.png) and lets the
            // test driver answer it, until every held kind is done or the game ends.
            if (name == "snapall") { SnapAll(); return; }
            // "examples" waits for your turn, then stages the rare screens over it (Command Group, aircraft, a pick with
            // Skip, concede, pick several) using your own units - for the UI review; each is answered after its photo.
            if (name == "examples") { _examplesPending = true; return; }
            // "hordeshop" waits for your turn, then opens the Horde Shop over it with your units (an example wave).
            if (name == "hordeshop") { if (!HordeShopExample() && ++_waitedFor < 20000) { _presses = new Queue<string>(_presses.Prepend(name)); return; } _waitedFor = 0; _wait = 30; return; }
            // "shop:<command>" drives the open Horde Shop (see HordeShopView.Drive).
            if (name.StartsWith("shop:"))
            {
                var view = (Find<Battle.BattleScreen>(GetTree().Root)?.Decisions.Current as Battle.HordeShopDecision)?.View;
                if ((view == null || !view.IsReady) && ++_waitedFor < 4000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                view?.Drive(name[5..]);
                _wait = 20;
                return;
            }
            // "lab:<file>" opens that file in the Asset Lab; "labview:0-3" picks its camera (close / play / table / top);
            // "labcopies:0-4" its copies (1 / 5 / 10 / 20 / 100); "labset:Height=40" sets one of its boxes by node name.
            if (name.StartsWith("lab") && Find<Lab.AssetLabScreen>(GetTree().Root) is { } lab)
            {
                string arg = name[(name.IndexOf(':') + 1)..];
                if (name.StartsWith("lab:")) lab.Open(arg);
                else if (name.StartsWith("labview:") && int.TryParse(arg, out int v)) lab.View(v);
                else if (name.StartsWith("labcopies:") && int.TryParse(arg, out int c)) lab.SetCopies(c);
                else if (name.StartsWith("labset:") && arg.Split('=') is [var box, var val]) lab.SetBox(box, val);
                // "labportraits:<army>" makes that army's portraits ("*" every army, "open" just opens the window).
                else if (name.StartsWith("labportraits:"))
                {
                    var pp = lab.OpenPortraits();
                    if (arg != "open") Callable.From(() => pp.StartFor(arg)).CallDeferred();
                }
                // "labdl:<catalogue>|<unit list>|<book>" runs Download models for that book (test files; "-" = the usual one).
                else if (name.StartsWith("labdl:") && arg.Split('|') is [var catFile, var mapFile, var book])
                {
                    var dlp = lab.OpenDownloads(catFile == "-" ? null : catFile, mapFile == "-" ? null : mapFile);
                    if (book != "open") dlp.StartFor(book); // "open" = just open the window
                    _wait = 600;
                }
                _wait = 30;
                return;
            }
            // "rev:turn:90" / "rev:ok" / "rev:skip" / "rev:all:<yaw>" drive the facing review.
            if (name.StartsWith("rev:") && Find<Lab.FacingReviewScreen>(GetTree().Root) is { } rev)
            {
                string[] a = name.Split(':');
                if (a[1] == "turn" && float.TryParse(a[2], out float deg)) rev.Turn(deg);
                else if (a[1] == "ok") rev.MarkAndNext();
                else if (a[1] == "all" && float.TryParse(a[2], out float yaw)) rev.ApplyToAll(yaw);
                _wait = 40;
                return;
            }
            // "pick:<unit>|<n>" in Choose models: select that unit's nth version (preview); "pick:tick" ticks the unit.
            if (name.StartsWith("pick:") && Find<Lab.ModelPickerScreen>(GetTree().Root) is { } picker)
            {
                string[] a = name[5..].Split('|');
                if (a[0] == "tick") picker.TickSelectedUnit();
                else if (a.Length == 2 && int.TryParse(a[1], out int n)) picker.SelectVersion(a[0], n);
                _wait = 120;
                return;
            }
            // "match:<command>" drives Match models (see MatchModelsScreen.Drive): "match:fac:Tyranids", "match:use"...
            if (name.StartsWith("match:") && Find<Lab.MatchModelsScreen>(GetTree().Root) is { } match)
            {
                match.Drive(name[6..]);
                _wait = 30;
                return;
            }
            // "forge:<command>" drives the Army Forge (see ArmyForgeScreen.Drive): "forge:book:Alien Hives", "forge:add:Shooter Grunts"...
            if (name.StartsWith("hero:") && Find<Quest.HeroBuilderScreen>(GetTree().Root) is { } heroes) { heroes.Drive(name[5..]); _wait = 20; return; }
            // "qc:<command>" drives Quest Campaigns (see QuestCampaignsScreen.Drive); "partypick:N" picks N heroes in an open Choose heroes popup.
            if (name.StartsWith("qc:") && Find<Quest.QuestCampaignsScreen>(GetTree().Root) is { } qc && qc.Visible) { qc.Drive(name[3..]); _wait = 20; return; }
            // "ac:<command>" drives the army campaign screen; "ms:<command>" Mission Setup (see their Drive methods).
            if (name.StartsWith("ac:") && Find<Campaign.ArmyCampaignsScreen>(GetTree().Root) is { } ac && ac.Visible) { ac.Drive(name[3..]); _wait = 20; return; }
            // "mm:<command>" the map list, "me:<command>" the map editor.
            if (name.StartsWith("mm:") && Find<Maps.MapMakerScreen>(GetTree().Root) is { } mm && mm.Visible) { mm.Drive(name[3..]); _wait = 20; return; }
            if (name.StartsWith("me:") && Find<Maps.MapEditorScreen>(GetTree().Root) is { } me && me.Visible) { me.Drive(name[3..]); _wait = 20; return; }
            if (name.StartsWith("ms:") && Find<Campaign.MissionSetupScreen>(GetTree().Root) is { } ms && ms.Visible) { ms.Drive(name[3..]); _wait = 20; return; }
            if (name.StartsWith("partypick:") && int.TryParse(name[10..], out int pn) && Find<Quest.HeroPartyPopup>(GetTree().Root) is { } party) { party.PickFirst(pn); _wait = 20; return; }
            if (name.StartsWith("forge:") && Find<Forge.ArmyForgeScreen>(GetTree().Root) is { } forge)
            {
                if (!forge.IsReady && ++_waitedFor < 2000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                forge.Drive(name[6..]);
                _wait = 20;
                return;
            }
            if (name == "confirm")
            {
                foreach (var d in AllOf<AcceptDialog>(GetTree().Root).Where(d => d.Visible)) d.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
                return;
            }
            // "waitfor:ShootDecision" holds until the battle is waiting on that kind of decision (max ~3000 frames).
            if (name.StartsWith("waitfor:"))
            {
                bool there = Find<Battle.BattleScreen>(GetTree().Root)?.Decisions.Current?.GetType().Name == name[8..];
                if (!there && ++_waitedFor < 200) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} {(there ? "reached" : "timed out")}");
                return;
            }
            // "waitround:2" holds until the battle has reached that round (or ended).
            if (name.StartsWith("waitround:") && int.TryParse(name[10..], out int round))
            {
                var b = Find<Battle.BattleScreen>(GetTree().Root);
                bool there = b != null && (b.Session.Table.Progress.RoundCount >= round || b.Session.EndedText != null);
                if (!there && ++_waitedFor < 2000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} {(there ? "reached" : "timed out")}");
                return;
            }
            // "snap:Name" saves a picture of the screen as it is now (<shot dir>/Name.png).
            if (name.StartsWith("snap:"))
            {
                string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_shot ?? "/tmp/x.png") ?? "/tmp", name[5..] + ".png");
                GetViewport().GetTexture().GetImage().SavePng(file);
                GD.Print($"ScriptedRun: snapped {name[5..]} - {file}");
                return;
            }
            // Real mouse clicks through the whole input pipeline, on the battle table:
            // "clickmine" (your first ready unit), "clickenemy" (the enemy nearest it), "clickrel:dx;dz"
            // (the selected unit's middle + dx,dz inches), "hoverrel:dx;dz" (the same, pointer only).
            // "autopick" lets the bot answer whatever the battle is asking right now (e.g. which unit to deploy next).
            if (name == "autopick" && Find<Battle.BattleScreen>(GetTree().Root) is { Decisions.Current: { } now } ab) { now.AutoPlay(ab); return; }
            if (name.StartsWith("click") || name.StartsWith("hover")) { TableMouse(name); return; }  // also "clickdrag:x;z|x2;z2"
            if (FindButton(GetTree().Root, name) is { } button) button.EmitSignal(BaseButton.SignalName.Pressed);
            else GD.PrintErr($"ScriptedRun: no button named '{name}' on screen.");
            return;
        }

        if (_shot != null)
        {
            Image image = GetViewport().GetTexture().GetImage();
            Error saved = image.SavePng(_shot);
            GD.Print($"ScriptedRun: screenshot {(saved == Error.Ok ? "saved" : "FAILED")} - {_shot}");
        }
        GetTree().Quit();
    }

    private bool _examplesPending;

    /// <summary>Opens the Horde Shop over your turn with your own units: the first in reach, the last out of reach,
    /// the second down to half and reinforceable - so every tab has something on it.</summary>
    private static int Cost(FDG.IUnit u) => (u as FDG.UnitData)?.PointCost ?? 100;

    private bool HordeShopExample()
    {
        var battle = Find<Battle.BattleScreen>(GetTree().Root);
        if (battle?.Decisions.Current is not Battle.ActivateDecision act) return false;
        var me = act.Request.TargetPlayerID;
        var table = battle.Session.Table;
        var objectives = table.Objectives.Objects.Take(2).Select((o, i) =>
            new FDG.StageResolution.Requests.HordeShopObjective(i + 1, o.Position.x, o.Position.z, i == 0)).ToList();
        if (objectives.Count == 0) objectives.Add(new FDG.StageResolution.Requests.HordeShopObjective(1, 36, 24, true));
        var mine = table.Units.Objects.Where(u => u.PlayerID == me).ToList();
        var units = mine.Select((u, i) => new FDG.StageResolution.Requests.HordeShopUnit(u.ID.ID, u.Name, u.Models.Count, Cost(u), null,
            string.Join(", ", u.Models.SelectMany(m => m.Weapons).GroupBy(w => w.Name).Select(g => $"{g.Count()}x {g.Key}")))
        {
            NotEligibleReason = i == mine.Count - 1 && mine.Count > 1 ? "It is more than 9\" from every objective your side seized this wave." : null,
            ReinforceCost = (Cost(u) + 1) / 2,
            ReinforceRestores = i == 1 ? $"up to {Math.Max(1, u.Models.Count / 2)} models" : null,
            NotReinforceableReason = i == 1 ? null : "It is not down to half strength.",
        }).ToList();
        var seat = battle.Session.SeatOf(me);
        var request = new FDG.StageResolution.Requests.HordeShopRequest(me, 1, 120, seat?.Faction ?? "", seat?.Name ?? "", objectives)
        {
            Upgradable = units, GameSystemSlug = FDG.ArmyBuilding.GameSystems.GrimdarkFuture, PointsEarned = 100, PointsPerObjective = 40, PriorityBonus = 20,
        };
        Battle.BattleResolvers.Inbox.OfferFirst(new Battle.HordeShopDecision(request));
        GD.Print("ScriptedRun: horde shop staged");
        return true;
    }

    /// <summary>Stages the example screens once it is your turn (checked from snapall each tick).</summary>
    private void Examples()
    {
        var battle = Find<Battle.BattleScreen>(GetTree().Root);
        if (battle?.Decisions.Current is not Battle.ActivateDecision act || act.Request.ValidOptions.Count < 2) return;
        _examplesPending = false;
        var me = act.Request.TargetPlayerID;
        var units = act.Request.ValidOptions.Select(o => o.Option).ToList();
        var inbox = Battle.BattleResolvers.Inbox;
        inbox.OfferFirst(new Battle.PickStringsDecision(new FDG.StageResolution.Requests.PickStringsRequest(me,
            "Build your mission card deck: pick 3", new[] { "Seize Ground", "Hold the Line", "Assassinate", "Recon", "Sabotage" }, 3, 3,
            new[] { "Seize Ground", "Recon" })));
        inbox.OfferFirst(new Battle.ConcedeDecision(new FDG.StageResolution.Requests.ConcedeCheckRequest(me, 3)));
        inbox.OfferFirst(new Battle.CancellableUnitPickDecision(new FDG.StageResolution.Requests.CancellableSelectionRequest<FDG.UnitData>(me,
            "Bold Commander: pick a friendly unit to activate next (or skip)",
            units.Skip(1).Select(u => new FDG.StageResolution.Requests.CancellableSelectionRequest<FDG.UnitData>.ValidOption(u, u.GetValue().Name + " (next)")).ToList(),
            Array.Empty<FDG.StageResolution.Requests.CancellableSelectionRequest<FDG.UnitData>.InvalidOption>())));
        inbox.OfferFirst(new Battle.AircraftDecision(new FDG.StageResolution.Requests.AircraftAdvanceRequest(me, "Aircraft move",
            units[0], new FDG.Float2(0, 1), 30, 36)));
        inbox.OfferFirst(new Battle.CommandGroupDecision(new FDG.StageResolution.Requests.PickCommandGroupUnitsRequest(me,
            units[0].GetValue().Name, "Hold the Line Aura", 2,
            units.Skip(1).Select(u => new FDG.StageResolution.Requests.PickCommandGroupUnitsRequest.ValidOption(u, u.GetValue().Name)).ToList())));
        GD.Print("ScriptedRun: examples staged");
    }

    private int _snapSettle = -1, _snapTries;
    private FDG.IUnit? _fireFrom, _fireAt;

    private void SnapAll()
    {
        var battle = Find<Battle.BattleScreen>(GetTree().Root);
        bool more = Battle.BattleScreen.Hold.Count > 0 && battle?.Session.EndedText == null && ++_snapTries < 20000;
        if (!more) { GD.Print($"ScriptedRun: snapall done, not seen: {string.Join(",", Battle.BattleScreen.Hold)}"); return; }
        _presses = new Queue<string>(_presses.Prepend("snapall"));
        _wait = 3;
        if (_examplesPending) Examples();
        string? kind = battle?.Decisions.Current?.SnapName;
        if (kind == null || !Battle.BattleScreen.Hold.Contains(kind)) { _snapSettle = -1; return; }
        // Only photograph the action menu when it has extra actions beside the four (that is what is being reviewed).
        if (battle!.Decisions.Current is Battle.ActionDecision { Rows: null } plain) { plain.AutoPlay(battle); _snapSettle = -1; return; }
        if (_snapSettle < 0)
        {
            _snapSettle = 25;
            // Set the screen up the way it is being reviewed: a unit picked, the pointer on a target, the popup open.
            switch (battle.Decisions.Current)
            {
                case Battle.ActivateDecision act when act.Request.ValidOptions.Count > 0:
                    battle.Decisions.UnitPicked(act.Request.ValidOptions[0].Option.GetValue());
                    break;
                case Battle.ShootDecision shoot when shoot.AnyTarget is { } target:
                    battle.TestHover = target;
                    _snapSettle = 40;
                    _fireFrom = shoot.ShooterUnit; _fireAt = target;
                    break;
                case Battle.MoveDecision move:
                {
                    // Two turn points, then the photo shows the bent path.
                    var mine = battle.SelectedUnit?.Models.Where(m => m.RemainingWounds > 0).ToList();
                    if (mine is { Count: > 0 })
                    {
                        float cx = mine.Average(m => m.Position.x), cz = mine.Average(m => m.Position.z);
                        float way = cz < battle.Table.D / 2 ? 1 : -1;
                        foreach (var (dx, dz) in new[] { (0f, 3f * way), (3f, 5f * way) })
                        {
                            move.TableClick(battle, battle.Table.Camera.UnprojectPosition(battle.Table.World(cx + dx, cz + dz, 0)));
                            GD.Print($"ScriptedRun: move click ({cx + dx:0.0},{cz + dz:0.0}) -> {move.ConfirmText ?? "-"} | {move.Hint}");
                        }
                    }
                    break;
                }
                case Battle.MissionCardPickMany many when many.SnapName != "MissionCardDeck":
                    many.PickForPhoto();
                    break;
                case Battle.MissionCardPickOne one:
                    one.PickForPhoto();
                    break;
                case Battle.ConcedeDecision concede:
                    concede.Rows[1].Pick?.Invoke();
                    break;
            }
            return;
        }
        if (_snapSettle == 4 && _fireFrom != null && _fireAt != null)
        {
            // A volley just before the photo, so the shot effect shows.
            battle.Table.Shots(_fireFrom.Models.Where(m => m.RemainingWounds > 0).Select(m => m.Position).ToList(),
                _fireAt.Models.Where(m => m.RemainingWounds > 0).Select(m => m.Position).ToList(), 1, false, life: 20f);
            _fireFrom = _fireAt = null;
            GD.Print($"ScriptedRun: shots fired, table children {battle.Table.GetChildCount()}");
        }
        if (--_snapSettle > 0) return;
        string dir = System.IO.Path.GetDirectoryName(_shot ?? "/tmp/x.png") ?? "/tmp";
        string file = System.IO.Path.Combine(dir, kind + ".png");
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print($"ScriptedRun: snapped {kind} - {file}");
        Battle.BattleScreen.Hold.Remove(kind);
        foreach (var dialog in AllOf<ConfirmationDialog>(GetTree().Root).ToList()) dialog.QueueFree();
        battle.TestHover = null;
        _snapSettle = -1;
    }

    private int _waitedFor;

    private void TableMouse(string step)
    {
        if (Find<Battle.BattleScreen>(GetTree().Root) is not { } battle) { GD.PrintErr("ScriptedRun: no battle on screen."); return; }
        // "clickdrag:x;z|x2;z2" (table inches): press at the first point, drag to the second, release - the hold-and-turn.
        if (step.StartsWith("clickdrag:") && step[10..].Split('|') is [var a, var b])
        {
            Vector2 Screen(string xz)
            {
                string[] p = xz.Split(';');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return battle.Table.Camera.UnprojectPosition(battle.Table.World(float.Parse(p[0], ci), float.Parse(p[1], ci)));
            }
            Vector2 from = Screen(a), to = Screen(b);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
            for (int i = 1; i <= 4; i++)
            {
                Vector2 step2 = from.Lerp(to, i / 4f);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = step2, GlobalPosition = step2, ButtonMask = MouseButtonMask.Left });
            }
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
            GD.Print($"ScriptedRun: {step}");
            return;
        }
        var mine = battle.Session.Table.Units.Objects.Where(u => battle.Session.IsMine(u.PlayerID) && battle.Table.UnitCentre(u) != null).ToList();
        FDG.IUnit? Pick() => battle.SelectedUnit ?? mine.FirstOrDefault();
        Vector3? world = null;
        if (step == "clickmine") world = mine.Select(u => battle.Table.UnitCentre(u)).FirstOrDefault(c => c != null);
        else if (step == "clickenemy" && Pick() is { } me && battle.Table.UnitCentre(me) is { } from)
            world = battle.Session.Table.Units.Objects.Where(u => !battle.Session.IsMine(u.PlayerID))
                .Select(u => battle.Table.UnitCentre(u)).Where(c => c != null).OrderBy(c => c!.Value.DistanceTo(from)).FirstOrDefault();
        else if (step.StartsWith("clickat:") || step.StartsWith("hoverat:"))
        {
            string[] xz = step[8..].Split(';');
            world = battle.Table.World(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (step.Contains(':') && Pick() is { } sel && battle.Table.UnitCentre(sel) is { } c)
        {
            string[] xy = step[(step.IndexOf(':') + 1)..].Split(';');
            world = c + new Vector3(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), 0, -float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        if (world is not { } w) { GD.PrintErr($"ScriptedRun: nothing to {step}."); return; }
        Vector2 at = battle.Table.Camera.UnprojectPosition(w);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        if (step.StartsWith("hover")) return;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        GD.Print($"ScriptedRun: {step} at {at}");
    }

    private static IEnumerable<T> AllOf<T>(Node node) where T : Node
    {
        if (node is T t) yield return t;
        foreach (Node child in node.GetChildren())
            foreach (T x in AllOf<T>(child)) yield return x;
    }

    private static void CallVm(object vm, string call)
    {
        string method = call.Split('=')[0];
        string[] args = call.Contains('=') ? call[(call.IndexOf('=') + 1)..].Split(';') : Array.Empty<string>();
        var m = vm.GetType().GetMethods().FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        if (m == null) { GD.PrintErr($"ScriptedRun: no lobby setter {method}/{args.Length}"); return; }
        var ps = m.GetParameters();
        object?[] values = new object?[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            Type t = Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType;
            values[i] = t.IsEnum ? Enum.Parse(t, args[i]) : Convert.ChangeType(args[i], t, System.Globalization.CultureInfo.InvariantCulture);
        }
        m.Invoke(vm, values);
        GD.Print($"ScriptedRun: {call}");
    }

    private static T? Find<T>(Node node) where T : Node
    {
        if (node is T t) return t;
        foreach (Node child in node.GetChildren())
            if (Find<T>(child) is { } f) return f;
        return null;
    }

    private static bool OpenTab(Node node, string name)
    {
        if (node is TabContainer tabs)
            for (int i = 0; i < tabs.GetTabCount(); i++)
                if (tabs.GetTabControl(i).Name == name) { tabs.CurrentTab = i; return true; }
        foreach (Node child in node.GetChildren())
            if (OpenTab(child, name)) return true;
        return false;
    }

    private static BaseButton? FindButton(Node node, string name)
    {
        if (node is BaseButton b && b.Name == name && b.IsVisibleInTree()) return b;
        foreach (Node child in node.GetChildren())
            if (FindButton(child, name) is { } found) return found;
        return null;
    }
}
