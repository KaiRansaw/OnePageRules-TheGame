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
            // "lab:<file>" opens that file in the Asset Lab; "labview:0-3" picks its camera (close / play / table / top);
            // "labcopies:0-4" its copies (1 / 5 / 10 / 20 / 100); "labset:Height=40" sets one of its boxes by node name.
            if (name.StartsWith("lab") && Find<Lab.AssetLabScreen>(GetTree().Root) is { } lab)
            {
                string arg = name[(name.IndexOf(':') + 1)..];
                if (name.StartsWith("lab:")) lab.Open(arg);
                else if (name.StartsWith("labview:") && int.TryParse(arg, out int v)) lab.View(v);
                else if (name.StartsWith("labcopies:") && int.TryParse(arg, out int c)) lab.SetCopies(c);
                else if (name.StartsWith("labset:") && arg.Split('=') is [var box, var val]) lab.SetBox(box, val);
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
            if (name.StartsWith("click") || name.StartsWith("hover")) { TableMouse(name); return; }
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
