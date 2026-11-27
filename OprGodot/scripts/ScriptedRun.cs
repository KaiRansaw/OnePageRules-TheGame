using FDG;
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
    private Vector2 _mouse;
    private Queue<string> _presses = new();
    private string? _shot;
    /// <summary>Where "audit:" steps append their findings (--audit=file); the console only when not given.</summary>
    private string? _audit;
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
            else if (arg.StartsWith("--audit=")) run._audit = arg["--audit=".Length..];
            else if (arg.StartsWith("--size=") && arg["--size=".Length..].Split('x') is [var w, var h]
                     && int.TryParse(w, out int width) && int.TryParse(h, out int height))
                DisplayServer.WindowSetSize(new Vector2I(width, height));
        }
        return run._presses.Count > 0 || run._shot != null;
    }

    /// <summary>While this says so, the next step waits (a slow job such as an STL import).</summary>
    private Func<bool>? _holdWhile;

    // "perf:N" (performance sweep, 2026-11-15): samples the next N frames' script time, draw calls, objects drawn,
    // node count and memory, then prints one summary line ("ScriptedRun: perf ...") and carries on.
    private int _perfLeft;
    private readonly List<double> _perfProcess = new();
    private double _perfDraw, _perfObjects, _perfNodes, _perfMem, _perfFrames, _perfWall;

    private void SamplePerf(double delta)
    {
        _perfProcess.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
        _perfDraw = Math.Max(_perfDraw, Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        _perfObjects = Math.Max(_perfObjects, Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
        _perfNodes = Math.Max(_perfNodes, Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        _perfMem = Math.Max(_perfMem, Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0);
        _perfFrames++; _perfWall += delta;
        if (--_perfLeft > 0) return;
        _perfProcess.Sort();
        double avg = _perfProcess.Average(), p95 = _perfProcess[(int)(_perfProcess.Count * 0.95)], max = _perfProcess[^1];
        long managed = GC.GetTotalMemory(false) / 1048576;
        GD.Print($"ScriptedRun: perf frames={_perfFrames} process_ms avg={avg:0.00} p95={p95:0.00} max={max:0.00} drawcalls_max={_perfDraw} "
            + $"objects_max={_perfObjects} nodes_max={_perfNodes} godot_mem_mb={_perfMem:0} managed_mb={managed} wall_s={_perfWall:0.0}");
        GD.Print($"ScriptedRun: perf ms/frame {PerfProbe.TakeReport(_perfFrames)}");
        PerfProbe.Enabled = false;
        _perfProcess.Clear(); _perfDraw = _perfObjects = _perfNodes = _perfMem = _perfFrames = _perfWall = 0;
    }

    private async void TrimShot(string name)
    {
        string[] parts = name[9..].Split('|');
        Battle.TerrainBaseTrim.Verbose = true;
        var studio = new Lab.PortraitShot.Studio(512);
        AddChild(studio);
        for (int f = 0; f < 2; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        bool shown = studio.Show(parts[0], out string? why, trimTerrainBase: parts.Length < 3);
        studio.Aim(new Lab.PortraitShot.Angle("T", "Terrain", 30, 32, 1f, 1f));
        for (int f = 0; f < 4; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        studio.Grab(512).SavePng(parts[1]);
        GD.Print($"ScriptedRun: trimshot {(shown ? "shot" : "failed: " + why)} -> {parts[1]}");
    }

    public override void _Process(double delta)
    {
        if (_perfLeft > 0) { SamplePerf(delta); return; }
        if (--_wait > 0) return;
        _wait = 15;
        if (_holdWhile?.Invoke() == true) return;
        _holdWhile = null;

        if (_presses.TryDequeue(out string? name))
        {
            // "audit:<tag>" checks every piece of text on screen for not fitting (LayoutAudit) and writes what it finds,
            // each line headed by the tag; "auditsnap:<tag>" also saves a picture (<shot dir>/<tag>.png).
            if (name.StartsWith("audit:") || name.StartsWith("auditsnap:"))
            {
                string tag = name[(name.IndexOf(':') + 1)..];
                var vp = GetViewport().GetVisibleRect();
                var found = LayoutAudit.Run(GetTree().Root, vp);
                var lines = found.Select(f => $"{tag}\t{f.Kind}\t{f.Path}\t{f.Text}\t{f.Detail}").ToList();
                lines.Insert(0, $"{tag}\t#\t{found.Count} finding(s), top screen {App.I.TopScreen?.Name}, window {vp.Size.X}x{vp.Size.Y}");
                if (_audit != null) System.IO.File.AppendAllLines(_audit, lines);
                foreach (string l in lines) GD.Print("audit " + l);
                if (name.StartsWith("auditsnap:"))
                    GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_shot ?? "/tmp/x.png") ?? "/tmp", tag + ".png"));
                return;
            }
            // "eachtab:<tag>" audits (and photographs) every tab of every TabContainer on screen, one after another
            // ("tabat:<n>|<i>" opens tab i of the nth visible TabContainer).
            if (name.StartsWith("eachtab:"))
            {
                var steps = new List<string>();
                var all = AllOf<TabContainer>(GetTree().Root).Where(t => t.IsVisibleInTree()).ToList();
                for (int n = 0; n < all.Count; n++)
                    for (int i = 0; i < all[n].GetTabCount(); i++)
                        steps.AddRange(new[] { $"tabat:{n}|{i}", $"auditsnap:{name[8..]}-{n}-{Clean(all[n].GetTabTitle(i))}",
                            $"hoveraudit:{name[8..]}-{n}-{Clean(all[n].GetTabTitle(i))}" });
                _presses = new Queue<string>(steps.Concat(_presses));
                return;
            }
            if (name.StartsWith("tabat:") && name[6..].Split('|') is [var tn, var ti])
            {
                var all = AllOf<TabContainer>(GetTree().Root).Where(t => t.IsVisibleInTree()).ToList();
                if (int.Parse(tn) < all.Count) all[int.Parse(tn)].CurrentTab = int.Parse(ti);
                _wait = 20;
                return;
            }
            // "hoveraudit:<tag>" lists every control a player can use (buttons, tick boxes, drop-downs, sliders, boxes to
            // type in) that has no hover text, on itself or the row holding it (Kai's rule: every setting and option has one).
            if (name.StartsWith("hoveraudit:"))
            {
                string tag = name[11..];
                var lines = LayoutAudit.NoHover(GetTree().Root, GetViewport().GetVisibleRect())
                    .Select(f => $"{tag}\t{f.Kind}\t{f.Path}\t{f.Text}\t{f.Detail}").ToList();
                lines.Insert(0, $"{tag}\t#\t{lines.Count} without hover text, top screen {App.I.TopScreen?.Name}");
                if (_audit != null) System.IO.File.AppendAllLines(_audit, lines);
                foreach (string l in lines) GD.Print("audit " + l);
                return;
            }
            // "clickwalk:<tag>" presses every button on the screen one at a time and reports the ones that change nothing
            // (no new screen, no popup, nothing on screen different). Whatever a press opens is closed again before the
            // next. Buttons that would leave the run (Quit, Launch, Update, downloads, deletes) are skipped.
            if (name.StartsWith("clickwalk:"))
            {
                string tag = name[10..];
                var buttons = AllOf<BaseButton>(App.I.TopScreen ?? (Node)GetTree().Root)
                    .Where(b => b.IsVisibleInTree() && !b.Disabled && b.GetGlobalRect().Intersects(GetViewport().GetVisibleRect()))
                    .Where(b => !LayoutAudit.Risky(b)).ToList();
                _walkButtons = buttons.Select(b => b.GetPath()).ToList();
                _walkTag = tag;
                _walkHome = App.I.TopScreen;
                var steps = _walkButtons.Select((_, i) => $"clickone:{i}").ToList();
                GD.Print($"ScriptedRun: clickwalk {tag} - {steps.Count} buttons");
                _presses = new Queue<string>(steps.Concat(_presses));
                return;
            }
            if (name.StartsWith("clickone:") && int.TryParse(name[9..], out int clickIndex))
            {
                ClickOne(clickIndex);
                return;
            }
            if (name.StartsWith("clickcheck:") && int.TryParse(name[11..], out int cc))
            {
                ClickCheck(cc);
                return;
            }
            // "skin:<id>" picks a UI theme ("classic", "hologram", "minimal") for this run, as Settings does.
            if (name.StartsWith("skin:")) { Hud.Skin.Set(name[5..]); _wait = 10; return; }
            // "open:<Scenes field>" opens a screen that needs nothing set first (Settings, Credits, LoadGame...).
            if (name.StartsWith("open:") && typeof(Scenes).GetField(name[5..])?.GetValue(null) is string scenePath)
            { App.I.Open<Control>(scenePath); _wait = 30; return; }
            // "back" is the screen's Back (Esc).
            if (name == "back") { App.I.Back(); _wait = 20; return; }
            // "sizes:CheckBox" prints every visible control of that class: size, smallest size, text (layout debugging).
            if (name.StartsWith("sizes:"))
            {
                foreach (var ctl in AllOf<Control>(GetTree().Root).Where(x => x.GetClass() == name[6..] && x.IsVisibleInTree()).Take(12))
                    GD.Print($"ScriptedRun: size {ctl.Name} {ctl.Size} min {ctl.GetCombinedMinimumSize()} '{(ctl as Button)?.Text}' clip {(ctl as Button)?.ClipText} overrun {(ctl as Button)?.TextOverrunBehavior}");
                return;
            }
            // "dropdown:Name" opens the drop-down list of the OptionButton called Name (see-through / cut-off checks).
            if (name.StartsWith("dropdown:") && GetTree().Root.FindChild(name[9..], true, false) is OptionButton dd) { dd.ShowPopup(); _wait = 20; return; }
            // "kbtype:<text>" types into the open on-screen keyboard and presses Done.
            if (name.StartsWith("kbtype:") && Pads.OnScreenKeyboard.Open is { } kb) { kb.DriveType(name[7..]); _wait = 10; return; }
            // "home" closes every screen down to the main menu.
            if (name == "home") { App.I.ToMainMenu(); _wait = 20; return; }
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
            // "key:Down" presses and releases that key (keyboard focus checks); "hover:Name" acts as if the mouse moved onto
            // the button called Name (the menu feel - UiJuice).
            // "press:Name" left-clicks the button called Name with the real pointer, the way a player does: a toggle button
            // fires Pressed as well as Toggled, and a button lying under another control doesn't get the click.
            if (name.StartsWith("press:"))
            {
                if (FindButton(GetTree().Root, name[6..]) is not { } target) { GD.PrintErr($"ScriptedRun: no button named '{name[6..]}' on screen."); return; }
                Vector2 at = target.GetGlobalRect().GetCenter();
                _mouse = at;
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
                return;
            }
            // "ctrlkey:Z" presses the key with Ctrl held (undo, copy, paste, save...).
            if (name.StartsWith("ctrlkey:") && Enum.TryParse(name[8..], out Key withCtrl))
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = withCtrl, PhysicalKeycode = withCtrl, Pressed = true, CtrlPressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = withCtrl, PhysicalKeycode = withCtrl, Pressed = false, CtrlPressed = true });
                return;
            }
            // Controller (2026-11-24): "pad:A" presses a pad button, "pad:A+" holds it, "pad:A-" lets go; "padptr:x;y" puts
            // the pad pointer there; "padmode:pointer|menus" swaps modes.
            if (name.StartsWith("pad:") && Pads.PadDriver.I is { } padDrv)
            {
                // "pad:A@1" = the same from pad 1 (two-player tests with "pads:0;1").
                string spec = name[4..];
                int dev = 0;
                if (spec.Split('@') is [var bpart, var dpart] && int.TryParse(dpart, out int dv)) { spec = bpart; dev = dv; }
                string pb = spec.TrimEnd('+', '-');
                bool? hold = spec.EndsWith('+') ? true : spec.EndsWith('-') ? false : null;
                if (Enum.TryParse(pb, true, out Pads.PadButton padButton)) padDrv.TestButton(padButton, hold, dev);
                _wait = 12;
                return;
            }
            if (name.StartsWith("padptr:") && Pads.PadDriver.I is { } padDrv2 && name[7..].Split(';') is [var px, var py]
                && float.TryParse(px, System.Globalization.CultureInfo.InvariantCulture, out float pxv) && float.TryParse(py, System.Globalization.CultureInfo.InvariantCulture, out float pyv))
            {
                padDrv2.TestPointer(new Vector2(pxv, pyv));
                _wait = 12;
                return;
            }
            if (name.StartsWith("pads:")) { Pads.PadDriver.TestPads = name[5..].Split(';').Select(int.Parse).ToList(); return; }
            // "say" prints what the battle is asking now (the open decision and its prompt) and which screen is on top.
            if (name == "say")
            {
                var sb = Find<Battle.BattleScreen>(GetTree().Root);
                GD.Print($"ScriptedRun: say - top {App.I.TopScreen?.GetType().Name}, decision {sb?.Decisions.Current?.GetType().Name ?? "none"}: {sb?.Decisions.Current?.Prompt ?? ""} (pickable {sb?.Decisions.LastPickable.Count})");
                return;
            }
            // "waitnode:Name" waits (up to ~3000 frames) until a visible control with that name is on screen.
            if (name.StartsWith("waitnode:"))
            {
                bool seen = false;
                void Look(Node n) { foreach (Node c in n.GetChildren()) { if (seen) return; if (c is Control k && k.Name == name[9..] && k.IsVisibleInTree()) seen = true; else Look(c); } }
                Look(GetTree().Root);
                if (!seen && ++_waitedFor < 3000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} {(seen ? "there" : "timed out")}");
                _wait = 10;
                return;
            }
            // "focus:Name" puts the controller highlight on that control (the first visible one with the name).
            if (name.StartsWith("focus:"))
            {
                Control? f = null;
                string want = name[6..];
                int nthF = 1;
                if (want.LastIndexOf('#') is int hf and > 0 && int.TryParse(want[(hf + 1)..], out int kf)) { nthF = kf; want = want[..hf]; }
                int seenF = 0;
                void Seek(Node n) { foreach (Node c in n.GetChildren()) { if (f != null) return; if (c is Control k && k.Name == want && k.IsVisibleInTree() && ++seenF == nthF) f = k; else Seek(c); } }
                Seek(GetTree().Root);
                if (f != null) { f.FocusMode = Control.FocusModeEnum.All; f.GrabFocus(); }
                GD.Print($"ScriptedRun: focus {name[6..]} -> {(f == null ? "not found" : "ok")}");
                _wait = 8;
                return;
            }
            if (name.StartsWith("padmode:") && Pads.PadDriver.I is { } padDrv3) { padDrv3.SetPointerMode(name[8..] == "pointer"); _wait = 12; return; }
            if (name.StartsWith("key:") && Enum.TryParse(name[4..], out Key key))
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                return;
            }
            // "mouseat:Name" moves the real pointer onto the control called Name (tooltips, hover panels); "rclick" right-clicks there.
            if (name.StartsWith("mouseat:") && GetTree().Root.FindChild(name[8..], true, false) is Control under)
            {
                Vector2 at = under.GetGlobalRect().GetCenter();
                _mouse = at;
                GetViewport().WarpMouse(at);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                return;
            }
            // "lclick" left-clicks where the pointer is (after "mouseat:Name": a real click into a text box or onto a row).
            if (name == "lclick")
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = _mouse, GlobalPosition = _mouse, ButtonMask = MouseButtonMask.Left });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = _mouse, GlobalPosition = _mouse });
                return;
            }
            if (name == "rclick")
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = _mouse, GlobalPosition = _mouse });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = _mouse, GlobalPosition = _mouse });
                return;
            }
            if (name.StartsWith("hover:") && GetTree().Root.FindChild(name[6..], true, false) is Control hovered)
            { hovered.EmitSignal(Control.SignalName.MouseEntered); return; }
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
            // "allbots" hands every human seat in the lobby to a bot (bot vs bot, to watch).
            if (name == "allbots" && Find<LobbyScreen>(GetTree().Root) is { } botLobby)
            {
                foreach (var p in botLobby.Session.Vm.PlayerInfos.ToList())
                    if (p.PlayerType == FDG.Players.EPlayerType.Local) botLobby.Session.ToggleBot(p);
                GD.Print("ScriptedRun: every seat is a bot");
                return;
            }
            // "allplayers" hands every bot seat to a person at this computer (hot-seat: two players, one PC).
            if (name == "allplayers" && Find<LobbyScreen>(GetTree().Root) is { } seatLobby)
            {
                foreach (var p in seatLobby.Session.Vm.PlayerInfos.ToList())
                    if (p.PlayerType == FDG.Players.EPlayerType.AI) seatLobby.Session.ToggleBot(p);
                GD.Print("ScriptedRun: every seat is a local player");
                return;
            }
            // "army2:<file>" gives the SECOND seat that army (a bot opponent for a scripted game).
            if (name.StartsWith("army2:") && Find<LobbyScreen>(GetTree().Root) is { } lobby2 && lobby2.Session.Vm.PlayerInfos.Count > 1)
            {
                if (FdgRaylib.Lobby.LobbySession.LoadArmyFile(name[6..]) is { } army2) lobby2.Session.Vm.UpdateArmyListFile(lobby2.Session.Vm.PlayerInfos[1].PlayerID, army2);
                else GD.PrintErr($"ScriptedRun: no army at {name[6..]}");
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
            // "map:<name>" picks the saved map with that name in the lobby (as the Map drop-down does).
            if (name.StartsWith("map:") && Find<LobbyScreen>(GetTree().Root) is { } mapLobby)
            {
                string? path = FdgRaylib.SaveLoad.MapPaths.List().FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name[4..]);
                if (path == null) GD.PrintErr($"ScriptedRun: no map {name[4..]}"); else mapLobby.Session.SetMap(path);
                return;
            }
            // "wheel:<seat>" opens that seat's colour wheel (the "Choose colour..." item); "wheelset:RRGGBB" dials a colour into it.
            if (name.StartsWith("wheel:") && int.TryParse(name[6..], out int wheelSeat) && Find<LobbyScreen>(GetTree().Root) is { } wheelLobby) { wheelLobby.OpenColourWheelFor(wheelSeat); _wait = 10; return; }
            if (name.StartsWith("wheelset:") && Find<ColorPicker>(GetTree().Root) is { } wheel) { wheel.Color = Color.FromHtml(name[9..]); wheel.EmitSignal(ColorPicker.SignalName.ColorChanged, wheel.Color); return; }
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
            // "mapdo:<garrison|claim|attack|rest>" gives that order on the open campaign map and ends the turn (#467).
            if (name.StartsWith("mapdo:") && Find<Campaign.MapCampaignScreen>(GetTree().Root) is { } mcs)
            {
                mcs.DemoTurn(name[6..]);
                _wait = 20;
                return;
            }
            // "mapdemo:<rivals>" starts a Grimdark Future map campaign with armies built for both sides, and opens its map
            // (#467 - screenshots of the map screen without clicking through the wizard).
            if (name.StartsWith("mapdemo:") && int.TryParse(name[8..], out int demoRivals))
            {
                var rng = new System.Random(7);
                var taken = new List<string>();
                var mine = FdgRaylib.Quest.MapCampaignSession.BuildRival(FDG.EGameSystem.GrimdarkFuture, rng, taken)!.Value;
                taken.Add("Iron Reavers");
                var rivals = new List<(string, FDG.ArmyBuilding.BuiltArmyFile)>();
                for (int i = 0; i < demoRivals; i++)
                {
                    var r = FdgRaylib.Quest.MapCampaignSession.BuildRival(FDG.EGameSystem.GrimdarkFuture, rng, taken)!.Value;
                    taken.Add(r.Name);
                    rivals.Add((r.Name, r.Army));
                }
                mine.Army.StrikeTeam = null;
                var started = FdgRaylib.Quest.MapCampaignSession.Start(FDG.EGameSystem.GrimdarkFuture, "Iron Reavers " + rng.Next(1000), mine.Army, rivals,
                    FDG.Campaign.Map.EMapSize.Medium, 10, false, 11, out string? err);
                GD.Print("mapdemo: " + (started?.PlayerPath ?? err));
                if (started != null)
                {
                    App.I.Open<Campaign.ArmyCampaignsScreen>(Scenes.ArmyCampaigns, x => x.System = FDG.EGameSystem.GrimdarkFuture);
                    App.I.Open<Campaign.MapCampaignScreen>(Scenes.MapCampaign, x => { x.CampaignPath = started.PlayerPath; });
                }
                _wait = 30;
                return;
            }
            // "sharecheck:<army>|<unit>|<out.png>" packs that unit's picture as it is sent to other players, unpacks it, and
            // saves what the other player would see (Kai, 2026-11-14 - shared pictures).
            if (name.StartsWith("sharecheck:") && name[11..].Split('|') is [var sa, var su, var sout])
            {
                var army = new FDG.SaveLoad.ArmyListFile { Faction = sa, Units = new() { new FDG.SaveLoad.UnitFileEntry { Name = su, ModelCount = 1 } } };
                var sent = Hud.SharedLooks.Build(army);
                foreach (var kv in sent.Pictures)
                {
                    var img = new Image();
                    byte[] bytes = System.Convert.FromBase64String(kv.Value);
                    var ok = img.LoadWebpFromBuffer(bytes);
                    GD.Print($"sharecheck {kv.Key}: {bytes.Length} bytes, {img.GetWidth()}x{img.GetHeight()}, load {ok}, made {sent.ModelMade.Contains(kv.Key)}");
                    img.SavePng(sout);
                }
                foreach (var kv in sent.Models) GD.Print($"sharecheck model {kv.Key} -> {kv.Value}");
                if (sent.Pictures.Count == 0) GD.Print("sharecheck: no picture found");
                _wait = 2;
                return;
            }
            // "lab:<file>" opens that file in the Asset Lab; "labview:0-3" picks its camera (close / play / table / top);
            // "labcopies:0-4" its copies (1 / 5 / 10 / 20 / 100); "labset:Height=40" sets one of its boxes by node name.
            if (name.StartsWith("lab") && Find<Lab.AssetLabScreen>(GetTree().Root) is { } lab)
            {
                string arg = name[(name.IndexOf(':') + 1)..];
                if (name.StartsWith("lab:")) lab.Open(arg);
                // "labstl:<figure.stl>" opens Import a printable model; "labstlset:base|book|unit" (blank = leave);
                // "labstlsave" saves.
                else if (name.StartsWith("labstl:")) lab.OpenStlImport(arg);
                else if (name.StartsWith("labstlset:") && Find<Lab.StlImportPopup>(GetTree().Root) is { } imp && arg.Split('|') is { Length: 3 } st)
                    imp.Drive(int.TryParse(st[0], out int bp) ? bp : null, st[1].Length > 0 ? st[1] : null, st[2].Length > 0 ? st[2] : null);
                else if (name == "labstlsave" && Find<Lab.StlImportPopup>(GetTree().Root) is { } imp2) imp2.DriveSave();
                // "labstlwait" holds the next step until the import has read and lightened its files.
                else if (name == "labstlwait" && Find<Lab.StlImportPopup>(GetTree().Root) is { } imp3) _holdWhile = () => imp3.Busy;
                else if (name.StartsWith("labview:") && int.TryParse(arg, out int v)) lab.View(v);
                else if (name.StartsWith("labcopies:") && int.TryParse(arg, out int c)) lab.SetCopies(c);
                else if (name.StartsWith("labset:") && arg.Split('=') is [var box, var val]) lab.SetBox(box, val);
                // "labportraits:<army>" makes that army's portraits ("*" every army, "open" just opens the window).
                else if (name.StartsWith("labportraits:"))
                {
                    var pp = lab.OpenPortraits();
                    if (arg != "open") Callable.From(() => pp.StartFor(arg)).CallDeferred();
                }
                // "labframe:<army>|<unit>[|x|y|zoom]" opens Frame pictures on that unit (and frames it).
                else if (name.StartsWith("labframe:") && arg.Split('|') is { Length: >= 2 } fr)
                {
                    var frp = lab.OpenFraming();
                    FdgRaylib.Models3D.PictureFraming.Frame? frame = fr.Length >= 5
                        ? new(float.Parse(fr[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(fr[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(fr[4], System.Globalization.CultureInfo.InvariantCulture))
                        : null;
                    Callable.From(() => frp.Drive(fr[0], fr[1], frame)).CallDeferred();
                    _wait = 40;
                    return;
                }
                // "labpics:<game number>|<army>" downloads official pictures (0 = all games; "open" just opens the window).
                else if (name.StartsWith("labpics:"))
                {
                    var op = lab.OpenOfficialPictures();
                    if (arg != "open" && arg.Split('|') is [var g, var army] && int.TryParse(g, out int gi))
                        Callable.From(() => op.StartFor(gi, army)).CallDeferred();
                    _wait = arg == "open" ? 40 : 400;
                    return;
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
            // "pic:<slot key>|<file>" picks a slot in the picture manager (Asset Lab -> Your pictures) and gives it a picture;
            // "pic:<slot key>" only picks it. "viewer" closes the Lab's home page onto the 3D viewer.
            // "framegen:<file>" writes the screen-on-display's Frame boxes (.tscn text) from where its panels are now.
            if (name.StartsWith("framegen:") && App.I.TopScreen is { } fs)
            { System.IO.File.WriteAllText(name[9..], Hud.ScreenFrame.Generate(fs)); GD.Print("ScriptedRun: frame written - " + name[9..]); return; }
            if (name.StartsWith("pic:") && Find<Lab.PictureManager>(GetTree().Root) is { } pm)
            { var parts = name[4..].Split('|'); pm.Drive(parts[0], parts.Length > 1 ? parts[1] : null); _wait = 20; return; }
            if (name == "viewer" && Find<Lab.AssetLabScreen>(GetTree().Root) is { } lv) { lv.ShowViewer(""); return; }
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
            if (name.StartsWith("ws:") && Find<Lab.TerrainWorkshopScreen>(GetTree().Root) is { } ws && ws.Visible) { ws.Drive(name[3..]); _wait = 20; return; }
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
            // "waitfor:ShootDecision" holds until the battle is waiting on that kind of decision (max ~3000 frames);
            // "waitfor:Any" until it is waiting on any decision at all (which one comes first depends on the roll-offs).
            // "until:Kind" answers every other decision the test driver's way until one of that kind is open.
            if (name.StartsWith("until:") && Find<Battle.BattleScreen>(GetTree().Root) is { } ub)
            {
                ub.Decisions.Prompt();
                var cur = ub.Decisions.Current;
                if (cur != null && cur.GetType().Name == name[6..]) { _waitedFor = 0; GD.Print($"ScriptedRun: {name} reached"); return; }
                if (cur != null && !cur.Done && _waitedFor % 30 == 0) cur.AutoPlay(ub);
                if (++_waitedFor < 3000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} timed out");
                return;
            }
            // "untilround:2" answers every decision the test driver's way until that round has started.
            if (name.StartsWith("untilround:") && Find<Battle.BattleScreen>(GetTree().Root) is { } urb)
            {
                urb.Decisions.Prompt();
                if ((urb.Session.Table.Progress.RoundCount ?? 0) >= int.Parse(name[11..])) { _waitedFor = 0; GD.Print($"ScriptedRun: {name} reached"); return; }
                var cur = urb.Decisions.Current;
                if (cur != null && !cur.Done && _waitedFor % 30 == 0) cur.AutoPlay(urb);
                if (++_waitedFor < 60000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} timed out");
                return;
            }
            if (name.StartsWith("waitfor:"))
            {
                string? waitingOn = Find<Battle.BattleScreen>(GetTree().Root)?.Decisions.Current?.GetType().Name;
                bool there = waitingOn != null && (name[8..] == "Any" || waitingOn == name[8..]);
                if (!there && ++_waitedFor < 200) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                _waitedFor = 0;
                GD.Print($"ScriptedRun: {name} {(there ? "reached" : "timed out")}{(there && name[8..] == "Any" ? " - " + waitingOn : "")}");
                return;
            }
            // "trimshot:<model file>|<png>" pictures a terrain model with its scenic base cut off (TerrainBaseTrim checks).
            if (name.StartsWith("trimshot:")) { TrimShot(name); return; } // follow with wait:N
            // "terrainboard:<spec.json>|<out folder>" pictures candidate terrain models on the table (Lab.TerrainBoard), then quits.
            if (name.StartsWith("blend:"))
            {
                // Dev tool: switch the ground blend layers (s shade, d worn ground, t tufts; "blend:" = off) for screenshots.
                Battle.GroundBlend.Layers = name[6..];
                // The blend only draws on the battlefield look; switch it on for this run (not saved).
                if (Battle.GroundBlend.Layers.Length > 0)
                    FdgRaylib.Config.UserConfig.Current.View.Room = FdgRaylib.Terrain.TableRooms.Battlefield;
                GD.Print($"ScriptedRun: ground blend '{Battle.GroundBlend.Layers}'");
                _wait = 5;
                return;
            }
            if (name.StartsWith("pieceheights:"))
            {
                // Dev tool: every built-in piece's solid parts and how tall each stands (heights read from the models).
                var rows = new List<string>();
                foreach (var pc in FdgRaylib.Terrain.TerrainPieces.All)
                {
                    var hp = new FdgRaylib.Terrain.MapPiece { Kind = pc.Kind, Name = pc.Name, Look = FdgRaylib.Terrain.TerrainPieces.LookPrefix + pc.Id,
                        Rules = FDG.ETerrainType.Impassible | FDG.ETerrainType.Blocking, X = 24, Z = 24, W = pc.W, D = pc.D, Round = pc.Round };
                    var parts = Battle.TerrainSolidShape.ForPiece(hp);
                    string hs = parts == null ? "none" : string.Join(" ", parts.GroupBy(r => r.H).OrderBy(g => g.Key)
                        .Select(g => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.##}in x{1}", g.Key, g.Count())));
                    rows.Add($"{pc.Id} ({pc.Style}) parts={parts?.Count ?? 0}: {hs}");
                }
                System.IO.File.WriteAllText(name[13..], string.Join("\n", rows) + "\n");
                GD.Print($"ScriptedRun: piece heights for {rows.Count} pieces");
                _wait = 5;
                return;
            }
            if (name.StartsWith("measuremodels:"))
            {
                // Dev tool: every built-in terrain model's size at scale 1 (model units), for laying out pieces.
                var rows = new List<string>();
                var space = new Node3D();
                GetTree().Root.AddChild(space);
                foreach (var m in Battle.PieceModels.Catalogue)
                {
                    if (Battle.PieceModels.Instance(m.Path) is not { } n) { rows.Add($"{{\"p\":\"{m.Path}\",\"missing\":true}}"); continue; }
                    space.AddChild(n);
                    if (OprGodot.Lab.AssetLoader.Bounds(n, space) is { } b)
                        rows.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"p\":\"{0}\",\"g\":\"{1}\",\"x0\":{2:0.###},\"y0\":{3:0.###},\"z0\":{4:0.###},\"x1\":{5:0.###},\"y1\":{6:0.###},\"z1\":{7:0.###}}}",
                            m.Path, m.Group, b.Position.X, b.Position.Y, b.Position.Z, b.End.X, b.End.Y, b.End.Z));
                    n.Free();
                }
                space.QueueFree();
                System.IO.File.WriteAllText(name[14..], "[\n" + string.Join(",\n", rows) + "\n]\n");
                GD.Print($"ScriptedRun: measured {rows.Count} models");
                _wait = 5;
                return;
            }
            if (name.StartsWith("terrainboard:") && name[13..].Split('|') is [var tbSpec, var tbOut])
            {
                var board = new Lab.TerrainBoard(tbSpec, tbOut);
                board.Done += () => GetTree().Quit();
                AddChild(board);
                _wait = int.MaxValue;
                return;
            }
            // "option:WeatherPick|2" picks item 2 of the drop-down named WeatherPick, as if chosen by hand.
            if (name.StartsWith("option:") && name[7..].Split('|') is [var optName, var optIdx] && int.TryParse(optIdx, out int oi)
                && GetTree().Root.FindChild(optName, true, false) is OptionButton ob)
            {
                ob.Select(oi);
                ob.EmitSignal(OptionButton.SignalName.ItemSelected, (long)oi);
                _wait = 30;
                return;
            }
            // "value:Sink|1.5" sets the slider / spin box named Sink, as if dragged by hand.
            if (name.StartsWith("value:") && name[6..].Split('|') is [var rName, var rVal]
                && GetTree().Root.FindChild(rName, true, false) is Godot.Range range)
            {
                range.Value = double.Parse(rVal, System.Globalization.CultureInfo.InvariantCulture);
                _wait = 30;
                return;
            }
            // "scroll:Name|y" scrolls the ScrollContainer named Name to y; "scrollat:Name" prints where it is.
            if (name.StartsWith("scroll:") && name[7..].Split('|') is [var sName, var sY] && GetTree().Root.FindChild(sName, true, false) is ScrollContainer sc)
            {
                sc.ScrollVertical = int.Parse(sY);
                return;
            }
            if (name.StartsWith("scrollat:") && GetTree().Root.FindChild(name[9..], true, false) is ScrollContainer sat)
            {
                GD.Print($"ScriptedRun: {name[9..]} scrolled to {sat.ScrollVertical}");
                return;
            }
            // "fire:Plasma Rifle" / "strike:Great Axe": the battle effect of that weapon from the first unit of the player
            // whose turn it is to the nearest enemy, played slowly so a screenshot catches it.
            // "firebig:Battle Cannon" plays it as a big weapon (vehicle / Blast / heavy). Two target models flinch, as if wounded.
            if ((name.StartsWith("fire:") || name.StartsWith("firebig:") || name.StartsWith("strike:")) && Find<Battle.BattleScreen>(GetTree().Root) is { } fxScr)
            {
                bool melee = name.StartsWith("strike:"), bigFx = name.StartsWith("firebig:");
                string weapon = name[(name.IndexOf(':') + 1)..];
                var units = fxScr.Table.Session.Table.Units.Objects.Where(u => u.Models.Any(m => m.RemainingWounds > 0)).ToList();
                if (units.Count >= 2)
                {
                    FDG.IUnit a = units[0];
                    static float D(FDG.IUnit x, FDG.IUnit y) => MathF.Pow(x.Models[0].Position.x - y.Models[0].Position.x, 2) + MathF.Pow(x.Models[0].Position.z - y.Models[0].Position.z, 2);
                    FDG.IUnit? b = units.Where(u => !u.PlayerID.Equals(a.PlayerID)).OrderBy(u => D(a, u)).FirstOrDefault();
                    if (b != null)
                        fxScr.Table.Attack(a.Models.Where(m => m.RemainingWounds > 0).Select(m => m.Position).ToList(),
                            b.Models.Where(m => m.RemainingWounds > 0).Select(m => m.Position).ToList(), 2, melee, weapon, 3, 5, big: bigFx);
                    if (b != null) foreach (var hurt in b.Models.Where(m => m.RemainingWounds > 0).Take(2)) fxScr.Table.Flinch(hurt.ID);
                    GD.Print($"ScriptedRun: {name} {a.Name} -> {b?.Name}");
                }
                _wait = 12;
                return;
            }
            // "batcam:30|200|20" puts the battle camera at 30 degrees, 200" back, turned 20 degrees.
            if (name.StartsWith("batcam:") && name[7..].Split('|') is [var bcp, var bcd, var bcy] && Find<Battle.BattleScreen>(GetTree().Root) is { } batScr)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                batScr.SetView(float.Parse(bcp, ci), float.Parse(bcd, ci), float.Parse(bcy, ci));
                _wait = 30;
                return;
            }
            // "mapcam:12|120" tilts the Map Maker's camera to 12 degrees, 120" back (horizon checks).
            if (name.StartsWith("mapcam:") && name[7..].Split('|') is [var mp, var md] && Find<Maps.MapEditorScreen>(GetTree().Root) is { } mapEd)
            {
                mapEd.SetView(float.Parse(mp, System.Globalization.CultureInfo.InvariantCulture), float.Parse(md, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            // "selectshooter" selects the first unit on the table with a ranged weapon (sight view checks).
            if (name == "selectshooter")
            {
                var b = Find<Battle.BattleScreen>(GetTree().Root);
                var unit = b?.Session.Table.Units.Objects.FirstOrDefault(u => u.GetIsOnBattlefield()
                    && u.Models.Any(m => m.GetIsAlive() && m.Weapons.Any(w => w.RangeInches > 0)));
                if (unit != null) b!.SelectUnit(unit);
                GD.Print($"ScriptedRun: selectshooter {(unit != null ? unit.Name : "none")}");
                return;
            }
            // "audiocheck" prints what the game's audio is doing (music, ambience beds, the cues played so far).
            if (name == "audiocheck") { GD.Print("ScriptedRun: audio " + (Hud.GameAudio.I?.Report() ?? "no GameAudio")); return; }
            // "markerdemo" puts one of every optional-rules marker on the battle table (Random Events markers, living
            // spells, army terrain, forward positions) straight into the game's record - a picture check of how they
            // read, not a rules test. Waits until the game has its progress record.
            if (name == "markerdemo")
            {
                var mb = Find<Battle.BattleScreen>(GetTree().Root);
                if (mb == null || !MarkerDemo(mb))
                {
                    if (++_waitedFor < 20000) { _presses = new Queue<string>(_presses.Prepend(name)); return; }
                    GD.Print("ScriptedRun: markerdemo timed out");
                }
                _waitedFor = 0;
                _wait = 20;
                return;
            }
            // "selectunit:Hive Guardians" selects the first unit with that name, so its card shows (screenshot checks).
            if (name.StartsWith("selectunit:"))
            {
                var b = Find<Battle.BattleScreen>(GetTree().Root);
                var unit = b?.Session.Table.Units.Objects.FirstOrDefault(u => u.Name.Contains(name[11..], StringComparison.OrdinalIgnoreCase));
                if (unit != null) b!.SelectUnit(unit);
                GD.Print($"ScriptedRun: {name} {(unit != null ? "selected" : "not found")}");
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
            if (name.StartsWith("perf:") && int.TryParse(name[5..], out int perfFrames)) { _perfLeft = perfFrames; PerfProbe.TakeReport(1); PerfProbe.Enabled = true; return; }
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
            // "reportcard" / "reportcardpin" shows (or pins) the battle report's card for the latest fight.
            // "ruletip:Scurry" shows that rule's hover card in the battle and prints what it reads; "camera" prints where
            // the camera is; "tiltdrag:40" drags the camera's turn button 40 pixels down the screen (tilt checks).
            if (name.StartsWith("ruletip:") && Find<Battle.BattleScreen>(GetTree().Root) is { } tipBattle)
            { GD.Print($"ScriptedRun: ruletip {name[8..]} = {tipBattle.DriveRuleTip(name[8..]) ?? "(no wording found)"}"); return; }
            if (name == "camera" && Find<Battle.BattleScreen>(GetTree().Root) is { } camBattle)
            { GD.Print($"ScriptedRun: camera {camBattle.CameraState}"); return; }
            if (name.StartsWith("tiltdrag:") && float.TryParse(name[9..], System.Globalization.CultureInfo.InvariantCulture, out float tiltBy))
            {
                Vector2 from = GetViewport().GetVisibleRect().Size / 2, to = from + new Vector2(0, tiltBy);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = from, GlobalPosition = from });
                Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Right });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = to, GlobalPosition = to });
                return;
            }
            if (name.StartsWith("reportcard") && Find<Battle.BattleScreen>(GetTree().Root) is { } rb)
            { GD.Print($"ScriptedRun: report card {(rb.DriveReportCard(name == "reportcardpin") ? "shown" : "- no fight yet")}"); return; }
            if (name.StartsWith("click") || name.StartsWith("hover")) { TableMouse(name); return; }  // also "clickdrag:x;z|x2;z2"
            // A mode button ("AgeOfFantasy_Horde") lives in its game's pop-up box on the game screen (2026-11-16): open it.
            if (FindButton(GetTree().Root, name) == null && name.Split('_') is [var sys, _] && Enum.TryParse(sys, out FDG.EGameSystem game)
                && Find<GameScreen>(GetTree().Root) is { Visible: true } gs) gs.OpenModes(game);
            // "Name#3" presses the third button with that name (cards repeat their buttons' names).
            int nth = 1;
            if (name.LastIndexOf('#') is int hash and > 0 && int.TryParse(name[(hash + 1)..], out int k)) { nth = k; name = name[..hash]; }
            var matches = new List<BaseButton>();
            CollectButtons(GetTree().Root, name, matches);
            if (matches.Count >= nth && matches[nth - 1] is { } button)
            {
                // A toggle: flip it (fires Toggled), then Pressed too - a real click fires both.
                if (button.ToggleMode) { button.ButtonPressed = !button.ButtonPressed; button.EmitSignal(BaseButton.SignalName.Pressed); }
                else button.EmitSignal(BaseButton.SignalName.Pressed);
            }
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

    /// <summary>"markerdemo": every kind of marker on the table, for screenshots (see the step).</summary>
    private static bool MarkerDemo(Battle.BattleScreen b)
    {
        var table = b.Session.Table;
        if (FDG.GameProgressUtilities.TryGetProgress(table.DataStore) is not { } p) return false;
        var seats = table.Players.Objects.Where(s => s.IsFilled).ToList();
        if (seats.Count == 0) return false;
        string id0 = seats[0].PlayerID.ID.ToString(), id1 = seats[^1].PlayerID.ID.ToString();
        string n0 = seats[0].Name, n1 = seats[^1].Name;
        float w = FDG.GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, d = FDG.GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
        p.Battlefield ??= new FDG.BattlefieldEffectsState();
        p.Battlefield.Markers.Clear();
        int[] codes = { 21, 31, 32, 33, 34, 35, 36 };
        for (int i = 0; i < codes.Length; i++)
            p.Battlefield.Markers.Add(new FDG.EventMarker { Code = codes[i], X = w * (i + 1) / (codes.Length + 1), Z = d * 0.5f });
        p.LivingSpells ??= new FDG.LivingSpellsState();
        p.LivingSpells.OnTable.Clear();
        string[] spells =
        {
            FDG.LivingSpells.BurningPyre, FDG.LivingSpells.SuffocatingCloud, FDG.LivingSpells.ShardOfMist, FDG.LivingSpells.MagicShackles,
            FDG.LivingSpells.ArcanePalisade, FDG.LivingSpells.KillingSun, FDG.LivingSpells.DivineMaelstrom, FDG.LivingSpells.SwordSwarm,
        };
        for (int i = 0; i < spells.Length; i++)
            p.LivingSpells.OnTable.Add(new FDG.LivingSpellPiece
            {
                Name = spells[i], Owner = i % 2 == 0 ? id0 : id1, OwnerName = i % 2 == 0 ? n0 : n1,
                X = w * (i + 1) / (spells.Length + 1), Z = d * 0.25f, Fx = 0, Fz = 1,
            });
        p.LivingSpells.OnTable.Add(new FDG.LivingSpellPiece
        {
            Name = FDG.LivingSpells.TwinShockSpheres, Owner = id1, OwnerName = n1, X = w * 0.2f, Z = d * 0.38f, X2 = w * 0.32f, Z2 = d * 0.38f,
        });
        p.ArmyTerrain ??= new FDG.ArmyTerrainState();
        p.ArmyTerrain.Pieces.Clear();
        string[] army =
        {
            FDG.ArmyTerrain.ArmyBanner, FDG.ArmyTerrain.BattleForge, FDG.ArmyTerrain.ProtectiveDome, FDG.ArmyTerrain.WatchTower,
            FDG.ArmyTerrain.FactionRelic, FDG.ArmyTerrain.HealingWell, FDG.ArmyTerrain.MagicArmory, FDG.ArmyTerrain.EnchantedShrine,
        };
        for (int i = 0; i < army.Length; i++)
            p.ArmyTerrain.Pieces.Add(new FDG.ArmyPiece { Name = army[i], Owner = i % 2 == 0 ? id0 : id1, X = w * (i + 1) / (army.Length + 1), Z = d * 0.75f });
        p.ArmyTerrain.Pieces.Add(new FDG.ArmyPiece { Name = FDG.ArmyTerrain.ArcanePillars, Owner = id0, X = w * 0.62f, Z = d * 0.62f, X2 = w * 0.75f, Z2 = d * 0.62f });
        p.ArmyTerrain.Pieces.Add(new FDG.ArmyPiece { Name = FDG.ArmyTerrain.TwinJumpgates, Owner = id1, X = w * 0.85f, Z = d * 0.38f, X2 = w * 0.9f, Z2 = d * 0.62f });
        p.TacticalPhase ??= new FDG.TacticalPhaseState();
        p.TacticalPhase.Markers.Clear();
        for (int i = 0; i < 3; i++)
            p.TacticalPhase.Markers.Add(new FDG.ForwardMarker { Slot = seats[0].SlotID, Number = i + 1, X = w * (0.4f + i * 0.08f), Z = d * 0.9f });
        p.Quest ??= new FDG.QuestProgress { Primary = 2, Secondary = 6 };
        p.Quest.Markers.Clear();
        p.Quest.Tokens.Clear();
        string[] quest = { "objective", "delivery", "extraction", "smoke", "fire", "phantasm", "noise" };
        for (int i = 0; i < quest.Length; i++)
            p.Quest.Markers.Add(new FDG.QuestMarker
            {
                Id = i + 1, Kind = quest[i], X = w * (i + 1) / (quest.Length + 3), Z = d * 0.1f, RoundsLeft = 3,
                Action = i == 0 ? "seize" : "", Activated = i == 0, Tokens = 2, TokensNeeded = 6,
            });
        p.Quest.Tokens.Add(new FDG.QuestToken { Id = 1, X = w * 0.8f, Z = d * 0.1f });
        p.Quest.Tokens.Add(new FDG.QuestToken { Id = 2, X = w * 0.9f, Z = d * 0.1f, IsTreasure = true, Contents = new List<string> { "Med Kit" } });
        GD.Print("ScriptedRun: markerdemo placed");
        return true;
    }

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
            // The game being played, as the engine's own request says it (HordeShop.cs) - this example used to say
            // Grimdark Future always, so a Firefight or Skirmish shop opened on the wrong game's book.
            Upgradable = units, GameSystemSlug = FDG.GameSystemInfo.Slug(battle.Session.Lobby.Vm.GameSystem),
            AllowedGameSystems = battle.Session.Lobby.Vm.AllowedGameSystems, PointsEarned = 100, PointsPerObjective = 40, PriorityBonus = 20,
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
            battle.Table.Attack(_fireFrom.Models.Where(m => m.RemainingWounds > 0).Select(m => m.Position).ToList(),
                _fireAt.Models.Where(m => m.RemainingWounds > 0).Select(m => m.Position).ToList(), 1, false, null, life: 20f);
            _fireFrom = _fireAt = null;
            GD.Print($"ScriptedRun: shots fired, table children {battle.Table.GetChildCount()}");
        }
        if (--_snapSettle > 0) return;
        string dir = System.IO.Path.GetDirectoryName(_shot ?? "/tmp/x.png") ?? "/tmp";
        string file = System.IO.Path.Combine(dir, kind + ".png");
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print($"ScriptedRun: snapped {kind} - {file}");
        AuditNow(kind, hover: true);
        Battle.BattleScreen.Hold.Remove(kind);
        foreach (var dialog in AllOf<ConfirmationDialog>(GetTree().Root).ToList()) dialog.QueueFree();
        battle.TestHover = null;
        _snapSettle = -1;
    }

    private int _waitedFor;

    private void TableMouse(string step)
    {
        if (Find<Battle.BattleScreen>(GetTree().Root) is not { } battle) { GD.PrintErr("ScriptedRun: no battle on screen."); return; }
        // "clickdragmodel:i|dx;dz": drag the i-th model being set up or moved by dx, dz inches (Kai, 2026-11-17).
        if (step.StartsWith("clickdragmodel:") && step[15..].Split('|') is [var mi, var md]
            && battle.Decisions.Current?.DraggableAt(int.Parse(mi)) is { } mp)
        {
            string[] d = md.Split(';');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            step = $"clickdrag:{mp.X.ToString(ci)};{mp.Y.ToString(ci)}|{(mp.X + float.Parse(d[0], ci)).ToString(ci)};{(mp.Y + float.Parse(d[1], ci)).ToString(ci)}";
        }
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
        // "clicktarget": the open shooting decision's first enemy in reach (whoever owns it - a hot-seat check).
        else if (step == "clicktarget" && battle.Decisions.Current is Battle.ShootDecision { AnyTarget: { } shootAt })
            world = battle.Table.UnitCentre(shootAt);
        else if (step == "clickenemy" && Pick() is { } me && battle.Table.UnitCentre(me) is { } from)
            world = battle.Session.Table.Units.Objects.Where(u => battle.Session.IsEnemy(me.PlayerID, u.PlayerID))
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


    // ---- clickwalk: does every button do something?
    private List<NodePath> _walkButtons = new();
    private string _walkTag = "";
    private Control? _walkHome;
    private string _walkBefore = "";
    private int _walkWindows;
    private int _walkOption = -1;
    private HashSet<ulong> _walkCovers = new();

    /// <summary>Visible controls covering most of the window inside the top screen (pickers and popups opened in it).</summary>
    private List<Control> Covers()
    {
        var vp = GetViewport().GetVisibleRect();
        var list = new List<Control>();
        if (App.I.TopScreen is not { } top) return list;
        foreach (Control c in AllOf<Control>(top))
            if (c != top && c.IsVisibleInTree() && c.GetGlobalRect().Size.X * c.GetGlobalRect().Size.Y > vp.Size.X * vp.Size.Y * 0.6f
                && (c is ArmyPicker || c.GetType().Name.EndsWith("Popup")))
                list.Add(c);
        return list;
    }

    /// <summary>What is on screen, as text: the top screen, open windows, and every visible control's kind, name, text,
    /// toggle state and value. Two equal signatures = the press changed nothing a player could see.</summary>
    private string Signature(Node? skip = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(App.I.TopScreen?.Name).Append('|').Append(App.I.TopScreen?.GetInstanceId()).Append('|');
        foreach (var w in AllOf<Window>(GetTree().Root).Where(w => w.Visible && w != GetTree().Root)) sb.Append("W:").Append(w.Name).Append(';');
        void Walk(Node n)
        {
            if (n == skip) return;   // the drop-down being tried: its own text changes, that proves nothing
            if (n is CanvasItem { Visible: false }) return;
            if (n is Window { Visible: false }) return;
            if (n is Control c)
            {
                sb.Append(c.GetClass()).Append(':').Append(c.Name);
                switch (c)
                {
                    case Label l: sb.Append('=').Append(l.Text); break;
                    case BaseButton b: sb.Append('=').Append((b as Button)?.Text).Append(b.ButtonPressed ? "+" : "-"); break;
                    case Godot.Range r: sb.Append('=').Append(r.Value); break;
                    case LineEdit le: sb.Append('=').Append(le.Text); break;
                    case TabContainer tc: sb.Append('=').Append(tc.CurrentTab); break;
                    case ItemList il: sb.Append('=').Append(string.Join(",", il.GetSelectedItems())); break;
                }
                sb.Append(';');
            }
            foreach (Node ch in n.GetChildren()) Walk(ch);
        }
        Walk(GetTree().Root);
        // A setting that saves something did something, even when nothing on screen changes.
        sb.Append("|config:").Append(System.Text.Json.JsonSerializer.Serialize(FdgRaylib.Config.UserConfig.Current));
        return sb.ToString();
    }

    private void ClickOne(int i)
    {
        if (i >= _walkButtons.Count || GetNodeOrNull<BaseButton>(_walkButtons[i]) is not { } b || !b.IsVisibleInTree() || b.Disabled)
        {
            // Gone or hidden by an earlier press (a tab's own buttons, a closed panel) - nothing to try.
            return;
        }
        _walkBefore = Signature(b as OptionButton);
        _walkCovers = Covers().Select(c => c.GetInstanceId()).ToHashSet();
        _walkWindows = AllOf<Window>(GetTree().Root).Count(w => w.Visible);
        // A drop-down: pick its next item (a setting that changes nothing on screen shows up as DEAD), put back after.
        if (b is OptionButton ob)
        {
            if (ob.ItemCount < 2) return;
            _walkOption = ob.Selected;
            int next = (ob.Selected + 1) % ob.ItemCount;
            ob.Select(next);
            ob.EmitSignal(OptionButton.SignalName.ItemSelected, (long)next);
        }
        else if (b.ToggleMode) { b.ButtonPressed = !b.ButtonPressed; b.EmitSignal(BaseButton.SignalName.Pressed); }
        else b.EmitSignal(BaseButton.SignalName.Pressed);
        _presses = new Queue<string>(_presses.Prepend($"clickcheck:{i}"));
        _wait = 25;
    }

    private void ClickCheck(int i)
    {
        string path = _walkButtons[i].ToString();
        var b = GetNodeOrNull<BaseButton>(_walkButtons[i]);
        string label = b is Button bt && bt.Text.Length > 0 ? bt.Text : b?.TooltipText is { Length: > 0 } tt ? "(hover) " + tt : "(no text)";
        string after = Signature(b as OptionButton);
        string verdict = after == _walkBefore ? "DEAD" : "ok";
        string line = $"{_walkTag}\tclick-{verdict}\t{path[Math.Max(0, path.Length - 120)..]}\t{label.Replace('\n', ' ')[..Math.Min(label.Length, 60)]}\t";
        if (_audit != null) System.IO.File.AppendAllLines(_audit, new[] { line });
        GD.Print("audit " + line);
        // Put things back for the next button: close popups and windows it opened, step back from a screen it opened.
        foreach (var w in AllOf<Window>(GetTree().Root).Where(w => w.Visible && w != GetTree().Root).ToList())
            if (w is Popup or AcceptDialog || w.Exclusive || w.Transient) w.Hide();
        // An overlay the press opened inside the screen (Choose Army, Choose heroes, a colour wheel): close it.
        foreach (Control cover in Covers().Where(c => !_walkCovers.Contains(c.GetInstanceId())))
        {
            if (cover is ArmyPicker picker) picker.Close();
            else cover.QueueFree();
        }
        int guard = 0;
        while (App.I.TopScreen != _walkHome && App.I.TopScreen != null && guard++ < 4) App.I.Back();
        if (b is OptionButton back && IsInstanceValid(back) && back.IsInsideTree() && _walkOption >= 0)
        {
            back.Select(_walkOption);
            back.EmitSignal(OptionButton.SignalName.ItemSelected, (long)_walkOption);
            _walkOption = -1;
        }
        if (b is { ToggleMode: true } && b.IsInsideTree() && b is not OptionButton && b.IsVisibleInTree())
        { b.ButtonPressed = !b.ButtonPressed; b.EmitSignal(BaseButton.SignalName.Pressed); }
        _wait = 10;
    }

    /// <summary>The "audit:" step's work, also run on every battle decision "snapall" photographs.</summary>
    private void AuditNow(string tag, bool hover)
    {
        var vp = GetViewport().GetVisibleRect();
        var found = LayoutAudit.Run(GetTree().Root, vp);
        if (hover) found.AddRange(LayoutAudit.NoHover(GetTree().Root, vp));
        var lines = found.Select(f => $"{tag}\t{f.Kind}\t{f.Path}\t{f.Text}\t{f.Detail}").ToList();
        lines.Insert(0, $"{tag}\t#\t{found.Count} finding(s), top screen {App.I.TopScreen?.Name}, window {vp.Size.X}x{vp.Size.Y}");
        if (_audit != null) System.IO.File.AppendAllLines(_audit, lines);
        foreach (string l in lines) GD.Print("audit " + l);
    }

    private static string Clean(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

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

    private static void CollectButtons(Node node, string name, List<BaseButton> into)
    {
        if (node is BaseButton b && b.Name == name && b.IsVisibleInTree()) into.Add(b);
        foreach (Node child in node.GetChildren()) CollectButtons(child, name, into);
    }

    private static BaseButton? FindButton(Node node, string name)
    {
        if (node is BaseButton b && b.Name == name && b.IsVisibleInTree()) return b;
        foreach (Node child in node.GetChildren())
            if (FindButton(child, name) is { } found) return found;
        return null;
    }
}
