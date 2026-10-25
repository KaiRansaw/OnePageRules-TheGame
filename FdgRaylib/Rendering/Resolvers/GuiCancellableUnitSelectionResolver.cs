using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A <see cref="GuiCancellableSelectionResolver{UnitData}"/> that also lets the player click a valid unit on
/// the table canvas — the cancellable counterpart of <see cref="GuiUnitSelectionResolver"/>. Used for the
/// #100 pre-attack cross-unit targeting picker (grant/mark/debuff a unit). Valid units are ringed; clicking
/// one selects it; the dialog's Back button (Cancel) is unaffected.
/// </summary>
public class GuiCancellableUnitSelectionResolver
    : GuiCancellableSelectionResolver<UnitData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;
    // Protected so GuiChooseMeleeDefenderResolver's confirm-card path (#237) shares the ring
    // drawing and clears the hover ref exactly like the base Draw does.
    protected DataReference? _hoveredValidRef;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        CancellableSelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        _hoveredValidRef = null;
        if (request == null) return null;

        foreach (var opt in request.ValidOptions)
        {
            if (opt.Option.GetValue() == unit)
            {
                _hoveredValidRef = opt.Option.Reference;
                return $"Click to select {opt.Name}";
            }
        }
        foreach (var opt in request.InvalidOptions)
        {
            if (opt.Option.GetValue() == unit) return $"Invalid: {opt.Reason}";
        }
        return null;
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        CancellableSelectionRequest<UnitData>? request;
        TaskCompletionSource<CancellableResult<DataBinding<UnitData>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        foreach (var opt in request.ValidOptions)
        {
            if (opt.Option.GetValue() == unit)
            {
                Complete(tcs, new Selected<DataBinding<UnitData>>(opt.Option));
                return;
            }
        }
    }

    public override void Draw(int screenW, int screenH)
    {
        // Pick by clicking a ringed unit on the table; a small pop-up beside the acting unit says what is
        // being picked and offers Back (right-click on empty table backs out too). No side list.
        DrawTargetRings();
        DrawCompactPicker();
        _hoveredValidRef = null;
    }

    private Vector2 _pickerSize = new(260f, 120f);

    private void DrawCompactPicker()
    {
        CancellableSelectionRequest<UnitData>? request;
        TaskCompletionSource<CancellableResult<DataBinding<UnitData>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        ResolverPanelLayout.BeginDocked("##unitpicker");   // the Actions window (right column)

        if (!string.IsNullOrEmpty(request.DisplayName)) ImGui.TextUnformatted(request.DisplayName);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 300f);
        if (!string.IsNullOrEmpty(request.Instructions)) ImGui.TextDisabled(request.Instructions);
        ImGui.PopTextWrapPos();
        ImGui.Separator();

        bool done = false;
        if (request.ValidOptions.Count == 0)
            ImGui.TextUnformatted("No units in range");
        else
            ImGui.TextUnformatted("Click a highlighted unit.");

        // Units that can't be clicked because nothing of them is on the table (inside a transport).
        foreach (var opt in request.ValidOptions)
        {
            UnitData u = opt.Option.GetValue();
            bool onTable = u.Models.Any(m => m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f));
            if (onTable) continue;
            if (!done && UiButton.Navigate($"{opt.Name}##pick{opt.Option.Reference}", new Vector2(MathF.Max(260f, ImGui.GetContentRegionAvail().X), 0f)))
            {
                Complete(tcs, new Selected<DataBinding<UnitData>>(opt.Option));
                done = true;
            }
        }

        if (!done && (UiButton.Back("Back", new Vector2(120f, 0f)) || PopupAnchor.RightClickOnEmptyTable()))
            Complete(tcs, new Cancelled<DataBinding<UnitData>>());

        _pickerSize = ImGui.GetWindowSize();
        ImGui.End();
    }

    // #248: keyboard highlight rings the unit like a hover. Rings draw before the base dialog each
    // frame, so a highlight set here paints on the next frame — one frame of lag, imperceptible.
    protected override void OnValidOptionHighlighted(CancellableSelectionRequest<UnitData>.ValidOption opt) =>
        _hoveredValidRef = opt.Option.Reference;

    protected void DrawTargetRings()
    {
        CancellableSelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        uint colorValid = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.80f, 1.00f, 0.75f));
        uint colorHover = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 1.00f));

        foreach (var opt in request.ValidOptions)
        {
            bool hovered = _hoveredValidRef != null && opt.Option.Reference.Equals(_hoveredValidRef);
            uint color = hovered ? colorHover : colorValid;
            float thickness = hovered ? 3f : 2f;

            foreach (IModel model in opt.Option.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;
                var (px, py) = InchesToPixel(pos.x, pos.z);
                // Shape-aware ring: matches the true base (rectangle for rectangular bases), inflated 3px.
                ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, new Vector2(px, py), _scale,
                    color, thickness, 3f / _scale, model.Facing);
            }
        }
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
