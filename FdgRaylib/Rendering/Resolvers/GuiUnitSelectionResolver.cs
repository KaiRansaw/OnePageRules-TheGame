using System.Numerics;
using FDG;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A <see cref="GuiSelectionResolver{UnitData}"/> that also lets the player click a valid unit directly on
/// the table canvas — the same interaction the shooting resolver offers — instead of only using the dialog
/// button list. Used for every SelectionRequest&lt;UnitData&gt;: choose-unit-to-deploy, choose-unit-to-
/// activate, spell targets (#103) and melee defender selection. Valid target units are ringed on the canvas;
/// clicking one (via the shared <see cref="ICanvasInteractionHandler"/> seam driven by
/// <see cref="TableHitTester"/>) selects it. Each dialog button carries the unit's stats (models, Quality,
/// Defense, weapons) and hovering a button highlights that unit on the table (and vice versa) — matching the
/// model picker (<see cref="GuiModelSelectionResolver"/>) and the wound-assignment dialog. The dialog stays
/// as a fallback and for the Back button. Units not yet on the table (deployment) simply have no rings.
/// #315: an embarked unit's row rings its transport in amber instead (its own models are off-table), and
/// canvas-hovering a transport emphasises its occupants' rows — so two same-named squads riding two
/// transports can be told apart before committing an activation.
/// </summary>
public class GuiUnitSelectionResolver : GuiSelectionResolver<UnitData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    // The unit to emphasise this frame — set by a hovered dialog button (OnValidOptionHovered) OR by the
    // canvas hover (GetHoverLabel); cleared at the end of Draw so it lasts a single frame. Main-thread only.
    private DataReference? _hoveredValidRef;

    // #315: rows to emphasise this frame — the occupants of a canvas-hovered transport, set by
    // GetHoverLabel (which runs earlier in the same frame) and consumed by the base draw loop via
    // IsRowEmphasized; cleared at the end of Draw like _hoveredValidRef. Main-thread only.
    private readonly HashSet<DataReference> _cargoRowRefs = new();

    private static readonly uint HoverCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 1.00f));
    private static readonly uint HoverHaloCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.45f));

    // #315: an embarked unit's models sit at the off-table sentinel, so hovering its row rings its
    // TRANSPORT instead — in amber, not the hover cyan, so it reads "your ride is here", not "you'd be
    // selecting the transport".
    private static readonly uint TransportCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.80f, 0.35f, 1.00f));
    private static readonly uint TransportHaloCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.80f, 0.35f, 0.45f));

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    // Dialog content: a bright heading (unit name) + smaller, dimmer detail lines (models/Quality/Defense,
    // then weapons) so the pick is informed without hunting the canvas — the same treatment the model picker
    // and wounds dialog give. Unit-wide special rules are omitted here (they live in the hover tooltip);
    // opt.Name may already carry a reserve suffix like "(Ambush)", which is kept as the heading.
    protected override (string Heading, IReadOnlyList<string> Details) OptionContent(
        DataBinding<UnitData> option, string name)
    {
        UnitData unit = option.GetValue();
        int liveModels = unit.Models.Count(m => m.GetIsAlive());
        var weapons = unit.AllWeapons()
            .DistinctBy(w => w.Name)
            .Select(w => (w.Name, w.RangeInches, WeaponStatFormatter.RuleList(w)))
            .ToList();
        return UnitOptionLabel.Build(name, liveModels, unit.Quality, unit.Defense, weapons);
    }

    // #337: the engine appends a status badge to the picker label of a unit whose activation will not be a
    // normal one ("Blade Squad (Shaken - recovers)" - it skips the action menu and spends the activation
    // recovering). Splitting it out here draws that run amber and underlined, with the token catalog's own
    // sentence on hover; a label with no badge returns null and draws exactly as it always did.
    protected override IReadOnlyList<RuleHoverText.Segment>? HeadingSegments(
        DataBinding<UnitData> option, string heading) => UnitStatusBadge.Segments(heading);

    // Hovering a valid option highlights its models on the canvas AND raises a full-spec tooltip (#223): the
    // same read-only stat block the canvas hover shows, so the player can compare units to deploy / activate
    // without hunting the board. Called from the base draw loop while the option's button is the hovered item.
    protected override void OnValidOptionHovered(SelectionRequest<UnitData>.ValidOption opt)
    {
        _hoveredValidRef = opt.Option.Reference;
        // Same translucent well as the canvas hover (ImGuiTheme.TooltipBg) — this tooltip pops next to the
        // picker, over the board, and hovering a row also rings that unit's models underneath it.
        ImGuiTheme.BeginTranslucentTooltip();
        UnitStatBlockRenderer.Draw(opt.Option.GetValue(), includeRuleDescriptions: true);
        ImGuiTheme.EndTranslucentTooltip();
    }

    // #248: keyboard highlight rings the unit like a hover, but skips the tooltip (it would pop at the
    // unrelated mouse position).
    protected override void OnValidOptionHighlighted(SelectionRequest<UnitData>.ValidOption opt) =>
        _hoveredValidRef = opt.Option.Reference;

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return null;

        // #315: hovering a transport on the table emphasises its occupants' rows in the list — the
        // reverse of hovering an occupant's row ringing the transport (#286's two-way rule). Computed
        // here because this runs earlier in the same frame than Draw; empty for non-transports.
        _cargoRowRefs.Clear();
        foreach (DataReference cargo in TransportOptionLookup.CargoOptionRefs(request, unit))
            _cargoRowRefs.Add(cargo);

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
        SelectionRequest<UnitData>? request;
        TaskCompletionSource<DataBinding<UnitData>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        foreach (var opt in request.ValidOptions)
        {
            if (opt.Option.GetValue() == unit)
            {
                Complete(tcs, opt.Option);
                return;
            }
        }
    }

    // #315: paint the canvas-hovered transport's occupants' rows (the base class does the painting).
    protected override bool IsRowEmphasized(DataBinding<UnitData> option) =>
        _cargoRowRefs.Contains(option.Reference);

    /// <summary>A unit clicked while the action pop-up was open: the pop-up backs out of the current
    /// activation and this unit is picked as soon as the "choose a unit" request comes back.</summary>
    public static volatile IUnit? PendingActivationPick;

    public override void Draw(int screenW, int screenH)
    {
        SelectionRequest<UnitData>? pending;
        TaskCompletionSource<DataBinding<UnitData>>? pendingTcs;
        lock (_lock) { pending = _request; pendingTcs = _tcs; }
        if (pending is ChooseUnitToActivateRequest activation && pendingTcs != null)
        {
            // Switching units from the action pop-up: pick the clicked unit straight away.
            if (PendingActivationPick is IUnit want)
            {
                PendingActivationPick = null;
                foreach (var opt in activation.ValidOptions)
                    if (opt.Option.GetValue().ID.Equals(want.ID)) { Complete(pendingTcs, opt.Option); return; }
            }
            // Activation is picked by clicking the unit on the table: no side list, just a short hint (plus
            // buttons for units that can't be clicked because they ride inside a transport).
            DrawActivationHint(activation, pendingTcs);
            DrawHoverHighlight();
            _hoveredValidRef = null;
            _cargoRowRefs.Clear();
            return;
        }
        if (pending != null && pendingTcs != null)
        {
            // Any other "pick a unit" (which unit to deploy next, ...) is a row of unit cards along the
            // bottom of the board instead of the side list.
            DrawUnitCards(pending, pendingTcs);
            DrawHoverHighlight();
            _hoveredValidRef = null;
            _cargoRowRefs.Clear();
            return;
        }

        // Only the hovered unit is ringed -- no persistent ring on every valid unit (that was visual noise
        // during activation, where every one of the player's units is a valid pick). The dialog draws first;
        // DrawHoverHighlight runs after so a hovered dialog button lands the highlight the same frame.
        base.Draw(screenW, screenH);
        DrawHoverHighlight();
        _hoveredValidRef = null;
        _cargoRowRefs.Clear();
    }

    private void DrawUnitCards(SelectionRequest<UnitData> request, TaskCompletionSource<DataBinding<UnitData>> tcs)
    {
        // Tall unit cards in a row, like a strategy game's army bar: points tab, models-left bar, the unit's
        // picture, its name, and a shoot / melee icon. Hover for the full card; click to pick.
        const float cardW = 118f, gap = 6f;
        float lineH = ImGui.GetTextLineHeight();
        const float tabH = 22f, barH = 16f, imgH = 112f, iconH = 26f;
        float nameH = lineH * 3f + 4f;   // two lines of name + the Q / D line
        float cardH = tabH + barH + imgH + nameH + iconH + 6f;
        int count = request.ValidOptions.Count + request.InvalidOptions.Count;
        float rowW = MathF.Max(1, count) * (cardW + gap);
        float winW = MathF.Min(rowW + ImGui.GetStyle().WindowPadding.X * 2f, MathF.Max(260f, PopupAnchor.AreaW - 16f));
        float winH = cardH + lineH * 2f + ImGui.GetStyle().ScrollbarSize + 26f;

        ImGui.SetNextWindowPos(PopupAnchor.PlaceTopStrip(new Vector2(winW, winH)), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(winW, winH), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.90f);
        ImGui.Begin("##unitcards", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.HorizontalScrollbar |
            ImGuiWindowFlags.NoFocusOnAppearing);

        string title = !string.IsNullOrEmpty(request.DisplayName) ? request.DisplayName : request.Instructions;
        ImGui.TextUnformatted(title);
        if (request.AllowCancel)
        {
            ImGui.SameLine();
            if (UiButton.Back(string.IsNullOrEmpty(request.CancelLabel) ? "Back" : request.CancelLabel)
                || PopupAnchor.RightClickOnEmptyTable())
            {
                ImGui.End();
                Complete(tcs, null!);
                return;
            }
        }

        DataBinding<UnitData>? picked = null;
        var dl = ImGui.GetWindowDrawList();
        uint U(float r, float g, float b, float a = 1f) => ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
        uint white = U(1f, 1f, 1f), dim = U(0.75f, 0.78f, 0.82f), frame = U(0.42f, 0.30f, 0.16f);
        int idx = 0;

        void Card(DataBinding<UnitData> option, string name, bool valid, string? reason,
            SelectionRequest<UnitData>.ValidOption? validOpt)
        {
            if (idx > 0) ImGui.SameLine(0f, gap);
            idx++;
            Vector2 p0 = ImGui.GetCursorScreenPos();
            ImGui.BeginDisabled(!valid);
            bool clicked = ImGui.InvisibleButton($"##card{idx}", new Vector2(cardW, cardH));
            ImGui.EndDisabled();
            bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            UnitData u = option.GetValue();
            Vector2 p1 = p0 + new Vector2(cardW, cardH);

            // Body and frame.
            dl.AddRectFilled(p0, p1, U(0.10f, 0.09f, 0.08f), 4f);

            // Top tab: the unit's points.
            Vector2 t0 = p0 + new Vector2(10f, 0f), t1 = p0 + new Vector2(cardW - 10f, tabH);
            dl.AddRectFilled(t0, t1, U(0.10f, 0.22f, 0.42f), 6f, ImDrawFlags.RoundCornersTop);
            string pts = $"{u.PointCost} pts";
            Vector2 ps = ImGui.CalcTextSize(pts);
            dl.AddText(new Vector2(p0.X + (cardW - ps.X) / 2f, p0.Y + (tabH - ps.Y) / 2f), white, pts);

            // Models-left bar: green, turning red below half.
            int live = u.Models.Count(m => m.GetIsAlive());
            int all = Math.Max(1, u.Models.Count);
            float frac = Math.Clamp(live / (float)all, 0f, 1f);
            Vector2 b0 = p0 + new Vector2(4f, tabH + 2f), b1 = new(p1.X - 4f, b0.Y + barH);
            dl.AddRectFilled(b0, b1, U(0.15f, 0.05f, 0.05f), 2f);
            dl.AddRectFilled(b0, new Vector2(b0.X + (b1.X - b0.X) * frac, b1.Y),
                frac > 0.5f ? U(0.45f, 0.80f, 0.20f) : U(0.80f, 0.15f, 0.15f), 2f);
            string liveText = live.ToString();
            Vector2 ls = ImGui.CalcTextSize(liveText);
            dl.AddText(new Vector2(b0.X + (b1.X - b0.X - ls.X) / 2f, b0.Y + (barH - ls.Y) / 2f), white, liveText);

            // Picture (cropped to fill), or the unit's initials when there is none.
            Vector2 i0 = new(p0.X + 4f, b1.Y + 3f), i1 = new(p1.X - 4f, i0.Y + imgH);
            Texture2D? tex = PopupAnchor.CardImageFor?.Invoke(u);
            if (tex is Texture2D t && t.Width > 0 && t.Height > 0)
            {
                float boxAspect = (i1.X - i0.X) / (i1.Y - i0.Y);
                float texAspect = t.Width / (float)t.Height;
                Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
                if (texAspect > boxAspect) { float w = boxAspect / texAspect; uv0.X = (1f - w) / 2f; uv1.X = uv0.X + w; }
                else { float h = texAspect / boxAspect; uv0.Y = (1f - h) / 2f; uv1.Y = uv0.Y + h; }
                dl.AddImage((IntPtr)t.Id, i0, i1, uv0, uv1);
            }
            else
            {
                dl.AddRectFilledMultiColor(i0, i1, U(0.12f, 0.30f, 0.38f), U(0.12f, 0.30f, 0.38f),
                    U(0.05f, 0.12f, 0.18f), U(0.05f, 0.12f, 0.18f));
                string initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(3).Select(w => char.ToUpperInvariant(w[0])));
                ImGui.SetWindowFontScale(2f);
                Vector2 isz = ImGui.CalcTextSize(initials);
                ImGui.SetWindowFontScale(1f);
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 2f,
                    new Vector2((i0.X + i1.X - isz.X) / 2f, (i0.Y + i1.Y - isz.Y) / 2f), U(0.75f, 0.90f, 1f, 0.85f), initials);
            }

            // Name, up to two lines, centred.
            float ny = i1.Y + 3f;
            foreach (string line in WrapCardText(name, cardW - 10f, 2))
            {
                Vector2 nsz = ImGui.CalcTextSize(line);
                dl.AddText(new Vector2(p0.X + (cardW - nsz.X) / 2f, ny), valid ? white : dim, line);
                ny += lineH;
            }

            // Quality / Defense.
            string qd = $"Q {u.Quality}+ / D {u.Defense}+";
            Vector2 qsz = ImGui.CalcTextSize(qd);
            dl.AddText(new Vector2(p0.X + (cardW - qsz.X) / 2f, i1.Y + 3f + lineH * 2f), U(1f, 0.85f, 0.45f), qd);

            // Bottom icon: crosshair for a shooting unit, crossed blades for a melee one.
            bool ranged = u.AllWeapons().Any(w => w.RangeInches > 0);
            Vector2 c = new((p0.X + p1.X) / 2f, p1.Y - iconH / 2f - 3f);
            float r = iconH / 2f - 2f;
            dl.AddCircleFilled(c, r + 2f, U(0.30f, 0.20f, 0.10f));
            dl.AddCircle(c, r + 2f, U(0.75f, 0.55f, 0.25f), 0, 1.5f);
            uint ic = U(0.95f, 0.80f, 0.45f);
            if (ranged)
            {
                dl.AddCircle(c, r * 0.6f, ic, 0, 1.5f);
                dl.AddLine(c - new Vector2(r * 0.9f, 0), c + new Vector2(r * 0.9f, 0), ic, 1.5f);
                dl.AddLine(c - new Vector2(0, r * 0.9f), c + new Vector2(0, r * 0.9f), ic, 1.5f);
            }
            else
            {
                dl.AddLine(c + new Vector2(-r * 0.7f, -r * 0.7f), c + new Vector2(r * 0.7f, r * 0.7f), ic, 2f);
                dl.AddLine(c + new Vector2(r * 0.7f, -r * 0.7f), c + new Vector2(-r * 0.7f, r * 0.7f), ic, 2f);
            }

            // Frame: gold when hovered; greyed and labelled when it can't be picked.
            dl.AddRect(p0, p1, hovered && valid ? U(1f, 0.85f, 0.25f) : frame, 4f, ImDrawFlags.None, hovered && valid ? 3f : 2f);
            if (!valid) dl.AddRectFilled(p0, p1, U(0f, 0f, 0f, 0.55f), 4f);

            if (hovered && validOpt != null) OnValidOptionHovered(validOpt);
            else if (hovered && reason != null) ImGui.SetTooltip(reason);
            if (clicked && valid) picked = option;
        }

        foreach (var opt in request.ValidOptions) Card(opt.Option, opt.Name, true, null, opt);
        foreach (var opt in request.InvalidOptions) Card(opt.Option, opt.Name, false, opt.Reason, null);

        ImGui.End();
        if (picked != null) Complete(tcs, picked);
    }

    private static List<string> WrapCardText(string text, float maxW, int maxLines)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string attempt = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && ImGui.CalcTextSize(attempt).X > maxW) { lines.Add(current); current = word; }
            else current = attempt;
        }
        if (current.Length > 0) lines.Add(current);
        if (lines.Count > maxLines) { lines = lines.Take(maxLines).ToList(); lines[^1] += "..."; }
        return lines;
    }

    private void DrawActivationHint(ChooseUnitToActivateRequest request, TaskCompletionSource<DataBinding<UnitData>> tcs)
    {
        ResolverPanelLayout.BeginDocked("##activatehint");   // the Actions window (right column)
        ImGui.TextUnformatted("Your turn: click a unit to activate it.");

        // Units with no models on the table (inside a transport) can't be clicked - offer them as buttons.
        foreach (var opt in request.ValidOptions)
        {
            UnitData u = opt.Option.GetValue();
            bool onTable = u.Models.Any(m => m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f));
            if (onTable) continue;
            if (UiButton.Navigate($"{opt.Name}##act{opt.Option.Reference}", new Vector2(MathF.Max(260f, ImGui.GetContentRegionAvail().X), 0f)))
            {
                ImGui.End();
                Complete(tcs, opt.Option);
                return;
            }
        }
        ImGui.End();
    }

    // The hovered unit (dialog button or canvas) rings with a halo, connecting the list entry to the figures
    // on the board — same affordance as the model picker and wounds dialog. #315: an embarked unit has no
    // figures on the board (its models park at the off-table sentinel), so its TRANSPORT rings instead, in
    // the distinct amber style — that is where activating it will play out.
    private void DrawHoverHighlight()
    {
        if (_hoveredValidRef == null) return;

        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        foreach (var opt in request.ValidOptions)
        {
            if (!opt.Option.Reference.Equals(_hoveredValidRef)) continue;

            UnitData unit = opt.Option.GetValue();
            UnitData? transport = TransportOptionLookup.FindTransportOf(request, unit);
            if (transport != null)
                RingModels(dl, transport, TransportCol, TransportHaloCol);
            else
                RingModels(dl, unit, HoverCol, HoverHaloCol);
            return;
        }
    }

    private void RingModels(ImDrawListPtr dl, UnitData unit, uint col, uint haloCol)
    {
        foreach (IModel model in unit.Models)
        {
            if (!model.GetIsAlive()) continue;
            var pos = model.Position;
            if (pos.x == 0f && pos.z == 0f) continue;
            var (px, py) = InchesToPixel(pos.x, pos.z);
            var c = new Vector2(px, py);
            // Shape-aware highlight: matches the true base outline (rectangle for rectangular bases).
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, col, 3f, 3f / _scale, model.Facing);
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, haloCol, 2f, 7f / _scale, model.Facing);
        }
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
