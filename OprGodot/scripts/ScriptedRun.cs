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
            // "trimshot:<model file>|<png>" pictures a terrain model with its scenic base cut off (TerrainBaseTrim checks).
            if (name.StartsWith("trimshot:")) { TrimShot(name); return; } // follow with wait:N
            // "option:WeatherPick|2" picks item 2 of the drop-down named WeatherPick, as if chosen by hand.
            if (name.StartsWith("option:") && name[7..].Split('|') is [var optName, var optIdx] && int.TryParse(optIdx, out int oi)
                && GetTree().Root.FindChild(optName, true, false) is OptionButton ob)
            {
                ob.Select(oi);
                ob.EmitSignal(OptionButton.SignalName.ItemSelected, (long)oi);
                _wait = 30;
                return;
            }
            // "mapcam:12|120" tilts the Map Maker's camera to 12 degrees, 120" back (horizon checks).
            if (name.StartsWith("mapcam:") && name[7..].Split('|') is [var mp, var md] && Find<Maps.MapEditorScreen>(GetTree().Root) is { } mapEd)
            {
                mapEd.SetView(float.Parse(mp, System.Globalization.CultureInfo.InvariantCulture), float.Parse(md, System.Globalization.CultureInfo.InvariantCulture));
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
                if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed; // a tick box: flip it (fires Toggled)
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
