using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiStringSelectionResolver : IStageResolver<StringSelectionRequest, string>,
    IStageResolver<ChooseActionRequest, string>, IGuiResolver, ICanvasInteractionHandler
{
    /// <summary>True while the pending menu is the activation's action menu (Hold / Advance / Rush /
    /// Charge / Special), which draws as a small pop-up next to the unit instead of the side panel.</summary>
    private bool _actionMenu;
    /// <summary>
    /// #191 B1 step 5a: Choose Action is its own request type now, but the menu/canvas rendering
    /// below is unchanged - so this mirrors the request into the shape <see cref="Resolve(StringSelectionRequest)"/>
    /// already draws rather than duplicating the ImGui layout. The mirror never crosses the wire;
    /// it exists only for this one in-process call.
    /// </summary>
    public Task<string> Resolve(ChooseActionRequest request)
    {
        Task<string> task = Resolve(new StringSelectionRequest(
            request.TargetPlayerID, request.TaskID, "Choose Action", request.ValidOptions, request.InvalidOptions,
            request.OptionDescriptions, request.AllowCancel, secondaryActions: null, request.DisplayName,
            optionRules: null, request.OptionDescriptionRules));
        lock (_lock) _actionMenu = true;
        return task;
    }

    /// <summary>#311 — the Pass confirmation's title, which doubles as its ImGui popup id.</summary>
    private const string ConfirmPassTitle = "End this activation?";

    private readonly object _lock = new();
    private StringSelectionRequest? _request;
    private TaskCompletionSource<string>? _tcs;

    /// <summary>
    /// #311: true between pressing Pass and answering the confirmation. Only the render thread reads or
    /// writes it, but it is cleared under the same lock as the request so a request that resolves or is
    /// replaced underneath can never leave a confirmation armed over the next menu.
    /// </summary>
    private bool _confirmingPass;

    /// <summary>
    /// #336: the option whose rules the details strip is showing - the last rule-bearing row the mouse was
    /// over. Sticky on purpose: shooting's Details pane follows the SELECTED weapon, but a melee row has no
    /// selected state (clicking it commits the attack), so this follows hover instead, and a strip that
    /// emptied the moment the cursor left the row would be unreadable by the time you looked at it.
    /// Render-thread only, but cleared under the lock so a request that resolves underneath cannot leave
    /// the next menu pointing at an option it does not have.
    /// </summary>
    private string? _focusedOption;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<string> Resolve(StringSelectionRequest request)
    {
        var tcs = new TaskCompletionSource<string>();
        lock (_lock) { _tcs = tcs; _request = request; _confirmingPass = false; _focusedOption = null; _actionMenu = false; }
        return tcs.Task;
    }

    /// <summary>One row of the scrolling option list: a valid option (ValidIndex >= 0, clickable, may
    /// carry subtext) or a greyed-out one carrying its reason. Measured once per frame, then drawn.</summary>
    internal sealed class MenuRow
    {
        public MenuRow(string option, string label, string? desc, int validIndex)
        {
            Option = option; Label = label; Desc = desc; ValidIndex = validIndex;
        }

        public string Option { get; }
        public string Label { get; }
        public string? Desc { get; }
        public int ValidIndex { get; }

        /// <summary>#321: the companion action riding this row (melee's "Hold back"), or null.</summary>
        public StringSelectionRequest.SecondaryAction? Secondary { get; set; }

        /// <summary>Its button text, and the reason it is greyed out when it may not be taken.</summary>
        public string SecondaryLabel { get; set; } = string.Empty;
        public string? SecondaryDisabledReason { get; set; }

        /// <summary>Width of the companion button, or 0 when the row has none. The row's own button
        /// gives it up, so the label must wrap into what is left.</summary>
        public float SecondaryWidth { get; set; }

        /// <summary>#336: the label split around its special-rule names, when the request said this option
        /// has any. Null on an ordinary option, which keeps the plain word-wrapped path below.</summary>
        public IReadOnlyList<RuleHoverText.Segment>? Segments { get; set; }

        /// <summary>#336: <see cref="Segments"/> wrapped to the row's width, one entry per drawn line.
        /// Empty when the row has no rules and <see cref="Lines"/> is what gets drawn.</summary>
        public List<List<RuleHoverText.Segment>> SegmentLines { get; set; } = new();

        /// <summary>#369: <see cref="Desc"/> split around the rule names it MENTIONS (a buff's
        /// "...which gains Courage..."), so the subtext gets the same underline-and-hover treatment the
        /// label does. Null when the description names no rule, which keeps the plain ImGui text path.</summary>
        public IReadOnlyList<RuleHoverText.Segment>? DescSegments { get; set; }

        /// <summary>#369: <see cref="DescSegments"/> wrapped to the description's width, one entry per
        /// drawn line. Measured at the subtext's own smaller font scale.</summary>
        public List<List<RuleHoverText.Segment>> DescSegmentLines { get; set; } = new();

        /// <summary>How many text lines the label occupies, whichever of the two paths drew it.</summary>
        public int LineCount => Segments != null ? SegmentLines.Count : Lines.Count;

        public List<string> Lines { get; set; } = new();
        public float ButtonHeight { get; set; }
        public float TotalHeight { get; set; }
    }

    public void Draw(int screenW, int screenH)
    {
        StringSelectionRequest? request;
        TaskCompletionSource<string>? tcs;
        bool confirming;
        bool actionMenu;
        lock (_lock) { request = _request; tcs = _tcs; confirming = _confirmingPass; actionMenu = _actionMenu; }
        if (request == null || tcs == null) return;
        if (actionMenu) { DrawActionPopup(request, tcs); return; }
        // Every other menu (melee weapons, abilities, yes/no-style picks...) is a compact pop-up beside the
        // acting unit too - the old side panel below is no longer drawn.
        DrawCompactMenu(request, tcs);
        return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##StrSelBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        int validCount   = request.ValidOptions.Count;
        int invalidCount = request.InvalidOptions.Count;
        bool hasDescriptions = request.OptionDescriptions != null && request.OptionDescriptions.Count > 0;

        const float pad = 16f;
        const float descScale = 0.82f;   // descriptions render smaller than the option label
        const float descIndent = 10f;
        const float gapAfterBtn = 6f;
        const float gapAfterDesc = 8f;
        const float textPadX = 10f;      // inset of the hand-drawn label from the button's left edge
        const float btnPadY = 6f;
        const float gapBetweenRowButtons = 4f;   // #321: between a row and its companion action

        // #298: rows are sized from the font, not from a hardcoded pixel count - see OptionRowHeight.
        float lineH = ImGui.GetTextLineHeight();
        float btnH = ResolverPanelLayout.OptionRowHeight();

        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float dh = ResolverPanelLayout.H;   // fill the panel; the option list scrolls inside it
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;
        float fullBtnW = dw - pad * 2;      // full-width row: the footer buttons never lose a scrollbar strip

        // #321: options that BELONG to another row (melee's "Hold back: <weapon>") are drawn as a second
        // button on that row, so they must not also appear as rows of their own - which is the whole point:
        // two peer entries for one weapon read as two unrelated choices.
        HashSet<string> companionOptions = CompanionOptions(request);

        // #248: letter hotkeys - pinned letters for the built-in actions, pool letters for the rest.
        // The label carries the letter so the shortcut is discoverable on the button itself. Assigned
        // from the REQUEST's option order (not the display order) so pulling Pass out below cannot
        // shuffle anyone else's letter.
        // #321: companions are skipped when handing out letters - they share their owner row's letter
        // under Shift - so they neither burn pool letters nor shift anyone else's.
        char?[] letters = AssignRowLetters(request, companionOptions);

        // #311: Pass is pinned to the bottom of the panel behind a confirmation instead of sitting in the
        // list as an ordinary row - see ActionMenuLayout for why. It keeps that spot when it is greyed
        // out (Instinctive compels an attack), so the rows above never shift under the cursor.
        int passValid   = ActionMenuLayout.PassIndex(request.ValidOptions);
        int passInvalid = passValid >= 0 ? -1 : ActionMenuLayout.PassIndex(request.InvalidOptions);
        bool pinPass = passValid >= 0 || passInvalid >= 0;

        // Everything the scrolling list shows: valid options first, then the greyed ones - minus the
        // pinned Pass. Menus without a Pass option (weapon / spell / ability pickers) keep the original
        // layout entirely, Back included.
        var rows = new List<MenuRow>(validCount + invalidCount);
        for (int i = 0; i < validCount; i++)
        {
            if (i == passValid) continue;
            string opt = request.ValidOptions[i];
            if (companionOptions.Contains(opt)) continue;
            string? desc = null;
            if (hasDescriptions
                && request.OptionDescriptions!.TryGetValue(opt, out string? d)
                && !string.IsNullOrEmpty(d))
            {
                desc = d;
            }
            var row = new MenuRow(opt, LabelFor(opt, letters[i]), desc, i);
            AttachSecondary(row, request, letters[i]);
            AttachRuleSegments(row, request, row.Label, tail: string.Empty);
            AttachDescriptionSegments(row, request);
            rows.Add(row);
        }
        for (int i = 0; i < invalidCount; i++)
        {
            if (i == passInvalid) continue;
            StringSelectionRequest.InvalidOption opt = request.InvalidOptions[i];
            if (companionOptions.Contains(opt.Option)) continue;
            // Same hand-drawn treatment as a valid row, so a greyed-out option reads the same and its
            // reason ("Must attack with Deadly weapons first.") is never clipped away either.
            string reasonTail = $" ({opt.Reason})";
            var invalidRow = new MenuRow(opt.Option, opt.Option + reasonTail, null, -1);
            AttachRuleSegments(invalidRow, request, opt.Option, reasonTail);
            rows.Add(invalidRow);
        }

        // Measure heights up front so the dialog is sized to its content and a wrapped description can never
        // overflow into the next row. CalcTextSize ignores SetWindowFontScale, so measure the description at
        // the scaled-equivalent wrap width and scale the resulting height back down.
        float MeasureRows(float width)
        {
            float descWrapMeasure = (width - descIndent) / descScale;
            float total = 0f;
            foreach (MenuRow row in rows)
            {
                // #321: a companion button takes its text's width off the right of the row (capped, so a
                // long verb can never squeeze the weapon line out), and the label wraps into the rest.
                row.SecondaryWidth = row.Secondary == null
                    ? 0f
                    : MathF.Min(ImGui.CalcTextSize(row.SecondaryLabel).X + textPadX * 2f, width * 0.42f);
                float labelWrap = width - row.SecondaryWidth
                    - (row.SecondaryWidth > 0f ? gapBetweenRowButtons : 0f) - textPadX * 2f;
                if (row.Segments != null)
                    row.SegmentLines = OptionRuleSegments.Wrap(row.Segments, MeasureText, labelWrap);
                else
                    row.Lines = ResolverText.Wrap(row.Label, labelWrap);
                row.ButtonHeight = MathF.Max(btnH, row.LineCount * lineH + btnPadY * 2f);
                if (row.Desc == null)
                {
                    row.TotalHeight = row.ButtonHeight + gapAfterBtn;
                }
                else if (row.DescSegments != null)
                {
                    // #369: measured in the same scaled-equivalent space as the wrap width above - the
                    // segments are measured unscaled and the resulting line count costed at descScale.
                    row.DescSegmentLines =
                        OptionRuleSegments.Wrap(row.DescSegments, MeasureText, descWrapMeasure);
                    row.TotalHeight = row.ButtonHeight + 2f
                        + row.DescSegmentLines.Count * lineH * descScale + gapAfterDesc;
                }
                else
                {
                    row.TotalHeight = row.ButtonHeight + 2f
                        + ImGui.CalcTextSize(row.Desc, false, descWrapMeasure).Y * descScale + gapAfterDesc;
                }
                total += row.TotalHeight;
            }
            return total;
        }

        // The pinned Pass row: one line when it is available, taller when it is greyed and the reason wraps.
        string passLabel = passValid >= 0
            ? LabelFor(request.ValidOptions[passValid], letters[passValid])
            : passInvalid >= 0
                ? $"{request.InvalidOptions[passInvalid].Option} ({request.InvalidOptions[passInvalid].Reason})"
                : string.Empty;
        List<string> passLines = ResolverText.Wrap(passLabel, fullBtnW - textPadX * 2f);
        float passRowH = MathF.Max(btnH, passLines.Count * lineH + btnPadY * 2f);

        float instrH = ImGui.CalcTextSize(request.Instructions, false, dw - pad * 2).Y + 8f;
        float listTop = pad + instrH + 4f;
        float footerH = pinPass ? ActionMenuLayout.FooterHeight(lineH, passRowH, request.AllowCancel) : 0f;

        // #336: the rule-details strip is costed before the list, like the footer - the strip is a fixed
        // share of the panel and the list takes what is left, so a weapon with a wall of rules scrolls its
        // own strip instead of squeezing the options it is there to be compared against.
        bool hasRuleDetails = request.OptionRules != null && request.OptionRules.Count > 0;
        float detailsH = hasRuleDetails ? OptionRuleDetailsLayout.StripHeight(dh, lineH) : 0f;
        float detailsCost = hasRuleDetails ? OptionRuleDetailsLayout.TotalHeight(dh, lineH) : 0f;

        float listH = pinPass
            ? ActionMenuLayout.ListHeight(dh, listTop, footerH + detailsCost, lineH)
            : MathF.Max(lineH, dh - listTop - pad - detailsCost);

        float listBtnW = fullBtnW;
        if (MeasureRows(listBtnW) > listH)
        {
            // The list will scroll, so hand the scrollbar its strip back and re-wrap into what is left -
            // otherwise a long option label is drawn underneath it.
            listBtnW = fullBtnW - ImGui.GetStyle().ScrollbarSize;
            MeasureRows(listBtnW);
        }

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##StrSelDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();

        // The picked option is completed once after the loop so two same-frame presses can't double-resolve.
        string? picked = null;
        bool cancelled = false;
        bool passPressed = false;

        // #311: the confirmation is a modal popup, so it blocks the mouse - but the letter and Backspace
        // hotkeys are read straight from ImGui and would still fire behind it, exactly the case #248's
        // muting rule exists for. Freeze every key path while it is up.
        bool keysLive = !confirming;

        // ---- the scrolling option list ---------------------------------------------------------------
        // Its own child so the footer below stays put while the list scrolls. Zero window padding lines
        // its content up with the panel coordinates the rest of this method uses.
        ImGui.SetCursorPos(new Vector2(pad, listTop));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.BeginChild("##StrSelOptions", new Vector2(fullBtnW, listH), ImGuiChildFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Labels are drawn by hand over an empty button: a melee weapon option ("2x Great Sword - A2, AP1,
        // Rending") is wider than the narrow panel, and ImGui.Button center-CLIPS its label, which silently
        // ate the stat tail - the special rules included (#298). Wrapped left-aligned text shows all of it.
        var dl = ImGui.GetWindowDrawList();
        uint labelCol = ImGui.GetColorU32(ImGuiCol.Text);
        uint dimCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.58f, 1f));

        // #336: rule names are tinted brighter than the rest of the label so they read as "there is more
        // here", exactly as the shoot panel tints its stat subline. A greyed row keeps its own dim colour
        // for both - an unavailable option must not advertise itself with a bright run.
        uint ruleCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.82f, 0.86f, 0.95f, 1f));

        // #369: the subtext's own colour, matching what the plain ImGui path below pushes for it, so a
        // description that gained hoverable rule names did not also change shade.
        uint descCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.66f, 0.74f, 1f));

        // #292's one-tooltip-per-frame rule: the rows are hand-drawn, so a hovered rule name is collected
        // across the loop and raised after it.
        bool listHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        string? ruleTooltip = null;

        float y = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            MenuRow row = rows[i];
            bool enabled = row.ValidIndex >= 0;

            float rowBtnW = listBtnW - row.SecondaryWidth
                - (row.SecondaryWidth > 0f ? gapBetweenRowButtons : 0f);

            ImGui.SetCursorPos(new Vector2(0f, y));
            Vector2 origin = ImGui.GetCursorScreenPos();
            if (!enabled) ImGui.BeginDisabled(true);
            if (ImGui.Button($"##row{i}", new Vector2(rowBtnW, row.ButtonHeight)) && enabled)
                picked ??= row.Option;
            if (!enabled) ImGui.EndDisabled();

            // #336: hovering a rule-bearing row aims the details strip at it. AllowWhenDisabled because
            // BeginDisabled suppresses the plain query and a greyed weapon still has rules worth reading -
            // that IS the reason it is greyed, when the Deadly gate is what did it.
            if (row.Segments != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                _focusedOption = row.Option;

            if (enabled && keysLive
                && letters[row.ValidIndex] is char pressedKey && ResolverHotkeys.IsLetterPressed(pressedKey))
            {
                picked ??= row.Option;
            }

            // #321: the companion action, on the SAME row and to its right - "attack with this weapon" and
            // "hold this weapon back" are one decision about one weapon, and were two peer list entries
            // before, which read as two unrelated choices. Shares the row's letter under Shift.
            if (row.Secondary != null)
            {
                bool secondaryEnabled = row.SecondaryDisabledReason == null;
                ImGui.SetCursorPos(new Vector2(rowBtnW + gapBetweenRowButtons, y));
                if (!secondaryEnabled) ImGui.BeginDisabled(true);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.26f, 0.20f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.40f, 0.36f, 0.24f, 1f));
                bool secondaryClicked = ImGui.Button($"{row.SecondaryLabel}##sec{i}",
                    new Vector2(row.SecondaryWidth, row.ButtonHeight));
                ImGui.PopStyleColor(2);
                if (!secondaryEnabled) ImGui.EndDisabled();

                // BeginDisabled suppresses IsItemHovered, so re-query with AllowWhenDisabled: a refused
                // companion explains why, an available one explains what taking it buys (its own
                // description - "Keeps its Limited once-per-game use for a later melee"), which has no
                // room on a two-word button.
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    string? tip = row.SecondaryDisabledReason ?? SecondaryDescription(request, row);
                    if (tip != null) ImGui.SetTooltip(tip);
                }

                bool secondaryKey = secondaryEnabled && enabled && keysLive
                    && letters[row.ValidIndex] is char shiftKey
                    && ResolverHotkeys.IsLetterPressed(shiftKey, shift: true);

                if ((secondaryClicked && secondaryEnabled) || secondaryKey)
                    picked ??= row.Secondary.Option;
            }

            float ty = origin.Y + (row.ButtonHeight - row.LineCount * lineH) * 0.5f;
            if (row.Segments != null)
            {
                // #336: the shoot panel's treatment (#292) - the label draws as before, but each special
                // rule name inside it gets an underline and its own hover tooltip.
                uint rowTextCol = enabled ? labelCol : dimCol;
                uint rowRuleCol = enabled ? ruleCol : dimCol;
                foreach (List<RuleHoverText.Segment> segmentLine in row.SegmentLines)
                {
                    // DrawInline DRAWS as well as reporting what is hovered, so it must be called for
                    // every line unconditionally and the tooltip picked afterwards. Folding the call into
                    // `ruleTooltip ??= ...` reads fine and is wrong: `??=` skips its right-hand side once
                    // a tooltip has been found, so hovering any rule name stopped every line after it -
                    // in practice the greyed rows, which sort last - from being drawn at all.
                    string? hovered = RuleHoverText.DrawInline(dl, new Vector2(origin.X + textPadX, ty),
                        segmentLine, rowTextCol, rowRuleCol, listHovered);
                    ruleTooltip ??= hovered;
                    ty += lineH;
                }
            }
            else
            {
                foreach (string line in row.Lines)
                {
                    dl.AddText(new Vector2(origin.X + textPadX, ty), enabled ? labelCol : dimCol, line);
                    ty += lineH;
                }
            }

            // Optional subtext under the option (a weapon rule's text, a spell's effect summary):
            // smaller and dimmed.
            if (row.DescSegments != null)
            {
                // #369: the subtext names rules of its own ("...which gains Courage..."), so it draws the
                // same way a rule-bearing label does - hand-drawn segments with the names underlined in
                // place and hoverable - rather than as one flat string. Drawn at descScale explicitly:
                // SetWindowFontScale would move the glyphs and leave the measurements behind.
                float descY = origin.Y + row.ButtonHeight + 2f;
                foreach (List<RuleHoverText.Segment> segmentLine in row.DescSegmentLines)
                {
                    // Called for every line unconditionally - DrawInline draws as well as hit-tests, so
                    // folding it into `??=` would stop drawing the moment a tooltip was found (see the
                    // label loop above, where that exact bug was).
                    string? hovered = RuleHoverText.DrawInline(dl,
                        new Vector2(origin.X + descIndent, descY), segmentLine, descCol, ruleCol,
                        listHovered, descScale);
                    ruleTooltip ??= hovered;
                    descY += lineH * descScale;
                }
            }
            else if (row.Desc != null)
            {
                ImGui.SetCursorPos(new Vector2(descIndent, y + row.ButtonHeight + 2f));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.66f, 0.74f, 1f));
                ImGui.SetWindowFontScale(descScale);
                ImGui.PushTextWrapPos(listBtnW);
                ImGui.TextUnformatted(row.Desc);
                ImGui.PopTextWrapPos();
                ImGui.SetWindowFontScale(1f);
                ImGui.PopStyleColor();
            }

            y += row.TotalHeight;
        }

        // Menus with no Pass to pin keep Back where it has always been: last in the flow.
        if (!pinPass && request.AllowCancel)
        {
            ImGui.SetCursorPos(new Vector2(0f, y + 8f));
            if (DrawBackButton(listBtnW)) cancelled = true;
        }

        ImGui.EndChild();

        if (ruleTooltip != null) RuleHoverText.ShowTooltip(ruleTooltip);

        // ---- #336: the rule-details strip, pinned under the list ---------------------------------------
        if (hasRuleDetails)
        {
            DrawRuleDetails(request, rows, new Vector2(pad, listTop + listH + OptionRuleDetailsLayout.GapAbove(lineH)),
                new Vector2(fullBtnW, detailsH));
        }

        // ---- the pinned footer: Pass, then Back, raised off the panel's bottom edge --------------------
        // Back goes last on purpose: the panel's bottom edge is the easiest target in it to slam into, so
        // it belongs to the harmless action rather than the irreversible one.
        if (pinPass)
        {
            float fy = listTop + listH + detailsCost + ActionMenuLayout.GapAbove(lineH);

            // An available Pass is drawn at full strength, exactly like an option row: the position and
            // the confirmation are what make it deliberate. Dimming it (tried first) read as "disabled"
            // and left the player unsure it could be clicked at all. Only a Pass that really is
            // unavailable is greyed, and it carries its reason like any other invalid option.
            bool passEnabled = passValid >= 0;
            ImGui.SetCursorPos(new Vector2(pad, fy));
            Vector2 passOrigin = ImGui.GetCursorScreenPos();

            if (!passEnabled) ImGui.BeginDisabled(true);
            if (ImGui.Button("##pass", new Vector2(fullBtnW, passRowH)) && passEnabled)
                passPressed = true;
            if (!passEnabled) ImGui.EndDisabled();

            if (passEnabled && keysLive
                && letters[passValid] is char passKey && ResolverHotkeys.IsLetterPressed(passKey))
            {
                passPressed = true;
            }

            var passDl = ImGui.GetWindowDrawList();
            float passTy = passOrigin.Y + (passRowH - passLines.Count * lineH) * 0.5f;
            foreach (string line in passLines)
            {
                passDl.AddText(new Vector2(passOrigin.X + textPadX, passTy), passEnabled ? labelCol : dimCol, line);
                passTy += lineH;
            }

            // The soft back tone, as on every other receding action - the confirmation is next, not the pass.
            if (passPressed) UiSound.Back();

            if (request.AllowCancel)
            {
                fy += passRowH + ActionMenuLayout.GapBetween(lineH);
                ImGui.SetCursorPos(new Vector2(pad, fy));
                if (DrawBackButton(fullBtnW)) cancelled = true;
            }
        }

        // #248: cancellable string menus (a pristine activation's action menu) get a Back button +
        // Backspace, replying null — the SelectionRequest cancel sentinel. Esc is reserved for the
        // in-game menu (except while the #311 confirmation owns it, below).
        if (request.AllowCancel && keysLive && ResolverHotkeys.IsBackPressed())
            cancelled = true;

        ImGui.EndChild();
        ImGui.End();

        // #311: Escape answers the confirmation, and claims the press from the arbiter so the same Escape
        // does not also open the in-game menu behind it. Read before this frame's Pass press so opening
        // the popup and dismissing it can never happen on one keystroke.
        bool escCancel = confirming && EscapeRouter.TryConsumeEscape();

        if (passPressed && !confirming)
        {
            lock (_lock) _confirmingPass = true;
            confirming = true;
        }

        if (confirming)
        {
            // Keep the modal request alive across frames (OpenPopup is consumed by the first BeginPopupModal).
            if (!ImGui.IsPopupOpen(ConfirmPassTitle)) ImGui.OpenPopup(ConfirmPassTitle);

            // The width is pinned and the message wraps at an explicit position rather than at the window
            // edge. Wrapped text inside an AlwaysAutoResize window is circular - the wrap width comes from
            // the window width, which comes from the content - and on the frame the popup appears ImGui has
            // no width yet, so the message wrapped to a column of single words and the popup opened nearly
            // as tall as the screen for one frame before snapping down. A fixed width breaks the loop: the
            // height (0 = auto-fit) is then measured correctly the first time, on the frame ImGui hides a
            // brand-new window anyway. Position is re-applied every frame (Always) so the centre pivot uses
            // the real size instead of whatever the window had when it appeared.
            float rowH = ResolverPanelLayout.OptionRowHeight();
            float btnW = MathF.Max(150f,
                ImGui.CalcTextSize($"Pass  {ResolverKeybinds.Confirm.Parenthetical}").X + 24f);
            float contentW = btnW * 2f + ImGui.GetStyle().ItemSpacing.X;

            Vector2 center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(contentW + ImGui.GetStyle().WindowPadding.X * 2f, 0f),
                ImGuiCond.Always);
            if (ImGui.BeginPopupModal(ConfirmPassTitle,
                    ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                    ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + contentW);
                ImGui.TextUnformatted(
                    "Passing ends this unit's activation - it does nothing else this round.");
                ImGui.PopTextWrapPos();
                ImGui.Spacing();

                bool confirmed = ResolverButtons.Primary("Pass", new Vector2(btnW, rowH));
                ImGui.SameLine();
                bool cancelConfirm = ImGui.Button("Cancel  (Esc)", new Vector2(btnW, rowH)) || escCancel;

                if (confirmed)
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    Complete(tcs, ActionMenuLayout.PassOption);
                    return;
                }

                if (cancelConfirm)
                {
                    UiSound.Back();
                    ImGui.CloseCurrentPopup();
                    lock (_lock) _confirmingPass = false;
                }

                ImGui.EndPopup();
            }
            return;   // the menu underneath is inert while the confirmation is up
        }

        if (picked != null)
            Complete(tcs, picked);
        else if (cancelled)
            Complete(tcs, null!);
    }

    /// <summary>
    /// #336: splits a row's label around the special-rule names the request says it carries, so the names
    /// draw underlined and hoverable where they already sit. Leaves <see cref="MenuRow.Segments"/> null on
    /// an option with no rules, which keeps every other menu on the plain word-wrapped path unchanged.
    /// </summary>
    /// <param name="head">The part of the label the rule names actually live in - the option's own text.
    /// A greyed row's label continues with its reason, and a reason can repeat a rule's name verbatim
    /// ("1x Demo Charge - A1, AP0, Limited (Already used this game (Limited).)"), which the trailing-match
    /// scan would otherwise underline instead of the rule.</param>
    /// <param name="tail">The rest of the label, appended as plain text.</param>
    internal static void AttachRuleSegments(MenuRow row, StringSelectionRequest request,
        string head, string tail)
    {
        if (request.OptionRules == null
            || !request.OptionRules.TryGetValue(row.Option, out List<StringSelectionRequest.OptionRule>? rules)
            || rules.Count == 0)
        {
            return;
        }

        IReadOnlyList<RuleHoverText.Segment> segments = OptionRuleSegments.Build(head, rules);
        if (tail.Length > 0)
        {
            var withTail = new List<RuleHoverText.Segment>(segments) { new(tail, null, null) };
            segments = withTail;
        }
        row.Segments = segments;
    }

    /// <summary>
    /// #369: the same treatment one step down - splits the row's DESCRIPTION around the rule names it
    /// mentions, from <see cref="StringSelectionRequest.OptionDescriptionRules"/>. A separate map from
    /// <see cref="StringSelectionRequest.OptionRules"/> because it annotates a different string: an
    /// ability row's label IS a rule name ("Courage Buff"), so the label matcher would find the "Courage"
    /// inside it and explain the wrong rule. Leaves <see cref="MenuRow.DescSegments"/> null when the
    /// description names none, which keeps the plain ImGui subtext path.
    /// </summary>
    internal static void AttachDescriptionSegments(MenuRow row, StringSelectionRequest request)
    {
        if (row.Desc == null
            || request.OptionDescriptionRules == null
            || !request.OptionDescriptionRules.TryGetValue(row.Option,
                out List<StringSelectionRequest.OptionRule>? rules)
            || rules.Count == 0)
        {
            return;
        }

        row.DescSegments = OptionRuleSegments.Build(row.Desc, rules);
    }

    /// <summary>ImGui text measurement, as the plain function <see cref="OptionRuleSegments.Wrap"/> wants
    /// (tests hand it their own).</summary>
    private static float MeasureText(string text) => ImGui.CalcTextSize(text).X;

    /// <summary>
    /// #336: the shoot panel's Details pane, for a menu that has no selection to hang one off. Spells out
    /// the rules of the row under attention - every name, and either what it does or the note that the
    /// engine will not resolve it - in a scrolling child, so a wall of rules never grows the panel.
    /// </summary>
    private void DrawRuleDetails(StringSelectionRequest request, List<MenuRow> rows, Vector2 at, Vector2 size)
    {
        // The focus survives the mouse leaving the row, but not the row leaving the menu: fall back to the
        // first option that has rules, so the strip says something the moment the menu opens.
        if (_focusedOption == null || !rows.Exists(r => r.Segments != null && r.Option == _focusedOption))
            _focusedOption = rows.Find(r => r.Segments != null)?.Option;

        ImGui.SetCursorPos(at);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.11f, 0.11f, 0.15f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.BeginChild("##StrSelRuleDetails", size, ImGuiChildFlags.Borders);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        if (_focusedOption != null
            && request.OptionRules!.TryGetValue(_focusedOption, out List<StringSelectionRequest.OptionRule>? rules))
        {
            ImGui.TextDisabled("Rules");
            ImGui.Separator();
            foreach (StringSelectionRequest.OptionRule rule in rules)
            {
                ImGui.TextUnformatted(rule.Name);
                ImGui.Indent();
                ImGui.PushTextWrapPos(0f);   // wrap at the strip's right edge
                ImGui.TextDisabled(string.IsNullOrWhiteSpace(rule.Description)
                    ? RuleHoverText.UnknownRuleText
                    : rule.Description);
                ImGui.PopTextWrapPos();
                ImGui.Unindent();
            }
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// #321: hangs a row's companion action off it, if it has one - its button text (the short label plus
    /// the Shift shortcut it shares with the row's own letter) and, when the companion is currently
    /// refused, the reason to show greyed instead of a live button.
    /// </summary>
    internal static void AttachSecondary(MenuRow row, StringSelectionRequest request, char? letter)
    {
        if (request.SecondaryActions == null
            || !request.SecondaryActions.TryGetValue(row.Option, out StringSelectionRequest.SecondaryAction? secondary))
        {
            return;
        }

        row.Secondary = secondary;
        row.SecondaryLabel = letter is char key
            ? $"[^{key}] {secondary.ShortLabel}"   // "^E" = Shift+E; caret keeps it inside Latin-1 (CLAUDE.md)
            : secondary.ShortLabel;

        // A companion that is not in ValidOptions is refused, and InvalidOptions says why.
        if (!request.ValidOptions.Contains(secondary.Option))
        {
            foreach (StringSelectionRequest.InvalidOption invalid in request.InvalidOptions)
            {
                if (invalid.Option == secondary.Option) { row.SecondaryDisabledReason = invalid.Reason; break; }
            }
            row.SecondaryDisabledReason ??= "Not available.";
        }
    }

    /// <summary>
    /// #321: every option that is some other option's companion action. These are drawn as a second button
    /// on their owner's row, so they must be skipped everywhere a row is built from the request's lists -
    /// valid, invalid, and letter assignment alike. Internal for tests.
    /// </summary>
    internal static HashSet<string> CompanionOptions(StringSelectionRequest request)
    {
        var companions = new HashSet<string>(StringComparer.Ordinal);
        if (request.SecondaryActions == null) return companions;
        foreach (StringSelectionRequest.SecondaryAction secondary in request.SecondaryActions.Values)
            companions.Add(secondary.Option);
        return companions;
    }

    /// <summary>
    /// #248 letters, #321-aware: indexed by VALID-OPTION index so the draw loop can keep asking
    /// <c>letters[row.ValidIndex]</c>, but companions are passed over - they share their owner's letter
    /// under Shift, so giving them their own would both burn the ten-letter pool and shift the letters of
    /// every option after them. Internal for tests.
    /// </summary>
    internal static char?[] AssignRowLetters(StringSelectionRequest request, HashSet<string> companions)
    {
        var owners = new List<string>(request.ValidOptions.Count);
        var ownerIndex = new List<int>(request.ValidOptions.Count);
        for (int i = 0; i < request.ValidOptions.Count; i++)
        {
            if (companions.Contains(request.ValidOptions[i])) continue;
            owners.Add(request.ValidOptions[i]);
            ownerIndex.Add(i);
        }

        char?[] ownerLetters = ResolverHotkeys.AssignLetters(owners);
        char?[] letters = new char?[request.ValidOptions.Count];
        for (int k = 0; k < ownerIndex.Count; k++) letters[ownerIndex[k]] = ownerLetters[k];
        return letters;
    }

    /// <summary>#321: the companion action's own subtext, if the request gave it one.</summary>
    private static string? SecondaryDescription(StringSelectionRequest request, MenuRow row)
    {
        if (row.Secondary == null || request.OptionDescriptions == null) return null;
        return request.OptionDescriptions.TryGetValue(row.Secondary.Option, out string? desc)
               && !string.IsNullOrEmpty(desc)
            ? desc
            : null;
    }

    /// <summary>The shared Back button (#248): same look wherever it lands, in the flow or in the footer.</summary>
    private static bool DrawBackButton(float width)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
        bool clicked = ImGui.Button($"Back  {ResolverKeybinds.Back.Parenthetical}##back",
            new Vector2(width, ResolverPanelLayout.ActionRowHeight()));
        ImGui.PopStyleColor();
        return clicked;
    }

    /// <summary>The text drawn on an option's button: the option, prefixed with its letter hotkey when
    /// one was assigned ("[A] Advance"). Game-facing: ASCII only (CLAUDE.md).</summary>
    private static string LabelFor(string option, char? letter) =>
        letter is char key ? $"[{key}] {option}" : option;

    // ── The action pop-up ──────────────────────────────────────────────────────────────────────────────
    private Vector2 _popupSize = new(220f, 220f);

    /// <summary>The player chose Charge but had to move into contact first; take the charge when offered.</summary>
    private bool _chargeIntent;

    /// <summary>True when any living enemy model is within <paramref name="inches"/> (base to base) of any of
    /// the unit's models - i.e. there is something to charge.</summary>
    private static bool EnemyWithin(IUnit unit, float inches)
    {
        ITableState? table = PopupAnchor.TableState;
        if (table == null) return false;
        foreach (IUnit enemy in table.Units.Objects)
        {
            if (enemy.PlayerID == unit.PlayerID) continue;
            foreach (IModel em in enemy.Models)
            {
                if (!em.GetIsAlive() || (em.Position.x == 0f && em.Position.z == 0f)) continue;
                foreach (IModel m in unit.Models)
                {
                    if (!m.GetIsAlive() || (m.Position.x == 0f && m.Position.z == 0f)) continue;
                    float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        m.Position, em.Position, m.BaseShape, m.Facing, em.BaseShape, em.Facing);
                    if (d <= inches) return true;
                }
            }
        }
        return false;
    }

    // ── Compact menu for every other choice (melee weapons, abilities, ...) ─────────────────────────────
    private Vector2 _menuSize = new(320f, 200f);
    private static float MenuWidth => MathF.Max(240f, ResolverPanelLayout.ContentWidth);

    /// <summary>
    /// Any non-action menu as a small window beside the acting unit: title, instructions (wrapped), one
    /// wrapped button per option (its description on hover), a companion button under its row where the
    /// option has one (melee "Hold back"), greyed options with their reason on hover, and Back / right-click
    /// when the menu can be cancelled.
    /// </summary>
    private void DrawCompactMenu(StringSelectionRequest request, TaskCompletionSource<string> tcs)
    {
        ResolverPanelLayout.BeginDocked("##compactmenu");   // the Actions window (right column)

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MenuWidth);
        if (!string.IsNullOrEmpty(request.DisplayName)) ImGui.TextUnformatted(request.DisplayName);
        if (!string.IsNullOrEmpty(request.Instructions) && request.Instructions != request.DisplayName)
            ImGui.TextDisabled(request.Instructions);
        ImGui.PopTextWrapPos();
        ImGui.Separator();

        HashSet<string> companions = CompanionOptions(request);
        string? picked = null;
        for (int i = 0; i < request.ValidOptions.Count; i++)
        {
            string opt = request.ValidOptions[i];
            if (companions.Contains(opt)) continue;
            if (WrappedButton(opt, $"opt{i}", MenuWidth, enabled: true)) picked = opt;
            if (request.OptionDescriptions != null && request.OptionDescriptions.TryGetValue(opt, out string? desc)
                && ImGui.IsItemHovered())
                SetWrappedTooltip(desc);

            // Melee's companion action ("Hold back" with this weapon) sits right under its row.
            if (request.SecondaryActions != null && request.SecondaryActions.TryGetValue(opt, out var sec))
            {
                bool secValid = request.ValidOptions.Contains(sec.Option);
                ImGui.Indent(16f);
                if (WrappedButton(sec.ShortLabel, $"sec{i}", MenuWidth - 16f, secValid)) picked = sec.Option;
                if (!secValid && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    SetWrappedTooltip(request.InvalidOptions.FirstOrDefault(x => x.Option == sec.Option)?.Reason ?? "Not available.");
                ImGui.Unindent(16f);
            }
        }
        foreach (var inv in request.InvalidOptions)
        {
            if (companions.Contains(inv.Option)) continue;
            WrappedButton(inv.Option, $"inv{inv.Option}", MenuWidth, enabled: false);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) SetWrappedTooltip(inv.Reason);
        }

        bool cancelled = false;
        if (request.AllowCancel)
        {
            ImGui.Spacing();
            if (UiButton.Back("Back", new Vector2(120f, 0f)) || PopupAnchor.RightClickOnEmptyTable()) cancelled = true;
        }

        _menuSize = ImGui.GetWindowSize();
        ImGui.End();

        if (picked != null) Complete(tcs, picked);
        else if (cancelled) Complete(tcs, null!);
    }

    private static void SetWrappedTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>A button whose label wraps onto as many lines as it needs within <paramref name="width"/>.</summary>
    private static bool WrappedButton(string label, string id, float width, bool enabled)
    {
        const float padX = 10f, padY = 6f;
        float wrap = width - padX * 2f;
        List<string> lines = WrapText(label, wrap);
        float lineH = ImGui.GetTextLineHeight();
        Vector2 size = new(width, lines.Count * lineH + padY * 2f);
        Vector2 p0 = ImGui.GetCursorScreenPos();

        ImGui.BeginDisabled(!enabled);
        bool clicked = ImGui.InvisibleButton($"##{id}", size);
        ImGui.EndDisabled();
        bool hovered = ImGui.IsItemHovered(), held = ImGui.IsItemActive();

        var style = ImGui.GetStyle();
        Vector4 bg = !enabled ? style.Colors[(int)ImGuiCol.FrameBg] * new Vector4(1, 1, 1, 0.5f)
            : held ? style.Colors[(int)ImGuiCol.ButtonActive]
            : hovered ? style.Colors[(int)ImGuiCol.ButtonHovered]
            : style.Colors[(int)ImGuiCol.Button];
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p0 + size, ImGui.ColorConvertFloat4ToU32(bg), style.FrameRounding);
        uint textCol = ImGui.ColorConvertFloat4ToU32(enabled ? style.Colors[(int)ImGuiCol.Text] : style.Colors[(int)ImGuiCol.TextDisabled]);
        float y = p0.Y + padY;
        foreach (string line in lines)
        {
            dl.AddText(new Vector2(p0.X + padX, y), textCol, line);
            y += lineH;
        }
        if (clicked && enabled) UiSound.Navigate();
        return clicked && enabled;
    }

    private static List<string> WrapText(string text, float maxW)
    {
        var lines = new List<string>();
        foreach (string para in text.Split('\n'))
        {
            string current = "";
            foreach (string word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string attempt = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && ImGui.CalcTextSize(attempt).X > maxW) { lines.Add(current); current = word; }
                else current = attempt;
            }
            lines.Add(current);
        }
        return lines;
    }

    private static string MoveOpt   => FDG.Stages.ChooseActionStage.MOVEMENT_CHOICE_NAME;
    private static string ChargeOpt => FDG.Stages.ChooseActionStage.CHARGE_CHOICE_NAME;
    private static string ShootOpt  => FDG.Stages.ChooseActionStage.SHOOT_CHOICE_NAME;
    private static string PassOpt   => FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME;

    /// <summary>
    /// The activation's choices as a small window beside the unit: Hold (stay and shoot, or just stay when
    /// nothing is in range), Advance and Rush (both the engine's move; Advance keeps the move within the
    /// advance distance), Charge (greyed with "No units in range" when it can't), and Special for anything
    /// else the unit can do (spells, abilities, embarking...). Right-click backs out while the activation is
    /// still untouched; clicking another of your units switches to it.
    /// </summary>
    private void DrawActionPopup(StringSelectionRequest request, TaskCompletionSource<string> tcs)
    {
        bool Valid(string o) => request.ValidOptions.Contains(o);
        string? Reason(string o)
        {
            foreach (var inv in request.InvalidOptions) if (inv.Option == o) return inv.Reason;
            return null;
        }
        bool Known(string o) => o == MoveOpt || o == ChargeOpt || o == ShootOpt || o == PassOpt;
        var extraValid = request.ValidOptions.Where(o => !Known(o)).ToList();
        var extraInvalid = request.InvalidOptions.Where(o => !Known(o.Option)).ToList();

        IUnit? unit = PopupAnchor.Unit;
        float advance = 0f, chargeDist = 0f;
        bool hasMobility = unit != null && unit.GetMobility(out advance, out chargeDist);

        // After the unit has moved the engine asks again. Carry straight on instead of making the player
        // press Hold: a charge move goes into the charge, an advance goes into shooting (click a target or
        // Hold fire). Only when there is nothing to shoot does the pop-up stay, saying "No Targets".
        bool afterMove = !request.AllowCancel && !Valid(MoveOpt);
        // A fresh activation: no charge in progress, and nothing has been declined yet.
        if (request.AllowCancel)
        {
            _chargeIntent = false;
            GuiChooseRangedAttackResolver.HeldFireThisActivation = false;
        }
        // Once the player has backed out of the weapon picker, never push them back into it - that is the
        // Hold -> Hold fire -> Hold loop that made it impossible to simply stand still.
        bool heldFire = GuiChooseRangedAttackResolver.HeldFireThisActivation;
        if (afterMove && _chargeIntent && Valid(ChargeOpt)) { _chargeIntent = false; Complete(tcs, ChargeOpt); return; }
        if (afterMove && !heldFire && Valid(ShootOpt) && !Valid(ChargeOpt)) { _chargeIntent = false; Complete(tcs, ShootOpt); return; }
        if (afterMove && heldFire && Valid(PassOpt) && !Valid(ChargeOpt)) { Complete(tcs, PassOpt); return; }

        ResolverPanelLayout.BeginDocked("##actionpopup");   // the Actions window (right column)

        if (unit != null)
        {
            ImGui.TextUnformatted(unit.Name);
            ImGui.Separator();
        }

        string? picked = null;
        bool cancelled = false;
        // Full width of the Actions window.
        Vector2 btn = new(MathF.Max(200f, ImGui.GetContentRegionAvail().X), ResolverPanelLayout.OptionRowHeight() * 0.8f);

        bool ActionButton(string label, bool enabled, string tooltip)
        {
            ImGui.BeginDisabled(!enabled);
            bool clicked = UiButton.Navigate(label, btn);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
            return clicked && enabled;
        }

        // Hold: stay put and shoot; with nothing in range it just ends the activation.
        bool canShoot = Valid(ShootOpt) && !heldFire, canPass = Valid(PassOpt);
        string holdTip = canShoot ? "Stay in place and shoot."
            : canPass ? "Stay in place. Nothing is in range to shoot."
            : Reason(ShootOpt) ?? Reason(PassOpt) ?? "Not available.";
        if (ActionButton(afterMove ? "End activation" : "Hold", canShoot || canPass,
                afterMove ? "Nothing left to do this activation." : holdTip))
            picked = canShoot ? ShootOpt : PassOpt;

        // Holding is a legal action on its own - a unit may stand still and not shoot at all, and one with
        // no ranged weapon has no other way to hold. Offered as its own button so the player never has to
        // walk into the weapon picker just to come back out of it.
        if (!afterMove && canPass && canShoot &&
            ActionButton("Hold, don't shoot", true, "Stay in place and fire nothing this activation."))
            picked = PassOpt;

        bool canMove = Valid(MoveOpt);
        string moveReason = Reason(MoveOpt) ?? "Not available.";
        string advLabel = hasMobility ? $"Advance ({advance:0.#}\")" : "Advance";
        string rushLabel = hasMobility ? $"Rush ({chargeDist:0.#}\")" : "Rush";   // rush = charge distance
        if (!afterMove && ActionButton(advLabel, canMove, canMove ? "Move, then shoot." : moveReason))
        {
            GuiDefineMovementResolver.NextMoveAdvanceOnly = true;
            picked = MoveOpt;
        }
        if (!afterMove && ActionButton(rushLabel, canMove, canMove ? "Move up to double, no shooting." : moveReason))
        {
            GuiDefineMovementResolver.NextMoveAdvanceOnly = false;
            picked = MoveOpt;
        }

        // Charge: straight away when the engine offers it; otherwise, with an enemy within charge distance, it
        // starts the move (up to the charge distance) and goes into the charge once the unit is in contact.
        bool chargeNow = Valid(ChargeOpt);
        bool chargeByMove = !chargeNow && canMove && unit != null && hasMobility && EnemyWithin(unit, chargeDist);
        string chargeLabel = hasMobility ? $"Charge ({chargeDist:0.#}\")" : "Charge";
        if ((!afterMove || chargeNow) && ActionButton(chargeLabel, chargeNow || chargeByMove,
                chargeNow ? "Charge an enemy and fight in melee."
                : chargeByMove ? "Move into contact with an enemy to charge it."
                : "No units in range"))
        {
            if (chargeNow) picked = ChargeOpt;
            else
            {
                _chargeIntent = true;
                GuiDefineMovementResolver.NextMoveAdvanceOnly = false;
                picked = MoveOpt;
            }
        }
        if (afterMove && !Valid(ShootOpt) && !Valid(ChargeOpt) && extraValid.Count == 0)
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.35f, 1f), "No Targets");

        // Special: everything else the unit can do this activation.
        if (extraValid.Count > 0 || extraInvalid.Count > 0)
        {
            // Shown right here as full-size buttons (embark, spells, abilities...) - no separate pop-out.
            ImGui.Spacing();
            ImGui.TextDisabled("Special");
            int si = 0;
            foreach (string o in extraValid)
            {
                if (WrappedButton(o, $"special{si++}", btn.X, enabled: true)) picked = o;
                string? desc = request.OptionDescriptions != null && request.OptionDescriptions.TryGetValue(o, out string? d) ? d : null;
                if (desc != null && ImGui.IsItemHovered()) SetWrappedTooltip(desc);
            }
            foreach (var inv in extraInvalid)
            {
                WrappedButton(inv.Option, $"special{si++}", btn.X, enabled: false);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) SetWrappedTooltip(inv.Reason);
            }
        }

        if (request.AllowCancel)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Right-click: back");
            ImGui.TextDisabled("Click another unit to switch");
        }

        _popupSize = ImGui.GetWindowSize();
        ImGui.End();

        // A right-CLICK (released without dragging - right-drag turns the 3D camera) on empty table backs out
        // of an untouched activation. Right-clicking a unit opens its card instead.
        if (request.AllowCancel && PopupAnchor.RightClickOnEmptyTable())
            cancelled = true;

        if (picked != null) Complete(tcs, picked);
        else if (cancelled) Complete(tcs, null!);
    }

    // Clicking another of your own units that hasn't activated, while the action pop-up is open on an
    // untouched activation, switches to it: back out of this one and pre-pick that unit.
    private static bool CanSwitchTo(IUnit unit)
    {
        IUnit? current = PopupAnchor.Unit;
        IGameProgress? progress = PopupAnchor.Progress;
        if (current == null || progress == null) return false;
        if (unit.PlayerID != current.PlayerID || unit.ID.Equals(current.ID)) return false;
        return progress.UnactivatedUnits.Any(u => u.ID.Equals(unit.ID));
    }

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        StringSelectionRequest? request;
        bool actionMenu;
        lock (_lock) { request = _request; actionMenu = _actionMenu; }
        if (!actionMenu || request == null || !request.AllowCancel) return null;
        return CanSwitchTo(unit) ? $"Click to activate {unit.Name} instead" : null;
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        StringSelectionRequest? request;
        TaskCompletionSource<string>? tcs;
        bool actionMenu;
        lock (_lock) { request = _request; tcs = _tcs; actionMenu = _actionMenu; }
        if (!actionMenu || request == null || tcs == null || !request.AllowCancel) return;
        if (!CanSwitchTo(unit)) return;
        GuiUnitSelectionResolver.PendingActivationPick = unit;
        Complete(tcs, null!);
    }

    private void Complete(TaskCompletionSource<string> tcs, string choice)
    {
        lock (_lock) { _request = null; _tcs = null; _confirmingPass = false; _actionMenu = false; }
        tcs.SetResult(choice);
    }
}
