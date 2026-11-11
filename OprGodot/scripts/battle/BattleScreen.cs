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
        MouseFilter = MouseFilterEnum.Ignore;

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

    public override void _ExitTree() => MenuBackdrop.Instance?.SetActive(true);

    public BattleTable Table => _table;

    /// <summary><c>--autoplay</c>: the test driver answers every decision (see <see cref="Decision.AutoPlay"/>).</summary>
    private static readonly bool AutoPlayRequested = OS.GetCmdlineUserArgs().Any(x => x.StartsWith("--autoplay"));

    /// <summary><c>--autoplay=ActivateDecision,MoveDecision</c> limits it to those kinds (a screenshot of the rest).</summary>
    private static readonly HashSet<string>? AutoPlayOnly = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--autoplay="))
        ?["--autoplay=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    private static bool AutoPlays(Decision d) => AutoPlayOnly == null || AutoPlayOnly.Contains(d.GetType().Name);
    public Control Hud => _hud;

    // ================================================================== frame

    public override void _Process(double delta)
    {
        UpdateHover(delta);
        if (AutoPlayRequested && Decisions.Current is { } open && AutoPlays(open)) { Decisions.Prompt(); if (!open.Done) open.AutoPlay(this); }
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

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(8f, _distance * 0.9f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(220f, _distance * 1.1f); ApplyCamera(); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _dragRotate = mb.Pressed ? mb.Position : null;
                if (!mb.Pressed && _pinned.Count > 0 && mb.Position.DistanceTo(_rightDown) < 4) CloseNewestPin();
                if (mb.Pressed) _rightDown = mb.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragPan = mb.Pressed ? mb.Position : null; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                OnLeftClick(mb.Position); break;
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
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.T }:
                ToggleTopView(); break;
            case InputEventKey { Pressed: true, Keycode: Key.Q }:
                _yaw += 5; ApplyCamera(); break;
            case InputEventKey { Pressed: true, Keycode: Key.E }:
                _yaw -= 5; ApplyCamera(); break;
            case InputEventKey { Pressed: true, Keycode: Key.Tab }:
                SelectNextReady(); GetViewport().SetInputAsHandled(); break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }:
                OpenFullReport(); break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: >= Key.Key1 and <= Key.Key4 } k:
                if (Decisions.HotKey((int)(k.Keycode - Key.Key1))) GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.M }:
                _mission.Collapsed = !_mission.Collapsed; break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                if (_pinned.Count > 0) CloseNewestPin();
                else if (!Decisions.Cancel()) OpenMenu();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private Vector2 _rightDown;

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
        if (Session.IsMine(unit.PlayerID)) SelectUnit(unit, focus: false);
        else Pin(unit);
    }

    private void UpdateHover(double delta)
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        bool overHud = GetViewport().GuiGetHoveredControl() is { } c && c != this && c != _hud;
        IUnit? under = overHud ? null : _table.UnitAt(mouse);
        if (!overHud) Decisions.TableHover(mouse);
        if (under != null && Session.IsMine(under.PlayerID)) under = null;
        if (!ReferenceEquals(under, _hoverUnit))
        {
            _hoverUnit = under;
            _hoverTime = 0;
            _hoverTip?.QueueFree();
            _hoverTip = null;
        }
        if (_hoverUnit == null) return;
        _table.ShowEnemyNames.Add(_hoverUnit.ID);
        _hoverTime += delta;
        // Kai, 2026-11-10: the enemy's info comes up after the pointer rests on it for a second.
        if (_hoverTip == null && _hoverTime >= 1.0)
        {
            _hoverTip = HudKit.Panel(Tight(10).With(x => { x.Fill = HudKit.Glass with { A = 0.97f }; x.Border = HudKit.Enemy with { A = 0.85f }; x.Glow = HudKit.Enemy with { A = 0.25f }; }),
                UnitInfo(_hoverUnit, 300, "Left-click to pin"));
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
    public void Confirm(string title, string text, string ok, Action yes)
    {
        var d = new ConfirmationDialog { Title = title, DialogText = text, OkButtonText = ok, CancelButtonText = "Cancel" };
        d.Confirmed += () => { yes(); d.QueueFree(); };
        d.Canceled += () => d.QueueFree();
        _hud.AddChild(d);
        d.PopupCentered();
    }

    public void SelectUnit(IUnit unit, bool focus)
    {
        _selected = unit;
        _table.Selected = unit.ID;
        _cardSig = "";
        if (focus && _table.UnitCentre(unit) is { } c) FocusOn(c);
        RefreshHud();
    }

    private void SelectNextReady()
    {
        var mine = MyUnits().Where(u => !UnitActivation.HasActivated(Session.Table.Progress, u)).ToList();
        if (mine.Count == 0) return;
        int i = _selected == null ? -1 : mine.FindIndex(u => u.ID.Equals(_selected.ID));
        SelectUnit(mine[(i + 1) % mine.Count], focus: true);
    }

    private List<IUnit> MyUnits() => Session.Table.Units.Objects.Where(u => Session.IsMine(u.PlayerID)).ToList();

    // ================================================================== HUD build

    private static ChamferBox Tight(float cut = 10) => new()
    {
        Fill = HudKit.Glass, Border = HudKit.Edge, Cut = cut,
        ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
    };

    private const float Pad = 12;

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
        HudKit.Place(_hud, Decisions.BuildChoicePanel(), LayoutPreset.CenterTop, new Vector2(0, 70));

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
        _unitCard = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        block.AddChild(_unitCard);
        block.AddChild(new ColorRect { Color = HudKit.Edge, CustomMinimumSize = new Vector2(1, 0) });
        block.AddChild(Decisions.BuildActionArea());
        _unitBlock = HudKit.Panel(Tight(12).With(x => { x.Accent = HudKit.Lime; x.AccentLength = 40; x.ContentMarginLeft = 10; x.ContentMarginTop = 8; x.ContentMarginBottom = 8; }), block);
        HudKit.Place(_hud, _unitBlock, LayoutPreset.BottomLeft, new Vector2(Pad, -Pad));

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
        head.AddChild(HudKit.Heading("Your army", 13));
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
            else { prompt = "SETTING UP"; hint = "The table is being laid out."; }
        }
        _prompt.Text = prompt.ToUpperInvariant();
        _promptHint.Text = hint;

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

        if (Session.EndedText != null && _gameOver == null) ShowGameOver();
    }

    private static Control Count(int n, Color c, string tip)
    {
        var h = new HBoxContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Stop };
        h.AddThemeConstantOverride("separation", 2);
        h.AddChild(HudKit.Icon("flag-objective", 18, c));
        h.AddChild(HudKit.Text(n.ToString(), 20, c, HudKit.Bold));
        return h;
    }

    private void RefreshArmy(IGameProgress progress)
    {
        List<IUnit> mine = MyUnits();
        var ready = mine.Where(u => !UnitActivation.HasActivated(progress, u) && Alive(u)).ToList();
        var done = mine.Where(u => UnitActivation.HasActivated(progress, u) && Alive(u)).ToList();
        string sig = string.Join("|", mine.Select(u => $"{u.ID.ID}:{Alive(u)}:{UnitActivation.HasActivated(progress, u)}:{LeftOf(u)}:{u.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken)}"))
            + (_selected?.ID.ID.ToString() ?? "") + (progress.ActivatingUnit?.ID.ID.ToString() ?? "");
        if (sig == _armySig) return;
        _armySig = sig;
        _armyHead.Text = $"{ready.Count}/{ready.Count + done.Count} to go - Tab";
        foreach (Node c in _armyRows.GetChildren()) c.QueueFree();
        foreach (IUnit u in ready) _armyRows.AddChild(ArmyRow(u, false));
        if (done.Count > 0)
        {
            var sep = new HBoxContainer();
            sep.AddThemeConstantOverride("separation", 6);
            sep.AddChild(HudKit.Text("ACTIVATED", 11, HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2)));
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
        h.AddChild(HudKit.Portrait(faction, u.Name, HudKit.KindIcon(u.Name, hero, shoots, wounds ? max : 1), 36, selected || active ? HudKit.Lime : _table.SeatColour(u.PlayerID), a));
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
        h.AddChild(v);
        var box = new ChamferBox
        {
            Fill = selected || active ? new Color(HudKit.Lime, 0.13f) : new Color(0, 0, 0, done ? 0.12f : 0.25f),
            Border = selected || active ? HudKit.Lime : shaken ? Palette.WoundsRed with { A = 0.6f } : HudKit.Edge with { A = done ? 0.2f : 0.4f },
            Cut = 6, ContentMarginLeft = 3, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3,
        };
        var row = HudKit.Panel(box, h);
        row.MouseFilter = MouseFilterEnum.Stop;
        row.TooltipText = shaken ? "Shaken" : done ? "Already activated this round" : "Click to select - Tab for the next ready unit";
        row.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { SelectUnit(u, focus: true); Decisions.UnitPicked(u); } };
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
        top.AddChild(HudKit.Portrait(faction, u.Name, HudKit.KindIcon(u.Name, hero, shoots, wounds ? max : 1), 46, team));
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
            var name = HudKit.Text((g.Count() > 1 ? $"{g.Count()}x " : "") + g.Key.Name, 15, HudKit.Fg, HudKit.Semi);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            name.CustomMinimumSize = new Vector2(120, 0);
            string rules = string.Join(", ", g.First().RuleDefinitions.Select(r => r.RequestedName));
            if (rules.Length > 0) { name.TooltipText = rules; name.MouseFilter = MouseFilterEnum.Stop; }
            grid.AddChild(name);
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
            chip.TooltipText = RuleText(rule);
            chip.MouseFilter = MouseFilterEnum.Stop;
            rulesFlow.AddChild(chip);
        }
        col.AddChild(rulesFlow);
        if (hint != null) col.AddChild(HudKit.Text(hint, 12, HudKit.Dim with { A = 0.7f }, HudKit.Medium));
        return col;
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
        if (Session.Log.Version == _logVersion) return;
        _logVersion = Session.Log.Version;
        foreach (Node c in _reportRows.GetChildren()) c.QueueFree();
        foreach (GameLogBuffer.Line line in Session.Log.Last(4))
        {
            var l = HudKit.Text(line.Text, 14, Color.Color8(line.Colour.R, line.Colour.G, line.Colour.B).Lerp(HudKit.Fg, 0.4f), HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(10, 0);
            _reportRows.AddChild(l);
        }
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

    private void OpenFullReport()
    {
        var v = new VBoxContainer();
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(560, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (GameLogBuffer.Line line in Session.Log.All())
        {
            var l = HudKit.Text(line.Text, 15, Color.Color8(line.Colour.R, line.Colour.G, line.Colour.B).Lerp(HudKit.Fg, 0.4f), HudKit.Medium);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(10, 0);
            rows.AddChild(l);
        }
        scroll.AddChild(rows);
        v.AddChild(scroll);
        var panel = new FloatingPanel("Battle report", v, HudKit.Teal);
        _hud.AddChild(panel);
        panel.Position = new Vector2(GetViewportRect().Size.X / 2 - 290, 90);
        CallDeferred(nameof(ScrollToEnd), scroll);
    }

    private static void ScrollToEnd(ScrollContainer s) => s.ScrollVertical = (int)s.GetVScrollBar().MaxValue;

    private void OpenArmies()
    {
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
        var panel = new FloatingPanel("Army lists", scroll, HudKit.Teal);
        _hud.AddChild(panel);
        panel.Position = new Vector2(GetViewportRect().Size.X / 2 - 250, 90);
    }

    private void OpenMenu()
    {
        var confirm = new ConfirmationDialog
        {
            Title = "Menu", DialogText = "Leave this game and go back to the main menu?\n(Saving comes with Load Game.)",
            OkButtonText = "Leave game", CancelButtonText = "Keep playing",
        };
        confirm.Confirmed += App.I.ToMainMenu;
        confirm.Canceled += () => confirm.QueueFree();
        _hud.AddChild(confirm);
        confirm.PopupCentered();
    }

    private void ShowGameOver()
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        v.AddChild(HudKit.Text("GAME OVER", 48, HudKit.Lime, HudKit.Exo(800), HorizontalAlignment.Center));
        v.AddChild(HudKit.Text(Session.EndedText ?? "", 24, HudKit.Fg, HudKit.Semi, HorizontalAlignment.Center));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        var look = Ui.Button("Look at the table", () => { _gameOver?.Hide(); }, "Close this card and look round the final table.");
        var menu = Ui.Button("Main menu", App.I.ToMainMenu, "Back to the main menu.", "CommitButton");
        row.AddChild(look);
        row.AddChild(menu);
        v.AddChild(row);
        _gameOver = HudKit.Panel(new ChamferBox { Fill = HudKit.Glass with { A = 0.96f }, Border = HudKit.Lime, Cut = 16, Glow = HudKit.Lime with { A = 0.35f }, ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 28, ContentMarginBottom = 28 }, v);
        HudKit.Place(_hud, _gameOver, LayoutPreset.Center);
    }
}
