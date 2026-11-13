using FDG;
using FdgRaylib.Game;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// A game in progress (port steps 3-4): the 3D table and the HUD Kai decided on 2026-11-10 (port plan step 1b,
/// <c>mockups/MockFinal.tscn</c>). Round box top-left with the army list under it, the selected unit's card and
/// the 2x2 actions bottom-left, the prompt line top-centre, Top view / Report / Armies / Menu top-right, the
/// Mission panel over the battle report bottom-right, enemy info as a hover tooltip that a click pins.
///
/// <para>Decisions the Godot build has a screen for go to the player; the rest are made by the official bot
/// for the player (autopilot, <see cref="HybridResolverRegistry"/>) and the prompt line says so.</para>
/// </summary>
public partial class BattleScreen : Control, IOwnBackdrop
{
    public GameSession Session { get; set; } = null!;

    private BattleTable _table = null!;
    private Control _hud = null!;

    // HUD parts refreshed each tick.
    private Label _round = null!, _roundOf = null!, _turn = null!, _turnLine = null!, _prompt = null!, _promptHint = null!;
    private HBoxContainer _objectiveCounts = null!;
    private VBoxContainer _armyRows = null!, _reportRows = null!, _missionBody = null!;
    private Label _armyHead = null!;
    private PanelContainer _unitBlock = null!;
    private VBoxContainer _unitCard = null!;
    private CollapsePanel _mission = null!;
    private Control? _gameOver;

    // Decisions waiting on the player, from the Godot resolvers (BattleDecisions).
    public BattleDecisions Decisions { get; private set; } = null!;

    // Camera: orbit around a point on the table.
    private Vector3 _focus;
    private float _yaw, _pitch = 52f, _distance = 64f;
    private bool _topView;
    private float _savedPitch = 52f;
    private Vector2? _dragRotate, _dragPan;

    // Hover / pinning enemy info.
    private IUnit? _hoverUnit;
    private double _hoverTime;
    private Control? _hoverTip;
    private readonly Dictionary<UnitID, FloatingPanel> _pinned = new();

    private int _logVersion = -1;
    private double _tick;
    private string _armySig = "", _cardSig = "";

