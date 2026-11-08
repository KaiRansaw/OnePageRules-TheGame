using System.Numerics;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Map Maker, from the main menu (Map Maker Mockup, 2026-11-07): your maps as pictures, grouped by table
/// size - 6' x 4' (Grimdark Future, Age of Fantasy) and 4' x 4' (Firefight, Skirmish, both Quests) - with
/// "+ New Map" at the top; on the right a big preview of the selected map, the games it is used in, its
/// terrain and objective counts, and Delete / Copy / Edit.
///
/// <para>
/// A map is terrain and objective markers only. Deployment is picked in the lobby (Kai, 2026-11-07), so
/// zones an older map still carries in its file are ignored by the game and not shown here.
/// </para>
///
/// <para>Rental layout (plain ImGui). The grouping and the copy name are pure helpers, pinned by tests.</para>
/// </summary>
public sealed class MapMakerScreen : IAppScreen
{
    public Action? OnBack;

    private readonly MapEditor _editor = new();
    private IReadOnlyList<string> _maps = Array.Empty<string>();

    /// <summary>
    /// Each map's contents, read once per Reload. The list shows a map's OWN name, not its filename -
    /// renaming a map used to change nothing anybody could see (Kai, 2026-11-02), because this list
    /// read the filename and saving never renamed the file.
    /// </summary>
    private readonly Dictionary<string, TerrainLayoutFile?> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private string? _selected;
    private string _message = "";

