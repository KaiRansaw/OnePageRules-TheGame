using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The full-screen "Choose Army" picker. The lobby's Army cell used to be a dropdown, which is fine
/// for five lists and unusable for fifty: a dropdown gives you one narrow column of names, no way to
/// search, and nowhere to show what you are about to pick. This screen gives the choice the room it
/// needs - a search box, a faction filter, the three facts you choose on in aligned columns, and a
/// preview of the selected list beside the table.
///
/// <para>Batch B (wish list "Choose Army"): the table has Army / Faction / Units / Points, over-limit lists
/// grey and last but still pickable; the right side shows the faction strip, points, unit count, a
/// legal / over-limit badge, the list as portrait unit cards (hover = stats, click = full unit), SELECT ARMY.</para>
///
/// <para>It owns no lobby state. <see cref="Open"/> hands it a snapshot of what to show, and a pick
/// leaves through <see cref="OnPicked"/>; the lobby does the loading and the assigning, because it is
/// the one that knows which player slot asked.</para>
/// </summary>
public class ArmyPickerScreen : IAppScreen
{
    /// <summary>An army was chosen. The lobby loads the file and assigns it to the waiting slot.</summary>
    public Action<ArmyCatalogEntry>? OnPicked;

    /// <summary>Closed without choosing.</summary>
    public Action? OnCancel;

    private IReadOnlyList<ArmyCatalogEntry> _armies = Array.Empty<ArmyCatalogEntry>();
    private string _playerName = "";
    private int _pointsLimit;

    private string _search = "";
    private string _faction = "";          // "" = every faction
    private int _sort;                     // 0 name, 1 points low-high, 2 points high-low
    private string _selectedKey = "";
    private bool _focusSearch;

    /// <summary>
    /// Point the screen at one slot's choice. Called just before navigating to it, so the search box
    /// and the selection always start fresh rather than showing the last slot's leftovers.
    /// </summary>
    public void Open(string playerName, IReadOnlyList<ArmyCatalogEntry> armies, int pointsLimit,
        string currentKey)
    {
        _playerName  = playerName;
        _armies      = armies;
        _pointsLimit = pointsLimit;
        _selectedKey = currentKey;   // open on the list the slot already has, if it has one
        _search      = "";
        _faction     = "";
        _sort        = 0;
        _focusSearch = true;
    }

    public void OnShown() => _focusSearch = true;

