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
        float previewW = MathF.Max(320f, screenW * 0.28f);
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

        float closeW = (ImGui.CalcTextSize("CLOSE").X + 40f) * ws;
        ImGui.SetCursorPos(new Vector2(screenW - margin * 2f - closeW - 6f, 5f));
        if (UiButton.Back("CLOSE", new Vector2(closeW, headerH - 12f)))
            OnCancel?.Invoke();
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
        else if (ImGui.BeginTable("##armies", 3,
                     ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV |
                     ImGuiTableFlags.SizingStretchProp,
                     new Vector2(0f, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() * 1.4f)))
        {
            ImGui.TableSetupColumn("Army", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableSetupColumn("Faction", ImGuiTableColumnFlags.WidthStretch, 0.30f);
            ImGui.TableSetupColumn("Points", ImGuiTableColumnFlags.WidthStretch, 0.15f);
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
                bool clicked = ImGui.Selectable($"{entry.Name}##{entry.Path}", entry.Key == _selectedKey,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(entry.Faction);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(entry.Points.ToString());
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

    private void DrawPreview(List<ArmyCatalogEntry> shown)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.HeaderAccent);
        ImGui.TextUnformatted("PREVIEW");
        ImGui.PopStyleColor();
        ImGui.Separator();

        ArmyCatalogEntry? selected = null;
        foreach (ArmyCatalogEntry e in shown)
            if (e.Key == _selectedKey) { selected = e; break; }

        if (selected is not { } army)
        {
            UiText.Disabled("Pick a row to see it here.");
            return;
        }

        ImGui.TextUnformatted(army.Name);
        ImGui.Spacing();
        UiText.Disabled($"Faction: {army.Faction}");
        UiText.Disabled($"Points: {army.Points}");
        if (_pointsLimit > 0 && army.Points > _pointsLimit)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.90f, 0.45f, 0.35f, 1f));
            UiText.Wrapped($"Over the {_pointsLimit} pt limit for this game - you can still pick it.");
            ImGui.PopStyleColor();
        }
        ImGui.Spacing();
        UiText.Wrapped(System.IO.Path.GetFileName(army.Path));

        // The unit list belongs here, and is not here yet: the army catalog stores a file's name,
        // faction and points, not its roster, so showing units means opening every .fdgarmy on the
        // folder scan. That is a catalog change, not a picker change.
        ImGui.Spacing();
        UiText.Disabled("[ unit list: not read by the catalog yet ]");

        // The button pins to the bottom of the panel: the one thing this screen is for.
        float btnH = MathF.Max(48f, ImGui.GetFontSize() * 2.2f);
        float remaining = ImGui.GetContentRegionAvail().Y - btnH;
        if (remaining > 0f) ImGui.Dummy(new Vector2(1f, remaining));
        if (UiButton.Confirm("SELECT ARMY", new Vector2(-1f, btnH)))
            OnPicked?.Invoke(army);
    }

    private void DrawFactionFilter()
    {
        List<string> factions = new() { "" };
        foreach (ArmyCatalogEntry e in _armies)
            if (!string.IsNullOrWhiteSpace(e.Faction) && !factions.Contains(e.Faction))
                factions.Add(e.Faction);
        factions.Sort(StringComparer.OrdinalIgnoreCase);

        if (!ImGui.BeginCombo("##faction", _faction.Length == 0 ? "All factions" : _faction)) return;
        foreach (string f in factions)
        {
            string label = f.Length == 0 ? "All factions" : f;
            if (ImGui.Selectable(label, f == _faction)) _faction = f;
        }
        ImGui.EndCombo();
    }

    private void DrawSortCombo()
    {
        string[] names = { "Sort: Name", "Sort: Points up", "Sort: Points down" };
        if (!ImGui.BeginCombo("##sort", names[Math.Clamp(_sort, 0, names.Length - 1)])) return;
        for (int i = 0; i < names.Length; i++)
            if (ImGui.Selectable(names[i], i == _sort)) _sort = i;
        ImGui.EndCombo();
    }

    private List<ArmyCatalogEntry> Filtered()
    {
        string needle = _search.Trim();
        IEnumerable<ArmyCatalogEntry> q = _armies.Where(e =>
            (needle.Length == 0
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
                  .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        return q.ToList();
    }
}
