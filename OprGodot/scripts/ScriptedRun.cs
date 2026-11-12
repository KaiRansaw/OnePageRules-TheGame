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
                var seat = lobby.Session.Vm.PlayerInfos.First().PlayerID;
                if (FdgRaylib.Lobby.LobbySession.LoadArmyFile(name[5..]) is { } army) lobby.Session.Vm.UpdateArmyListFile(seat, army);
                else GD.PrintErr($"ScriptedRun: no army at {name[5..]}");
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

    private void SnapAll()
    {
        var battle = Find<Battle.BattleScreen>(GetTree().Root);
        bool more = Battle.BattleScreen.Hold.Count > 0 && battle?.Session.EndedText == null && ++_snapTries < 20000;
        if (!more) { GD.Print($"ScriptedRun: snapall done, not seen: {string.Join(",", Battle.BattleScreen.Hold)}"); return; }
        _presses = new Queue<string>(_presses.Prepend("snapall"));
        _wait = 3;
        if (_examplesPending) Examples();
        string? kind = battle?.Decisions.Current?.GetType().Name;
        if (kind == null || !Battle.BattleScreen.Hold.Contains(kind)) { _snapSettle = -1; return; }
        // Only photograph the action menu when it has extra actions beside the four (that is what is being reviewed).
        if (battle!.Decisions.Current is Battle.ActionDecision { Rows: null } plain) { plain.AutoPlay(battle); _snapSettle = -1; return; }
        if (_snapSettle < 0) { _snapSettle = 25; return; }
        if (--_snapSettle > 0) return;
        string dir = System.IO.Path.GetDirectoryName(_shot ?? "/tmp/x.png") ?? "/tmp";
        string file = System.IO.Path.Combine(dir, kind + ".png");
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print($"ScriptedRun: snapped {kind} - {file}");
        Battle.BattleScreen.Hold.Remove(kind);
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
