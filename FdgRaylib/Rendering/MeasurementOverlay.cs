using System;
using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// An ad-hoc ruler: hold Alt and drag on the table to measure inches between two points, snapping each
/// end to the nearest model base. The ruler shows only while Alt or the left button is still held --
/// releasing both makes it vanish. (Was Ctrl until #277 claimed Ctrl+Wheel for the formation cycle --
/// and holding Ctrl raised WantCaptureMouse here, hiding the wheel from the resolvers. Note some Linux
/// window managers grab Alt+drag to move windows; GNOME defaults to Super, so the owner's setup is fine.)
///
/// <para><b>Resolver coexistence (Model A).</b> Measuring is a modifier gesture, not a mode, so it shares
/// no input with resolvers: a plain click still drives whatever resolver is active. The one precedence
/// rule is that while Alt is held over the table this overlay sets <c>io.WantCaptureMouse = true</c>, and
/// every table-click consumer (the hit tester and the canvas resolvers) already skips when that flag is
/// set -- so an Alt-drag can never double as a place/move/select. Because the ruler holds no game state
/// and is tied to no request, a resolver appearing (or the turn changing) mid-measure can't corrupt it,
/// and it can't corrupt the resolver.</para>
///
/// Drawn on the background draw list, so it sits over the table but under ImGui windows and never grabs
/// the mouse itself.
///
/// <para><b>Measuring tool (toggle).</b> A "Measure" button beside the status strip (or the M key) turns on
/// a plain click-and-drag ruler: the press locks the start - snapped to a model's base when it lands on
/// one - and dragging draws a black line to the cursor with the distance from the BASE EDGE to the cursor
/// (the OPR measuring convention), or point-to-point from bare table. The last ruler stays up after release
/// until the next click or the tool is turned off. While on, only the left-button press/drag/release frames
/// are captured from the resolvers, so hover tooltips keep working between measurements.</para>
/// </summary>
public class MeasurementOverlay
{
    private ITableState? _tableState;
    private float _scale;
    private int   _originX, _originY;
    private float _tableH;

    private bool _measuring;
    private bool _hasRuler;
    private MeasurePoint _anchor;
    private MeasurePoint _cursor;

    /// <summary>Whether the click-and-drag measuring tool is on (the "Measure" button / M key).</summary>
    public bool ToolEnabled { get; private set; }

    private bool _toolDragging;
    private bool _toolHasRuler;
    private MeasurePoint _toolStart;   // snapped to a model when the press landed on one
    private Position _toolEnd;          // always the raw cursor point

    // Snap a ruler end to a model whose centre is within (base radius + this) of the cursor, in inches.
    private const float SnapMarginInches = 0.35f;

    private static readonly uint LineColor  = U32(0.60f, 0.90f, 1.00f, 0.95f); // cyan
    private static readonly uint DotColor   = U32(0.85f, 0.96f, 1.00f, 1.00f);
    private static readonly uint TextColor  = U32(0.90f, 0.97f, 1.00f, 1.00f);
    private static readonly uint Shadow     = U32(0f, 0f, 0f, 0.75f);

    // Measuring tool: a black line with a faint light halo so it still reads on the darker table felts.
    private static readonly uint ToolLine   = U32(0f, 0f, 0f, 1f);
    private static readonly uint ToolHalo   = U32(1f, 1f, 1f, 0.45f);
    private static readonly uint ToolPill   = U32(1f, 1f, 1f, 0.90f);
    private static readonly uint ToolText   = U32(0f, 0f, 0f, 1f);

    public void Attach(ITableState tableState) => _tableState = tableState;

    public void Reset()
    {
        _measuring = false;
        _hasRuler  = false;
        ToolEnabled = false;
        ClearTool();
    }

    private void SetToolEnabled(bool enabled)
    {
        ToolEnabled = enabled;
        ClearTool();
    }

