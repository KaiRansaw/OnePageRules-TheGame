using System.Numerics;
using FDG;
using FDG.SaveLoad;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Picking the table you will fight on: a scrollable grid of map thumbnails, with <b>Random</b> first.
/// Kai's mock, 2026-11-02 (screen "1.21.1"), reached from the lobby's Select Map button.
///
/// <para>
/// <b>This replaces the lobby's terrain and objective settings</b>, which is a real rules change and is
/// recorded as a house rule: the books have the players <i>alternate placing terrain</i> and
/// <i>alternate placing objectives</i> before the game, and a chosen map does neither. Kai's call, and
/// the trade is deliberate - a map you drew is a better table than one assembled by two people clicking,
/// and the pre-game placement steps are the slowest part of starting a game.
/// </para>
///
/// <para>
/// <b>Random is always the first tile</b>, and it is not a courtesy. Without it the grid is empty until
/// somebody has drawn a map, and an empty picker means you cannot start a game at all - the picker would
/// be a wall rather than a choice. Random generates terrain the way the game always has.
/// </para>
///
/// <para>
/// Deliberately plain under the working agreement; the grid and the scroll are throwaway. What is not
/// throwaway is which maps exist and what a tile tells you about one, which is why a tile shows the
/// table as it will actually look - terrain, deployment zones and objective markers - rather than a
/// name in a list.
/// </para>
/// </summary>
public sealed class MapPickerScreen : IAppScreen
{
    /// <summary>A map was chosen: its path, or null for Random.</summary>
    public Action<string?>? OnChosen;

    public Action? OnBack;

    private IReadOnlyList<string> _paths = Array.Empty<string>();
    private readonly Dictionary<string, TerrainLayoutFile?> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private string? _selected;
    private bool _randomSelected = true;

