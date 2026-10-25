using System.Numerics;
using System.Text;
using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiAssignWoundsResolver
    : IStageResolver<AssignWoundsRequest, AssignWoundsResults>, IGuiResolver,
      IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private readonly object _lock = new();
    private AssignWoundsRequest? _request;
    private AssignWoundsResults? _results;
    private TaskCompletionSource<AssignWoundsResults>? _tcs;

    // Layout — main-thread only (UpdateLayout + Draw both run on the main thread).
    private float _scale = 10f;
    private float _tableH;
    private int   _originX, _originY;

    // The model whose wound-button is hovered this frame; highlighted on the table canvas.
    // Main-thread only: reset at the top of Draw, set while iterating the buttons.
    private IModel? _hoveredModel;

    // #286: the model hovered on the TABLE this frame, set by GetHoverLabel — which TableTooltipOverlay
    // calls before this resolver's own Draw (RaylibRenderer draws the tooltip overlay first), so the value
    // is fresh by the time the rows are laid out. Cleared at the end of Draw so it lasts a single frame,
    // the same one-frame handshake GuiChooseRangedAttackResolver uses for its canvas hover.
    private IModel? _canvasHoveredModel;

    /// <summary>The model the cursor is over on the TABLE this frame, or null. Internal for tests: the
    /// ring and row highlights it drives are ImGui draw calls (hand-verified), but "did the canvas hover
    /// register at all" is the seam that was missing before #286.</summary>
    internal IModel? CanvasHoveredModel => _canvasHoveredModel;

    private static readonly uint HighlightCol      = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.95f));
    private static readonly uint HighlightHaloCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.45f));
    // #286: the dialog row belonging to the model under the cursor on the table — same yellow as the ring
    // so the two ends of the connection read as one highlight.
    private static readonly uint RowHighlightCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.90f));
    private static readonly uint RowHighlightBgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.13f));
    private static readonly uint WeaponTextCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.78f, 0.85f, 1f));
    private static readonly uint InvalidFillCol    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.12f, 0.62f));
    private static readonly uint InvalidOutlineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.60f, 0.80f));
    // #401: the clump strip - placed (green), next (amber, ringed in the highlight yellow), pending
    // (grey) - plus the explanation line and the per-row "takes N" preview.
    private static readonly uint ChipPlacedCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.40f, 0.24f, 0.95f));
    private static readonly uint ChipPlacedTextCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.80f, 0.95f, 0.82f, 1f));
    private static readonly uint ChipNextCol       = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.40f, 0.12f, 0.95f));
    private static readonly uint ChipPendingCol    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.28f, 0.28f, 0.34f, 0.95f));
    private static readonly uint ExplanationCol    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.80f, 0.60f, 1f));
    private static readonly uint EffectCol         = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.90f, 0.55f, 1f));

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
    {
        var tcs     = new TaskCompletionSource<AssignWoundsResults>();
        var results = new AssignWoundsResults(request.UnitReceivingWounds, request.Packets);
        lock (_lock) { _tcs = tcs; _request = request; _results = results; }
        _active = this;
        return tcs.Task;
    }

    /// <summary>Where the wound window sits: the bottom of the right column (set by the renderer every frame).</summary>
    public static Vector4 Dock = new(0f, 0f, 380f, 300f);

    private static GuiAssignWoundsResolver? _active;

    /// <summary>True while a unit on this computer has wounds waiting to be placed (the renderer makes room).</summary>
    public static bool AnyPending => _active?.HasPendingRequest == true;

    /// <summary>Forget a request left over from a previous game.</summary>
    public static void ClearActive() => _active = null;

    private static uint C(float r, float g, float b, float a = 1f) => ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

    public void Draw(int screenW, int screenH)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        TaskCompletionSource<AssignWoundsResults>? tcs;
        lock (_lock) { request = _request; results = _results; tcs = _tcs; }
        if (request == null || results == null || tcs == null) return;

        // #286: the canvas hover is the frame's starting emphasis; a hovered tile overrides it below.
        _hoveredModel = _canvasHoveredModel;

        float dx = Dock.X, dy = Dock.Y, dw = Dock.Z, dh = Dock.W;
        ImGui.SetNextWindowPos(new Vector2(dx, dy), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(dw, dh), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.07f, 0.08f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.85f, 0.30f, 0.25f, 0.9f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2f);
        ImGui.Begin("##WoundsDialog", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        var dl = ImGui.GetWindowDrawList();
        IUnit unit = request.UnitReceivingWounds.GetValue();

        // ── Header: picture, "Assign wounds", the unit, how many are left ──
        const float thumb = 46f;
        Vector2 h0 = ImGui.GetCursorScreenPos();
        Texture2D? tex = PopupAnchor.CardImageFor?.Invoke(unit);
        if (tex is Texture2D t && t.Width > 0 && t.Height > 0)
        {
            float aspect = t.Width / (float)t.Height;
            Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
            if (aspect > 1f) { float w = 1f / aspect; uv0.X = (1f - w) / 2f; uv1.X = uv0.X + w; }
            else { float hh = aspect; uv0.Y = (1f - hh) / 2f; uv1.Y = uv0.Y + hh; }
            dl.AddImageRounded((IntPtr)t.Id, h0, h0 + new Vector2(thumb, thumb), uv0, uv1, C(1f, 1f, 1f), 4f);
        }
        else
        {
            dl.AddRectFilledMultiColor(h0, h0 + new Vector2(thumb, thumb), C(0.40f, 0.12f, 0.10f), C(0.40f, 0.12f, 0.10f),
                C(0.15f, 0.05f, 0.05f), C(0.15f, 0.05f, 0.05f));
        }
        ImGui.SetCursorScreenPos(h0 + new Vector2(thumb + 8f, 0f));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + dw - thumb - 30f);
        ImGui.TextColored(new Vector4(1f, 0.55f, 0.45f, 1f), "ASSIGN WOUNDS");
        ImGui.TextUnformatted(unit.Name);
        ImGui.TextDisabled(WoundAssignmentText.Progress(results));
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        float headBottom = MathF.Max(h0.Y + thumb, ImGui.GetItemRectMax().Y);
        ImGui.SetCursorScreenPos(new Vector2(h0.X, headBottom + 4f));

        // #401: a Deadly queue explains why it is in clump mode and shows every clump as a chip.
        string explanation = WoundAssignmentText.Explanation(results);
        if (explanation.Length > 0)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.PushStyleColor(ImGuiCol.Text, ExplanationCol);
            ImGui.TextUnformatted(explanation);
            ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            DrawClumpStrip(results, ImGui.GetCursorPosX(), ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight());
        }

        // ── Model tiles: click one to give it the next wound ──
        const float footH = 34f;
        float listH = MathF.Max(60f, ImGui.GetContentRegionAvail().Y - footH - 6f);
        ImGui.BeginChild("##WoundsList", new Vector2(0f, listH), ImGuiChildFlags.None);
        var tdl = ImGui.GetWindowDrawList();
        var models = results.PendingWounds;
        float availW = ImGui.GetContentRegionAvail().X;
        const float tileW = 104f, gap = 6f;
        int perRow = Math.Max(1, (int)((availW + gap) / (tileW + gap)));
        float lineH = ImGui.GetTextLineHeight();
        float small = MathF.Round(ImGui.GetFontSize() * 0.78f);
        var font = ImGui.GetFont();
        bool clump = WoundAssignmentText.ClumpModeMatters(results);
        uint white = C(0.95f, 0.95f, 0.93f), dimText = C(0.55f, 0.55f, 0.58f);

        for (int i = 0; i < models.Count; i++)
        {
            var pw = models[i];
            var md = pw.Model.GetValue();
            float total = md.TotalWounds, dealt = md.WoundsDealt;
            bool canTake = results.CanAssignWoundTo(pw);
            List<string> weapons = WeaponLines(md);
            int wLines = Math.Min(3, weapons.Count);
            float tileH = 18f + 12f + 6f + wLines * (small + 1f) + (clump ? lineH : 0f) + 8f;

            if (i % perRow != 0) ImGui.SameLine(0f, gap);
            Vector2 p0 = ImGui.GetCursorScreenPos();
            float rowY = ImGui.GetCursorPosY();
            if (ReferenceEquals(_canvasHoveredModel, md)) ScrollRowIntoView(rowY, tileH);

            ImGui.BeginDisabled(!canTake);
            bool clicked = ImGui.InvisibleButton($"##model{i}", new Vector2(tileW, tileH));
            ImGui.EndDisabled();
            bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            if (hovered) _hoveredModel = md;
            bool lit = hovered || ReferenceEquals(_canvasHoveredModel, md);
            Vector2 p1 = p0 + new Vector2(tileW, tileH);

            // Body, then the top tab with the model's number.
            tdl.AddRectFilled(p0, p1, canTake ? C(0.11f, 0.10f, 0.09f) : C(0.08f, 0.08f, 0.08f), 4f);
            Vector2 t0 = p0 + new Vector2(8f, 0f), t1 = new(p1.X - 8f, p0.Y + 18f);
            tdl.AddRectFilled(t0, t1, canTake ? C(0.42f, 0.12f, 0.10f) : C(0.22f, 0.22f, 0.24f), 6f, ImDrawFlags.RoundCornersTop);
            string label = $"Model {i + 1}";
            Vector2 ls = ImGui.CalcTextSize(label);
            tdl.AddText(new Vector2(p0.X + (tileW - ls.X) / 2f, p0.Y + (18f - ls.Y) / 2f), canTake ? white : dimText, label);

            // Wound pips: lost (dark), about to be taken (orange), still healthy (green).
            int pips = Math.Max(1, (int)MathF.Ceiling(total));
            float pipW = MathF.Min(12f, (tileW - 12f - (pips - 1) * 2f) / pips);
            float px = p0.X + (tileW - (pips * pipW + (pips - 1) * 2f)) / 2f, py = p0.Y + 22f;
            for (int k = 0; k < pips; k++)
            {
                uint pc = k < dealt ? C(0.25f, 0.08f, 0.08f)
                    : k < dealt + pw.Wounds ? C(1f, 0.55f, 0.15f)
                    : C(0.35f, 0.80f, 0.30f);
                tdl.AddRectFilled(new Vector2(px, py), new Vector2(px + pipW, py + 10f), pc, 2f);
                px += pipW + 2f;
            }

            // Weapons, small.
            float wy = py + 16f;
            for (int k = 0; k < wLines; k++)
            {
                string line = weapons[k];
                float shrink = small / ImGui.GetFontSize();
                while (line.Length > 4 && ImGui.CalcTextSize(line).X * shrink > tileW - 10f)
                    line = line[..^3] + "..";
                tdl.AddText(font, small, new Vector2(p0.X + 5f, wy), canTake ? WeaponTextCol : dimText, line);
                wy += small + 1f;
            }
            if (weapons.Count > wLines)
                tdl.AddText(font, small, new Vector2(p1.X - 16f, wy - small - 1f), dimText, "+");

            // #401: what the next clump would do to this model.
            if (clump)
            {
                string effect = WoundAssignmentText.ModelEffect(results, pw);
                if (effect.Length > 0) tdl.AddText(font, small, new Vector2(p0.X + 5f, p1.Y - small - 5f), EffectCol, effect);
            }

            // Frame: yellow under the cursor, green when it can take the wound, grey when it can't.
            if (lit) tdl.AddRect(p0, p1, HighlightCol, 4f, ImDrawFlags.None, 2.5f);
            else if (canTake) tdl.AddRect(p0, p1, C(0.30f, 0.85f, 0.35f, 0.85f), 4f, ImDrawFlags.None, 1.5f);
            else tdl.AddRect(p0, p1, C(0.30f, 0.30f, 0.33f), 4f, ImDrawFlags.None, 1f);

            if (hovered)
            {
                ImGui.BeginTooltip();
                foreach (string line in weapons) ImGui.TextUnformatted(line);
                if (total > 1f) ImGui.TextDisabled($"Wounds left: {WoundFormat.Fraction(total - dealt - pw.Wounds, total)}");
                if (!canTake) ImGui.TextDisabled("Can't take this wound yet.");
                ImGui.EndTooltip();
            }

            if (clicked)
            {
                results.TryAddWounds(pw.Model);
                if (results.IsFinishedAssigning) Complete(tcs, results);
            }
        }
        ImGui.EndChild();

        // ── Footer ──
        if (ResolverButtons.Primary("Auto-assign the rest", new Vector2(ImGui.GetContentRegionAvail().X, footH - 4f)))
        {
            results.AutoFill();
            Complete(tcs, results);
        }
        ImGui.End();

        DrawInvalidTargets(results);
        DrawMapHighlight();

        // Single-frame handshake: GetHoverLabel sets this before the next Draw if the mouse is still over a model.
        _canvasHoveredModel = null;
    }

    /// <summary>#401: one chip per packet - placed (green), next (amber with the highlight ring), pending
    /// (grey) - wrapping into as many rows as the panel width needs, each with a hover tooltip saying what
    /// was rolled, what Regeneration ignored and where it went. Draw-list chips over invisible buttons, so
    /// they read as status rather than as something to click.</summary>
    private static void DrawClumpStrip(AssignWoundsResults results, float pad, float availW, float lineH)
    {
        var dl = ImGui.GetWindowDrawList();
        const float spacing = 6f;
        float chipH = lineH + 6f;
        float leftScreen = ImGui.GetWindowPos().X + pad;
        float rightEdge = leftScreen + availW;
        int RowOf(PacketCommit commit) => results.PendingWounds.FindIndex(e => e.Model == commit.Model) + 1;

        ImGui.SetCursorPosX(pad);
        for (int i = 0; i < results.Packets.Count; i++)
        {
            string label = WoundAssignmentText.ChipLabel(results, i, RowOf);
            float chipW = ImGui.CalcTextSize(label).X + 14f;
            if (i > 0)
            {
                if (ImGui.GetItemRectMax().X + spacing + chipW <= rightEdge) ImGui.SameLine(0f, spacing);
                else ImGui.SetCursorPosX(pad);
            }

            ImGui.InvisibleButton($"##clump{i}", new Vector2(chipW, chipH));
            Vector2 min = ImGui.GetItemRectMin();
            Vector2 max = ImGui.GetItemRectMax();
            bool placed = i < results.PacketsCommitted;
            bool next = i == results.PacketsCommitted;
            dl.AddRectFilled(min, max, placed ? ChipPlacedCol : next ? ChipNextCol : ChipPendingCol, 4f);
            if (next) dl.AddRect(min, max, HighlightCol, 4f, ImDrawFlags.None, 2f);
            dl.AddText(new Vector2(min.X + 7f, min.Y + 3f),
                placed ? ChipPlacedTextCol : ImGui.GetColorU32(ImGuiCol.Text), label);

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
                ImGui.TextUnformatted(WoundAssignmentText.ChipTooltip(results, i, RowOf));
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }
    }

    /// <summary>Scrolls the model list so a row at <paramref name="rowY"/> (content coordinates) is fully
    /// visible, and does nothing when it already is. Mirrors <see cref="GuiSelectionResolver{T}"/>'s
    /// keyboard-navigation scroll.</summary>
    private static void ScrollRowIntoView(float rowY, float rowHeight)
    {
        float scrollY = ImGui.GetScrollY();
        float viewH   = ImGui.GetWindowHeight();
        if (rowY < scrollY)
            ImGui.SetScrollY(MathF.Max(0f, rowY));
        else if (rowY + rowHeight > scrollY + viewH)
            ImGui.SetScrollY(rowY + rowHeight - viewH);
    }

    /// <summary>Dims every model on the table that can't be assigned a wound right now — those already
    /// killed in this pending assignment (incl. locked Tough pre-assignments) AND a joined hero while the
    /// rest of the unit still has room (#006, wounds-last) — so the player can see at a glance which figures
    /// are valid targets, mirroring the disabled dialog buttons.</summary>
    private void DrawInvalidTargets(AssignWoundsResults results)
    {
        var bg = ImGui.GetBackgroundDrawList();
        foreach (PendingWounds pw in results.PendingWounds)
        {
            if (results.CanAssignWoundTo(pw)) continue; // still a valid target

            var md = pw.Model.GetValue();
            var p = md.Position;
            if (p.x == 0f && p.z == 0f) continue;

            var (px, py) = InchesToPixel(p.x, p.z);
            // Shape-aware dim overlay: matches the true base (rectangle for rectangular bases).
            ModelBaseRenderer.DrawFilledImGui(bg, md.BaseShape, new Vector2(px, py), _scale,
                InvalidFillCol, InvalidOutlineCol, 1.5f, md.Facing);
        }
    }

    /// <summary>Rings the hovered button's model on the table canvas so the player can connect the
    /// button to the figure on the board. Drawn on the background draw list (above the Raylib canvas,
    /// below ImGui windows).</summary>
    private void DrawMapHighlight()
    {
        IModel? model = _hoveredModel;
        if (model == null) return;

        var p = model.Position;
        if (p.x == 0f && p.z == 0f) return; // never positioned — don't ring the origin

        var (px, py) = InchesToPixel(p.x, p.z);
        var c = new Vector2(px, py);
        var bg = ImGui.GetBackgroundDrawList();
        // Shape-aware highlight: matches the true base outline (rectangle for rectangular bases).
        ModelBaseRenderer.DrawOutlineImGui(bg, model.BaseShape, c, _scale, HighlightCol, 3f, 3f / _scale, model.Facing);
        ModelBaseRenderer.DrawOutlineImGui(bg, model.BaseShape, c, _scale, HighlightHaloCol, 2f, 7f / _scale, model.Facing);
    }

    // ICanvasInteractionHandler — lets the player read each model's weapons by hovering it on the
    // table, and assign a wound by clicking it (mirrors the dialog buttons).

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        lock (_lock) { request = _request; results = _results; }
        if (request == null || results == null) return null;

        PendingWounds? pw = results.PendingWounds
            .FirstOrDefault(e => ReferenceEquals(e.Model.GetValue(), model));
        if (pw == null) return null; // not a model of the unit receiving wounds

        var modelData = pw.Model.GetValue();
        // #286: hovering the figure on the table is a first-class hover — it rings the model (via
        // DrawMapHighlight, which reads _hoveredModel) and highlights the matching dialog row. Set here
        // because this runs before Draw; Draw seeds _hoveredModel from it and clears it at the end.
        _canvasHoveredModel = modelData;
        float remaining = modelData.TotalWounds - modelData.WoundsDealt - pw.Wounds;

        var sb = new StringBuilder();
        // Show remaining/total only for multi-wound models; single-wound models need no counter.
        if (modelData.TotalWounds > 1f)
            sb.AppendLine($"This model - ({WoundFormat.Fraction(remaining, modelData.TotalWounds)})");
        if (pw.Wounds > 0f)
            sb.AppendLine($"({WoundFormat.Format(pw.Wounds)} already assigned)");
        sb.AppendLine("Weapons:");
        foreach (string line in WeaponLines(modelData))
            sb.AppendLine($"  {line}");
        if (WoundAssignmentText.ClumpModeMatters(results))
        {
            string effect = WoundAssignmentText.ModelEffect(results, pw);
            if (effect.Length > 0) sb.AppendLine($"Next clump here: {effect}");
        }
        if (results.CanAssignWoundTo(pw))
            sb.Append(WoundAssignmentText.ClickHint(results));
        else if (remaining > 0f)
            sb.Append("(a hero is assigned wounds last - not yet a valid target)");
        else
            sb.Append("(no longer a valid target)");
        return sb.ToString();
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        TaskCompletionSource<AssignWoundsResults>? tcs;
        lock (_lock) { request = _request; results = _results; tcs = _tcs; }
        if (request == null || results == null || tcs == null) return;

        PendingWounds? pw = results.PendingWounds
            .FirstOrDefault(e => ReferenceEquals(e.Model.GetValue(), model));
        if (pw == null) return;

        if (results.TryAddWounds(pw.Model) && results.IsFinishedAssigning)
            Complete(tcs, results);
    }

    /// <summary>Distinct weapon groups for a model, e.g. "2x Rifle (24\")", "Heavy Rifle (30\")".
    /// Shared with <see cref="GuiModelSelectionResolver"/> (model-pick stats).</summary>
    internal static List<string> WeaponLines(IModel model)
    {
        var lines = new List<string>();
        foreach (var grp in model.Weapons.GroupBy(w => w.Name))
        {
            var w = grp.First();
            int count = grp.Count();
            string range  = w.RangeInches > 0f ? $"{w.RangeInches:0.#}\"" : "melee";
            string prefix = count > 1 ? $"{count}x " : "";
            lines.Add($"{prefix}{w.Name} ({range})");
        }
        if (lines.Count == 0) lines.Add("(no weapons)");
        return lines;
    }

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);

    private void Complete(TaskCompletionSource<AssignWoundsResults> tcs, AssignWoundsResults results)
    {
        lock (_lock) { _request = null; _results = null; _tcs = null; }
        _hoveredModel       = null;
        _canvasHoveredModel = null;
        tcs.SetResult(results);
    }

}