    private void ClearTool()
    {
        _toolDragging = false;
        _toolHasRuler = false;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    /// <summary>
    /// Must run BEFORE the hit tester and resolvers each frame, so the WantCaptureMouse override lands
    /// before they read it.
    /// </summary>
    public void Draw(int screenW, int screenH)
    {
        if (_tableState == null) return;

        var io = ImGui.GetIO();
        bool overPanel = io.WantCaptureMouse;          // an ImGui window owns the mouse (measured before our override)
        // Robust modifier read: the legacy io.KeyAlt isn't populated by every backend, so also check the keys.
        bool alt      = io.KeyAlt || ImGui.IsKeyDown(ImGuiKey.LeftAlt) || ImGui.IsKeyDown(ImGuiKey.RightAlt);
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // M toggles the measuring tool (muted while typing or while the in-game menu owns input, #246).
        if (!io.WantCaptureKeyboard && !EscapeRouter.MenuOpen && ImGui.IsKeyPressed(ImGuiKey.M))
            SetToolEnabled(!ToolEnabled);

        // The toggled tool owns plain clicks; holding Alt still gives the original Alt-drag ruler.
        if (ToolEnabled && !alt)
        {
            UpdateTool(io, overPanel, leftDown);
            if (_toolHasRuler) DrawToolRuler();
            return;
        }

        // Begin a measurement on Alt+click over the table.
        if (alt && !overPanel && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _anchor    = Snap(TableInput.MousePos);
            _cursor    = _anchor;
            _measuring = true;
            _hasRuler  = true;
        }
        // Track the moving end while dragging (button held) with Alt.
        else if (_measuring && leftDown && !overPanel)
        {
            _cursor = Snap(TableInput.MousePos);
        }

        // The ruler lives only while a button is still held; the instant BOTH Alt and the left button
        // are released, it disappears (no lingering / no Esc-to-clear).
        if (!alt && !leftDown)
        {
            _measuring = false;
            _hasRuler  = false;
        }

        // Suppress table clicks while the Alt gesture is active over the table.
        if (alt && !overPanel)
            io.WantCaptureMouse = true;

        if (_hasRuler)
            DrawRuler();
    }

    private void UpdateTool(ImGuiIOPtr io, bool overPanel, bool leftDown)
    {
        bool pressed  = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool released = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        if (pressed && !overPanel)
        {
            // The press locks the start: on a model it snaps to that model's base, otherwise the bare point.
            _toolStart    = Snap(TableInput.MousePos);
            _toolEnd      = ToTable(TableInput.MousePos);
            _toolDragging = true;
            _toolHasRuler = true;
        }
        else if (_toolDragging && leftDown)
        {
            _toolEnd = ToTable(TableInput.MousePos);
        }

        if (!leftDown) _toolDragging = false;

        // Keep the press, drag and release away from the resolvers (no accidental moves / selections),
        // but leave plain hovering alone so unit and objective tooltips still work while the tool is on.
        if ((pressed && !overPanel) || _toolDragging || (released && !overPanel))
            io.WantCaptureMouse = true;
    }

    private void DrawToolRuler()
    {
        var dl = ImGui.GetBackgroundDrawList();

        // From a model: start on the base edge nearest the cursor and read the base-edge distance.
        Position start;
        IModel? model = _toolStart.Model;
        if (model != null)
            start = MeasurementGeometry.ClosestPointOnBase(model.BaseShape, model.Position, model.Facing, _toolEnd);
        else
            start = new Position(_toolStart.XIn, 0f, _toolStart.ZIn);

        float dx = _toolEnd.x - start.x;
        float dz = _toolEnd.z - start.z;
        float inches = MathF.Sqrt(dx * dx + dz * dz);

        Vector2 a = ToPixel(start.x, start.z);
        Vector2 b = ToPixel(_toolEnd.x, _toolEnd.z);

        dl.AddLine(a, b, ToolHalo, 6f);
        dl.AddLine(a, b, ToolLine, 3f);
        dl.AddCircleFilled(a, 5f, ToolHalo);
        dl.AddCircleFilled(a, 3.5f, ToolLine);
        dl.AddCircle(b, 6f, ToolHalo, 0, 4f);
        dl.AddCircle(b, 6f, ToolLine, 0, 2f);

        // Distance in a light pill just up-right of the cursor end, where the eye already is.
        string label = $"{inches:0.0}\"";
        Vector2 size = ImGui.CalcTextSize(label);
        var pad = new Vector2(6f, 3f);
        var at  = new Vector2(b.X + 12f, b.Y - size.Y - 14f);
        dl.AddRectFilled(at - pad, at + size + pad, ToolPill, 4f);
        dl.AddRect(at - pad, at + size + pad, ToolLine, 4f);
        dl.AddText(at, ToolText, label);
    }

    /// <summary>
    /// The "Measure" toggle, docked just left of the status strip's round / score text
    /// (<paramref name="stripLeftX"/>, or top-centre when the strip is empty). Call inside the ImGui frame.
    /// </summary>
    /// <returns>The button's left edge (screen x), so neighbours can line up beside it; null when not drawn.</returns>
    /// <summary>The Measure button, drawn inline in the current window (the bottom-left bar).</summary>
    public void DrawInlineToggle()
    {
        if (_tableState == null) return;
        bool on = ToolEnabled;
        if (on)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.ButtonCommit);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.ButtonCommitHover);
        }
        if (ImGui.Button(on ? "Measuring (M)" : "Measure (M)"))
            SetToolEnabled(!ToolEnabled);
        if (on) ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(on
                ? "Measuring tool is ON (M).\nClick and drag on the table to measure.\nStart on a model to measure from its base."
                : "Turn on the measuring tool (M).");
    }

    public float? DrawToggleButton(int? stripLeftX, int areaWidth)
    {
        if (_tableState == null) return null;

        string label = ToolEnabled ? "Measuring" : "Measure";
        var style = ImGui.GetStyle();
        float buttonW = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f;
        float buttonH = ImGui.GetFrameHeight();

        float x = stripLeftX.HasValue
            ? stripLeftX.Value - 16f - buttonW
            : (areaWidth - buttonW) * 0.5f;
        float y = StatusHudOverlay.StripTop + (StatusHudOverlay.StripHeight - buttonH) * 0.5f;

        x = MathF.Max(4f, x);
        ImGui.SetNextWindowPos(new Vector2(x, MathF.Max(2f, y)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##measureTool", flags))
        {
            bool on = ToolEnabled;
            if (on)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.ButtonCommit);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.ButtonCommitHover);
            }
            if (ImGui.Button(label))
                SetToolEnabled(!ToolEnabled);
            if (on) ImGui.PopStyleColor(2);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(on
                    ? "Measuring tool is ON (M).\nClick and drag on the table to measure.\nStart on a model to measure from its base."
                    : "Turn on the measuring tool (M).");
            }
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
        return x;
    }

    private Position ToTable(Vector2 mouse) =>
        new((mouse.X - _originX) / _scale, 0f, _tableH - (mouse.Y - _originY) / _scale);

    private Vector2 ToPixel(float xIn, float zIn) =>
        new(_originX + xIn * _scale, _originY + (_tableH - zIn) * _scale);

    private void DrawRuler()
    {
        var dl = ImGui.GetBackgroundDrawList();
        Vector2 a = ToPixel(_anchor);
        Vector2 b = ToPixel(_cursor);

        // Main line + a small perpendicular tick at each end.
        dl.AddLine(a, b, LineColor, 2f);
        Vector2 dir = b - a;
        float len = dir.Length();
        if (len > 0.001f)
        {
            Vector2 perp = new Vector2(-dir.Y, dir.X) / len * 6f;
            dl.AddLine(a - perp, a + perp, LineColor, 2f);
            dl.AddLine(b - perp, b + perp, LineColor, 2f);
        }
        dl.AddCircleFilled(a, 3.5f, DotColor);
        dl.AddCircleFilled(b, 3.5f, DotColor);

        // Distances: centre-to-centre always; base-to-base too when at least one end snapped to a model.
        float dx = _anchor.XIn - _cursor.XIn;
        float dz = _anchor.ZIn - _cursor.ZIn;
        float centerIn = MathF.Sqrt(dx * dx + dz * dz);

        string label = TryEdgeDistance(out float edge)
            ? $"{edge:0.0}\" edge / {centerIn:0.0}\" ctr"
            : $"{centerIn:0.0}\"";

        Vector2 mid  = (a + b) * 0.5f;
        Vector2 size = ImGui.CalcTextSize(label);
        var at = new Vector2(mid.X - size.X * 0.5f, mid.Y - size.Y - 8f);
        dl.AddText(at + new Vector2(1, 1), Shadow, label);
        dl.AddText(at, TextColor, label);
    }

    // The ruler's base-to-base ("edge") reading — see MeasurementGeometry (#251) for why this measures
    // against true base shapes rather than subtracting scalar radii.
    private bool TryEdgeDistance(out float edgeIn)
    {
        IModel? a = _anchor.Model, b = _cursor.Model;
        float? edge = MeasurementGeometry.EdgeDistanceInches(
            a?.BaseShape, a?.Position ?? new Position(_anchor.XIn, 0f, _anchor.ZIn), a?.Facing ?? default,
            b?.BaseShape, b?.Position ?? new Position(_cursor.XIn, 0f, _cursor.ZIn), b?.Facing ?? default);
        edgeIn = edge ?? 0f;
        return edge.HasValue;
    }

    // Nearest living, placed model whose base (plus a small margin) contains the cursor; else the raw point.
    private MeasurePoint Snap(Vector2 mouse)
    {
        float xIn = (mouse.X - _originX) / _scale;
        float zIn = _tableH - (mouse.Y - _originY) / _scale;
        var cursor = new Position(xIn, 0f, zIn);

        IModel? best = null;
        float bestD = float.MaxValue;
        foreach (IUnit unit in _tableState!.Units.Objects)
        {
            // Off-table units (Ambush reserve, embarked, flown-off Aircraft) park at the origin.
            if (!unit.GetIsOnBattlefield()) continue;

            foreach (IModel m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                Position p = m.Position;
                if (p.x == 0f && p.z == 0f) continue;

                // #251: snap against the true base surface (0 when inside), not a circle of the inscribed
                // radius — that under-snapped every rectangle, making the long ends of a bike or tank base
                // unclickable. Ties break on the smallest surface distance, so an overlapping/nested base
                // still picks the one the cursor is most inside of.
                float d = MeasurementGeometry.SnapDistanceInches(m.BaseShape, p, m.Facing, cursor);
                if (d <= SnapMarginInches && d < bestD)
                {
                    bestD = d;
                    best  = m;
                }
            }
        }

        if (best != null)
        {
            Position c = best.Position;
            return new MeasurePoint { XIn = c.x, ZIn = c.z, Model = best };
        }
        return new MeasurePoint { XIn = xIn, ZIn = zIn, Model = null };
    }

    private Vector2 ToPixel(MeasurePoint p) =>
        new(_originX + p.XIn * _scale, _originY + (_tableH - p.ZIn) * _scale);

    private struct MeasurePoint
    {
        public float XIn, ZIn;

        // #251: the snapped model itself, not a scalar radius — the edge reading measures against its true
        // base shape and facing. Null when this end is a free point (the single "did it snap?" signal;
        // a separate bool would be a second source of truth that could drift).
        public IModel? Model;
    }

    private static uint U32(float r, float g, float b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
