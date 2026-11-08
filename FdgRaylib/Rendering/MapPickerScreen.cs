using System.Numerics;
using FDG;
using FDG.SaveLoad;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Picking the table you will fight on (wish list, "Select Map"; "Choose Army and Select Map Mockup"): a
/// grid of map pictures with <b>Random</b> first, and on the right a big preview of the selected map -
/// its size, terrain pieces (hover one to see what it is), objectives and deployment zones - with
/// USE THIS MAP under it. Reached from the lobby's Map button; Back returns there unchanged.
///
/// <para>
/// <b>Only maps made for this game's table size are shown</b> (Kai, 2026-11-07, decision 10): the size
/// is the map file's own <see cref="TerrainLayoutFile.TableWidthInches"/> x
/// <see cref="TerrainLayoutFile.TableHeightInches"/> against the engine's
/// <see cref="GameWideConstants.TableSizeFor"/> - 6'x4' for GF and AoF, 4'x4' for Firefight, Skirmish
/// and the Quest games. The old WRONG SIZE badge is gone with the maps it marked; a line under the grid
/// says how many were left out, so a map never seems lost.
/// </para>
///
/// <para>
/// <b>Choosing a map replaces the books' alternating terrain and objective placement</b> - a house rule,
/// Kai's call (claude/house-rules.md). <b>Random is always first</b>: without it the grid is empty until
/// somebody draws a map, and an empty picker would stop a game being set up at all.
/// </para>
///
/// <para>Rental layout, plain ImGui. Permanent: which maps are offered and what the preview tells you.</para>
/// </summary>
public sealed class MapPickerScreen : IAppScreen
{
    /// <summary>What the lobby is choosing a map for.</summary>
    public readonly record struct Request(EGameSystem System, EGameMode Mode, ETableBackground Background,
        EDeploymentStyle Deployment, int Sides, string? Current);

    /// <summary>A map was chosen: its path, or null for Random.</summary>
    public Action<string?>? OnChosen;

    public Action? OnBack;

    private IReadOnlyList<string> _paths = Array.Empty<string>();
    private int _otherSizeCount;
    private readonly Dictionary<string, TerrainLayoutFile?> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private string? _selected;
    private bool _randomSelected = true;
    private Request _request = new(EGameSystem.GrimdarkFuture, EGameMode.Standard, ETableBackground.Forest,
        EDeploymentStyle.Core, 2, null);

    private static readonly Vector4 Dim = new(0.62f, 0.63f, 0.66f, 1f);
    private static readonly Vector4 Accent = new(0.45f, 0.68f, 1f, 1f);

    private bool Horde => GameSystemInfo.IsHordeLike(_request.Mode);

    /// <summary>Opened from the lobby, which knows the game, mode, look and deployment being set up.</summary>
    public void Open(Request request)
    {
        _request = request;
        _selected = request.Current;
        _randomSelected = string.IsNullOrEmpty(request.Current);
        Reload();
    }

    public void OnShown() => Reload();

    private void Reload()
    {
        _layouts.Clear();
        (float gw, float gh) = GameWideConstants.TableSizeFor(_request.System);
        var fitting = new List<string>();
        _otherSizeCount = 0;
        foreach (string path in FdgRaylib.SaveLoad.MapPaths.List())
        {
            if (FitsTable(Layout(path), gw, gh)) fitting.Add(path);
            else _otherSizeCount++;
        }
        _paths = fitting;
        if (_selected != null && !_paths.Contains(_selected, StringComparer.OrdinalIgnoreCase))
        {
            // The chosen map was deleted, renamed, or is for another table: fall back to Random rather than
            // launching onto a map that is not offered here.
            _selected = null;
            _randomSelected = true;
        }
    }

    /// <summary>
    /// Whether a map was made for a table of <paramref name="tableW"/> x <paramref name="tableH"/> inches.
    /// A map that cannot be read is not offered (there is nothing to play on).
    /// </summary>
    internal static bool FitsTable(TerrainLayoutFile? layout, float tableW, float tableH) =>
        layout != null
        && MathF.Abs(layout.TableWidthInches - tableW) < 0.01f
        && MathF.Abs(layout.TableHeightInches - tableH) < 0.01f;

    /// <summary>A map's contents, read once and kept until the screen is reopened.</summary>
    private TerrainLayoutFile? Layout(string path)
    {
        if (_layouts.TryGetValue(path, out TerrainLayoutFile? cached)) return cached;
        TerrainLayoutFile? loaded = TerrainLayoutLoader.TryLoadFromFile(path, out _);
        _layouts[path] = loaded;
        return loaded;
    }

    private string NameOf(string path)
    {
        TerrainLayoutFile? layout = Layout(path);
        return layout != null && !string.IsNullOrWhiteSpace(layout.Name)
            ? layout.Name
            : Path.GetFileNameWithoutExtension(path);
    }

