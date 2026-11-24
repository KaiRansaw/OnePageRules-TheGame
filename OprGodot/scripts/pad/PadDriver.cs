using Godot;
using GInput = Godot.Input;

namespace OprGodot.Pads;

/// <summary>A screen whose camera the right stick drives in pointer mode (the battle).</summary>
public interface IPadCamera
{
    /// <summary><paramref name="look"/> turns (x) and tilts (y) the camera, <paramref name="pan"/> slides it
    /// (y up = forward, like W), both -1..1, for <paramref name="dt"/> seconds.</summary>
    void PadCamera(Vector2 look, Vector2 pan, double dt);
}

/// <summary>A screen whose pointer can jump between units, XCOM-style (the battle). Each returns where the pointer
/// should go on screen, or null for nowhere.</summary>
public interface IPadTargets
{
    /// <summary>LB / RB: the previous / next unit the open decision can pick (else the next enemy).</summary>
    Vector2? PadCycle(int dir);
    /// <summary>Y: select the next ready unit of yours.</summary>
    Vector2? PadNextUnit();
    /// <summary>Stick let go here: the centre of a pickable unit under it, to settle on.</summary>
    Vector2? PadSnap(Vector2 at);
    /// <summary>The pointer is over a pickable unit (it slows down there).</summary>
    bool PadOverTarget(Vector2 at);
}

/// <summary>A screen that wants the controller pointer from the start rather than the menu highlight.</summary>
public interface IPadPointerScreen { }

/// <summary>
/// Xbox / PlayStation controller support (Kai, 2026-11-24, for couch play). One node under App that turns pad input
/// into what the game already understands, so no screen needs its own controller code:
/// <list type="bullet">
/// <item><b>Menu mode</b> - the d-pad / left stick move a highlight between buttons and A presses it (Godot's own focus
/// handling); B is Back. Buttons screens made mouse-only are opened up to the highlight while the pad is in use.
/// LB / RB change tabs, Y held shows the highlighted thing's hover text, the right stick and triggers scroll.</item>
/// <item><b>Pointer mode</b> - the left stick moves a pointer that is the mouse: A is the left button (hold to drag), B
/// the right, the triggers the wheel. Hover cards and tooltips work as they do with a mouse. The battle, map editor,
/// Asset Lab and campaign map start in it; the left stick click swaps modes anywhere.</item>
/// <item><b>Battle</b> - on top of the pointer: the right stick turns and tilts the camera, the pointer at a screen
/// edge pans it, and the face buttons / d-pad press the battle's own keys (<see cref="Pad.All"/>).</item>
/// </list>
/// The layout itself is data in <see cref="Pad"/>; this only carries it out.
/// </summary>
public partial class PadDriver : Node
{
    public static PadDriver? I { get; private set; }

    /// <summary>The last pad that did anything (-1 = none yet) - decides Xbox or PlayStation pictures.</summary>
    public static int LastDevice { get; private set; } = -1;

    /// <summary>True while the pad is what the player is using (the last input came from it), false after the mouse
    /// or keyboard is used. The highlight, pointer ring and hints only show while true.</summary>
    public static bool Active { get; private set; }

    /// <summary>Marks the mouse and key events this node makes, so they don't count as the player using the mouse.</summary>
    private const int Synth = 9001;

    private PadOverlay _overlay = null!;
    private Control? _lastTop;
    private bool? _pointerOverride;
    private Vector2 _ptr;
    private bool _ptrPlaced;
    private MouseButtonMask _mask;
    private bool _lt, _rt;
    private int _scrollHeld;
    private double _wheelAcc, _unlockTimer;
    private readonly List<(WeakRef Ref, Control.FocusModeEnum Was)> _unlocked = new();

    /// <summary>Y is held in menu mode: show the highlighted thing's hover text.</summary>
    public bool InfoHeld { get; private set; }

    /// <summary>Pointer mode on the screen showing (the screen's default unless the left stick click swapped it).</summary>
    public bool PointerMode => _pointerOverride ?? DefaultPointer(App.I.TopScreen);

    public bool InBattle => App.I.TopScreen is Battle.BattleScreen;