    private EGameSystem _system = EGameSystem.GrimdarkFuture;
    private ETableBackground _background = ETableBackground.Forest;
    private bool _horde;

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.78f, 0.3f, 1f);

    /// <summary>
    /// Opened from the lobby, which knows the table this picker is choosing for. The system decides the
    /// table's size, so a 48x48 Firefight map is drawn square and a 72x48 Grimdark Future one wide.
    /// </summary>
    public void Open(EGameSystem system, ETableBackground background, bool horde, string? current)
    {
        _system = system;
        _background = background;
        _horde = horde;
        _selected = current;
        _randomSelected = string.IsNullOrEmpty(current);
        Reload();
    }

    public void OnShown() => Reload();

    private void Reload()
    {
        _paths = FdgRaylib.SaveLoad.MapPaths.List();
        _layouts.Clear();
        if (_selected != null && !_paths.Contains(_selected))
        {
            // The chosen map was deleted or renamed while we were away. Fall back to Random rather than
            // launching onto a map that is not there.
            _selected = null;
            _randomSelected = true;
        }
    }

    /// <summary>
    /// A map's contents, read once and kept. Re-reading every file every frame would open a dozen files
    /// sixty times a second for a screen that only changes when somebody saves a map.
    /// </summary>
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

    /// <summary>A map's own table, falling back to the game's for a map written before sizes existed.</summary>
    private static (float W, float H) TableOf(TerrainLayoutFile? layout, float fallbackW, float fallbackH) =>
        layout is { TableWidthInches: > 0f, TableHeightInches: > 0f }
            ? (layout.TableWidthInches, layout.TableHeightInches)
            : (fallbackW, fallbackH);

    /// <summary>
    /// Whether this map was drawn for the table the game being set up plays on. Maps that were not are
    /// shown anyway and marked, rather than hidden: a map is somebody's work, and silently omitting it
    /// from the grid looks like the game lost it.
    /// </summary>
    private bool FitsThisGame(string path)
    {
        (float gw, float gh) = GameWideConstants.TableSizeFor(_system);
        (float mw, float mh) = TableOf(Layout(path), gw, gh);
        return MathF.Abs(mw - gw) < 0.01f && MathF.Abs(mh - gh) < 0.01f;
    }

    private static string SizeLabel(TerrainLayoutFile layout) =>
        $"{layout.TableWidthInches / 12f:0.#}' x {layout.TableHeightInches / 12f:0.#}'";

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##mappicker",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar);

        (float tw, float th) = GameWideConstants.TableSizeFor(_system);

        float margin = 20f;
        ImGui.SetCursorPos(new Vector2(margin, margin));
        UiText.Colored(Accent, "SELECT MAP");
        ImGui.SameLine();
        UiText.Colored(Dim, $"   {GameSystemInfo.DisplayName(_system)}   "
                            + $"{tw / 12f:0.#}' x {th / 12f:0.#}'");
        ImGui.Separator();

        float bottomBar = 74f;
        float gridH = screenH - ImGui.GetCursorPosY() - bottomBar;
        ImGui.BeginChild("##mapgrid", new Vector2(screenW - margin * 2f, gridH));

        // Four across, as in the mock, and the tile keeps the table's shape so a wide table reads wide.
        float gridW = ImGui.GetContentRegionAvail().X;
        const int columns = 4;
        float tileW = MathF.Max(160f, (gridW - (columns + 1) * 14f) / columns);
        float tileH = tileW * (th / tw);
        float labelH = ImGui.GetTextLineHeightWithSpacing() + 6f;

        int index = 0;
        DrawTile(ref index, null, "Random", "Terrain laid out for you.", tileW, tileH, labelH, columns);
        foreach (string path in _paths)
        {
            TerrainLayoutFile? layout = Layout(path);
            string sub = layout == null
                ? "Could not be read."
                : $"{SizeLabel(layout)}   -   {layout.Pieces.Count} pieces, "
                  + $"{layout.Objectives.Count} objectives";
            DrawTile(ref index, path, NameOf(path), sub, tileW, tileH, labelH, columns);
        }
        ImGui.EndChild();

        Vector2 btn = new(190f, 44f);
        float by = screenH - bottomBar + 14f;

        ImGui.SetCursorPos(new Vector2(margin, by));
        if (UiButton.Back("Back", btn) || ImGui.IsKeyPressed(ImGuiKey.Escape, false)) OnBack?.Invoke();

        ImGui.SetCursorPos(new Vector2(margin + btn.X + 20f, by + 10f));
        UiText.Colored(Dim, _randomSelected
            ? "Random: the game lays the table out when the mission starts."
            : _selected != null ? $"Chosen: {NameOf(_selected)}" : "Nothing chosen.");

        ImGui.SetCursorPos(new Vector2(screenW - margin - btn.X, by));
        if (UiButton.Confirm("Confirm", btn)) OnChosen?.Invoke(_randomSelected ? null : _selected);

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawTile(ref int index, string? path, string name, string subtitle,
        float tileW, float tileH, float labelH, int columns)
    {
        if (index % columns != 0) ImGui.SameLine();
        index++;

        bool chosen = path == null ? _randomSelected : !_randomSelected && path == _selected;

        ImGui.PushID(path ?? "##random");
        Vector2 at = ImGui.GetCursorScreenPos();

        // The whole tile is the button; the picture is painted over it. An invisible button under a
        // drawn thumbnail is how every other picture-grid in this app works.
        if (ImGui.InvisibleButton("##tile", new Vector2(tileW, tileH + labelH)))
        {
            _randomSelected = path == null;
            _selected = path;
        }
        bool hovered = ImGui.IsItemHovered();

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        (float tw, float th) = GameWideConstants.TableSizeFor(_system);

        if (path == null)
        {
            // Random has no layout to draw, so it gets the bare table: surface, grid and the standard
            // zones. It should look like a table nobody has dressed yet, which is what it is.
            MapThumbnail.Draw(dl, at, new Vector2(tileW, tileH), tw, th, null, _background,
                _horde ? MapThumbnail.EFallbackZones.Horde : MapThumbnail.EFallbackZones.Standard);
        }
        else
        {
            // The MAP's table, not the lobby's: a 4'x4' map drawn into a 6'x4' frame would be stretched
            // into a shape it is not, and the one thing a thumbnail has to get right is the shape of
            // the table.
            TerrainLayoutFile? layout = Layout(path);
            (float mw, float mh) = TableOf(layout, tw, th);
            MapThumbnail.Draw(dl, at, new Vector2(tileW, tileH), mw, mh, layout, _background,
                _horde ? MapThumbnail.EFallbackZones.Horde : MapThumbnail.EFallbackZones.Standard);
        }

        uint border = chosen ? ImGui.GetColorU32(Accent)
            : hovered ? ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.85f, 0.9f))
            : ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.34f, 1f));
        dl.AddRect(at, at + new Vector2(tileW, tileH), border, 3f, ImDrawFlags.None, chosen ? 3f : 1.5f);

        dl.AddText(at + new Vector2(4f, tileH + 2f), ImGui.GetColorU32(ImGuiCol.Text), name);

        // A map drawn for another table is still shown - it is a real map and Kai may want it - but it
        // is marked, because playing a 4'x4' map as a 6'x4' game leaves two feet of empty board.
        if (path != null && !FitsThisGame(path))
        {
            Vector2 badge = at + new Vector2(4f, 4f);
            string text = "WRONG SIZE";
            Vector2 ts = ImGui.CalcTextSize(text);
            dl.AddRectFilled(badge, badge + ts + new Vector2(8f, 4f), ImGui.GetColorU32(Warn), 3f);
            dl.AddText(badge + new Vector2(4f, 2f), ImGui.GetColorU32(new Vector4(0.1f, 0.08f, 0f, 1f)), text);
        }

        if (ImGui.IsItemHovered()) UiText.Tooltip($"{name}\n{subtitle}");

        ImGui.PopID();
    }
}