    internal static string SizeLabel(float w, float h) => $"{w / 12f:0.#}' x {h / 12f:0.#}'";

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##mappicker",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar);

        (float tw, float th) = GameWideConstants.TableSizeFor(_request.System);

        float margin = 20f;
        ImGui.SetCursorPos(new Vector2(margin, margin));
        UiText.Colored(Accent, "SELECT MAP", wrap: false);
        ImGui.SameLine();
        UiText.Colored(Dim, $"   {GameSystemInfo.DisplayName(_request.System)}   {SizeLabel(tw, th)} table", wrap: false);
        ImGui.Separator();

        float bottomBar = 74f;
        float top = ImGui.GetCursorPosY();
        float bodyH = screenH - top - bottomBar;
        float previewW = MathF.Max(360f, screenW * 0.36f);
        float gridW = screenW - margin * 3f - previewW;

        // ── Left: the grid ──
        ImGui.SetCursorPos(new Vector2(margin, top));
        ImGui.BeginChild("##mapgrid", new Vector2(gridW, bodyH));
        float avail = ImGui.GetContentRegionAvail().X;
        const int columns = 3;
        float tileW = MathF.Max(140f, (avail - (columns + 1) * 14f) / columns);
        float tileH = tileW * (th / tw);
        float labelH = ImGui.GetTextLineHeightWithSpacing() + 6f;

        int index = 0;
        DrawTile(ref index, null, "Random", tileW, tileH, labelH, columns);
        foreach (string path in _paths)
            DrawTile(ref index, path, NameOf(path), tileW, tileH, labelH, columns);

        ImGui.Spacing();
        if (_paths.Count == 0)
            UiText.Disabled($"No maps made for a {SizeLabel(tw, th)} table yet - make one in Map Maker on the main menu.");
        if (_otherSizeCount > 0)
            UiText.Disabled($"{_otherSizeCount} map{(_otherSizeCount == 1 ? " is" : "s are")} made for another table size and not shown here.");
        ImGui.EndChild();

        // ── Right: the preview ──
        ImGui.SetCursorPos(new Vector2(margin * 2f + gridW, top));
        ImGui.BeginChild("##mappreviewpanel", new Vector2(previewW, bodyH), ImGuiChildFlags.Borders);
        DrawPreview(tw, th);
        ImGui.EndChild();

        // ── Bottom bar ──
        Vector2 btn = new(220f, 44f);
        float by = screenH - bottomBar + 14f;
        ImGui.SetCursorPos(new Vector2(margin, by));
        if (UiButton.Back("Back", btn) || ImGui.IsKeyPressed(ImGuiKey.Escape, false)) OnBack?.Invoke();
        if (ImGui.IsItemHovered()) UiText.Tooltip("Back to the lobby without changing the map.");

        ImGui.SetCursorPos(new Vector2(screenW - margin - btn.X, by));
        if (UiButton.Confirm("USE THIS MAP", btn)) OnChosen?.Invoke(_randomSelected ? null : _selected);
        if (ImGui.IsItemHovered())
            UiText.Tooltip(_randomSelected ? "Back to the lobby with Random terrain." : $"Back to the lobby with {NameOf(_selected!)}.");

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawTile(ref int index, string? path, string name, float tileW, float tileH, float labelH, int columns)
    {
        if (index % columns != 0) ImGui.SameLine();
        index++;

        bool chosen = path == null ? _randomSelected : !_randomSelected && path == _selected;

        ImGui.PushID(path ?? "##random");
        Vector2 at = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##tile", new Vector2(tileW, tileH + labelH)))
        {
            _randomSelected = path == null;
            _selected = path;
        }
        bool hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _randomSelected = path == null;
            _selected = path;
            OnChosen?.Invoke(_randomSelected ? null : _selected);
        }

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        (float tw, float th) = GameWideConstants.TableSizeFor(_request.System);
        TerrainLayoutFile? layout = path == null ? null : Layout(path);
        MapThumbnail.Draw(dl, at, new Vector2(tileW, tileH), tw, th, layout, _request.Background,
            Horde ? MapThumbnail.EFallbackZones.Horde : MapThumbnail.EFallbackZones.None,
            showObjectives: !Horde, deploymentZones: ZonesFor(layout, tw, th).Zones);
        if (Horde) MapThumbnail.DrawHordeObjectives(dl, at, new Vector2(tileW, tileH), tw, th);