    public override void _Ready()
    {
        MenuBackdrop.Instance?.SetActive(false);
        BattleDecisions.PortraitFaction = u => Session.SeatOf(u.PlayerID)?.Faction ?? "";
        Session.Report.BeatSeen += OnBeat;
        // The table takes the clicks the HUD doesn't: this screen is the bottom-most control under the pointer
        // wherever no panel is, so table clicks arrive in _GuiInput. (In 1.45 they were left for
        // _UnhandledInput, and the main scene's full-window control ate them first - nothing on the table
        // could be clicked.)
        MouseFilter = MouseFilterEnum.Stop;

        _table = new BattleTable { Session = Session };
        AddChild(_table);
        _focus = Vector3.Zero;
        _distance = Mathf.Max(_table.W, _table.D) * 0.95f + 10f;

        _hud = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_hud);
        Decisions = new BattleDecisions(this);
        BuildHud();
        ApplyCamera();
    }

    public override void _ExitTree()
    {
        Session.Report.BeatSeen -= OnBeat;
        MenuBackdrop.Instance?.SetActive(true);
    }

    // Attacks arrive on the engine thread; the table effect is played on the next frame.
    private readonly System.Collections.Concurrent.ConcurrentQueue<FDG.Presentation.Beats.AttackBeat> _shots = new();
    private void OnBeat(FDG.Presentation.PresentationBeat beat)
    {
        if (beat is FDG.Presentation.Beats.AttackBeat a && _shots.Count < 64) _shots.Enqueue(a);
    }

    public BattleTable Table => _table;

    /// <summary><c>--autoplay</c>: the test driver answers every decision (see <see cref="Decision.AutoPlay"/>).</summary>
    private static readonly bool AutoPlayRequested = OS.GetCmdlineUserArgs().Any(x => x.StartsWith("--autoplay"));

    /// <summary><c>--autoplay=ActivateDecision,MoveDecision</c> limits it to those kinds (a screenshot of the rest).</summary>
    private static readonly HashSet<string>? AutoPlayOnly = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--autoplay="))
        ?["--autoplay=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    /// <summary>Which kinds of decision the test driver has answered (printed once each, so a run's log shows what was exercised).</summary>
    private static readonly Dictionary<string, int> AutoPlayCounts = new();

/// <summary><c>--hold=WoundsDecision,...</c>: kinds the test driver leaves open until ScriptedRun's "snapall" has
    /// photographed one of each (the in-game UI catalogue).</summary>
    public static readonly HashSet<string> Hold = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--hold="))
        ?["--hold=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() ?? new HashSet<string>();

    private static bool AutoPlays(Decision d) => !Hold.Contains(d.SnapName) && !Hold.Contains(d.GetType().Name) && (AutoPlayOnly == null || AutoPlayOnly.Contains(d.GetType().Name));
    public Control Hud => _hud;

    // ================================================================== frame

    public override void _Process(double delta)
    {
        UpdateHover(delta);
        while (_shots.TryDequeue(out var shot)) _table.Shots(shot.From, shot.To, shot.VolleyCount, shot.IsMelee);
        if (AutoPlayRequested && Decisions.Current is { } open && AutoPlays(open))
        {
            Decisions.Prompt();
            if (!open.Done) { if (AutoPlayCounts.TryAdd(open.GetType().Name, 0)) GD.Print($"Autoplay: first {open.GetType().Name}"); open.AutoPlay(this); }
        }
        _tick += delta;
        if (_tick < 0.15) return;
        _tick = 0;
        RefreshHud();
    }

    // ================================================================== camera

    private void ApplyCamera()
    {
        float pitch = _topView ? 89.5f : _pitch;
        Vector3 offset = new Vector3(0, 0, _distance).Rotated(Vector3.Right, -Mathf.DegToRad(pitch)).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        _table.Camera.Position = _focus + offset;
        _table.Camera.LookAt(_focus, _topView ? Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(_yaw)) : Vector3.Up);
    }

    public void ToggleTopView()
    {
        _topView = !_topView;
        if (_topView) { _savedPitch = _pitch; _distance = Mathf.Max(_distance, Mathf.Max(_table.W * 0.75f, _table.D * 1.2f)); }
        else _pitch = _savedPitch;
        ApplyCamera();
    }

    public void FocusOn(Vector3 point)
    {
        _focus = new Vector3(point.X, 0, point.Z);
        ApplyCamera();
    }

    // Tab is taken before the GUI sees it: otherwise it only moves keyboard focus between HUD buttons.
    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Tab } && IsVisibleInTree())
        {
            SelectNextReady();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouse) return;
        // Wheel and clicks only count when the pointer is on the table itself, not on a panel (whose events
        // bubble up here): scrolling a list or the report must not zoom the map (Kai, 2026-11-11). Releases and
        // drags already under way always go through, so a camera drag that ends over a panel still ends.
        bool onTable = GetViewport().GuiGetHoveredControl() == this;
        bool starts = e is InputEventMouseButton { Pressed: true };
        if (starts && !onTable) return;
        if (e is InputEventMouseMotion && _dragRotate == null && _dragPan == null && !_leftHeld) return;
        TableMouse(e);
        AcceptEvent();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouse) return;
        TableKeys(e);
    }

    private void TableMouse(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(8f, _distance * 0.9f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(220f, _distance * 1.1f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _dragRotate = mb.Pressed ? mb.Position : null;
                // A right CLICK (not a drag to turn the camera): drop the placed move / shot, step back, drop the
                // picked unit (Kai, 2026-11-10) - and only then close the newest pinned enemy window.
                if (!mb.Pressed && mb.Position.DistanceTo(_rightDown) < 4 && !Decisions.RightClick() && _pinned.Count > 0) CloseNewestPin();
                if (mb.Pressed) _rightDown = mb.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragPan = mb.Pressed ? mb.Position : null; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                // Kai, 2026-11-12: click places as before; holding the button and dragging then turns what was placed
                // towards the pointer (deployment and moves).
                if (mb.Pressed) { OnLeftClick(mb.Position); _leftDown = mb.Position; _leftHeld = true; _aiming = false; }
                else { _leftHeld = false; _aiming = false; }
                break;
            case InputEventMouseMotion mm when _leftHeld && (_aiming || mm.Position.DistanceTo(_leftDown) > 8f):
                _aiming = Decisions.TableAim(mm.Position);
                if (!_aiming) _leftHeld = false; // nothing to turn: an ordinary drag
                break;
            case InputEventMouseMotion mm when _dragRotate is { } r:
                _yaw -= (mm.Position.X - r.X) * 0.3f;
                if (!_topView) _pitch = Mathf.Clamp(_pitch + (mm.Position.Y - r.Y) * 0.25f, 15f, 88f);
                _dragRotate = mm.Position;
                ApplyCamera();
                break;
            case InputEventMouseMotion mm when _dragPan is { } p:
                Pan((mm.Position - p) * -_distance * 0.0016f);
                _dragPan = mm.Position;
                break;
        }
    }

    private void TableKeys(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.T }:
                ToggleTopView(); break;
            case InputEventKey { Pressed: true, Keycode: Key.Q }:
                _yaw += 5; ApplyCamera(); break;
            case InputEventKey { Pressed: true, Keycode: Key.E }:
                _yaw -= 5; ApplyCamera(); break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }:
                OpenFullReport(); break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.L }:
                OpenArmies(); break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space or Key.Enter or Key.KpEnter }:
                if (Decisions.ConfirmPending()) GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: >= Key.Key1 and <= Key.Key9 } k:
                if (Decisions.HotKey((int)(k.Keycode - Key.Key1))) GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.M }:
                _mission.Collapsed = !_mission.Collapsed; break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.S, CtrlPressed: true }:
                SaveNow(SuggestedSaveName());
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } when _menu != null && IsInstanceValid(_menu):
                CloseMenu();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                if (_pinned.Count > 0) CloseNewestPin();
                else if (!Decisions.Cancel()) OpenMenu();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private Vector2 _rightDown;
    private Vector2 _leftDown;
    private bool _leftHeld, _aiming;

    private void Pan(Vector2 screenDelta)
    {
        Vector3 right = new Vector3(1, 0, 0).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        Vector3 fwd = new Vector3(0, 0, 1).Rotated(Vector3.Up, Mathf.DegToRad(_yaw));
        _focus += right * screenDelta.X + fwd * screenDelta.Y;
        _focus.X = Mathf.Clamp(_focus.X, -_table.W / 2 - 10, _table.W / 2 + 10);
        _focus.Z = Mathf.Clamp(_focus.Z, -_table.D / 2 - 10, _table.D / 2 + 10);
        ApplyCamera();
    }

    public override void _PhysicsProcess(double delta)
    {
        // WASD / arrows pan, at a speed that suits the zoom.
        Vector2 dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (dir != Vector2.Zero && GetViewport().GuiGetFocusOwner() is not LineEdit)
            Pan(dir * (float)delta * _distance * 0.8f);
    }

    // ================================================================== clicks and hover

    private void OnLeftClick(Vector2 at)
    {
        if (Decisions.TableClick(at)) return;
        if (_table.UnitAt(at) is not { } unit) return;
        SelectUnit(unit);
        if (!Session.IsMine(unit.PlayerID)) Pin(unit);
    }

    /// <summary>Test driver only: treat this unit as hovered (a virtual display ignores a scripted pointer).</summary>
    public IUnit? TestHover { get; set; }

    private void UpdateHover(double delta)
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        bool overHud = GetViewport().GuiGetHoveredControl() is { } c && c != this && c != _hud;
        IUnit? under = TestHover ?? (overHud ? null : _table.UnitAt(mouse));
        if (!overHud && !_aiming) Decisions.TableHover(mouse);
        _table.ShowCursor(overHud ? null : _table.TableAt(mouse));
        if (under != null && Session.IsMine(under.PlayerID)) under = null;
        _table.HoverUnit = under?.ID;
        if (!ReferenceEquals(under, _hoverUnit))
        {
            if (_hoverUnit != null && !_pinned.ContainsKey(_hoverUnit.ID)) _table.ShowEnemyNames.Remove(_hoverUnit.ID);
            _hoverUnit = under;
            _hoverTime = 0;
            _hoverTip?.QueueFree();
            _hoverTip = null;
        }
        if (_hoverUnit == null) return;
        _table.ShowEnemyNames.Add(_hoverUnit.ID);
        _hoverTime += delta;
        // Kai, 2026-11-10: the enemy's info comes up after the pointer rests on it for a third of a second.
        if (_hoverTip == null && _hoverTime >= 0.33)
        {
            Control info = UnitInfo(_hoverUnit, 300, "Left-click to pin");
            if (Decisions.Current?.HoverOdds(_hoverUnit) is { Count: > 0 } odds)
            {
                // The shooting odds on top (Kai, 2026-11-11).
                var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                stack.AddThemeConstantOverride("separation", 2);
                for (int i = 0; i < odds.Count; i++)
                {
                    var l = HudKit.Text(odds[i], i == 0 ? 12 : i == odds.Count - 1 ? 11 : 15, i == 0 ? HudKit.Gold : i == odds.Count - 1 ? HudKit.Dim : HudKit.Fg,
                        i == 0 ? HudKit.Tracked(HudKit.Bold, 2) : HudKit.Semi);
                    l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    l.CustomMinimumSize = new Vector2(300, 0);
                    stack.AddChild(l);
                }
                stack.AddChild(HudKit.Gap(0, 8));
                stack.AddChild(info);
                info = stack;
            }
            _hoverTip = HudKit.Panel(Tight(10).With(x => { x.Fill = HudKit.Glass with { A = 0.97f }; x.Border = HudKit.Enemy with { A = 0.85f }; x.Glow = HudKit.Enemy with { A = 0.25f }; }), info);
            _hoverTip.MouseFilter = MouseFilterEnum.Ignore;
            _hud.AddChild(_hoverTip);
            CallDeferred(nameof(PlaceHoverTip), mouse);
        }
    }

    private void PlaceHoverTip(Vector2 mouse)
    {
        if (_hoverTip == null) return;
        Vector2 size = _hoverTip.Size, screen = GetViewportRect().Size;
        Vector2 at = mouse + new Vector2(24, -size.Y / 2);
        if (at.X + size.X > screen.X - 10) at.X = mouse.X - size.X - 24;
        at.Y = Mathf.Clamp(at.Y, 10, screen.Y - size.Y - 10);
        _hoverTip.Position = at;
    }

    private void Pin(IUnit unit)
    {
        if (_pinned.TryGetValue(unit.ID, out FloatingPanel? open)) { open.Minimised = false; return; }
        var panel = new FloatingPanel(unit.Name, UnitInfo(unit, 290, null), HudKit.Enemy);
        panel.Closed += () => { _pinned.Remove(unit.ID); _table.ShowEnemyNames.Remove(unit.ID); };
        _hud.AddChild(panel);
        panel.Position = new Vector2(GetViewportRect().Size.X - 360, 120 + _pinned.Count * 46);
        _pinned[unit.ID] = panel;
        _table.ShowEnemyNames.Add(unit.ID);
        _hoverTip?.QueueFree();
        _hoverTip = null;
    }

    private void CloseNewestPin()
    {
        if (_pinned.Count == 0) return;
        var last = _pinned.Last();
        last.Value.QueueFree();
        _pinned.Remove(last.Key);
        _table.ShowEnemyNames.Remove(last.Key);
    }

    // ================================================================== selection

    private IUnit? _selected;
    public IUnit? SelectedUnit => _selected;

    // ================================================================== small popups

    /// <summary>A one-line note that fades after a few seconds (why a click did nothing).</summary>
    public void Toast(string text)
    {
        var l = HudKit.Text(text, 17, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center);
        var p = HudKit.Panel(Tight(10).With(x => { x.Border = HudKit.Gold; x.ContentMarginLeft = 14; x.ContentMarginRight = 14; }), l);
        p.MouseFilter = MouseFilterEnum.Ignore;
        HudKit.Place(_hud, p, LayoutPreset.CenterBottom, new Vector2(0, -150));
        var tw = p.CreateTween();
        tw.TweenInterval(2.6);
        tw.TweenProperty(p, "modulate:a", 0.0, 0.5);
        tw.TweenCallback(Callable.From(p.QueueFree));
    }

    /// <summary>Asks before something that can't be taken back.</summary>
    public void Confirm(string title, string text, string ok, Action yes, string no = "Cancel")
    {
        var d = new ConfirmationDialog { Title = title, DialogText = text, OkButtonText = ok, CancelButtonText = no };
        d.Confirmed += () => { yes(); d.QueueFree(); };
        d.Canceled += () => d.QueueFree();
        _hud.AddChild(d);
        d.PopupCentered();
    }

    /// <summary>
    /// Selects a unit: its card shows bottom-left and it is ringed on the table. The camera stays where it is
    /// (Kai, 2026-11-10: it used to jump to the unit).
    /// </summary>
    public void SelectUnit(IUnit unit)
    {
        if (_selected?.ID.Equals(unit.ID) == true) return;
        _selected = unit;
        _table.Selected = unit.ID;
        _cardSig = "";
        _armySig = "";
    }

    public void ClearSelection()
    {
        _selected = null;
        _table.Selected = null;
        _cardSig = "";
        _armySig = "";
    }

    private void SelectNextReady()
    {
        var mine = MyUnits().Where(u => !UnitActivation.HasActivated(Session.Table.Progress, u)).ToList();
        if (mine.Count == 0) return;
        int i = _selected == null ? -1 : mine.FindIndex(u => u.ID.Equals(_selected.ID));
        SelectUnit(mine[(i + 1) % mine.Count]); Decisions.UnitPicked(mine[(i + 1) % mine.Count]);
    }

    private List<IUnit> MyUnits() => Session.Table.Units.Objects.Where(u => Session.IsMine(u.PlayerID)).ToList();

    // ================================================================== HUD build

    private static ChamferBox Tight(float cut = 10) => new()
    {
        Fill = HudKit.Glass, Border = HudKit.Edge, Cut = cut,
        ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    private const float Pad = 12;
    private const float CardHeight = 236;

    /// <summary>A rule's book wording for this game system (Army Forge's own text), or null.</summary>
    public string? RuleText(string name)
    {
        string slug = Session.Table.Progress is { } p ? FDG.GameSystemInfo.Slug(p.GameSystem) : "";
        return FDG.Rules.Dispatch.OfficialRuleText.Text(name, slug);
    }

    private void BuildHud()
    {
        // Top-left: round, objectives held, whose turn.
        var roundRow = new HBoxContainer();
        roundRow.AddThemeConstantOverride("separation", 10);
        var r = new VBoxContainer();
        r.AddThemeConstantOverride("separation", -6);
        r.AddChild(HudKit.Text("ROUND", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        var n = new HBoxContainer();
        _round = HudKit.Text("-", 30, HudKit.Lime, HudKit.Bold);
        _roundOf = HudKit.Text("", 18, HudKit.Dim, HudKit.Bold);
        n.AddChild(_round);
        n.AddChild(_roundOf);
        r.AddChild(n);
        roundRow.AddChild(r);
        roundRow.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 34), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var o = new VBoxContainer();
        o.AddThemeConstantOverride("separation", 0);
        o.AddChild(HudKit.Text("OBJECTIVES", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
        _objectiveCounts = new HBoxContainer();
        _objectiveCounts.AddThemeConstantOverride("separation", 10);
        o.AddChild(_objectiveCounts);
        roundRow.AddChild(o);
        roundRow.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 34), SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var t = new VBoxContainer();
        t.AddThemeConstantOverride("separation", -2);
        _turn = HudKit.Text("", 11, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2));
        _turnLine = HudKit.Text("", 15, HudKit.Fg, HudKit.Semi);
        t.AddChild(_turn);
        t.AddChild(_turnLine);
        roundRow.AddChild(t);
        var roundBox = HudKit.Panel(Tight(10).With(x => { x.Accent = HudKit.Lime; x.AccentLength = 40; x.ContentMarginLeft = 12; x.ContentMarginRight = 12; }), roundRow);
        HudKit.Place(_hud, roundBox, LayoutPreset.TopLeft, new Vector2(Pad, Pad));

        // Top-centre: what the game wants right now.
        var promptRow = new HBoxContainer();
        promptRow.AddThemeConstantOverride("separation", 12);
        _prompt = HudKit.Text("", 20, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2));
        _promptHint = HudKit.Text("", 14, HudKit.Dim, HudKit.Semi);
        promptRow.AddChild(_prompt);
        promptRow.AddChild(_promptHint);
        var promptBox = HudKit.Panel(Tight(10).With(x => { x.Fill = HudKit.Glass with { A = 0.9f }; x.Border = HudKit.Lime with { A = 0.55f }; x.ContentMarginLeft = 16; x.ContentMarginRight = 16; }), promptRow);
        HudKit.Place(_hud, promptBox, LayoutPreset.CenterTop, new Vector2(0, Pad));

        // Top-right: Top view, Report, Armies, Menu.
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 6);
        foreach (var (name, icon, label, key, act) in new (string, string, string, string, Action)[]
        {
            ("TopView", "binoculars", "Top view", "T", ToggleTopView),
            ("Report", "scroll-unfurled", "Report", "R", OpenFullReport),
            ("Armies", "stack", "Armies", "L", OpenArmies),
            ("Menu", "hamburger-menu", "Menu", "Esc", OpenMenu),
        })
        {
            var b = HudKit.ActionButton(name, icon, label, key, HudKit.Teal, 56, labelSize: 11);
            b.Pressed += act;
            menu.AddChild(b);
        }
        HudKit.Place(_hud, menu, LayoutPreset.TopRight, new Vector2(-Pad, Pad));

        // Bottom-left: the selected unit + the four actions.
        var block = new HBoxContainer();
        block.AddThemeConstantOverride("separation", 10);
        // Kai, 2026-11-11: one size at all times - a card that grows and shrinks from unit to unit is jarring.
        // Long weapon / rule lists and long choice lists scroll inside their half instead.
        _unitCard = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var cardScroll = new ScrollContainer { CustomMinimumSize = new Vector2(316, CardHeight), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        cardScroll.AddChild(_unitCard);
        block.AddChild(cardScroll);
        block.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 0) });
        var areaScroll = new ScrollContainer { CustomMinimumSize = new Vector2(300, CardHeight), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var area = Decisions.BuildActionArea();
        area.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        areaScroll.AddChild(area);
        block.AddChild(areaScroll);
        _unitBlock = HudKit.Panel(Tight(12).With(x => { x.Accent = HudKit.Lime; x.AccentLength = 40; x.ContentMarginLeft = 10; x.ContentMarginTop = 8; x.ContentMarginBottom = 8; }), block);
        HudKit.Place(_hud, _unitBlock, LayoutPreset.BottomLeft, new Vector2(Pad, -Pad));
        _confirmBox = HudKit.Place(_hud, Decisions.BuildConfirmBox(), LayoutPreset.CenterBottom, new Vector2(0, -170));

        // Bottom-right: mission panel over the battle report.
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 6);
        _missionBody = new VBoxContainer();
        _missionBody.AddThemeConstantOverride("separation", 4);
        _mission = new CollapsePanel("Mission", "M", "", _missionBody, 350);
        stack.AddChild(_mission);
        var rep = new VBoxContainer { CustomMinimumSize = new Vector2(350, 0) };
        rep.AddThemeConstantOverride("separation", 2);
        var rh = new HBoxContainer();
        rh.AddChild(HudKit.Heading("Battle report", 12));
        rh.AddChild(HudKit.Grow());
        rh.AddChild(HudKit.Text("R - open", 12, HudKit.Dim, HudKit.Semi));
        rep.AddChild(rh);
        _reportRows = new VBoxContainer();
        _reportRows.AddThemeConstantOverride("separation", 1);
        rep.AddChild(_reportRows);
        stack.AddChild(HudKit.Panel(Tight(10).With(x => x.Fill = x.Fill with { A = 0.8f }), rep));
        HudKit.Place(_hud, stack, LayoutPreset.BottomRight, new Vector2(-Pad, -Pad));

        // Left: the army list, between the round box and the unit block.
        var list = new VBoxContainer { CustomMinimumSize = new Vector2(286, 0) };
        list.AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        foreach (bool enemy in new[] { false, true })
        {
            var tab = new Button { Text = enemy ? "ENEMY" : "YOUR ARMY", Name = enemy ? "EnemyTab" : "ArmyTab", ToggleMode = true, ButtonPressed = !enemy,
                TooltipText = enemy ? "The other side's units - click one to see its card." : "Your units - click one to select it (on your turn, to activate it)." };
            tab.AddThemeFontSizeOverride("font_size", 13);
            tab.Pressed += () => ShowList(enemy);
            _armyTabs[enemy ? 1 : 0] = tab;
            head.AddChild(tab);
        }
        head.AddChild(HudKit.Grow());
        _armyHead = HudKit.Text("", 13, HudKit.Dim, HudKit.Semi);
        head.AddChild(_armyHead);
        list.AddChild(head);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) };
        _armyRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _armyRows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_armyRows);
        list.AddChild(scroll);
        var listBox = HudKit.Panel(Tight(10).With(x => x.ContentMarginRight = 4), list);
        HudKit.Place(_hud, listBox, LayoutPreset.TopLeft, new Vector2(Pad, 92));
        _armyScroll = scroll;
        _armyBox = listBox;
        _roundBox = roundBox;
    }

    private ScrollContainer _armyScroll = null!;
    private Control _confirmBox = null!;
    private Control _armyBox = null!, _roundBox = null!;

    // ================================================================== HUD refresh

    private void RefreshHud()
    {
        IGameProgress progress = Session.Table.Progress;
        _round.Text = progress.RoundCount?.ToString() ?? "-";
        _roundOf.Text = $"/{progress.TotalRounds}";

        // Objectives held, per seat colour.
        foreach (Node c in _objectiveCounts.GetChildren()) c.QueueFree();
        var objectives = Session.Table.Objectives.Objects.ToList();
        foreach (GameSession.Seat seat in Session.Seats)
        {
            int held = objectives.Count(ob => ob.OwnerID is { } owner && owner.Equals(seat.Id));
            _objectiveCounts.AddChild(Count(held, _table.SeatColour(seat.Id), $"{seat.Name} holds {held}"));
        }
        int neutral = objectives.Count(ob => ob.OwnerID == null);
        _objectiveCounts.AddChild(Count(neutral, new Color(0.85f, 0.88f, 0.9f), $"{neutral} held by nobody"));

        // Whose turn, and how many the other side still has to activate.
        IUnit? activating = progress.ActivatingUnit;
        if (activating != null)
        {
            bool mine = Session.IsMine(activating.PlayerID);
            _turn.Text = mine ? "YOUR TURN" : $"{Session.SeatOf(activating.PlayerID)?.Name.ToUpperInvariant() ?? "ENEMY"}'S TURN";
            _turn.AddThemeColorOverride("font_color", mine ? HudKit.Lime : HudKit.Enemy);
        }
        else _turn.Text = progress.RoundCount == null ? "SETTING UP" : "";
        var others = progress.UnactivatedUnits.Where(u => !Session.IsMine(u.PlayerID)).GroupBy(u => u.PlayerID).ToList();
        _turnLine.Text = others.Count == 0 ? "" : string.Join("  ", others.Select(g => $"{Session.SeatOf(g.Key)?.Name ?? "Enemy"}: {g.Count()} left"));

        // The prompt line.
        (string prompt, string hint) = Decisions.Prompt();
        if (prompt.Length == 0)
        {
            if (Session.EndedText != null) { prompt = "GAME OVER"; hint = Session.EndedText; }
            else if (activating != null && !Session.IsMine(activating.PlayerID)) { prompt = $"{Session.SeatOf(activating.PlayerID)?.Name ?? "Enemy"} IS PLAYING"; hint = activating.Name; }
            else if (Session.Registry.LastAutopilotTask is { } auto) { prompt = "AUTOPILOT"; hint = $"The bot plays this part for you for now: {auto}"; }
            else if (progress.RoundCount is int r) { prompt = $"ROUND {r}"; hint = "Between activations - the game is working out what happens next."; }
            else { prompt = "SETTING UP"; hint = "The table is being laid out."; }
        }
        _prompt.Text = prompt.ToUpperInvariant();
        _promptHint.Text = hint;

        // The report notes who activates (Kai, 2026-11-10: "which units activated").
        if (activating != null && !activating.ID.Equals(_lastActivating?.ID)) Session.Report.NoteActivation();
        _lastActivating = activating;

        // A decision about the other side's units shows their list.
        Decision? open = Decisions.Current;
        if (!ReferenceEquals(open, _listFollowed))
        {
            _listFollowed = open;
            if (open is IUnitPick && open.WantsEnemyList != _showEnemy) ShowList(open.WantsEnemyList);
            else if (open is ActivateDecision && _showEnemy) ShowList(false);
        }

        RefreshArmy(progress);
        RefreshCard(activating);
        RefreshReport();
        RefreshMission(progress);

        // Army list fills the space between the round box and the unit block.
        float top = _roundBox.GetRect().End.Y + 8, bottom = _unitBlock.GetRect().Position.Y - 8;
        _armyBox.Position = new Vector2(Pad, top);
        float want = Mathf.Max(80, bottom - top - 40);
        if (Mathf.Abs(_armyScroll.CustomMinimumSize.Y - want) > 1)
        {
            _armyScroll.CustomMinimumSize = new Vector2(0, want);
            _armyBox.Size = Vector2.Zero;   // a free-placed container doesn't shrink by itself
        }

        // Free-placed panels don't shrink by themselves: keep the unit block and the confirm box at their size.
        Vector2 screen = GetViewportRect().Size;
        Vector2 min = _unitBlock.GetCombinedMinimumSize();
        if (_unitBlock.Size != min) { _unitBlock.Size = min; }
        _unitBlock.Position = new Vector2(Pad, screen.Y - Pad - min.Y);
        Vector2 cmin = _confirmBox.GetCombinedMinimumSize();
        _confirmBox.Size = cmin;
        // Centred above the bottom edge, but never over the unit block's choice buttons (they grow to the right).
        float cx = Mathf.Max((screen.X - cmin.X) / 2, _unitBlock.GetRect().End.X + 16);
        _confirmBox.Position = new Vector2(Mathf.Min(cx, screen.X - cmin.X - 16), screen.Y - 170 - cmin.Y);

        if (Session.EndedText != null && _gameOver == null) ShowGameOver();
    }

    private IUnit? _lastActivating;
    private Decision? _listFollowed;

    private void ShowList(bool enemy)
    {
        _showEnemy = enemy;
        _armySig = "";
        _armyTabs[0].ButtonPressed = !enemy;
        _armyTabs[1].ButtonPressed = enemy;
    }

    private readonly Button[] _armyTabs = new Button[2];

    private static Control Count(int n, Color c, string tip)
    {
        var h = new HBoxContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Stop };
        h.AddThemeConstantOverride("separation", 2);
        h.AddChild(HudKit.Icon("flag-objective", 18, c));
        h.AddChild(HudKit.Text(n.ToString(), 20, c, HudKit.Bold));
        return h;
    }

    private bool _showEnemy;

    private void RefreshArmy(IGameProgress progress)
    {
        List<IUnit> mine = _showEnemy ? Session.Table.Units.Objects.Where(u => !Session.IsMine(u.PlayerID)).ToList() : MyUnits();
        // Before round 1 the list is the deployment list: still to place first, placed units under a divider.
        bool setup = progress.RoundCount == null;
        bool Done(IUnit u) => setup ? u.GetIsOnBattlefield() : UnitActivation.HasActivated(progress, u);
        var ready = mine.Where(u => !Done(u) && Alive(u)).ToList();
        var done = mine.Where(u => Done(u) && Alive(u)).ToList();
        // The open decision's choices first, then the units it can't take, then the rest.
        int Order(IUnit u) => Decisions.ListMark(u) is { } m ? (m.Pickable ? 0 : 1) : 2;
        ready = ready.OrderBy(Order).ToList();
        string sig = string.Join("|", mine.Select(u => $"{u.ID.ID}:{Alive(u)}:{Done(u)}:{LeftOf(u)}:{u.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken)}:{Decisions.ListMark(u)}"))
            + (_selected?.ID.ID.ToString() ?? "") + (progress.ActivatingUnit?.ID.ID.ToString() ?? "") + _showEnemy + setup;
        if (sig == _armySig) return;
        _armySig = sig;
        _armyHead.Text = setup ? $"{ready.Count} to deploy" : _showEnemy ? $"{ready.Count}/{ready.Count + done.Count} to go" : $"{ready.Count}/{ready.Count + done.Count} to go - Tab";
        foreach (Node c in _armyRows.GetChildren()) c.QueueFree();
        foreach (IUnit u in ready) _armyRows.AddChild(ArmyRow(u, false));
        if (done.Count > 0)
        {
            var sep = new HBoxContainer();
            sep.AddThemeConstantOverride("separation", 6);
            sep.AddChild(HudKit.Text(setup ? "DEPLOYED" : "ACTIVATED", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
            sep.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(0, 1), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter });
            _armyRows.AddChild(sep);
            foreach (IUnit u in done) _armyRows.AddChild(ArmyRow(u, true));
        }
    }

    private static bool Alive(IUnit u) => u.Models.Any(m => m.RemainingWounds > 0);

    /// <summary>Models left, or wounds left for a single model.</summary>
    private static (int Left, int Max, bool Wounds) LeftOf(IUnit u) => u.Models.Count == 1
        ? ((int)MathF.Ceiling(u.Models[0].RemainingWounds), (int)u.Models[0].TotalWounds, true)
        : (u.Models.Count(m => m.RemainingWounds > 0), u.Models.Count, false);

    private Control ArmyRow(IUnit u, bool done)
    {
        bool selected = _selected?.ID.Equals(u.ID) == true;
        bool shaken = u.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken);
        bool active = Session.Table.Progress.ActivatingUnit?.ID.Equals(u.ID) == true;
        float a = done ? 0.4f : 1f;
        var (left, max, wounds) = LeftOf(u);
        string faction = Session.SeatOf(u.PlayerID)?.Faction ?? "";
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 7);
        bool hero = u.RuleDefinitions.Any(r => r.RequestedName == "Hero");
        bool shoots = u.Models.FirstOrDefault()?.Weapons.Any(w => w.RangeInches > 0) == true;
        h.AddChild(HudKit.Portrait(faction, u.Name, HudKit.KindIcon(u.Name, hero, shoots, wounds ? max : 1), 36, selected || active ? HudKit.Lime : _table.SeatColour(u.PlayerID), a, HudKit.LookOf(u)));
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 3);
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 4);
        var name = HudKit.Text(u.Name.ToUpperInvariant(), 14, (selected || active ? HudKit.Lime : HudKit.Fg) with { A = a }, HudKit.Tracked(HudKit.Bold, 1));
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        top.AddChild(name);
        if (shaken) top.AddChild(HudKit.Icon("broken-shield", 15, Palette.WoundsRed));
        if (hero) top.AddChild(HudKit.Icon("flying-flag", 15, HudKit.Gold with { A = a }));
        top.AddChild(HudKit.Text(wounds ? $"{left}/{max}W" : $"{left}/{max}", 13, HudKit.Dim with { A = a }, HudKit.Semi));
        v.AddChild(top);
        v.AddChild(SegBar(left, max, a));
        UnitMark? mark = Decisions.ListMark(u);
        if (mark is { Pickable: false, Reason: { } why })
        {
            var reason = HudKit.Text(why, 12, Palette.WoundsRed with { A = 0.85f }, HudKit.Medium);
            reason.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            reason.CustomMinimumSize = new Vector2(10, 0);
            reason.MouseFilter = MouseFilterEnum.Ignore;
            v.AddChild(reason);
        }
        h.AddChild(v);
        var box = new ChamferBox
        {
            Fill = selected || active ? new Color(HudKit.Lime, 0.13f) : new Color(0, 0, 0, done ? 0.12f : 0.25f),
            Border = selected || active ? HudKit.Lime : mark is { Pickable: true } ? HudKit.Lime with { A = 0.55f } : shaken ? Palette.WoundsRed with { A = 0.6f } : HudKit.Edge with { A = done ? 0.2f : 0.4f },
            Accent = mark is { Pickable: true } ? HudKit.Lime : Colors.Transparent, AccentLength = mark is { Pickable: true } ? 18 : 0,
            Cut = 6, ContentMarginLeft = 3, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        var row = HudKit.Panel(box, h);
        row.MouseFilter = MouseFilterEnum.Stop;
        row.TooltipText = mark is { Pickable: true } ? "Click to pick this unit" : mark is { Reason: { } r } ? r
            : shaken ? "Shaken" : done ? "Already activated this round" : "Click to see its card";
        row.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { SelectUnit(u); Decisions.UnitPicked(u); } };
        return row;
    }

    private static Control SegBar(int left, int max, float alpha)
    {
        var bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 2);
        for (int i = 0; i < Math.Min(max, 20); i++)
        {
            Color seg = i < left ? (left * 2 <= max ? Palette.WoundsRed : HudKit.Lime) : new Color(1, 1, 1, 0.12f);
            bar.AddChild(new ColorRect { Color = seg with { A = seg.A * alpha }, CustomMinimumSize = new Vector2(0, 3), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        }
        return bar;
    }

    private void RefreshCard(IUnit? activating)
    {
        IUnit? show = _selected ?? (activating != null && Session.IsMine(activating.PlayerID) ? activating : null)
                      ?? MyUnits().FirstOrDefault(Alive);
        string sig = show == null ? "" : $"{show.ID.ID}:{LeftOf(show)}:{show.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken)}:{UnitActivation.HasActivated(Session.Table.Progress, show)}";
        if (sig == _cardSig) return;
        _cardSig = sig;
        foreach (Node c in _unitCard.GetChildren()) c.QueueFree();
        if (show == null) { _unitCard.AddChild(HudKit.Text("No units yet.", 15, HudKit.Dim, HudKit.Semi)); return; }
        _unitCard.AddChild(UnitInfo(show, 300, null));
    }

    /// <summary>A unit's card: portrait, name, status, Q / D / models, weapons, rules (with their book text).</summary>
    public Control UnitInfo(IUnit u, float width, string? hint)
    {
        bool mine = Session.IsMine(u.PlayerID);
        Color team = _table.SeatColour(u.PlayerID);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0), MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 5);
        string faction = Session.SeatOf(u.PlayerID)?.Faction ?? "";
        var (left, max, wounds) = LeftOf(u);
        bool hero = u.RuleDefinitions.Any(r => r.RequestedName == "Hero");
        bool shoots = u.Models.FirstOrDefault()?.Weapons.Any(w => w.RangeInches > 0) == true;

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        top.AddChild(HudKit.Portrait(faction, u.Name, HudKit.KindIcon(u.Name, hero, shoots, wounds ? max : 1), 46, team, look: HudKit.LookOf(u)));
        var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", -3);
        var nm = HudKit.Text(u.Name.ToUpperInvariant(), 22, HudKit.Fg, HudKit.Tracked(HudKit.Bold, 1));
        nm.ClipText = true;
        names.AddChild(nm);
        names.AddChild(HudKit.Text($"{faction} - {Session.SeatOf(u.PlayerID)?.Name}", 14, HudKit.Dim, HudKit.Semi));
        top.AddChild(names);
        bool done = UnitActivation.HasActivated(Session.Table.Progress, u);
        bool shaken = u.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken);
        var tag = HudKit.Chip(shaken ? "SHAKEN" : !mine ? "ENEMY" : done ? "ACTIVATED" : "READY",
            shaken || !mine ? HudKit.Enemy : done ? HudKit.Dim : HudKit.Lime, true, 12);
        tag.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        top.AddChild(tag);
        col.AddChild(top);

        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 5);
        stats.AddChild(Stat("QUA", $"{u.Quality}+", "targeting"));
        stats.AddChild(Stat("DEF", $"{u.Defense}+", "shield"));
        stats.AddChild(Stat(wounds ? "WOUNDS" : "MODELS", $"{left}/{max}", "rally-the-troops"));
        col.AddChild(stats);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 0);
        foreach (string h in new[] { "WEAPON", "RNG", "ATK", "AP" })
            grid.AddChild(HudKit.Text(h, 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        var weapons = u.Models.Where(m => m.RemainingWounds > 0).SelectMany(m => m.Weapons)
            .GroupBy(w => (w.Name, w.RangeInches, w.Attacks, w.ArmorPenetration)).ToList();
        foreach (var g in weapons)
        {
            // Kai, 2026-11-11: a weapon's special rules show in gold under its name, each with the book's
            // wording on hover.
            var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 0) };
            cell.AddThemeConstantOverride("separation", -3);
            var name = HudKit.Text((g.Count() > 1 ? $"{g.Count()}x " : "") + g.Key.Name, 15, HudKit.Fg, HudKit.Semi);
            name.ClipText = true;
            cell.AddChild(name);
            var weaponRules = g.First().RuleDefinitions.GroupBy(r => r.RequestedName).Select(x => x.First()).ToList();
            if (weaponRules.Count > 0)
            {
                var flow = new HFlowContainer();
                flow.AddThemeConstantOverride("h_separation", 6);
                foreach (var wr in weaponRules)
                {
                    var l = HudKit.Text(wr.RequestedName, 12, Palette.RuleGold, HudKit.Semi);
                    l.TooltipText = BookText(wr);
                    l.MouseFilter = MouseFilterEnum.Stop;
                    l.MouseDefaultCursorShape = CursorShape.Help;
                    flow.AddChild(l);
                }
                cell.AddChild(flow);
                name.TooltipText = string.Join("\n\n", weaponRules.Select(BookText));
                name.MouseFilter = MouseFilterEnum.Stop;
            }
            grid.AddChild(cell);
            grid.AddChild(HudKit.Text(g.Key.RangeInches > 0 ? $"{g.Key.RangeInches:0}\"" : "-", 15, g.Key.RangeInches > 0 ? HudKit.Lime : HudKit.Dim, HudKit.Bold));
            grid.AddChild(HudKit.Text($"A{g.Key.Attacks}", 15, HudKit.Fg, HudKit.Bold));
            grid.AddChild(HudKit.Text(g.Key.ArmorPenetration > 0 ? g.Key.ArmorPenetration.ToString() : "-", 15, g.Key.ArmorPenetration > 0 ? Palette.WoundsRed : HudKit.Dim, HudKit.Bold));
        }
        col.AddChild(grid);

        var rulesFlow = new HFlowContainer();
        rulesFlow.AddThemeConstantOverride("h_separation", 4);
        rulesFlow.AddThemeConstantOverride("v_separation", 4);
        foreach (var rule in u.RuleDefinitions.GroupBy(r => r.RequestedName).Select(g => g.First()))
        {
            var chip = HudKit.Chip(rule.RequestedName, Palette.RuleGold, false, 13);
            chip.TooltipText = BookText(rule);
            chip.MouseFilter = MouseFilterEnum.Stop;
            rulesFlow.AddChild(chip);
        }
        col.AddChild(rulesFlow);
        if (hint != null) col.AddChild(HudKit.Text(hint, 12, HudKit.Dim with { A = 0.7f }, HudKit.Medium));
        return col;
    }

    /// <summary>A rule's name and the book's wording (Army Forge's own text for this game), else the engine's summary.</summary>
    private string BookText(FDG.Rules.Dispatch.ResolvedRule rule)
    {
        string bare = rule.RequestedName.Contains('(') ? rule.RequestedName[..rule.RequestedName.IndexOf('(')].Trim() : rule.RequestedName;
        string? book = RuleText(rule.Definition.Name) ?? RuleText(bare) ?? RuleText(rule.RequestedName);
        return book != null ? $"{rule.RequestedName}:\n{book}" : RuleText(rule);
    }

    private static string RuleText(FDG.Rules.Dispatch.ResolvedRule rule) =>
        rule.RequestedName + (string.IsNullOrWhiteSpace(rule.Definition.Description) ? "" : ":\n" + rule.Definition.Description);

    private static Control Stat(string label, string value, string icon)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        row.AddChild(HudKit.Icon(icon, 18, HudKit.Teal));
        row.AddChild(HudKit.Text(label, 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 1)));
        var v = HudKit.Text(value, 19, HudKit.Fg, HudKit.Bold);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        v.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(v);
        var p = HudKit.Panel(new ChamferBox { Fill = new Color(0, 0, 0, 0.3f), Border = HudKit.Edge with { A = 0.5f }, Cut = 5, ContentMarginLeft = 6, ContentMarginRight = 7, ContentMarginTop = 1, ContentMarginBottom = 1 }, row);
        p.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return p;
    }

    private void RefreshReport()
    {
        if (Session.Report.Version == _logVersion) return;
        _logVersion = Session.Report.Version;
        var blocks = BattleReport.Blocks(Session.Report.Snapshot()).Where(Worth).ToList();
        foreach (Node c in _reportRows.GetChildren()) c.QueueFree();
        FillBlocks(_reportRows, blocks.Skip(Math.Max(0, blocks.Count - 3)).ToList());
        if (_fullReport != null && IsInstanceValid(_fullReport))
        {
            foreach (Node c in _fullReportRows!.GetChildren()) c.QueueFree();
            FillBlocks(_fullReportRows, blocks);
            CallDeferred(nameof(ScrollToEnd), _fullReportScroll!);
        }
    }

    // ================================================================== report lines (hover: what they are about)

    private static string Hex(Color c) => c.ToHtml(false);
    private static string Esc(string t) => t.Replace("[", "[lb]");

    /// <summary>
    /// One report line. Unit names in their side's colour, rules / terms underlined in gold (hover = the book's
    /// wording); hovering the line rings the units it is about on the table, draws the attacker -> target arrow,
    /// and shows where a moved unit stood before (Kai, 2026-11-10). The dice behind it are the line's tooltip.
    /// </summary>
    private Control ReportRow(BattleReport.Line line, int size, bool hover = true)
    {
        Color baseColour = Color.Color8(line.Colour.R, line.Colour.G, line.Colour.B).Lerp(HudKit.Fg, 0.35f);
        var sb = new System.Text.StringBuilder();
        foreach (BattleReport.Piece piece in line.Title)
        {
            switch (piece.Kind)
            {
                case BattleReport.PieceKind.Unit when piece.Unit is { } id:
                    IUnit? u = Session.Table.Units.Objects.FirstOrDefault(x => x.ID.Equals(id));
                    Color c = u == null ? HudKit.Fg : _table.SeatColour(u.PlayerID).Lightened(0.3f);
                    sb.Append($"[url=u:{id.ID}][color=#{Hex(c)}][b]{Esc(piece.Text)}[/b][/color][/url]");
                    break;
                case BattleReport.PieceKind.Rule:
                    sb.Append($"[url=r:{Esc(piece.Hover ?? piece.Text)}][color=#{Hex(Palette.RuleGold)}]{Esc(piece.Text)}[/color][/url]");
                    break;
                case BattleReport.PieceKind.Term:
                    _terms[piece.Text] = piece.Hover ?? "";
                    sb.Append($"[url=t:{Esc(piece.Text)}][color=#{Hex(Palette.RuleGold)}]{Esc(piece.Text)}[/color][/url]");
                    break;
                default:
                    sb.Append(Esc(piece.Text));
                    break;
            }
        }
        if (line.Body.Length > 0) sb.Append($"\n[color=#{Hex(HudKit.Dim)}]{Esc(line.Body)}[/color]");
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(10, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass,
            MetaUnderlined = true,
        };
        rich.AddThemeFontOverride("normal_font", HudKit.Medium);
        rich.AddThemeFontOverride("bold_font", HudKit.Bold);
        rich.AddThemeFontSizeOverride("normal_font_size", size);
        rich.AddThemeFontSizeOverride("bold_font_size", size);
        rich.AddThemeColorOverride("default_color", baseColour);
        rich.Text = sb.ToString();
        if (hover)
        {
            rich.MouseEntered += () => FocusReport(line);
            rich.MouseExited += () => { if (_focused == line.Id) FocusReport(null); };
        }
        rich.MetaHoverStarted += meta => ShowMetaTip(meta.AsString());
        rich.MetaHoverEnded += _ => HideMetaTip();
        string dice = string.Join("\n", line.Rolls.Select(r => $"{r.Who}: {r.Label} ({r.Threshold}+) - "
            + (r.Faces.Count > 0 ? "rolled " + string.Join(" ", r.Faces) + " - " : "") + r.Result
            + (r.Modifiers.Count > 0 ? " [" + string.Join(", ", r.Modifiers) + "]" : "")).Concat(line.Details));
        rich.TooltipText = dice;

        var box = new ChamferBox
        {
            Fill = line.Kind switch
            {
                BattleReport.Kind.Divider => new Color(HudKit.Lime, 0.12f),
                BattleReport.Kind.Activation => new Color(1, 1, 1, 0.06f),
                _ => new Color(0, 0, 0, 0),
            },
            Border = Colors.Transparent, Cut = 4,
            ContentMarginLeft = line.Kind is BattleReport.Kind.Attack or BattleReport.Kind.Move or BattleReport.Kind.Line ? 10 : 4,
            ContentMarginRight = 4, ContentMarginTop = 1, ContentMarginBottom = 1,
            Accent = line.Kind == BattleReport.Kind.Activation && line.Player is { } who ? _table.SeatColour(who) : Colors.Transparent,
            AccentLength = 14,
        };
        var row = HudKit.Panel(box, rich);
        row.MouseFilter = MouseFilterEnum.Pass;
        if (hover)
        {
            row.MouseEntered += () => FocusReport(line);
            row.MouseExited += () => { if (_focused == line.Id) FocusReport(null); };
        }
        return row;
    }

    // ================================================================== report blocks (Kai, 2026-11-11: mockup A)

    /// <summary>Lines that only repeat what the block already says (the engine's own "X's Activation").</summary>
    private static bool Noise(BattleReport.Line l)
    {
        string text = string.Concat(l.Title.Select(p => p.Text));
        return l.Kind == BattleReport.Kind.Line && (text.EndsWith("'s Activation") || text.StartsWith("Activating"));
    }

    private static bool Worth(BattleReport.Block b) => b.Activation != null || b.Lines.Any(l => !Noise(l));

    /// <summary>Blocks with a round bar whenever the round changes.</summary>
    private void FillBlocks(VBoxContainer into, List<BattleReport.Block> blocks)
    {
        int? round = -1;
        foreach (BattleReport.Block b in blocks)
        {
            if (b.Round != round)
            {
                round = b.Round;
                var bar = new HBoxContainer();
                bar.AddThemeConstantOverride("separation", 6);
                bar.AddChild(HudKit.Text(round == null ? "SET-UP" : $"ROUND {round}", 12, HudKit.Lime, HudKit.Tracked(HudKit.Bold, 2)));
                bar.AddChild(new ColorRect { Color = HudKit.Lime with { A = 0.35f }, CustomMinimumSize = new Vector2(0, 1), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter });
                into.AddChild(bar);
            }
            into.AddChild(ReportBlock(b));
        }
    }

    private Color SideOf(UnitID? id) =>
        id is { } u && Session.Table.Units.Objects.FirstOrDefault(x => x.ID.Equals(u)) is { } unit ? _table.SeatColour(unit.PlayerID).Lightened(0.25f) : HudKit.Fg;

    private string NameOf(UnitID? id, string? fallback = null) =>
        id is { } u && Session.Table.Units.Objects.FirstOrDefault(x => x.ID.Equals(u)) is { } unit ? unit.Name : fallback ?? "";

    /// <summary>
    /// One activation: the unit, what it did as symbols (moved / charged / held, shot, fought), then only the
    /// results - who it hit and what they lost. The dice are in the hover card; hovering also shows it on the
    /// table; clicking moves the camera there (Kai, 2026-11-11).
    /// </summary>
    private Control ReportBlock(BattleReport.Block b)
    {
        UnitID? me = b.Unit;
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 2);

        if (b.Activation != null && me is { } uid)
        {
            Color side = SideOf(uid);
            IUnit? unit = Session.Table.Units.Objects.FirstOrDefault(x => x.ID.Equals(uid));
            var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            top.AddThemeConstantOverride("separation", 6);
            if (unit != null)
            {
                bool hero = unit.RuleDefinitions.Any(r => r.RequestedName == "Hero");
                top.AddChild(HudKit.Portrait(Session.SeatOf(unit.PlayerID)?.Faction ?? "", unit.Name, HudKit.KindIcon(unit.Name, hero, true, 1), 26, _table.SeatColour(unit.PlayerID), look: HudKit.LookOf(unit)));
            }
            var name = HudKit.Text(NameOf(uid, "?").ToUpperInvariant(), 15, side, HudKit.Tracked(HudKit.Bold, 1));
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            name.VerticalAlignment = VerticalAlignment.Center;
            top.AddChild(name);
            // What the unit did, as symbols: moved / charged / held, then shot and / or fought.
            string move = b.Charged ? $"CHARGED {b.Moved:0}\"" : b.Moved > 0.05f ? $"MOVED {b.Moved:0}\"" : "HELD";
            top.AddChild(Did(b.Charged ? "sword-clash" : b.Moved > 0.05f ? "walk" : "shield", move, HudKit.Fg));
            if (b.Shot) top.AddChild(Did("crosshair", "SHOT", HudKit.Teal));
            if (b.Fought && !b.Charged) top.AddChild(Did("crossed-swords", "MELEE", Palette.WoundsRed));
            v.AddChild(top);
        }

        int results = 0;
        foreach (BattleReport.Line l in b.Lines.Where(l => !Noise(l)))
        {
            if (l.Attack is { } a)
            {
                bool mine = me != null && l.Attacker.Equals(me);
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 5);
                row.AddChild(HudKit.Gap(30));
                // The symbol says what happened: shot, fought, or struck back at the unit that attacked it.
                row.AddChild(HudKit.Icon(!mine && a.Melee ? "shield-reflect" : a.Melee ? "crossed-swords" : "crosshair", 15, a.Melee ? Palette.WoundsRed : HudKit.Teal));
                if (!mine) row.AddChild(HudKit.Text(NameOf(l.Attacker), 15, SideOf(l.Attacker), HudKit.Semi));
                if (!mine) row.AddChild(HudKit.Text(a.Melee ? "struck back at" : "shot", 14, HudKit.Dim, HudKit.Medium));
                row.AddChild(HudKit.Text(NameOf(l.Target, l.TargetName ?? "the target"), 15, SideOf(l.Target), HudKit.Semi));
                string result = a.ModelsLost > 0 ? $"-{a.ModelsLost} model{(a.ModelsLost == 1 ? "" : "s")}"
                    : a.Wounds > 0.01f ? $"-{a.Wounds:0} wound{(a.Wounds < 1.5f ? "" : "s")}" : "no damage";
                row.AddChild(HudKit.Text(result, 15, a.ModelsLost > 0 || a.Wounds > 0.01f ? Palette.WoundsRed : HudKit.Dim, HudKit.Bold));
                if (a.Routed) row.AddChild(HudKit.Chip("ROUTED", Palette.WoundsRed, true, 11));
                v.AddChild(row);
                results++;
            }
            else if (l.Kind == BattleReport.Kind.Move && me != null && l.Units.Contains(me.Value))
                continue;   // the header already says how far it went
            else
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddChild(HudKit.Gap(b.Activation != null ? 30 : 0));
                var line = ReportRow(l, 13, hover: false);
                line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(line);
                v.AddChild(row);
                results++;
            }
        }
        if (results == 0 && b.Activation != null)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(HudKit.Gap(30));
            row.AddChild(HudKit.Text(b.Shot || b.Fought ? "" : "nothing else", 13, HudKit.Dim, HudKit.Medium));
            v.AddChild(row);
        }

        var box = new ChamferBox
        {
            Fill = new Color(0, 0, 0, 0.25f), Border = HudKit.Edge with { A = 0.35f }, Cut = 6,
            Accent = me is { } m ? SideOf(m) : Colors.Transparent, AccentLength = 16,
            ContentMarginLeft = 6, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        var panel = HudKit.Panel(box, v);
        panel.MouseFilter = MouseFilterEnum.Stop;
        int key = b.Activation?.Id ?? b.Lines.FirstOrDefault()?.Id ?? 0;
        panel.MouseEntered += () =>
        {
            box.Fill = new Color(1, 1, 1, 0.1f);
            box.Border = Colors.White with { A = 0.6f };
            FocusReport(b.Lines.Append(b.Activation).OfType<BattleReport.Line>().ToList(), key);
            ShowBlockCard(b);
        };
        panel.MouseExited += () =>
        {
            box.Fill = new Color(0, 0, 0, 0.25f);
            box.Border = HudKit.Edge with { A = 0.35f };
            if (_focused == key) FocusReport(null);
            HideBlockCard();
        };
        // Click: the camera goes to the fight (or to the unit).
        panel.GuiInput += e =>
        {
            // Right-click: keep this block's card up as a window (move / minimise / close) - Kai, 2026-11-11.
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) { PinBlockCard(b); panel.AcceptEvent(); return; }
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
            BattleReport.Line? fight = b.Lines.FirstOrDefault(l => l.Attack != null);
            Vector2? at = fight != null ? Where(fight.Target, fight.ToAt) : Where(me, null);
            if (at is { } p) FocusOn(_table.World(p.X, p.Y));
        };
        return panel;
    }

    private static Control Did(string icon, string text, Color c)
    {
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        h.AddThemeConstantOverride("separation", 3);
        h.AddChild(HudKit.Icon(icon, 15, c));
        h.AddChild(HudKit.Text(text, 13, c, HudKit.Bold));
        return h;
    }

    private Control? _blockCard;

    /// <summary>The hover card: every die behind the block, attack by attack (the numbers live here, not in the report).</summary>
    private void ShowBlockCard(BattleReport.Block b)
    {
        HideBlockCard();
        if (BlockCardBody(b) is not { } v) return;
        _blockCard = HudKit.Panel(Tight(8).With(x => { x.Fill = HudKit.Glass with { A = 0.97f }; x.Border = Colors.White with { A = 0.6f }; }), v);
        _blockCard.MouseFilter = MouseFilterEnum.Ignore;
        _hud.AddChild(_blockCard);
        CallDeferred(nameof(PlaceBlockCard));
    }

    private readonly Dictionary<int, FloatingPanel> _pinnedBlocks = new();

    /// <summary>A block's card as its own window; right-clicking the same block again brings that window back.</summary>
    private void PinBlockCard(BattleReport.Block b)
    {
        int key = b.Activation?.Id ?? b.Lines.FirstOrDefault()?.Id ?? 0;
        if (_pinnedBlocks.TryGetValue(key, out FloatingPanel? open) && IsInstanceValid(open)) { open.Minimised = false; return; }
        Control body = BlockCardBody(b) ?? HudKit.Text("Nothing was rolled.", 14, HudKit.Dim, HudKit.Medium);
        string title = b.Unit is { } u ? NameOf(u, "Report") : "Report";
        var panel = new FloatingPanel(title, body, b.Unit is { } id ? SideOf(id) : HudKit.Teal);
        panel.Closed += () => _pinnedBlocks.Remove(key);
        _hud.AddChild(panel);
        Vector2 mouse = GetViewport().GetMousePosition();
        panel.Position = new Vector2(Mathf.Max(10, mouse.X - 460), Mathf.Clamp(mouse.Y - 200, 10, GetViewportRect().Size.Y - 420));
        _pinnedBlocks[key] = panel;
        HideBlockCard();
    }

    private Control? BlockCardBody(BattleReport.Block b)
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0), MouseFilter = MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 3);
        bool any = false;
        foreach (BattleReport.Line l in b.Lines.Where(l => l.Rolls.Count > 0 || l.Details.Count > 0))
        {
            any = true;
            string title = string.Concat(l.Title.Select(p => p.Text));
            var t = HudKit.Text(title, 14, l.Attack != null ? SideOf(l.Attacker) : HudKit.Fg, HudKit.Bold);
            t.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            t.CustomMinimumSize = new Vector2(380, 0);
            v.AddChild(t);
            foreach (BattleReport.Roll r in l.Rolls)
            {
                var row = HudKit.Text($"  {r.Who}: {r.Label} ({r.Threshold}+)  "
                    + (r.Faces.Count > 0 ? string.Join(" ", r.Faces) + "  -  " : "") + r.Result
                    + (r.Modifiers.Count > 0 ? "  [" + string.Join(", ", r.Modifiers) + "]" : ""), 13, HudKit.Dim, HudKit.Medium);
                row.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                row.CustomMinimumSize = new Vector2(380, 0);
                v.AddChild(row);
            }
            foreach (string d in l.Details) v.AddChild(HudKit.Text("  " + d, 13, HudKit.Dim, HudKit.Medium));
        }
        if (!any) return null;
        v.AddChild(HudKit.Text("Right-click the block to keep this open.", 12, HudKit.Dim with { A = 0.7f }, HudKit.Medium));
        return v;
    }

    private void PlaceBlockCard()
    {
        if (_blockCard == null || !IsInstanceValid(_blockCard)) return;
        Vector2 mouse = GetViewport().GetMousePosition(), size = _blockCard.Size, screen = GetViewportRect().Size;
        Vector2 at = new(mouse.X - size.X - 30, mouse.Y - size.Y / 2);
        if (at.X < 10) at.X = mouse.X + 30;
        at.Y = Mathf.Clamp(at.Y, 10, screen.Y - size.Y - 10);
        _blockCard.Position = at;
    }

    private void HideBlockCard()
    {
        if (_blockCard != null && IsInstanceValid(_blockCard)) _blockCard.QueueFree();
        _blockCard = null;
    }

    private readonly Dictionary<string, string> _terms = new();
    private int _focused = -1;

    /// <summary>Shows on the table what a hovered report line is about (null clears it).</summary>
    private void FocusReport(BattleReport.Line? line) => FocusReport(line == null ? null : new[] { line }, line?.Id ?? -1);

    private void FocusReport(IReadOnlyList<BattleReport.Line>? report, int key)
    {
        _focused = key;
        _table.Highlight.Clear();
        if (report == null) { _table.ClearGhosts("report"); return; }
        var ghosts = new List<BattleTable.Ghost>();
        var lines = new List<BattleTable.GhostLine>();
        foreach (BattleReport.Line line in report) FocusLine(line, ghosts, lines);
        _table.SetGhosts("report", ghosts, lines);
    }

    private void FocusLine(BattleReport.Line line, List<BattleTable.Ghost> ghosts, List<BattleTable.GhostLine> lines)
    {
        foreach (UnitID id in line.Units) _table.Highlight.Add(id);
        Color gold = new(1f, 0.86f, 0.3f);
        // Where a moved unit stood before, and the path it took.
        foreach (BattleReport.Spot spot in line.MovedFrom)
            ghosts.Add(new BattleTable.Ghost(spot.X, spot.Z, Mathf.Max(0.4f, spot.Radius), new Color(1, 1, 1, 0.9f)));
        if (line.MovedFrom.Count > 0 && line.MovedTo.Count > 0)
            lines.Add(new BattleTable.GhostLine(
                new Vector2(line.MovedFrom.Average(p => p.X), line.MovedFrom.Average(p => p.Z)),
                new Vector2(line.MovedTo.Average(p => p.X), line.MovedTo.Average(p => p.Z)), new Color(1, 1, 1, 0.8f), 0.14f));
        // Attacker -> target, with an arrowhead at the target.
        Vector2? from = Where(line.Attacker, line.FromAt), to = Where(line.Target, line.ToAt);
        if (from is { } a && to is { } b && a.DistanceTo(b) > 0.5f)
        {
            lines.Add(new BattleTable.GhostLine(a, b, gold, 0.28f));
            Vector2 dir = (b - a).Normalized();
            Vector2 side = new(-dir.Y, dir.X);
            lines.Add(new BattleTable.GhostLine(b, b - dir * 1.6f + side * 0.9f, gold, 0.28f));
            lines.Add(new BattleTable.GhostLine(b, b - dir * 1.6f - side * 0.9f, gold, 0.28f));
        }
    }

    /// <summary>A unit's middle now, or where the line says it was (it may have died since).</summary>
    private Vector2? Where(UnitID? id, Position? then)
    {
        if (id is { } uid && Session.Table.Units.Objects.FirstOrDefault(u => u.ID.Equals(uid)) is { } unit && unit.GetIsOnBattlefield())
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count > 0) return new Vector2(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }
        return then is { } p ? new Vector2(p.x, p.z) : null;
    }

    private Control? _metaTip;

    /// <summary>The hover card for a word in a report line: a unit's card, a rule's book wording, a term's text.</summary>
    private void ShowMetaTip(string meta)
    {
        HideMetaTip();
        Control? body = null;
        if (meta.StartsWith("u:") && Guid.TryParse(meta[2..], out Guid uid)
            && Session.Table.Units.Objects.FirstOrDefault(u => u.ID.ID == uid) is { } unit)
            body = UnitInfo(unit, 300, null);
        else if (meta.StartsWith("r:"))
        {
            string name = meta[2..].Replace("[lb]", "[");
            string slug = Session.Table.Progress is { } p ? FDG.GameSystemInfo.Slug(p.GameSystem) : "";
            body = TipText(name, FDG.Rules.Dispatch.OfficialRuleText.Text(name, slug) ?? "No book wording found for this rule.");
        }
        else if (meta.StartsWith("t:"))
        {
            string name = meta[2..].Replace("[lb]", "[");
            body = TipText(name, _terms.TryGetValue(name, out string? t) ? t : "");
        }
        if (body == null) return;
        _metaTip = HudKit.Panel(Tight(10).With(x => { x.Fill = HudKit.Glass with { A = 0.97f }; x.Border = Palette.RuleGold with { A = 0.8f }; }), body);
        _metaTip.MouseFilter = MouseFilterEnum.Ignore;
        _hud.AddChild(_metaTip);
        CallDeferred(nameof(PlaceMetaTip));
    }

    private static Control TipText(string title, string text)
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0), MouseFilter = MouseFilterEnum.Ignore };
        v.AddChild(HudKit.Text(title.ToUpperInvariant(), 15, Palette.RuleGold, HudKit.Tracked(HudKit.Bold, 1)));
        var l = HudKit.Text(text, 14, HudKit.Fg, HudKit.Medium);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(320, 0);
        v.AddChild(l);
        return v;
    }

    private void PlaceMetaTip()
    {
        if (_metaTip == null || !IsInstanceValid(_metaTip)) return;
        Vector2 mouse = GetViewport().GetMousePosition(), size = _metaTip.Size, screen = GetViewportRect().Size;
        Vector2 at = mouse + new Vector2(-size.X - 20, -size.Y / 2);
        if (at.X < 10) at.X = mouse.X + 20;
        at.Y = Mathf.Clamp(at.Y, 10, screen.Y - size.Y - 10);
        _metaTip.Position = at;
    }

    private void HideMetaTip()
    {
        if (_metaTip != null && IsInstanceValid(_metaTip)) _metaTip.QueueFree();
        _metaTip = null;
    }

    private void RefreshMission(IGameProgress progress)
    {
        foreach (Node c in _missionBody.GetChildren()) c.QueueFree();
        var vm = Session.Lobby.Vm;
        _missionBody.AddChild(HudKit.Text(vm.MissionCardGame == FDG.Missions.EMissionCardGame.Off
            ? "Seize objectives" : FDG.Missions.MissionCardCatalog.Name(vm.MissionCardGame), 16, HudKit.Fg, HudKit.Bold));
        _missionBody.AddChild(HudKit.Text($"Ends after round {progress.TotalRounds}. " +
            (vm.MissionCardGame == FDG.Missions.EMissionCardGame.Off ? "Most objectives held wins." : "Most victory points wins."), 13, HudKit.Dim, HudKit.Semi));
        var scores = progress.Scores;
        string summary = string.Join("  -  ", Session.Seats.Select(s => $"{s.Name} {scores.FirstOrDefault(x => x.PlayerID.Equals(s.Id)).ObjectiveCount}"));
        foreach (GameSession.Seat s in Session.Seats)
        {
            int n = scores.FirstOrDefault(x => x.PlayerID.Equals(s.Id)).ObjectiveCount;
            _missionBody.AddChild(HudKit.Text($"{s.Name}: {n} objective{(n == 1 ? "" : "s")}", 15, _table.SeatColour(s.Id), HudKit.Semi));
        }
        _mission.SetSummary(summary);
    }

    // ================================================================== panels

    private FloatingPanel? _fullReport, _armiesPanel;
    private VBoxContainer? _fullReportRows;
    private ScrollContainer? _fullReportScroll;

    /// <summary>The whole report (R). Only ever one: R or the button again brings the open one back.</summary>
    private void OpenFullReport()
    {
        if (_fullReport != null && IsInstanceValid(_fullReport)) { _fullReport.Minimised = false; return; }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(560, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        FillBlocks(rows, BattleReport.Blocks(Session.Report.Snapshot()).Where(Worth).ToList());
        scroll.AddChild(rows);
        _fullReport = new FloatingPanel("Battle report", scroll, HudKit.Teal);
        _fullReport.Closed += () => { _fullReport = null; FocusReport(null); };
        _fullReportRows = rows;
        _fullReportScroll = scroll;
        _hud.AddChild(_fullReport);
        _fullReport.Position = new Vector2(GetViewportRect().Size.X / 2 - 290, 90);
        CallDeferred(nameof(ScrollToEnd), scroll);
    }

    private static void ScrollToEnd(ScrollContainer s) => s.ScrollVertical = (int)s.GetVScrollBar().MaxValue;

    /// <summary>Both armies (L). Only ever one open.</summary>
    private void OpenArmies()
    {
        if (_armiesPanel != null && IsInstanceValid(_armiesPanel)) { _armiesPanel.Minimised = false; return; }
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        foreach (GameSession.Seat seat in Session.Seats)
        {
            v.AddChild(HudKit.Text(seat.Name.ToUpperInvariant(), 16, _table.SeatColour(seat.Id), HudKit.Tracked(HudKit.Bold, 2)));
            foreach (IUnit u in Session.Table.Units.Objects.Where(u => u.PlayerID.Equals(seat.Id)))
            {
                var (left, max, wounds) = LeftOf(u);
                v.AddChild(HudKit.Text($"{u.Name}  -  Q{u.Quality}+ D{u.Defense}+  -  {(wounds ? $"{left}/{max} wounds" : $"{left}/{max} models")}", 14,
                    Alive(u) ? HudKit.Fg : HudKit.Dim, HudKit.Semi));
            }
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(480, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(v);
        _armiesPanel = new FloatingPanel("Army lists", scroll, HudKit.Teal);
        _armiesPanel.Closed += () => _armiesPanel = null;
        _hud.AddChild(_armiesPanel);
        _armiesPanel.Position = new Vector2(GetViewportRect().Size.X / 2 - 250, 90);
    }

    // ================================================================== Esc menu, saving

    private Control? _menu;

    /// <summary>
    /// The in-game menu (Esc): Resume, Save game, Load game, Settings, Main menu, Quit. Saving writes to the Saved
    /// Games folder under a name you can change; Ctrl+S saves straight away under the suggested name.
    /// </summary>
    private void OpenMenu()
    {
        if (_menu != null && IsInstanceValid(_menu)) { CloseMenu(); return; }
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop, Name = "EscMenu" };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        v.AddThemeConstantOverride("separation", 10);
        v.AddChild(HudKit.Text("MENU", 34, HudKit.Lime, HudKit.Exo(800), HorizontalAlignment.Center));
        Button Big(string text, Action go, string tip, string variation = "BackButton")
        {
            var b = Ui.Button(text, go, tip, variation);
            b.CustomMinimumSize = new Vector2(0, 48);
            v.AddChild(b);
            return b;
        }
        Big("Resume  (Esc)", CloseMenu, "Back to the game.", "CommitButton").Name = "MenuResume";

        // Save: a name box (pre-filled), then Save.
        bool canSave = Session.Lobby.Vm.HasHostPrivileges && Session.Table.Progress.RoundCount != null;
        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 8);
        var name = new LineEdit { Text = SuggestedSaveName(), SizeFlagsHorizontal = SizeFlags.ExpandFill, Editable = canSave, Name = "SaveName" };
        var save = Ui.Button("Save game", () => SaveNow(name.Text), canSave ? "Save this game to the Saved Games folder (Ctrl+S saves straight away)."
            : !Session.Lobby.Vm.HasHostPrivileges ? "Only the host can save the game." : "Saving opens once round 1 starts (a game saved during set-up can't be resumed).", "CommitButton");
        save.Disabled = !canSave;
        save.Name = "MenuSave";
        name.TextSubmitted += t => { if (canSave) SaveNow(t); };
        saveRow.AddChild(name);
        saveRow.AddChild(save);
        v.AddChild(saveRow);

        Big("Load game", () => Leave("Load another game?", () => App.I.Later(() => App.I.Open<LoadGameScreen>(Scenes.LoadGame))),
            "Pick a saved game to carry on. This game ends - save it first if you want to come back to it.");
        Big("Settings", () => { CloseMenu(); App.I.Open<SettingsScreen>(Scenes.Settings); }, "Your name, display, camera and sound settings.");
        Big("Main menu", () => Leave("Leave this game?", () => { }), "Leave this game and go back to the main menu.");
        Big("Quit game", () => Leave("Quit the game?", App.I.Quit), "Close the game.").AddThemeColorOverride("font_color", Palette.WoundsRed);

        var panel = HudKit.Panel(new ChamferBox
        {
            Fill = HudKit.Glass with { A = 0.97f }, Border = HudKit.Lime, Cut = 14, Glow = HudKit.Lime with { A = 0.3f },
            ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 22, ContentMarginBottom = 26,
        }, v);
        veil.AddChild(panel);
        panel.SetAnchorsPreset(LayoutPreset.Center);
        panel.GrowHorizontal = GrowDirection.Both;
        panel.GrowVertical = GrowDirection.Both;
        _hud.AddChild(veil);
        _menu = veil;
    }

    private void CloseMenu()
    {
        if (_menu != null && IsInstanceValid(_menu)) _menu.QueueFree();
        _menu = null;
    }

    /// <summary>Leaving the game: asks first (unsaved progress is lost), then goes to the main menu and does <paramref name="then"/>.</summary>
    private void Leave(string question, Action then)
    {
        bool saved = _lastSaveRound == Session.Table.Progress.RoundCount && Session.Table.Progress.RoundCount != null;
        Confirm(question, Session.EndedText != null || saved ? "This game ends." : "This game ends - anything since your last save is lost.",
            "Leave", () => { App.I.ToMainMenu(); then(); }, "Stay");
    }

    private int? _lastSaveRound;

    private string SuggestedSaveName()
    {
        var p = Session.Table.Progress;
        string game = FDG.GameSystemInfo.DisplayName(p.GameSystem);
        string round = p.RoundCount is int r ? $"Round {r}" : "Set-up";
        string them = string.Join(" vs ", Session.Seats.Select(x => x.Faction.Length > 0 ? x.Faction : x.Name));
        return $"{game} - {them} - {round} - {DateTime.Now:yyyy-MM-dd HH-mm}";
    }

    private void SaveNow(string name)
    {
        if (!Session.Lobby.Vm.HasHostPrivileges) { Toast("Only the host can save the game."); return; }
        if (Session.Table.Progress.RoundCount == null) { Toast("Saving opens once round 1 starts."); return; }
        string? path = Session.Lobby.SaveGame(name, out string error);
        if (path == null) { Toast(error); return; }
        _lastSaveRound = Session.Table.Progress.RoundCount;
        Toast($"Saved: {System.IO.Path.GetFileNameWithoutExtension(path)}");
        CloseMenu();
    }

    // ================================================================== game over

    private void Rematch()
    {
        if (FdgRaylib.Lobby.LobbySession.LastLaunch is not { } plan) return;
        App.I.ToMainMenu();
        // The old lobby lets go of the network port as it leaves the tree; host the new one a moment later.
        App.I.Later(() =>
        {
            if (FdgRaylib.Lobby.LobbySession.TryHost(plan.System, plan.Mode, out var lobby, out string error) && lobby != null)
            {
                lobby.ApplyRematch(plan);
                App.I.Open<LobbyScreen>(Scenes.Lobby, l => l.Session = lobby);
            }
            else App.I.Message("Can't start the rematch", error);
        });
    }

    private void SaveReport()
    {
        var p = Session.Table.Progress;
        var scores = p.Scores;
        var lines = Session.Seats.Select(seat => $"{seat.Name}{(seat.Faction.Length > 0 ? $" ({seat.Faction})" : "")}: {scores.FirstOrDefault(x => x.PlayerID.Equals(seat.Id)).ObjectiveCount} objectives");
        try
        {
            string text = Session.Report.ExportText($"{FDG.GameSystemInfo.DisplayName(p.GameSystem)} - {FDG.GameSystemInfo.ModeLabel(p.GameMode, p.GameSystem)}",
                Session.EndedText ?? "", lines);
            string path = System.IO.Path.Combine(FdgRaylib.SaveLoad.SavePaths.FolderPath, $"Battle Report {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt");
            System.IO.File.WriteAllText(path, text);
            Toast($"Battle report saved in Saved Games: {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Toast("Couldn't write the battle report: " + ex.Message);
        }
    }

    private void ShowGameOver()
    {
        GD.Print($"Game over: {Session.EndedText}");
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        v.AddChild(HudKit.Text("GAME OVER", 48, HudKit.Lime, HudKit.Exo(800), HorizontalAlignment.Center));
        v.AddChild(HudKit.Text(Session.EndedText ?? "", 24, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center));
        // Kai, 2026-11-11: the score on the Game Over card - objectives held (what decides the game), and what is left.
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (string h in new[] { "", "OBJECTIVES", "UNITS LEFT", "POINTS LEFT" })
            grid.AddChild(HudKit.Text(h, 12, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center));
        var scores = Session.Table.Progress.Scores;
        foreach (GameSession.Seat seat in Session.Seats)
        {
            var units = Session.Table.Units.Objects.Where(u => u.PlayerID.Equals(seat.Id) && u.Models.Any(m => m.GetIsAlive())).ToList();
            int obj = scores.FirstOrDefault(x => x.PlayerID.Equals(seat.Id)).ObjectiveCount;
            grid.AddChild(HudKit.Text(seat.Name, 20, _table.SeatColour(seat.Id), HudKit.Bold));
            grid.AddChild(HudKit.Text(obj.ToString(), 26, HudKit.Fg, HudKit.Exo(800), HorizontalAlignment.Center));
            grid.AddChild(HudKit.Text(units.Count.ToString(), 20, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center));
            grid.AddChild(HudKit.Text(units.Sum(u => u is UnitData ud ? ud.PointCost : 0).ToString(), 20, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center));
        }
        v.AddChild(grid);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        string? noRematch = FdgRaylib.Lobby.LobbySession.RematchUnavailableReason(Session.Lobby);
        var rematch = Ui.Button("Rematch", Rematch, noRematch ?? "Same game, same seats, same armies - back to the lobby to LAUNCH again.", "CommitButton");
        rematch.Disabled = noRematch != null;
        rematch.Name = "Rematch";
        var report = Ui.Button("Save battle report", SaveReport, "Write everything that happened (with the dice) to a text file in the Saved Games folder.");
        report.Name = "SaveReport";
        var look = Ui.Button("Look at the table", () => { _gameOver?.Hide(); }, "Close this card and look round the final table.");
        var menu = Ui.Button("Main menu", App.I.ToMainMenu, "Back to the main menu.", "BackButton");
        row.AddChild(rematch);
        row.AddChild(report);
        row.AddChild(look);
        row.AddChild(menu);
        v.AddChild(row);
        _gameOver = HudKit.Panel(new ChamferBox { Fill = HudKit.Glass with { A = 0.96f }, Border = HudKit.Lime, Cut = 16, Glow = HudKit.Lime with { A = 0.35f }, ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 28, ContentMarginBottom = 28 }, v);
        HudKit.Place(_hud, _gameOver, LayoutPreset.Center);
    }
}