    public void Draw(int screenW, int screenH)
    {
        float ws = MathF.Min(screenW / 1920f, screenH / 1080f);
        float scale = 1.4f * ws;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 7f));
        ImGui.Begin("##ArmyPicker",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);
        ImGui.SetWindowFontScale(scale);

        float margin   = 14f;
        float headerH  = MathF.Max(48f, ImGui.GetFontSize() * 2.2f);
        float previewW = MathF.Max(420f, screenW * 0.45f);
        float tableW   = screenW - previewW - margin * 3f;
        float contentY = margin + headerH + margin;
        float contentH = MathF.Max(120f, screenH - contentY - margin);

        // ── Header: who this is for, and the way out ──────────────────────────
        ImGui.SetCursorPos(new Vector2(margin, margin));
        ImGui.BeginChild("##pickheader", new Vector2(screenW - margin * 2f, headerH),
            ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.SetWindowFontScale(scale);
        ImGui.SetCursorPosY((headerH - ImGui.GetFontSize() * 1.4f) * 0.5f);
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(_playerName)
            ? "CHOOSE ARMY"
            : $"CHOOSE ARMY - {_playerName}");

        float closeW = (ImGui.CalcTextSize("CLOSE").X + 40f) * ws + UiButton.CapsWidth(headerH - 12f);
        ImGui.SetCursorPos(new Vector2(screenW - margin * 2f - closeW - 6f, 5f));
        if (UiButton.Back("CLOSE", new Vector2(closeW, headerH - 12f)))
            OnCancel?.Invoke();
        if (ImGui.IsItemHovered()) UiText.Tooltip("Back to the lobby without changing this seat's army.");
        ImGui.EndChild();

        // ── Left: search, filters, the table ──────────────────────────────────
        ImGui.SetCursorPos(new Vector2(margin, contentY));
        ImGui.BeginChild("##picktable", new Vector2(tableW, contentH), ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(scale);

        // Focused on arrival: choosing an army is type-then-Enter, no reach for the mouse.
        if (_focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusSearch = false;
        }
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.55f);
        ImGui.InputTextWithHint("##search", "Search army or faction...", ref _search, 64);
        if (ImGui.IsItemHovered()) UiText.Tooltip("Type part of an army's name or faction to narrow the list.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.45f);
        DrawFactionFilter();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        DrawSortCombo();

        List<ArmyCatalogEntry> shown = Filtered();

        if (_armies.Count == 0)
            UiText.Disabled("No armies in this game type's folder yet.");
        else if (shown.Count == 0)
            UiText.Disabled("Nothing matches that search.");
        else if (ImGui.BeginTable("##armies", 4,
                     ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV |
                     ImGuiTableFlags.SizingStretchProp,
                     new Vector2(0f, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() * 1.4f)))
        {
            ImGui.TableSetupColumn("Army", ImGuiTableColumnFlags.WidthStretch, 0.48f);
            ImGui.TableSetupColumn("Faction", ImGuiTableColumnFlags.WidthStretch, 0.28f);
            ImGui.TableSetupColumn("Units", ImGuiTableColumnFlags.WidthStretch, 0.10f);
            ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthStretch, 0.14f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (ArmyCatalogEntry entry in shown)
            {
                bool over = _pointsLimit > 0 && entry.Points > _pointsLimit;
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (over) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);

                // One Selectable across the row, so the whole line is the hit target. A single click
                // previews; a double click is the shortcut past the Select button.
                bool clicked = ImGui.Selectable($"{entry.FileName}##{entry.Path}", entry.Key == _selectedKey,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
                bool rowHovered = ImGui.IsItemHovered();
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(entry.Faction);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(entry.UnitCount.ToString());
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(entry.Points.ToString());
                if (rowHovered)
                    UiText.Tooltip(over ? $"Over the {_pointsLimit} pt limit - you can still pick it. Click to preview, double-click to pick."
                                        : "Click to preview, double-click to pick.");
                if (over) ImGui.PopStyleColor();

                if (clicked)
                {
                    _selectedKey = entry.Key;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OnPicked?.Invoke(entry);
                }
            }
            ImGui.EndTable();
        }

        UiText.Disabled(shown.Count == _armies.Count
            ? $"{_armies.Count} armies"
            : $"{shown.Count} of {_armies.Count} armies");

        ImGui.EndChild();

        // ── Right: what you are about to pick ─────────────────────────────────
        ImGui.SetCursorPos(new Vector2(margin * 2f + tableW, contentY));
        ImGui.BeginChild("##pickpreview", new Vector2(previewW, contentH), ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(scale);
        DrawPreview(shown);
        ImGui.EndChild();

        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private readonly UnitPortraitCards _cards = new();
    private string? _loadedPath;
    private FDG.ArmyBuilding.BuiltArmyFile? _loaded;
    private IReadOnlyList<string> _problems = Array.Empty<string>();

    /// <summary>The selected file, read once per selection (the catalog only keeps name, faction, points
    /// and unit count), with its rule problems from the same check the lobby's launch gate runs.</summary>
    private FDG.ArmyBuilding.BuiltArmyFile? Load(string path)
    {
        if (path == _loadedPath) return _loaded;
        _loadedPath = path;
        _loaded = null;
        _problems = Array.Empty<string>();
        try
        {
            _loaded = System.Text.Json.JsonSerializer.Deserialize<FDG.ArmyBuilding.BuiltArmyFile>(
                File.ReadAllText(path), FDG.Rules.Serialization.RuleJson.Options);
            if (_loaded != null)
                _problems = FDG.ArmyBuilding.LaunchGate.OverridableProblems(new[] { ("", (FDG.SaveLoad.ArmyListFile?)_loaded) })
                    .Select(p => p.StartsWith(": ", StringComparison.Ordinal) ? p[2..] : p).ToList();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        return _loaded;
    }

    /// <summary>The badge: what it says, and whether it is the good kind.</summary>
    internal static (string Text, bool Ok) Badge(int points, int pointsLimit, int problemCount)
    {
        if (pointsLimit > 0 && points > pointsLimit) return ($"OVER LIMIT ({points} / {pointsLimit} pts)", false);
        if (problemCount > 0) return ($"{problemCount} PROBLEM{(problemCount == 1 ? "" : "S")}", false);
        return ("LEGAL", true);
    }

    private void DrawPreview(List<ArmyCatalogEntry> shown)
    {
        ArmyCatalogEntry? selected = null;
        foreach (ArmyCatalogEntry e in shown)
            if (e.Key == _selectedKey) { selected = e; break; }

        if (selected is not { } army)
        {
            UiText.Colored(ImGuiTheme.HeaderAccent, "PREVIEW", wrap: false);
            ImGui.Separator();
            UiText.Disabled("Pick a row to see it here.");
            return;
        }

        // Art space: no faction art exists in the data yet, so the strip carries the faction's name.
        Vector2 p0 = ImGui.GetCursorScreenPos();
        float stripH = ImGui.GetTextLineHeight() * 2.2f;
        Vector2 p1 = p0 + new Vector2(ImGui.GetContentRegionAvail().X, stripH);
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, p1, ImGui.GetColorU32(new Vector4(0.16f, 0.24f, 0.34f, 1f)), 6f);
        Vector2 ts = ImGui.CalcTextSize(army.Faction);
        dl.AddText(new Vector2(p0.X + 12f, p0.Y + (stripH - ts.Y) / 2f), ImGui.GetColorU32(ImGuiCol.Text), army.Faction);
        ImGui.Dummy(p1 - p0);

        ImGui.TextUnformatted(army.FileName);
        // #421: the army's stored name only when it says something different from the file's.
        if (!string.Equals(army.Name, army.FileName, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.SameLine();
            UiText.Disabled($"(saved as {army.Name})", wrap: false);
        }

        FDG.ArmyBuilding.BuiltArmyFile? file = Load(army.Path);
        UiText.Disabled($"{army.Points} pts   -   {army.UnitCount} unit{(army.UnitCount == 1 ? "" : "s")}", wrap: false);
        ImGui.SameLine();
        (string badge, bool ok) = Badge(army.Points, _pointsLimit, _problems.Count);
        UiText.Colored(ok ? new Vector4(0.45f, 0.85f, 0.45f, 1f) : new Vector4(0.95f, 0.55f, 0.35f, 1f), "[" + badge + "]", wrap: false);
        if (ImGui.IsItemHovered())
            UiText.TooltipWrapped(ok
                ? "Within the points limit, and the Army Forge check finds no problems."
                : (_pointsLimit > 0 && army.Points > _pointsLimit
                    ? $"Over this game's {_pointsLimit} pt limit - you can still pick it, but LAUNCH stays greyed until the limit is raised.\n"
                    : "")
                  + string.Join("\n", _problems));

        float btnH = MathF.Max(48f, ImGui.GetFontSize() * 2.2f);
        float cardsH = MathF.Max(ImGui.GetFrameHeight() * 3f,
            ImGui.GetContentRegionAvail().Y - btnH - ImGui.GetStyle().ItemSpacing.Y * 2f);
        _cards.Draw("pickcards", file, new Vector2(ImGui.GetContentRegionAvail().X, cardsH),
            "Couldn't read this army's units.");

        if (UiButton.Confirm("SELECT ARMY", new Vector2(-1f, btnH)))
            OnPicked?.Invoke(army);
        if (ImGui.IsItemHovered()) UiText.Tooltip("Give this army to the seat and go back to the lobby.");
    }

    private void DrawFactionFilter()
    {
        List<string> factions = new() { "" };
        foreach (ArmyCatalogEntry e in _armies)
            if (!string.IsNullOrWhiteSpace(e.Faction) && !factions.Contains(e.Faction))
                factions.Add(e.Faction);
        factions.Sort(StringComparer.OrdinalIgnoreCase);

        bool open = UiCombo.Begin("##faction", _faction.Length == 0 ? "All factions" : _faction);
        if (!open)
        {
            if (ImGui.IsItemHovered()) UiText.Tooltip("Show only one faction's armies.");
            return;
        }
        foreach (string f in factions)
        {
            string label = f.Length == 0 ? "All factions" : f;
            if (ImGui.Selectable(label, f == _faction)) _faction = f;
            if (ImGui.IsItemHovered()) UiText.Tooltip(f.Length == 0 ? "Show every faction's armies." : $"Show only {f} armies.");
        }
        ImGui.EndCombo();
    }

    private void DrawSortCombo()
    {
        string[] names = { "Sort: Name", "Sort: Points up", "Sort: Points down" };
        string[] tips = { "A to Z by name.", "Smallest army first.", "Biggest army first." };
        bool open = UiCombo.Begin("##sort", names[Math.Clamp(_sort, 0, names.Length - 1)]);
        if (!open)
        {
            if (ImGui.IsItemHovered()) UiText.Tooltip("How the list is ordered. Lists over the points limit always come last.");
            return;
        }
        for (int i = 0; i < names.Length; i++)
        {
            if (ImGui.Selectable(names[i], i == _sort)) _sort = i;
            if (ImGui.IsItemHovered()) UiText.Tooltip(tips[i]);
        }
        ImGui.EndCombo();
    }

    private List<ArmyCatalogEntry> Filtered()
    {
        string needle = _search.Trim();
        IEnumerable<ArmyCatalogEntry> q = _armies.Where(e =>
            (needle.Length == 0
             || e.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase)
             || e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
             || e.Faction.Contains(needle, StringComparison.OrdinalIgnoreCase))
            && (_faction.Length == 0
                || string.Equals(e.Faction, _faction, StringComparison.OrdinalIgnoreCase)));

        // Within every sort, lists that fit the points limit come first: an over-limit list is still
        // pickable, but it is never what you are looking for by default.
        q = _sort switch
        {
            1 => q.OrderBy(e => _pointsLimit > 0 && e.Points > _pointsLimit).ThenBy(e => e.Points),
            2 => q.OrderBy(e => _pointsLimit > 0 && e.Points > _pointsLimit).ThenByDescending(e => e.Points),
            _ => q.OrderBy(e => _pointsLimit > 0 && e.Points > _pointsLimit)
                  .ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase),
        };
        return q.ToList();
    }
}