    private const string DeletePopup = "Delete this map?";

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);

    public void OnShown()
    {
        _message = "";
        Reload();
    }

    private void Reload()
    {
        _maps = MapPaths.List();
        _layouts.Clear();
        if (_selected != null && !_maps.Contains(_selected)) _selected = null;
        _selected ??= _maps.Count > 0 ? _maps[0] : null;
    }

    private TerrainLayoutFile? Layout(string path)
    {
        if (_layouts.TryGetValue(path, out TerrainLayoutFile? cached)) return cached;
        TerrainLayoutFile? loaded = TerrainLayoutLoader.TryLoadFromFile(path, out _);
        _layouts[path] = loaded;
        return loaded;
    }

    /// <summary>What a map is called: its own name if it has one, else the file's.</summary>
    private string NameOf(string path)
    {
        TerrainLayoutFile? layout = Layout(path);
        return layout != null && !string.IsNullOrWhiteSpace(layout.Name)
            ? layout.Name
            : Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>A map's table, 6' x 4' for a map drawn before table sizes existed (which is what it was).</summary>
    private static (float W, float H) TableOf(TerrainLayoutFile? layout) =>
        layout is { TableWidthInches: > 0f, TableHeightInches: > 0f }
            ? (layout.TableWidthInches, layout.TableHeightInches)
            : (GameWideConstants.STANDARD_TABLE_WIDTH_INCHES, GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES);

    /// <summary>
    /// The table sizes the list is grouped under, biggest first, each with the games played on it - from
    /// the engine's own table sizes, so a new game on a new table gets its own group.
    /// </summary>
    internal static IReadOnlyList<(float W, float H, string Games)> TableGroups() =>
        Enum.GetValues<EGameSystem>()
            .Select(GameWideConstants.TableSizeFor).Distinct()
            .OrderByDescending(t => t.Width * t.Height)
            .Select(t => (t.Width, t.Height, MapEditor.SystemsOnTable(t.Width, t.Height)))
            .ToList();

    /// <summary>
    /// A name for a copy of <paramref name="name"/> that no map in <paramref name="taken"/> already has:
    /// "Crossroads (copy)", then "Crossroads (copy 2)" and so on. Copying a copy does not stack
    /// "(copy) (copy)" - it numbers the copy instead.
    /// </summary>
    internal static string CopyName(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        string stem = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s*\(copy( \d+)?\)$", "");
        if (stem.Length == 0) stem = "Map";
        string candidate = $"{stem} (copy)";
        for (int n = 2; used.Contains(candidate); n++) candidate = $"{stem} (copy {n})";
        return candidate;
    }

    public void Draw(int screenW, int screenH)
    {
        // The editor takes the whole screen while it is open.
        if (_editor.IsOpen)
        {
            _editor.Draw(screenW, screenH);
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##mapmaker",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        float margin = 24f;
        bool popupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

        // ── Header: title, what a map is, + New Map ──
        float headerH = 64f;
        ImGui.SetCursorPos(new Vector2(margin, (headerH - ImGui.GetTextLineHeight() * 1.4f) / 2f));
        ImGui.PushFont(RaylibRenderer.LargeFont);
        UiText.Colored(Accent, "MAP MAKER", wrap: false);
        ImGui.PopFont();
        ImGui.SameLine(0f, 20f);
        UiText.Colored(Dim, "Terrain and objectives. Deployment is picked in the lobby.", wrap: false);

        Vector2 newBtn = new(170f, 42f);
        ImGui.SetCursorPos(new Vector2(screenW - margin - newBtn.X, (headerH - newBtn.Y) / 2f));
        if (UiButton.Confirm("+ New Map", newBtn))
        {
            _editor.OnSaved ??= saved => { Reload(); _selected = saved; _message = "Map saved."; };
            _editor.OpenNew();
        }
        if (ImGui.IsItemHovered()) UiText.Tooltip("Draw a new map from an empty table.");
        ImGui.GetWindowDrawList().AddLine(new Vector2(0, headerH), new Vector2(screenW, headerH),
            ImGui.GetColorU32(new Vector4(0.29f, 0.29f, 0.29f, 1f)));

        // ── Body: grouped picture grid left, selected map right ──
        float footerH = 72f;
        float bodyY = headerH + 14f;
        float bodyH = screenH - bodyY - footerH - 6f;
        float detailW = Math.Clamp(screenW * 0.28f, 340f, 520f);
        float gridW = screenW - margin * 2f - detailW - 16f;

        ImGui.SetCursorPos(new Vector2(margin, bodyY));
        ImGui.BeginChild("##maplist", new Vector2(gridW, bodyH));
        if (_maps.Count == 0)
            UiText.Wrapped("No maps yet. Press + New Map to draw one - it will show up in the map picker when you start a game.");
        DrawGroups();
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(screenW - margin - detailW, bodyY));
        ImGui.BeginChild("##mapdetail", new Vector2(detailW, bodyH), ImGuiChildFlags.Borders);
        DrawDetail();
        DrawDeletePopup();
        ImGui.EndChild();

        // ── Footer: Back, and the last message ──
        Vector2 btn = new(140f, 48f);
        float by = screenH - footerH + (footerH - btn.Y) / 2f;
        ImGui.SetCursorPos(new Vector2(margin, by));
        if (UiButton.Back("Back", btn) || (!popupOpen && ImGui.IsKeyPressed(ImGuiKey.Escape, false))) OnBack?.Invoke();
        if (_message.Length > 0)
        {
            ImGui.SameLine(0f, 16f);
            ImGui.SetCursorPosY(by + (btn.Y - ImGui.GetTextLineHeight()) / 2f);
            UiText.Colored(Dim, _message, wrap: false);
        }

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawGroups()
    {
        const float gap = 12f;
        float avail = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)((avail + gap) / (220f + gap)));
        float tileW = (avail - gap * (columns - 1)) / columns;

        var groups = TableGroups();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((float gw, float gh, string games) in groups)
        {
            List<string> maps = _maps.Where(p => SameTable(TableOf(Layout(p)), (gw, gh))).ToList();
            placed.UnionWith(maps);
            DrawGroup($"{gw / 12f:0.#}' x {gh / 12f:0.#}' MAPS - {games.ToUpperInvariant()}", maps, columns, tileW, gap);
        }
        // A map drawn on a table no game uses (a hand-edited file) still gets listed, so it can be fixed or deleted.
        List<string> odd = _maps.Where(p => !placed.Contains(p)).ToList();
        if (odd.Count > 0) DrawGroup("OTHER TABLE SIZES", odd, columns, tileW, gap);
    }

    private static bool SameTable((float W, float H) a, (float W, float H) b) =>
        MathF.Abs(a.W - b.W) < 0.01f && MathF.Abs(a.H - b.H) < 0.01f;

    private void DrawGroup(string title, List<string> maps, int columns, float tileW, float gap)
    {
        ImGui.Spacing();
        UiText.Colored(new Vector4(0.82f, 0.82f, 0.82f, 1f), title, wrap: false);
        if (maps.Count == 0)
        {
            UiText.Disabled("No maps for this table yet.");
            ImGui.Spacing();
            return;
        }

        float lineH = ImGui.GetTextLineHeight();
        for (int i = 0; i < maps.Count; i++)
        {
            string path = maps[i];
            TerrainLayoutFile? layout = Layout(path);
            (float tw, float th) = TableOf(layout);
            float picH = tileW / 1.5f;
            float tileH = picH + lineH * 2.4f + 8f;

            if (i % columns != 0) ImGui.SameLine(0f, gap);
            ImGui.PushID(path);
            Vector2 p0 = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##tile", new Vector2(tileW, tileH));
            bool hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) _selected = path;
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenEditor(path);

            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            bool sel = path == _selected;
            dl.AddRectFilled(p0, p0 + new Vector2(tileW, picH), ImGui.GetColorU32(new Vector4(0.16f, 0.16f, 0.16f, 1f)));
            if (layout != null)
                MapThumbnail.Draw(dl, p0 + new Vector2(3f, 3f), new Vector2(tileW - 6f, picH - 6f), tw, th, layout,
                    ETableBackground.Forest, MapThumbnail.EFallbackZones.None);
            else
                dl.AddText(p0 + new Vector2(8f, picH / 2f - lineH / 2f), ImGui.GetColorU32(new Vector4(1f, 0.45f, 0.4f, 1f)),
                    "could not be read");
            dl.AddRect(p0, p0 + new Vector2(tileW, picH),
                ImGui.GetColorU32(sel ? ImGuiTheme.HeaderAccent : hovered ? new Vector4(0.8f, 0.8f, 0.85f, 1f) : new Vector4(0.35f, 0.35f, 0.35f, 1f)),
                0f, ImDrawFlags.None, sel ? 3f : 1f);
            dl.PushClipRect(p0, p0 + new Vector2(tileW, tileH), true);
            dl.AddText(p0 + new Vector2(2f, picH + 4f), ImGui.GetColorU32(ImGuiCol.Text), NameOf(path));
            if (layout != null)
                dl.AddText(p0 + new Vector2(2f, picH + 4f + lineH * 1.1f), ImGui.GetColorU32(ImGuiCol.TextDisabled),
                    $"{layout.Pieces.Count} terrain, {layout.Objectives.Count} objectives");
            dl.PopClipRect();
            if (hovered) UiText.Tooltip($"{NameOf(path)}\nClick to preview, double-click to edit.");
            ImGui.PopID();
        }
        ImGui.Spacing();
    }

    private void OpenEditor(string path)
    {
        // Take the path the save ACTUALLY landed at: renaming a map now renames its file, so the old path
        // is gone and Reload would otherwise drop the selection to whatever sorts first.
        _editor.OnSaved ??= saved => { Reload(); _selected = saved; _message = "Map saved."; };
        _editor.OpenExisting(path);
    }

    private void DrawDetail()
    {
        if (_selected == null)
        {
            UiText.Disabled("Pick a map on the left, or press + New Map.");
            return;
        }
        TerrainLayoutFile? layout = Layout(_selected);
        (float tw, float th) = TableOf(layout);
        float inner = ImGui.GetContentRegionAvail().X;
        float picH = MathF.Min(inner * th / tw, ImGui.GetContentRegionAvail().Y * 0.45f);
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(inner, picH));
        if (layout != null)
            MapThumbnail.Draw(ImGui.GetWindowDrawList(), p0, new Vector2(inner, picH), tw, th, layout,
                ETableBackground.Forest, MapThumbnail.EFallbackZones.None);

        ImGui.Spacing();
        ImGui.PushFont(RaylibRenderer.LargeFont);
        UiText.Wrapped(NameOf(_selected));
        ImGui.PopFont();

        if (layout == null)
        {
            UiText.Colored(new Vector4(1f, 0.45f, 0.4f, 1f), "This map file could not be read. You can still delete it.");
        }
        else if (ImGui.BeginTable("##mapfacts", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Terrain pieces").X + 16f);
            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);
            void Row(string k, string v)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); UiText.Disabled(k, wrap: false);
                ImGui.TableSetColumnIndex(1); ImGui.PushTextWrapPos(0f); ImGui.TextUnformatted(v); ImGui.PopTextWrapPos();
            }
            Row("Table", $"{tw / 12f:0.#}' x {th / 12f:0.#}'");
            string games = MapEditor.SystemsOnTable(tw, th);
            Row("Used in", games.Length > 0 ? games : "no game plays on this table size");
            Row("Terrain pieces", layout.Pieces.Count.ToString());
            Row("Objectives", layout.Objectives.Count > 0 ? layout.Objectives.Count.ToString()
                : "none - the game rolls D3+2 and places them");
            ImGui.EndTable();
            if (layout.DeploymentZones.Count > 0)
                UiText.Disabled("This map was drawn with deployment zones. They are ignored: deployment is "
                    + "picked in the lobby. Saving the map in the editor removes them.");
        }

        float rowH = 48f;
        float y = ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y - rowH;
        if (ImGui.GetCursorPosY() < y) ImGui.SetCursorPosY(y);
        float third = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
        if (UiButton.Back("Delete", new Vector2(third, rowH))) ImGui.OpenPopup(DeletePopup);
        if (ImGui.IsItemHovered()) UiText.Tooltip("Delete this map from disk. Asks first.");
        ImGui.SameLine();
        ImGui.BeginDisabled(layout == null);
        if (UiButton.Navigate("Copy", new Vector2(third, rowH))) CopySelected(layout!);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.Tooltip("Save a copy of this map under a new name, to change without losing the original.");
        ImGui.SameLine();
        if (UiButton.Confirm("Edit", new Vector2(third, rowH))) OpenEditor(_selected);
        ImGui.EndDisabled();
    }

    private void CopySelected(TerrainLayoutFile layout)
    {
        try
        {
            // A fresh read, so the copy is the file on disk and not the cached object being re-saved.
            TerrainLayoutFile? copy = TerrainLayoutLoader.TryLoadFromFile(_selected!, out string? error);
            if (copy == null) { _message = "Couldn't copy it: " + error; return; }
            copy.Name = CopyName(NameOf(_selected!), _maps.Select(NameOf));
            string saved = MapPaths.Save(copy, null);
            Reload();
            _selected = saved;
            _message = $"Copied as \"{copy.Name}\".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _message = "Couldn't copy it: " + ex.Message;
        }
    }

    private void DrawDeletePopup()
    {
        if (!ImGui.BeginPopupModal(DeletePopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            return;
        ImGui.TextUnformatted($"Delete \"{(_selected != null ? NameOf(_selected) : "this map")}\"?");
        UiText.Disabled("The map file is removed from disk. This can't be undone.", wrap: false);
        ImGui.Spacing();
        if (UiButton.Back("Keep it", new Vector2(140, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        if (UiButton.Confirm("Delete", new Vector2(140, 0)))
        {
            if (_selected != null)
            {
                try { File.Delete(_selected); _message = "Map deleted."; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _message = "Couldn't delete it: " + ex.Message; }
                _selected = null;
                Reload();
            }
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