    /// <summary>The pointer, in the game's 1920x1080-ish canvas coordinates.</summary>
    public Vector2 Pointer => _ptr;

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Pad.InstallMenuActions();
        var layer = new CanvasLayer { Layer = 120 };
        AddChild(layer);
        _overlay = new PadOverlay();
        layer.AddChild(_overlay);
        GInput.JoyConnectionChanged += OnJoyConnection;
        GetViewport().GuiFocusChanged += OnFocusChanged;
        foreach (int d in GInput.GetConnectedJoypads()) GD.Print($"Controller: {GInput.GetJoyName(d)} (device {d}).");
    }

    private void OnJoyConnection(long device, bool connected)
    {
        string name = GInput.GetJoyName((int)device);
        GD.Print($"Controller {(connected ? "connected" : "disconnected")}: {name} (device {device}).");
        if (!Pad.Prefs.Enabled) return;
        if (connected) LastDevice = (int)device;
        _overlay.Toast(connected
            ? $"Controller connected: {(Pad.IsPlayStation((int)device) ? "PlayStation" : "Xbox")} ({name})"
            : "Controller disconnected.");
        if (!connected && GInput.GetConnectedJoypads().Count == 0) SetActive(false);
    }

    /// <summary>The highlight moved: the (hidden) mouse goes with it, so everything a screen shows on hover - the Army
    /// Forge's unit preview, tooltips - shows for the highlighted thing too.</summary>
    private void OnFocusChanged(Control c)
    {
        if (!Active || PointerMode || InfoHeld || !c.IsVisibleInTree()) return;
        HoverAt(c.GetGlobalRect().GetCenter());
    }

    private void HoverAt(Vector2 at)
    {
        GetViewport().WarpMouse(at);
        Vector2 w = ToWindow(at);
        GInput.ParseInputEvent(new InputEventMouseMotion { Position = w, GlobalPosition = w, Relative = Vector2.Zero, Device = Synth });
    }

    /// <summary>The system mouse arrow hides while the pad drives the menu highlight (it would sit on the highlighted
    /// button), and shows otherwise.</summary>
    private void UpdateCursor()
    {
        var want = Active && !PointerMode ? GInput.MouseModeEnum.Hidden : GInput.MouseModeEnum.Visible;
        if (GInput.MouseMode is GInput.MouseModeEnum.Visible or GInput.MouseModeEnum.Hidden && GInput.MouseMode != want) GInput.MouseMode = want;
    }

    private static bool DefaultPointer(Control? top) => top is Battle.BattleScreen or IPadPointerScreen
        or Maps.MapEditorScreen or Lab.AssetLabScreen or Lab.TerrainWorkshopScreen or Campaign.MapCampaignScreen
        or Maps.MatchTerrainScreen or Lab.FacingReviewScreen;

    // ================================================================== input

    public override void _Input(InputEvent e)
    {
        bool pad = e is InputEventJoypadButton or InputEventJoypadMotion;
        if (!Pad.Prefs.Enabled)
        {
            if (pad) GetViewport().SetInputAsHandled();
            return;
        }
        if (!pad)
        {
            // The player went back to the mouse or keyboard: hide the pad's marks and put the menus back as they were.
            if (e.Device != Synth && (e is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true }
                                      || (e is InputEventMouseMotion m && m.Relative.LengthSquared() > 9)))
                SetActive(false);
            return;
        }
        Hud.UiJuice.NoteInput(e);

        if (e is InputEventJoypadMotion jm)
        {
            if (Math.Abs(jm.AxisValue) > Pad.Prefs.Deadzone) { LastDevice = jm.Device; SetActive(true); }
            if (jm.Axis is JoyAxis.TriggerLeft or JoyAxis.TriggerRight) { Trigger(jm.Axis == JoyAxis.TriggerLeft, jm.AxisValue); GetViewport().SetInputAsHandled(); return; }
            // Sticks are read each frame in _Process (the left one moves the highlight or the pointer).
            GetViewport().SetInputAsHandled();
            return;
        }

        var jb = (InputEventJoypadButton)e;
        LastDevice = jb.Device;
        SetActive(true);
        if (Pad.FromJoy(jb.ButtonIndex) is not { } button) { GetViewport().SetInputAsHandled(); return; }
        if (Button(button, jb.Pressed)) GetViewport().SetInputAsHandled();
    }

    /// <summary>The triggers are axes; past half-way they count as pressed.</summary>
    private void Trigger(bool left, float value)
    {
        ref bool held = ref (left ? ref _lt : ref _rt);
        bool now = held ? value > 0.3f : value > 0.5f;
        if (now == held) return;
        held = now;
        Button(left ? PadButton.LT : PadButton.RT, now);
    }

    /// <summary>One button going down or up. True = handled here (Godot's menus don't see it).</summary>
    private bool Button(PadButton b, bool pressed)
    {
        if (!PointerMode)
        {
            // The first press with nothing highlighted only puts the highlight on the screen.
            if (pressed && b == PadButton.A && NeedsFocus())
            {
                FocusDefault();
                return true;
            }
            // The d-pad moves the highlight here rather than through Godot, so it stays inside a popup that is open
            // (Godot's own search would wander to buttons behind it) and leaves a text box like any other row.
            if (Dir(b) is { } dir)
            {
                if (pressed) { Navigate(dir); _repeatDir = dir; _repeatIn = 0.38; }
                else if (_repeatDir == dir) _repeatDir = null;
                return true;
            }
            if (Pad.Find(b, false, false) is not { } bind) return false; // Godot's own: move, press, back
            Do(bind, pressed);
            return true;
        }
        if (Pad.Find(b, true, InBattle) is { } pb) Do(pb, pressed);
        return true; // in pointer mode nothing reaches the menus' highlight
    }

    private void Do(PadBinding bind, bool pressed)
    {
        switch (bind.Act)
        {
            case PadDo.Click: Mouse(MouseButton.Left, pressed); break;
            case PadDo.RightClick: Mouse(MouseButton.Right, pressed); break;
            case PadDo.Key: SendKey(bind.Key, pressed); break;
            case PadDo.TogglePointer: if (pressed) SetPointerMode(!PointerMode); break;
            case PadDo.Info:
                InfoHeld = pressed;
                if (pressed) HoverAt(new Vector2(1, 1));
                else if (GetViewport().GuiGetFocusOwner() is { } f && f.IsVisibleInTree()) HoverAt(f.GetGlobalRect().GetCenter());
                break;
            case PadDo.PrevTab: if (pressed) SwitchTab(-1); break;
            case PadDo.NextTab: if (pressed) SwitchTab(+1); break;
            case PadDo.PrevTarget: if (pressed && App.I.TopScreen is IPadTargets pt) GlideTo(pt.PadCycle(-1)); break;
            case PadDo.NextTarget: if (pressed && App.I.TopScreen is IPadTargets nt) GlideTo(nt.PadCycle(+1)); break;
            case PadDo.NextUnit: if (pressed && App.I.TopScreen is IPadTargets nu) GlideTo(nu.PadNextUnit()); break;
            case PadDo.ScrollUp: _scrollHeld = pressed ? -1 : (_scrollHeld == -1 ? 0 : _scrollHeld); if (pressed) _wheelAcc = 1; break;
            case PadDo.ScrollDown: _scrollHeld = pressed ? 1 : (_scrollHeld == 1 ? 0 : _scrollHeld); if (pressed) _wheelAcc = 1; break;
        }
    }

    private void SetActive(bool on)
    {
        if (Active == on) return;
        Active = on;
        UpdateCursor();
        if (!on)
        {
            InfoHeld = false;
            ReleaseMouse();
            Relock();
        }
        else if (PointerMode) PlacePointer();
    }

    /// <summary>The left stick click: swap between the menu highlight and the pointer on this screen.</summary>
    public void SetPointerMode(bool pointer)
    {
        _pointerOverride = pointer;
        if (pointer)
        {
            Vector2? at = GetViewport().GuiGetFocusOwner() is { } f && f.IsVisibleInTree() ? f.GetGlobalRect().GetCenter() : null;
            PlacePointer(at);
            GetViewport().GuiReleaseFocus();
            _overlay.Toast("Pointer: left stick moves, A clicks. Left stick click for the menu highlight.");
        }
        else
        {
            ReleaseMouse();
            FocusDefault(near: _ptr);
            _overlay.Toast("Menu highlight: d-pad moves, A presses. Left stick click for the pointer.");
        }
    }

    // ================================================================== frame

    public override void _Process(double delta)
    {
        Control? top = App.I?.TopScreen;
        if (!PointerMode && top != null && top == _lastTop && GetViewport().GuiGetFocusOwner() is { } owner && top.IsAncestorOf(owner))
            _lastFocus[top] = WeakRef(owner)!;
        if (top != _lastTop)
        {
            // A new screen: its own default mode, and in menu mode the highlight goes on its first button.
            _lastTop = top;
            _pointerOverride = null;
            ReleaseMouse();
            if (Active && Pad.Prefs.Enabled)
            {
                if (PointerMode) PlacePointer();
                else CallDeferred(MethodName.FocusSoon);
            }
        }
        if (!Pad.Prefs.Enabled) return;
        UpdateCursor();

        Vector2 left = Stick(JoyAxis.LeftX, JoyAxis.LeftY), right = Stick(JoyAxis.RightX, JoyAxis.RightY);
        if (left != Vector2.Zero || right != Vector2.Zero) SetActive(true);
        if (!Active) return;

        if (PointerMode)
        {
            MovePointer(left, delta);
            if (InBattle && top is IPadCamera cam)
            {
                Vector2 look = new(right.X, right.Y * (Pad.Prefs.InvertCameraTilt ? -1 : 1));
                cam.PadCamera(look * Pad.Prefs.CameraSpeed, EdgePan(), delta);
                Wheel(_scrollHeld, delta);
            }
            else Wheel(_scrollHeld + right.Y, delta);
        }
        else
        {
            StickNavigate(left, delta);
            if (_repeatDir is { } held)
            {
                _repeatIn -= delta;
                if (_repeatIn <= 0) { Navigate(held); _repeatIn = 0.11; }
            }
            float scroll = right.Y + _scrollHeld;
            if (Math.Abs(scroll) > 0.01f) ScrollFocused(scroll * 1100f * (float)delta);
            _unlockTimer -= delta;
            if (_unlockTimer <= 0)
            {
                // Rows rebuilt since the last look (a list refilled) get opened up to the highlight too.
                _unlockTimer = 0.5;
                if (ActiveRoot() is { } root) Unlock(root);
            }
        }
    }

    /// <summary>The highlight each open screen last had, so Back lands on the button you left from.</summary>
    private readonly Dictionary<Control, WeakRef> _lastFocus = new();

    private void FocusSoon() => GetTree().CreateTimer(0.05).Timeout += () =>
    {
        foreach (Control gone in _lastFocus.Keys.Where(k => !IsInstanceValid(k)).ToList()) _lastFocus.Remove(gone);
        if (App.I.TopScreen is { } top && _lastFocus.TryGetValue(top, out WeakRef? r)
            && r.GetRef().AsGodotObject() is Control c && IsInstanceValid(c) && c.IsVisibleInTree())
        {
            c.GrabFocus();
            return;
        }
        FocusDefault();
    };

    /// <summary>Both sticks of every connected pad added up (couch play: whoever has a pad can drive), with the deadzone
    /// taken off and the rest rescaled so a light push is slow and a full push is fast.</summary>
    private static Vector2 Stick(JoyAxis x, JoyAxis y)
    {
        Vector2 v = Vector2.Zero;
        foreach (int d in GInput.GetConnectedJoypads()) v += new Vector2(GInput.GetJoyAxis(d, x), GInput.GetJoyAxis(d, y));
        float mag = Math.Min(v.Length(), 1f), dz = Math.Clamp(Pad.Prefs.Deadzone, 0.05f, 0.5f);
        if (mag <= dz) return Vector2.Zero;
        float scaled = (mag - dz) / (1 - dz);
        return v.Normalized() * scaled;
    }

    // ================================================================== pointer

    private Vector2 ViewSize => GetViewport().GetVisibleRect().Size;

    private void PlacePointer(Vector2? at = null)
    {
        _ptr = at ?? (_ptrPlaced ? _ptr : GetViewport().GetMousePosition());
        if (!_ptrPlaced && at == null && (_ptr.X <= 1 && _ptr.Y <= 1)) _ptr = ViewSize / 2;
        _ptrPlaced = true;
        SendMotion(Vector2.Zero);
    }

    private Vector2? _glide;
    private bool _stickWasMoving;

    /// <summary>Sends the pointer smoothly to a spot (a target picked with LB / RB, a unit settled on).</summary>
    private void GlideTo(Vector2? at)
    {
        if (at is not { } p) return;
        Vector2 size = ViewSize;
        _glide = new Vector2(Math.Clamp(p.X, 0, size.X - 1), Math.Clamp(p.Y, 0, size.Y - 1));
    }

    private void MovePointer(Vector2 stick, double delta)
    {
        if (stick != Vector2.Zero) { _glide = null; _stickWasMoving = true; }
        else
        {
            // Let go of the stick over something pickable: settle on its middle, so A takes it.
            if (_stickWasMoving && App.I.TopScreen is IPadTargets snap) GlideTo(snap.PadSnap(_ptr));
            _stickWasMoving = false;
            if (_glide is { } g)
            {
                Vector2 to = _ptr.DistanceTo(g) < 2 ? g : _ptr.Lerp(g, (float)Math.Min(1, delta * 16));
                if (to == g) _glide = null;
                Vector2 r = to - _ptr;
                _ptr = to;
                if (r != Vector2.Zero) SendMotion(r);
            }
            return;
        }
        // A curve: small pushes for fine aim, a full push crosses the screen in about 1.6 s. Slower over a button.
        float curve = Mathf.Pow(stick.Length(), 2.2f);
        bool overButton = GetViewport().GuiGetHoveredControl() is BaseButton or Slider { Editable: true }
            || (App.I.TopScreen is IPadTargets tg && tg.PadOverTarget(_ptr));
        float speed = 1200f * Math.Clamp(Pad.Prefs.PointerSpeed, 0.25f, 3f) * curve * (overButton ? 0.6f : 1f);
        Vector2 moved = stick.Normalized() * speed * (float)delta;
        Vector2 size = ViewSize;
        Vector2 next = new(Math.Clamp(_ptr.X + moved.X, 0, size.X - 1), Math.Clamp(_ptr.Y + moved.Y, 0, size.Y - 1));
        Vector2 rel = next - _ptr;
        _ptr = next;
        if (rel != Vector2.Zero) SendMotion(rel);
    }

    /// <summary>The pointer pressed against a screen edge pans the battle camera that way.</summary>
    private Vector2 EdgePan()
    {
        const float edge = 18f;
        Vector2 size = ViewSize, d = Vector2.Zero;
        if (_ptr.X < edge) d.X = -1; else if (_ptr.X > size.X - 1 - edge) d.X = 1;
        if (_ptr.Y < edge) d.Y = -1; else if (_ptr.Y > size.Y - 1 - edge) d.Y = 1;
        return d;
    }

    private Vector2 ToWindow(Vector2 p) => GetViewport().GetFinalTransform() * p;

    private void SendMotion(Vector2 rel)
    {
        GetViewport().WarpMouse(_ptr);
        Vector2 w = ToWindow(_ptr);
        GInput.ParseInputEvent(new InputEventMouseMotion { Position = w, GlobalPosition = w, Relative = rel, ButtonMask = _mask, Device = Synth });
    }

    private void Mouse(MouseButton button, bool pressed)
    {
        MouseButtonMask bit = button == MouseButton.Left ? MouseButtonMask.Left : button == MouseButton.Right ? MouseButtonMask.Right : 0;
        if (pressed == ((_mask & bit) != 0)) return;
        _mask = pressed ? _mask | bit : _mask & ~bit;
        Vector2 w = ToWindow(_ptr);
        GInput.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = w, GlobalPosition = w, ButtonMask = _mask, Device = Synth });
    }

    /// <summary>Lets go of a held pointer button (screen changed, mode swapped, back on the mouse) so nothing sticks down.</summary>
    private void ReleaseMouse()
    {
        if ((_mask & MouseButtonMask.Left) != 0) Mouse(MouseButton.Left, false);
        if ((_mask & MouseButtonMask.Right) != 0) Mouse(MouseButton.Right, false);
    }

    /// <summary>Wheel clicks at the pointer, about 12 a second at full push (<paramref name="rate"/> &lt; 0 = up).</summary>
    private void Wheel(float rate, double delta)
    {
        if (Math.Abs(rate) < 0.05f) { _wheelAcc = 0; return; }
        _wheelAcc += Math.Abs(rate) * delta * 12;
        while (_wheelAcc >= 1)
        {
            _wheelAcc -= 1;
            Vector2 w = ToWindow(_ptr);
            MouseButton b = rate < 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            GInput.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = true, Factor = 1, Position = w, GlobalPosition = w, ButtonMask = _mask, Device = Synth });
            GInput.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = false, Factor = 1, Position = w, GlobalPosition = w, ButtonMask = _mask, Device = Synth });
        }
    }

    private static void SendKey(Key key, bool pressed)
    {
        GInput.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Device = Synth });
    }

    // ================================================================== menu highlight

    /// <summary>Nothing is highlighted, or what is can't be seen (its screen or tab closed).</summary>
    private bool NeedsFocus()
    {
        Control? f = GetViewport().GuiGetFocusOwner();
        if (f == null || !f.IsVisibleInTree()) return true;
        Node? root = ActiveRoot();
        return root != null && !root.IsAncestorOf(f);
    }

    /// <summary>What the highlight should stay inside: an open popup window, else a popup laid over the screen
    /// (the Join popup), else the screen itself.</summary>
    private Node? ActiveRoot()
    {
        foreach (Node n in Enumerable.Reverse(GetTree().Root.GetChildren()))
            if (n is Window { Visible: true } w && HasFocusable(w)) return w;
        Control? top = App.I.TopScreen;
        if (top == null) return null;
        float screenArea = ViewSize.X * ViewSize.Y;
        foreach (Control c in TopLevelPieces(top).Reverse())
        {
            if (!c.IsVisibleInTree()) continue;
            Vector2 s = c.GetGlobalRect().Size;
            if (s.X * s.Y >= screenArea * 0.1f && HasFocusable(c)) return c;
        }
        return top;
    }

    private static IEnumerable<Control> TopLevelPieces(Control top)
    {
        foreach (Node n in top.GetChildren())
        {
            if (n is Control c) yield return c;
            else if (n is CanvasLayer { Visible: true } layer)
                foreach (Node ln in layer.GetChildren()) if (ln is Control lc) yield return lc;
        }
    }

    private static bool CanFocus(Control c) =>
        c.IsVisibleInTree() && c is not ScrollBar && (c.FocusMode != Control.FocusModeEnum.None) && c is not BaseButton { Disabled: true }
        && c is BaseButton or Slider or SpinBox or LineEdit or TextEdit or ItemList or Tree or TabBar;

    private static bool HasFocusable(Node n)
    {
        if (n is Control c && (CanFocus(c) || (c.IsVisibleInTree() && Opens(c)))) return true;
        foreach (Node child in n.GetChildren()) if (HasFocusable(child)) return true;
        return false;
    }

    /// <summary>Controls the highlight can reach once opened up (they were made mouse-only).</summary>
    private static bool Opens(Control c) => c is BaseButton { Disabled: false } or Slider { Editable: true } or TabBar or ItemList;

    /// <summary>Puts the highlight on the screen's first button - one marked <c>pad_first</c>, else the top-left one (or
    /// the one nearest <paramref name="near"/>).</summary>
    public bool FocusDefault(Vector2? near = null, Node? within = null)
    {
        if (!Pad.Prefs.Enabled || (within ?? ActiveRoot()) is not { } root) return false;
        Unlock(root);
        List<Control> all = Focusables(root).ToList();
        if (all.Count == 0) return false;
        // The screen's content first: Back and the tab strip are one press away, not where a screen should open.
        List<Control> content = all.Where(c => c is not TabBar && !c.Name.ToString().Equals("Back", StringComparison.OrdinalIgnoreCase)).ToList();
        if (content.Count > 0 && near == null) all = content;
        Control pick = all.FirstOrDefault(c => c.HasMeta("pad_first"))
            ?? (near is { } p ? all.MinBy(c => c.GetGlobalRect().GetCenter().DistanceSquaredTo(p))!
                : all.MinBy(c => (Mathf.Round(c.GetGlobalRect().Position.Y / 40f), c.GetGlobalRect().Position.X))!);
        pick.GrabFocus();
        EnsureVisible(pick);
        return true;
    }

    private Vector2? _repeatDir;
    private double _repeatIn;
    private Vector2 _stickDir;

    private static Vector2? Dir(PadButton b) => b switch
    {
        PadButton.DUp => Vector2.Up, PadButton.DDown => Vector2.Down, PadButton.DLeft => Vector2.Left, PadButton.DRight => Vector2.Right, _ => null,
    };

    /// <summary>The left stick in menu mode: one step when pushed past half-way, then repeating while held.</summary>
    private void StickNavigate(Vector2 stick, double delta)
    {
        Vector2 dir = stick.Length() < 0.55f ? Vector2.Zero
            : Math.Abs(stick.X) > Math.Abs(stick.Y) ? new Vector2(Math.Sign(stick.X), 0) : new Vector2(0, Math.Sign(stick.Y));
        if (dir == Vector2.Zero) { _stickDir = Vector2.Zero; return; }
        if (dir != _stickDir) { _stickDir = dir; _stickIn = 0.38; Navigate(dir); return; }
        _stickIn -= delta;
        if (_stickIn <= 0) { Navigate(dir); _stickIn = 0.11; }
    }

    private double _stickIn;

    /// <summary>Moves the highlight to the nearest control that way, among those on the popup or screen in front.
    /// Nothing highlighted yet: highlights the screen's first button instead.</summary>
    private void Navigate(Vector2 dir)
    {
        if (NeedsFocus()) { FocusDefault(); return; }
        if (ActiveRoot() is not { } root || GetViewport().GuiGetFocusOwner() is not { } from) return;
        Unlock(root);
        Rect2 a = from.GetGlobalRect();
        Vector2 ac = a.GetCenter();
        Control? best = null;
        float bestScore = float.MaxValue;
        foreach (Control c in Focusables(root))
        {
            if (c == from || c.IsAncestorOf(from) || from.IsAncestorOf(c)) continue;
            Rect2 r = c.GetGlobalRect();
            Vector2 rc = r.GetCenter();
            // Must lie that way: its centre past this one's centre, and not overlapping back the other way much.
            float along = (rc - ac).Dot(dir);
            if (along <= 4) continue;
            float edgeGap = dir.X != 0
                ? (dir.X > 0 ? r.Position.X - a.End.X : a.Position.X - r.End.X)
                : (dir.Y > 0 ? r.Position.Y - a.End.Y : a.Position.Y - r.End.Y);
            if (edgeGap < -Math.Min(dir.X != 0 ? a.Size.X : a.Size.Y, dir.X != 0 ? r.Size.X : r.Size.Y) * 0.5f) continue;
            // Sideways: zero while the two overlap across the move, else the gap between them.
            float side = dir.X != 0
                ? Math.Max(0, Math.Max(r.Position.Y - a.End.Y, a.Position.Y - r.End.Y))
                : Math.Max(0, Math.Max(r.Position.X - a.End.X, a.Position.X - r.End.X));
            float score = Math.Max(edgeGap, 0) + side * 3f + Math.Abs((rc - ac).Dot(new Vector2(dir.Y, dir.X))) * 0.15f;
            if (score < bestScore) { bestScore = score; best = c; }
        }
        if (best == null) return;
        best.GrabFocus();
        EnsureVisible(best);
    }

    /// <summary>Scrolls every list the control sits in so it can be seen (screens' own scroll boxes don't always follow).</summary>
    private static void EnsureVisible(Control c)
    {
        for (Node? n = c.GetParent(); n != null; n = n.GetParent())
            if (n is ScrollContainer sc) sc.EnsureControlVisible(c);
    }

    private IEnumerable<Control> Focusables(Node root)
    {
        var all = new List<Control>();
        void Collect(Node n)
        {
            if (n is Control c)
            {
                if (!c.IsVisibleInTree()) return;
                if (CanFocus(c)) all.Add(c);
            }
            foreach (Node child in n.GetChildren()) Collect(child);
        }
        Collect(root);
        return all;
    }

    /// <summary>Opens mouse-only controls under <paramref name="root"/> to the highlight while the pad is in use, and has
    /// scroll boxes follow it. <see cref="Relock"/> puts them back once the mouse is used again.</summary>
    private void Unlock(Node root)
    {
        void Walk(Node n)
        {
            if (n is Control c)
            {
                if (!c.IsVisibleInTree()) return;
                if (c.FocusMode == Control.FocusModeEnum.None && Opens(c))
                {
                    _unlocked.Add((WeakRef(c)!, c.FocusMode));
                    c.FocusMode = Control.FocusModeEnum.All;
                }
                if (c is ScrollContainer sc) sc.FollowFocus = true;
            }
            foreach (Node child in n.GetChildren()) Walk(child);
        }
        Walk(root);
    }

    private void Relock()
    {
        foreach (var (r, was) in _unlocked)
            if (r.GetRef().AsGodotObject() is Control c && IsInstanceValid(c))
            {
                if (c.HasFocus()) c.ReleaseFocus();
                c.FocusMode = was;
            }
        _unlocked.Clear();
    }

    /// <summary>LB / RB: the tab strip the highlight (or pointer) is in, else the screen's main one.</summary>
    private void SwitchTab(int dir)
    {
        Node? from = PointerMode ? GetViewport().GuiGetHoveredControl() : GetViewport().GuiGetFocusOwner();
        TabContainer? tabs = null;
        for (Node? n = from; n != null && tabs == null; n = n.GetParent()) tabs = n as TabContainer;
        if (tabs == null && ActiveRoot() is { } root)
            tabs = Descendants<TabContainer>(root).Where(t => t.IsVisibleInTree()).MaxBy(t => t.Size.X * t.Size.Y);
        if (tabs == null || tabs.GetTabCount() < 2) return;
        int count = tabs.GetTabCount(), at = tabs.CurrentTab;
        for (int i = 0; i < count; i++)
        {
            at = (at + dir + count) % count;
            if (!tabs.IsTabDisabled(at) && !tabs.IsTabHidden(at)) break;
        }
        tabs.CurrentTab = at;
        Hud.UiSound.Click();
        if (!PointerMode && tabs.GetCurrentTabControl() is { } page)
            GetTree().CreateTimer(0.03).Timeout += () =>
            {
                Control? f = GetViewport().GuiGetFocusOwner();
                if (f == null || !f.IsVisibleInTree() || f is TabBar) FocusDefault(within: page);
            };
    }

    /// <summary>The right stick / triggers in menu mode: scroll the list the highlight is in, else the screen's biggest.</summary>
    private void ScrollFocused(float dy)
    {
        ScrollContainer? sc = null;
        for (Node? n = GetViewport().GuiGetFocusOwner(); n != null && sc == null; n = n.GetParent())
            if (n is ScrollContainer s && s.GetVScrollBar().MaxValue > s.Size.Y) sc = s;
        if (sc == null && ActiveRoot() is { } root)
            sc = Descendants<ScrollContainer>(root).Where(s => s.IsVisibleInTree() && s.GetVScrollBar().MaxValue > s.Size.Y).MaxBy(s => s.Size.X * s.Size.Y);
        if (sc == null) return;
        _scrollCarry += dy;
        int whole = (int)_scrollCarry;
        _scrollCarry -= whole;
        sc.ScrollVertical += whole;
    }

    private float _scrollCarry;

    private static IEnumerable<T> Descendants<T>(Node n) where T : Node
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (T d in Descendants<T>(c)) yield return d;
        }
    }

    /// <summary>The highlighted thing's hover text, for Y held (its own, else the nearest row's around it).</summary>
    public string? FocusedInfo(out Rect2 where)
    {
        where = default;
        Control? f = GetViewport().GuiGetFocusOwner();
        if (f == null || !f.IsVisibleInTree()) return null;
        where = f.GetGlobalRect();
        for (Node? n = f; n is Control c; n = n.GetParent())
        {
            string tip = c.GetTooltip(c.Size / 2);
            if (!string.IsNullOrWhiteSpace(tip)) return tip;
        }
        return null;
    }

    // ================================================================== test driver

    /// <summary>ScriptedRun "pad:A" / "pad:A+" (hold) / "pad:A-" (let go): sent as a real pad event, so the test goes
    /// through exactly the path a controller does.</summary>
    public void TestButton(PadButton b, bool? hold)
    {
        void Send(bool pressed)
        {
            InputEvent e = b switch
            {
                PadButton.LT => new InputEventJoypadMotion { Axis = JoyAxis.TriggerLeft, AxisValue = pressed ? 1 : 0 },
                PadButton.RT => new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = pressed ? 1 : 0 },
                _ => new InputEventJoypadButton { ButtonIndex = ToJoy(b), Pressed = pressed, Pressure = pressed ? 1 : 0 },
            };
            e.Device = 0;
            GInput.ParseInputEvent(e);
        }
        if (hold != false) Send(true);
        if (hold != true) Send(false);
        GetTree().CreateTimer(0.15).Timeout += () => GD.Print($"Pad test {b}: focus = {GetViewport().GuiGetFocusOwner()?.Name ?? "none"}, pointer mode = {PointerMode}");
    }

    private static JoyButton ToJoy(PadButton b) => b switch
    {
        PadButton.A => JoyButton.A, PadButton.B => JoyButton.B, PadButton.X => JoyButton.X, PadButton.Y => JoyButton.Y,
        PadButton.LB => JoyButton.LeftShoulder, PadButton.RB => JoyButton.RightShoulder,
        PadButton.LStick => JoyButton.LeftStick, PadButton.RStick => JoyButton.RightStick,
        PadButton.Back => JoyButton.Back, PadButton.Start => JoyButton.Start,
        PadButton.DUp => JoyButton.DpadUp, PadButton.DDown => JoyButton.DpadDown,
        PadButton.DLeft => JoyButton.DpadLeft, _ => JoyButton.DpadRight,
    };

    /// <summary>ScriptedRun "padptr:x;y": put the pointer there (canvas coordinates).</summary>
    public void TestPointer(Vector2 at)
    {
        SetActive(true);
        Vector2 rel = at - _ptr;
        _ptr = at;
        _ptrPlaced = true;
        SendMotion(rel);
    }
}