        uint border = chosen ? ImGui.GetColorU32(Accent)
            : hovered ? ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.85f, 0.9f))
            : ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.34f, 1f));
        dl.AddRect(at, at + new Vector2(tileW, tileH), border, 3f, ImDrawFlags.None, chosen ? 3f : 1.5f);
        dl.AddText(at + new Vector2(4f, tileH + 2f), ImGui.GetColorU32(ImGuiCol.Text),
            LobbyScreen.FitWithEllipsis(name, tileW - 8f));

        if (hovered)
            UiText.Tooltip(path == null
                ? "Random: the game lays the terrain out for you when the game starts.\nClick to preview, double-click to use it."
                : $"{name}\nClick to preview, double-click to use it.");

        ImGui.PopID();
    }

    private LobbyDeploymentPreview.Plan ZonesFor(TerrainLayoutFile? layout, float tw, float th) =>
        LobbyDeploymentPreview.For(_request.System, _request.Mode, _request.Deployment, _request.Sides, tw, th, layout);

    private void DrawPreview(float tw, float th)
    {
        TerrainLayoutFile? layout = _randomSelected || _selected == null ? null : Layout(_selected);
        string name = _randomSelected || _selected == null ? "Random" : NameOf(_selected);

        UiText.Colored(Accent, name.ToUpperInvariant(), wrap: false);
        UiText.Disabled($"Table: {SizeLabel(tw, th)}");

        // The big picture.
        float w = ImGui.GetContentRegionAvail().X;
        float h = w * (th / tw);
        Vector2 at = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##bigmap", new Vector2(w, h));
        bool pictureHovered = ImGui.IsItemHovered();
        LobbyDeploymentPreview.Plan plan = ZonesFor(layout, tw, th);
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        MapThumbnail.Draw(dl, at, new Vector2(w, h), tw, th, layout, _request.Background,
            Horde ? MapThumbnail.EFallbackZones.Horde : MapThumbnail.EFallbackZones.None,
            showObjectives: !Horde, deploymentZones: plan.Zones);
        if (Horde) MapThumbnail.DrawHordeObjectives(dl, at, new Vector2(w, h), tw, th);

        // Hover a terrain piece on the picture = what it is.
        if (pictureHovered && layout != null)
        {
            float scale = MathF.Min(w / tw, h / th);
            Vector2 o = at + new Vector2((w - tw * scale) / 2f, (h - th * scale) / 2f);
            Vector2 m = ImGui.GetMousePos() - o;
            var point = new Float2(m.X / scale, th - m.Y / scale);
            TerrainPieceEntry? piece = layout.Pieces.LastOrDefault(p => p.Shape != null && p.Shape.IsPointWithinZone(point));
            if (piece != null) UiText.TooltipWrapped(PieceText(piece));
        }

        ImGui.Spacing();
        if (layout == null)
        {
            UiText.Wrapped("Terrain: laid out for you when the game starts.");
            UiText.Wrapped(Horde ? "Objectives: Horde's 5 fixed markers." : "Objectives: placed the usual way at the start.");
        }
        else
        {
            UiText.Wrapped($"Terrain: {layout.Pieces.Count} piece{(layout.Pieces.Count == 1 ? "" : "s")} - hover one on the picture or below.");
            UiText.Wrapped(Horde
                ? "Objectives: Horde's 5 fixed markers (a map's own markers are not used in Horde)."
                : layout.Objectives.Count > 0
                    ? $"Objectives: {layout.Objectives.Count} marker{(layout.Objectives.Count == 1 ? "" : "s")} set by the map."
                    : "Objectives: none on the map - placed the usual way at the start.");
        }
        UiText.Wrapped(Horde
            ? "Deployment zones: Horde's - the defenders in the centre, the horde from the corners."
            : "Deployment zones: " + (plan.Note.Length > 0 ? plan.Note : "as the lobby's Deployment setting."));

        // The pieces, each with its hover.
        if (layout is { Pieces.Count: > 0 })
        {
            ImGui.Spacing();
            UiText.Colored(Accent, "TERRAIN PIECES", wrap: false);
            ImGui.BeginChild("##pieces", new Vector2(-1f, -1f));
            for (int i = 0; i < layout.Pieces.Count; i++)
            {
                TerrainPieceEntry piece = layout.Pieces[i];
                ImGui.Selectable($"{PieceName(piece, i)}##piece{i}", false);
                if (ImGui.IsItemHovered()) UiText.TooltipWrapped(PieceText(piece));
            }
            ImGui.EndChild();
        }
    }

    private static string PieceName(TerrainPieceEntry piece, int index) =>
        string.IsNullOrWhiteSpace(piece.Name) ? $"Piece {index + 1}" : piece.Name;

    /// <summary>A terrain piece's hover: its name, its terrain types and what each does.</summary>
    internal static string PieceText(TerrainPieceEntry piece)
    {
        string name = string.IsNullOrWhiteSpace(piece.Name) ? "Terrain piece" : piece.Name;
        var lines = new List<string> { name };
        lines.AddRange(TerrainEffectText.Effects(piece.TerrainType));
        if (piece.HeightInches > 0f) lines.Add($"Height: {piece.HeightInches:0.#}\"");
        return string.Join("\n", lines);
    }
}
