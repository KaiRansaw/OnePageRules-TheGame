using System.Numerics;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.SaveLoad;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The 2D map editor, opened from the Map Maker (Map Maker Mockup, 2026-11-07). It takes over the screen
/// while open: map name and table size in the top bar, tools and terrain presets on the left, the board
/// in the middle, the selected piece's settings on the right, Cancel / Save Map along the bottom. Cancel
/// asks before throwing changes away.
///
/// <para>Tools: Select (click to pick, drag to move), a palette of terrain pieces (building, sandbags,
/// trees, rocks, rubble, mine field, plain rectangle / circle - click the board to drop one), Objective
/// (click to drop a marker) and Model Base (a size reference). Right-click goes back to Select; Delete
/// removes the selected item. Positions snap to half inches.</para>
///
/// <para>A map is terrain and objectives only, saved as an ordinary terrain layout (.fdgterrain) in the
/// Maps folder. There is no Deploy Zone tool any more: deployment is picked in the lobby (Kai,
/// 2026-11-07). Zones an older map carries are ignored by the game and dropped when it is saved here.</para>
/// </summary>
public sealed class MapEditor
{
    public bool IsOpen { get; private set; }

    /// <summary>Called with the saved file's path after a successful save.</summary>
    public Action<string>? OnSaved;

    /// <summary>
    /// The table being drawn on, in inches. A FIELD, not a constant (Kai, 2026-11-02): the games are
    /// not all played on the same table - Firefight, Skirmish and both Quest systems use 4' x 4' where
    /// Grimdark Future and Age of Fantasy use 6' x 4' - and a map drawn at the wrong size is terrain
    /// hanging off the edge of the board.
    /// </summary>
    private float TW = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
    private float TH = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;

    /// <summary>
    /// The sizes a map can be drawn at: the books' tables (6' x 4', 4' x 4') and the bigger tables big games
    /// grow onto (Kai, 2026-11-08) - all from the engine's <see cref="TableSizing.Sizes"/>, biggest last.
    /// </summary>
    private static readonly (float W, float H)[] TableSizes = TableSizing.Sizes.ToArray();
    private static string TableSizeLabel(float w, float h) => $"{w / 12f:0.#}' x {h / 12f:0.#}'";

    /// <summary>
    /// Which games play on a given table, for the hint lines: the systems whose book table it is, or for a
    /// bigger table the army size each game grows onto it at (house rule).
    /// </summary>
    internal static string SystemsOnTable(float w, float h)
    {
        string book = string.Join(", ", Enum.GetValues<EGameSystem>()
            .Where(sys => GameWideConstants.TableSizeFor(sys) == (w, h))
            .Select(GameSystemInfo.DisplayName));
        if (book.Length > 0) return book;
        string big = string.Join(", ", Enum.GetValues<EGameSystem>()
            .Where(sys => TableSizing.SizesFor(sys).Any(s => TableSizing.Same(s, (w, h))))
            .Select(sys => $"{GameSystemInfo.DisplayName(sys)} ~{TableSizing.SuggestedPoints(sys, w, h)} pts"));
        return big.Length > 0 ? "big games: " + big : "";
    }

    private const float Snap = 0.5f;
    private const float ObjectivePickRadius = 1.5f;

    // ── Editable data ─────────────────────────────────────────────────────────────────────────────────
    private sealed class Piece
    {
        public string Name = "";
        public ETerrainType Type;
        public float Height;
        public int Points = 1;
        public bool Circle;
        public float X, Z;          // centre
        public float W = 4, D = 4;  // rectangle width (x) / depth (z)
        public float R = 3;         // circle radius
        public float Angle;         // rectangle rotation, degrees
        public IZone? Raw;          // a shape the editor can't reshape (e.g. a multi-part piece): kept as-is

        public IZone ToZone()
        {
            if (Raw != null) return Raw;
            if (Circle) return new CircularZone(X, Z, R);
            var rect = new RectangularZone(X - W / 2f, X + W / 2f, Z - D / 2f, Z + D / 2f);
            return MathF.Abs(Angle % 360f) < 0.01f ? rect : new RotatedZoneWrapper(rect, Angle, new Float2(X, Z));
        }

        public Piece Clone() => (Piece)MemberwiseClone();
    }

    private sealed record Preset(string Label, string Name, ETerrainType Type, bool Circle,
        float W, float D, float R, float Height, int Points);

    private const ETerrainType Solid = ETerrainType.Blocking | ETerrainType.Impassible;
    private static readonly Preset[] Presets =
    {
        new("Building",   "Building",   Solid,                                        false, 6, 4, 0,   4, 3),
        new("Sandbags",   "Sandbags",   ETerrainType.Cover,                           false, 8, 1, 0,   1, 1),
        new("Trees",      "Forest",     ETerrainType.Cover | ETerrainType.Difficult,  true,  0, 0, 5,   0, 3),
        new("Rocks",      "Rocks",      Solid,                                        true,  0, 0, 2.5f, 2, 2),
        new("Rubble",     "Rubble",     ETerrainType.Difficult,                       false, 6, 6, 0,   0, 2),
        new("Mine field", "Mine field", ETerrainType.Dangerous,                       false, 6, 6, 0,   0, 2),
        new("Rectangle",  "Rectangle",  ETerrainType.Cover,                           false, 4, 4, 0,   0, 1),
        new("Circle",     "Circle",     ETerrainType.Cover,                           true,  0, 0, 3,   0, 1),
    };

    private readonly List<Piece> _pieces = new();
    private readonly List<Vector2> _objectives = new();   // X = table x, Y = table z

    /// <summary>How many deployment zones the opened file carried (old maps). Shown, never edited or saved.</summary>
    private int _legacyZones;

    /// <summary>The map as it was when opened (or last saved), to tell whether Cancel would lose anything.</summary>
    private string _openedAs = "";

    /// <summary>
    /// Model bases dropped on the board purely as size references (Kai, 2026-11-02): can a unit
    /// actually get through that gap? Saved with the map so they survive a reopen, and ignored by the
    /// game entirely - they are not terrain and block nothing.
    /// </summary>
    private readonly List<MapBaseMarkerEntry> _baseMarkers = new();
    private string _name = "";
    private string? _path;
    private string _status = "";

    /// <summary>Set when the editor opens, cleared after one frame. See the note in Draw.</summary>
    private bool _focusWanted;

    // ── Tools and selection ───────────────────────────────────────────────────────────────────────────
    private enum ETool { Select, Place, Objective, BaseMarker }
    private enum ESel { None, Piece, Objective, BaseMarker }
    private ETool _tool = ETool.Select;
    private Preset? _preset;
    private ESel _selKind = ESel.None;
    private int _selIndex = -1;
    private bool _dragging;
    private Vector2 _dragOffset;
    private BasePreset _basePreset = BasePresets[1];

    /// <summary>
    /// A base you can drop on the map as a size reference. Sizes are in MILLIMETRES because that is
    /// how bases are sold and how everybody talks about them, converted to inches here because that is
    /// the table's unit.
    ///
    /// <para>
    /// 28mm is first among equals - it is <see cref="BaseShapeDefaults.CircleDiameterInches"/>, what a
    /// model gets when its army list says nothing - and the 25 x 50mm rectangle is the engine's own
    /// default rectangular base. The rest are the common sizes a model might be on. These are
    /// references for judging whether a unit fits through a gap, not a rule: OPR does not mandate base
    /// sizes.
    /// </para>
    /// </summary>
    private sealed record BasePreset(string Label, float Wmm, float Hmm)
    {
        public bool Circle => Hmm <= 0f;
        public float DiameterInches => Wmm / 25.4f;
        public float WidthInches => Wmm / 25.4f;
        public float HeightInches => Hmm / 25.4f;
    }

    private static readonly BasePreset[] BasePresets =
    {
        new("25mm", 25f, 0f),
        new("28mm", 28f, 0f),
        new("32mm", 32f, 0f),
        new("40mm", 40f, 0f),
        new("50mm", 50f, 0f),
        new("60mm", 60f, 0f),
        new("25x50", 25f, 50f),
    };

    // ── Opening ───────────────────────────────────────────────────────────────────────────────────────
    public void OpenNew()
    {
        Reset();
        _name = "New Map";
        _openedAs = Snapshot();
        IsOpen = true;
    }

    public void OpenExisting(string path)
    {
        TerrainLayoutFile? layout = TerrainLoader.TryLoadFromFile(path, out string? error);
        if (layout == null)
        {
            Console.WriteLine($"Map maker: could not open {path}: {error}");
            return;
        }
        Reset();
        _path = path;
        _name = string.IsNullOrWhiteSpace(layout.Name) ? Path.GetFileNameWithoutExtension(path) : layout.Name;
        // A map drawn before table sizes existed defaults to 6' x 4', which is the size it was in fact
        // drawn at - so nothing shifts under an existing map.
        if (layout.TableWidthInches > 0f) TW = layout.TableWidthInches;
        if (layout.TableHeightInches > 0f) TH = layout.TableHeightInches;
        foreach (TerrainPieceEntry e in layout.Pieces)
            if (e.Shape != null) _pieces.Add(FromEntry(e));
        foreach (MapObjectiveEntry o in layout.Objectives ?? new())
            _objectives.Add(new Vector2(o.X, o.Z));
        _legacyZones = layout.DeploymentZones?.Count ?? 0;
        foreach (MapBaseMarkerEntry m in layout.BaseMarkers ?? new())
            _baseMarkers.Add(m);
        // Taken after the load, without the old zones: opening an old map and pressing Cancel loses nothing
        // the game would ever have used, so it must not ask.
        _openedAs = Snapshot();
        IsOpen = true;
    }

    private void Reset()
    {
        _boardZoom = 1f; _boardPan = Vector2.Zero;
        _pieces.Clear(); _objectives.Clear(); _legacyZones = 0;
        _path = null; _status = "";
        _focusWanted = true;
        TW = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
        TH = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
        _baseMarkers.Clear();
        _tool = ETool.Select; _preset = null;
        _selKind = ESel.None; _selIndex = -1;
        _dragging = false;
    }

    private Piece FromEntry(TerrainPieceEntry e)
    {
        var p = new Piece { Name = e.Name ?? "", Type = e.TerrainType, Height = e.HeightInches, Points = Math.Max(1, e.Points) };
        switch (e.Shape)
        {
            case RectangularZone r:
                p.X = (r.Left + r.Right) / 2f; p.Z = (r.Bottom + r.Top) / 2f;
                p.W = r.Right - r.Left; p.D = r.Top - r.Bottom;
                break;
            case CircularZone c:
                p.Circle = true; p.X = c.Center.X; p.Z = c.Center.Y; p.R = c.Radius;
                break;
            case RotatedZoneWrapper w when w.Inner is RectangularZone r:
                // Rotating about any pivot = rotating about the rectangle's own centre, then moving that centre.
                Vector2 c0 = new((r.Left + r.Right) / 2f, (r.Bottom + r.Top) / 2f);
                Vector2 c1 = RotateAround(c0, new Vector2(w.Pivot.X, w.Pivot.Y), w.AngleDegrees);
                p.X = c1.X; p.Z = c1.Y; p.W = r.Right - r.Left; p.D = r.Top - r.Bottom; p.Angle = w.AngleDegrees;
                break;
            default:
                p.Raw = e.Shape;
                Vector2 center = ShapeCenter(e.Shape);
                p.X = center.X; p.Z = center.Y;
                break;
        }
        return p;
    }

    private static Vector2 RotateAround(Vector2 p, Vector2 pivot, float degrees)
    {
        float a = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(a), sin = MathF.Sin(a);
        Vector2 d = p - pivot;
        return pivot + new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
    }

    private Vector2 ShapeCenter(IZone shape)
    {
        if (shape is IBoundedZone b)
        {
            ZoneBounds bb = b.Bounds;
            return new Vector2(bb.CenterX, bb.CenterZ);
        }
        return new Vector2(TW / 2f, TH / 2f);
    }

    // ── Saving ────────────────────────────────────────────────────────────────────────────────────────
    private TerrainLayoutFile BuildLayout() => new()
    {
        Name = _name.Trim(),
        TableWidthInches = TW,
        TableHeightInches = TH,
        BaseMarkers = _baseMarkers.Select(m => new MapBaseMarkerEntry
        {
            X = m.X, Z = m.Z, Label = m.Label,
            Base = new BaseFileEntry
            {
                Shape = m.Base.Shape,
                DiameterInches = m.Base.DiameterInches,
                WidthInches = m.Base.WidthInches,
                HeightInches = m.Base.HeightInches,
            },
        }).ToList(),
        Pieces = _pieces.Select(p => new TerrainPieceEntry
        {
            Name = p.Name,
            TerrainType = p.Type,
            Shape = p.ToZone(),
            HeightInches = p.Height,
            Points = p.Points,
        }).ToList(),
        Objectives = _objectives.Select(o => new MapObjectiveEntry { X = o.X, Z = o.Y }).ToList(),
        // Never written: deployment is the lobby's pick (Kai, 2026-11-07).
        DeploymentZones = new List<MapDeploymentZoneEntry>(),
    };

    /// <summary>The map as it would be saved, as text - two equal snapshots mean nothing changed.</summary>
    private string Snapshot() => TerrainLoader.SerializeToJson(BuildLayout());

    /// <summary>True when Cancel would throw something away.</summary>
    private bool HasUnsavedChanges => Snapshot() != _openedAs;

    private bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(_name)) { _status = "Give the map a name first."; return false; }
        try
        {
            string path = MapPaths.Save(BuildLayout(), _path);
            _path = path;
            _status = "";
            IsOpen = false;
            OnSaved?.Invoke(path);
            return true;
        }
        catch (Exception ex)
        {
            _status = $"Could not save: {ex.Message}";
            return false;
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────────────────────────────────────
    private static uint U32(Color c, float alpha = 1f) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f * alpha));

    private static uint U32(int r, int g, int b, int a = 255) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));

    private static readonly uint SelectColor = U32(255, 235, 60);

    public void Draw(int screenW, int screenH)
    {
        if (!IsOpen) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        // Focus ONCE, when the editor opens - not every frame.
        //
        // This is why Clear Board did nothing (Kai, 2026-11-02). A modal popup is its own window, and
        // ImGui closes a modal that loses focus. Calling SetNextWindowFocus unconditionally put focus
        // back on the editor the very frame after the confirm box opened, so the box was created and
        // destroyed before it ever drew - the button looked dead. Any future dialog in here would have
        // had the same problem.
        if (_focusWanted)
        {
            ImGui.SetNextWindowFocus();
            _focusWanted = false;
        }
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.09f, 0.11f, 1f));
        ImGui.Begin("##MapEditor", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
                                   ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
                                   ImGuiWindowFlags.NoScrollWithMouse);

        float margin = 12f;
        float topH = MathF.Max(56f, ImGui.GetFrameHeight() * 2.2f);
        float footH = MathF.Max(64f, ImGui.GetFrameHeight() * 2.6f);
        float leftW = MathF.Max(250f, screenW * 0.16f);
        float rightW = MathF.Max(290f, screenW * 0.19f);
        float bodyY = topH + margin;
        float bodyH = screenH - bodyY - footH - margin;

        // Top bar: title, map name, table size, counts.
        ImGui.SetCursorPos(new Vector2(margin, margin));
        ImGui.BeginChild("##mapTop", new Vector2(screenW - margin * 2f, topH - margin), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawTopBar();
        ImGui.EndChild();

        // Left: tools and terrain presets.
        ImGui.SetCursorPos(new Vector2(margin, bodyY));
        ImGui.BeginChild("##mapTools", new Vector2(leftW, bodyH), ImGuiChildFlags.Borders);
        DrawToolPanel();
        ImGui.EndChild();

        // Middle: the board.
        float boardW = screenW - leftW - rightW - margin * 4f;
        ImGui.SetCursorPos(new Vector2(margin * 2f + leftW, bodyY));
        ImGui.BeginChild("##mapBoard", new Vector2(boardW, bodyH),
            ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawBoard();
        ImGui.EndChild();

        // Right: the selected piece's settings, then the counts and hints.
        ImGui.SetCursorPos(new Vector2(screenW - margin - rightW, bodyY));
        ImGui.BeginChild("##mapSelected", new Vector2(rightW, bodyH), ImGuiChildFlags.Borders);
        DrawSelectionProperties();
        ImGui.Separator();
        DrawHints();
        ImGui.EndChild();

        // Footer: Cancel, the status line, Save Map.
        ImGui.SetCursorPos(new Vector2(margin, screenH - footH));
        DrawFooter(screenW - margin * 2f, footH - margin);

        DrawConfirmPopups();

        // Keyboard: Delete removes the selection (not while typing in a text box).
        if (!ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete, false)) DeleteSelected();

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawTopBar()
    {
        float rowY = ImGui.GetCursorPosY();
        ImGui.PushFont(RaylibRenderer.LargeFont);
        ImGui.TextUnformatted(_path == null ? "MAP MAKER - New map" : "MAP MAKER - Editing");
        ImGui.PopFont();
        float frameY = rowY + MathF.Max(0f, (ImGui.GetItemRectSize().Y - ImGui.GetFrameHeight()) / 2f);

        ImGui.SameLine(0f, 20f);
        ImGui.SetCursorPosY(frameY);
        ImGui.SetNextItemWidth(MathF.Min(320f, ImGui.GetContentRegionAvail().X * 0.3f));
        ImGui.InputTextWithHint("##mapName", "Map name", ref _name, 64);
        if (ImGui.IsItemHovered()) UiText.Tooltip("The map's name, as the map lists show it.");

        ImGui.SameLine(0f, 16f);
        ImGui.SetCursorPosY(frameY);
        DrawTableSizePicker();

        string counts = $"{_pieces.Count} terrain - {_objectives.Count} objectives"
            + (_baseMarkers.Count > 0 ? $" - {_baseMarkers.Count} base refs" : "");
        float cw = ImGui.CalcTextSize(counts).X;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + 16f, ImGui.GetWindowWidth() - cw - 8f));
        ImGui.SetCursorPosY(frameY + (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) / 2f);
        UiText.Disabled(counts, wrap: false);
    }

    private void DrawToolPanel()
    {
        UiText.Disabled("TOOLS  (right-click the board for Select)");
        Vector2 full = new(-1f, 0f);
        if (ToolButton("Select / Move", _tool == ETool.Select, full)) SetTool(ETool.Select);
        if (ImGui.IsItemHovered()) UiText.Tooltip("Click a piece, objective or base to select it; drag to move it.");
        if (ToolButton("Objective", _tool == ETool.Objective, full)) SetTool(ETool.Objective);
        if (ImGui.IsItemHovered()) UiText.Tooltip("Click the board to drop an objective marker.");
        if (ToolButton("Model Base (size guide)", _tool == ETool.BaseMarker, full)) SetTool(ETool.BaseMarker);
        if (ImGui.IsItemHovered())
            UiText.Tooltip("Drop a model's base on the table to see how big things really are -\n" +
                           "whether a squad fits through that gap, how much of the board a\n" +
                           "unit covers. Saved with the map; the game ignores them entirely.");
        // Opened from DrawConfirmPopups, at the editor window's own level: a popup opened inside this child
        // panel would have a different ID from the one the editor's window looks for, and never show.
        if (ToolButton("Clear Board", false, full)) _askClear = true;
        if (ImGui.IsItemHovered()) UiText.Tooltip("Remove every terrain piece, objective and base reference. Asks first.");

        if (_tool == ETool.BaseMarker)
        {
            ImGui.Spacing();
            UiText.Disabled("Base size  (click the board to place)");
            float third = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
            for (int i = 0; i < BasePresets.Length; i++)
            {
                if (i % 3 != 0) ImGui.SameLine();
                if (ToolButton(BasePresets[i].Label, _basePreset == BasePresets[i], new Vector2(third, 0f)))
                    _basePreset = BasePresets[i];
            }
            UiText.Colored(new Vector4(0.62f, 0.63f, 0.66f, 1f),
                "28mm is what a model gets unless its army list says otherwise.");
        }

        ImGui.Spacing();
        UiText.Disabled("TERRAIN  (click the board to place)");
        float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        Vector2 btn = new(half, 0f);
        for (int i = 0; i < Presets.Length; i++)
        {
            if (i % 2 == 1) ImGui.SameLine();
            if (ToolButton(Presets[i].Label, _tool == ETool.Place && _preset == Presets[i], btn))
            {
                SetTool(ETool.Place);
                _preset = Presets[i];
            }
            if (ImGui.IsItemHovered()) UiText.Tooltip(PresetHover(Presets[i]));
        }
    }

    /// <summary>A terrain preset's hover: what it drops and the terrain types it starts with.</summary>
    private static string PresetHover(Preset p)
    {
        var types = Enum.GetValues<ETerrainType>().Where(t => t != 0 && p.Type.HasFlag(t)).Select(t => t.ToString());
        string size = p.Circle ? $"{p.R * 2f:0.#}\" across" : $"{p.W:0.#}\" x {p.D:0.#}\"";
        return $"{p.Name}, {size}: {string.Join(", ", types)}. Change any of it once placed.";
    }

    /// <summary>The counts' explanations: what the game does when the map leaves something out.</summary>
    private void DrawHints()
    {
        ImGui.PushTextWrapPos(0f);
        if (_objectives.Count == 0)
            UiText.Disabled("No objectives: the game rolls D3+2 and places them.");
        UiText.Disabled("Deployment is not part of a map - it is picked in the lobby.");
        if (_legacyZones > 0)
            UiText.Colored(new Vector4(1f, 0.75f, 0.4f, 1f),
                $"This map was drawn with {_legacyZones} deployment zone{(_legacyZones == 1 ? "" : "s")}. "
                + "Games ignore them, and saving removes them.");
        ImGui.PopTextWrapPos();
    }

    private const string DiscardPopup = "Throw away your changes?";

    private void DrawFooter(float width, float height)
    {
        float rowH = MathF.Min(height, 48f);
        Vector2 cancel = new(140f, rowH), save = new(200f, rowH);
        float x0 = ImGui.GetCursorPosX(), y0 = ImGui.GetCursorPosY();
        if (UiButton.Back("Cancel", cancel))
        {
            if (HasUnsavedChanges) ImGui.OpenPopup(DiscardPopup);
            else IsOpen = false;
        }
        if (ImGui.IsItemHovered()) UiText.Tooltip("Leave without saving. Asks first if you changed anything.");
        if (_status.Length > 0)
        {
            ImGui.SameLine(0f, 16f);
            ImGui.SetCursorPosY(y0 + (rowH - ImGui.GetTextLineHeight()) / 2f);
            UiText.Colored(new Vector4(1f, 0.55f, 0.45f, 1f), _status, wrap: false);
        }
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + 16f, x0 + width - save.X));
        ImGui.SetCursorPosY(y0);
        if (UiButton.Confirm("Save Map", save)) TrySave();
    }

    /// <summary>Clear Board was pressed this frame (the popup is opened at the editor window's level).</summary>
    private bool _askClear;

    private void DrawConfirmPopups()
    {
        if (_askClear)
        {
            ImGui.OpenPopup("Clear the whole board?");
            _askClear = false;
        }

        // Cancel with unsaved changes.
        if (ImGui.BeginPopupModal(DiscardPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextUnformatted("You have changes that are not saved. Throw them away?");
            if (UiButton.Back("Keep editing", new Vector2(150, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
                ImGui.CloseCurrentPopup();
            ImGui.SameLine();
            if (UiButton.Confirm("Throw away", new Vector2(150, 0)))
            {
                IsOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // Confirm popup for Clear Board.
        if (ImGui.BeginPopupModal("Clear the whole board?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Remove all terrain, objectives and base refs?");
            if (UiButton.Back("No", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            ImGui.SameLine();
            if (UiButton.Confirm("Yes, clear", new Vector2(120, 0)))
            {
                _pieces.Clear(); _objectives.Clear(); _baseMarkers.Clear();
                Deselect();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }


    /// <summary>
    /// What table this map is drawn on. The games do not all share one: Firefight, Skirmish and both
    /// Quest systems play on 4' x 4', Grimdark Future and Age of Fantasy on 6' x 4'. A map drawn at the
    /// wrong size has terrain off the edge of the board.
    ///
    /// <para>
    /// Changing it to a SMALLER table is the dangerous direction, because anything past the new edge
    /// would be stranded - present but unreachable, and invisible once the board is redrawn. Rather
    /// than silently deleting or silently keeping it, the pieces are pulled back inside and the panel
    /// says how many moved.
    /// </para>
    /// </summary>
    private void DrawTableSizePicker()
    {
        // A dropdown rather than a row of buttons: there are seventeen sizes now (Kai, 2026-11-08).
        ImGui.SetNextItemWidth(UiChrome.Em(16f));
        if (UiCombo.Begin("##mapTableSize", TableSizeLabel(TW, TH), ImGuiComboFlags.HeightLarge))
        {
            foreach ((float tw, float th) in TableSizes)
            {
                bool active = Nearly(TW, tw) && Nearly(TH, th);
                if (ImGui.Selectable($"{TableSizeLabel(tw, th)}##{tw}x{th}", active) && !active)
                    SetTableSize(tw, th);
                if (ImGui.IsItemHovered())
                    UiText.Tooltip($"{TableSizeLabel(tw, th)} - {SystemsOnTable(tw, th)}");
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            UiText.Tooltip($"The table this map is drawn for: {SystemsOnTable(TW, TH)}. A map always plays on its own table.");
    }

    private static bool Nearly(float a, float b) => MathF.Abs(a - b) < 0.01f;

    /// <summary>Re-sizes the board, dragging anything now outside it back in.</summary>
    private void SetTableSize(float width, float height)
    {
        TW = width; TH = height;

        int moved = 0;
        foreach (Piece p in _pieces)
        {
            float x = Math.Clamp(p.X, 0f, TW), z = Math.Clamp(p.Z, 0f, TH);
            if (!Nearly(x, p.X) || !Nearly(z, p.Z)) moved++;
            p.X = x; p.Z = z;
        }
        for (int i = 0; i < _objectives.Count; i++)
        {
            Vector2 o = _objectives[i];
            Vector2 c = new(Math.Clamp(o.X, 0f, TW), Math.Clamp(o.Y, 0f, TH));
            if (!Nearly(c.X, o.X) || !Nearly(c.Y, o.Y)) moved++;
            _objectives[i] = c;
        }
        foreach (MapBaseMarkerEntry m in _baseMarkers)
        {
            float x = Math.Clamp(m.X, 0f, TW), z = Math.Clamp(m.Z, 0f, TH);
            if (!Nearly(x, m.X) || !Nearly(z, m.Z)) moved++;
            m.X = x; m.Z = z;
        }

        _boardZoom = 1f; _boardPan = Vector2.Zero;
        _status = moved > 0
            ? $"Table is now {TableSizeLabel(TW, TH)}. {moved} thing(s) were outside it and have been "
              + "moved back onto the board - check them."
            : "";
    }

    private static bool ToolButton(string label, bool active, Vector2 size)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.45f, 0.70f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.52f, 0.78f, 1f));
        }
        bool clicked = ImGui.Button(label, size);
        if (active) ImGui.PopStyleColor(2);
        return clicked;
    }

    private void SetTool(ETool tool)
    {
        _tool = tool;
        _preset = null;
        _dragging = false;
    }

    private void Deselect()
    {
        _selKind = ESel.None;
        _selIndex = -1;
        _dragging = false;
    }

    private void DeleteSelected()
    {
        switch (_selKind)
        {
            case ESel.Piece when _selIndex < _pieces.Count: _pieces.RemoveAt(_selIndex); break;
            case ESel.Objective when _selIndex < _objectives.Count: _objectives.RemoveAt(_selIndex); break;
            case ESel.BaseMarker when _selIndex < _baseMarkers.Count: _baseMarkers.RemoveAt(_selIndex); break;
        }
        Deselect();
    }

    private void DrawSelectionProperties()
    {
        switch (_selKind)
        {
            case ESel.Piece when _selIndex >= 0 && _selIndex < _pieces.Count:
            {
                Piece p = _pieces[_selIndex];
                UiText.Disabled("SELECTED: " + (p.Name.Length > 0 ? p.Name.ToUpperInvariant() : "TERRAIN"));
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##pieceName", ref p.Name, 48);

                FlagBox("Cover", ref p.Type, ETerrainType.Cover);       ImGui.SameLine(); FlagBox("Difficult", ref p.Type, ETerrainType.Difficult);
                FlagBox("Dangerous", ref p.Type, ETerrainType.Dangerous); ImGui.SameLine(); FlagBox("Blocking", ref p.Type, ETerrainType.Blocking);
                FlagBox("Impassable", ref p.Type, ETerrainType.Impassible); ImGui.SameLine(); FlagBox("Elevated", ref p.Type, ETerrainType.Elevated);

                if (p.Raw == null)
                {
                    ImGui.SetNextItemWidth(-90); ImGui.DragFloat("X##px", ref p.X, 0.25f, 0f, TW, "%.1f in");
                    ImGui.SetNextItemWidth(-90); ImGui.DragFloat("Y##pz", ref p.Z, 0.25f, 0f, TH, "%.1f in");
                    if (p.Circle)
                    {
                        ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Radius", ref p.R, 0.5f, 18f, "%.1f in");
                    }
                    else
                    {
                        ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Width", ref p.W, 0.5f, 36f, "%.1f in");
                        ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Depth", ref p.D, 0.5f, 36f, "%.1f in");
                        ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Rotation", ref p.Angle, -180f, 180f, "%.0f deg");
                    }
                }
                else
                {
                    ImGui.TextWrapped("This piece has a special shape: it can't be moved or resized here, only renamed, retyped or deleted.");
                }
                ImGui.SetNextItemWidth(-90); ImGui.SliderFloat("Height", ref p.Height, 0f, 12f, "%.1f in");
                ImGui.SetNextItemWidth(-90); ImGui.SliderInt("Points", ref p.Points, 1, 5);
                UiText.Disabled("Points: its cost in 'Alternating: Points' terrain mode.");

                if (p.Raw == null && UiButton.Navigate("Duplicate", new Vector2(120, 0)))
                {
                    Piece copy = p.Clone();
                    copy.X = MathF.Min(TW, copy.X + 2f); copy.Z = MathF.Max(0f, copy.Z - 2f);
                    _pieces.Add(copy);
                    _selIndex = _pieces.Count - 1;
                }
                if (p.Raw == null) ImGui.SameLine();
                if (UiButton.Back("Delete", new Vector2(120, 0))) DeleteSelected();
                break;
            }
            case ESel.Objective when _selIndex >= 0 && _selIndex < _objectives.Count:
            {
                Vector2 o = _objectives[_selIndex];
                ImGui.TextUnformatted($"Selected objective {_selIndex + 1}");
                ImGui.SetNextItemWidth(-90); ImGui.DragFloat("X##ox", ref o.X, 0.25f, 0f, TW, "%.1f in");
                ImGui.SetNextItemWidth(-90); ImGui.DragFloat("Y##oz", ref o.Y, 0.25f, 0f, TH, "%.1f in");
                _objectives[_selIndex] = o;
                if (UiButton.Back("Delete", new Vector2(120, 0))) DeleteSelected();
                break;
            }
            case ESel.BaseMarker when _selIndex >= 0 && _selIndex < _baseMarkers.Count:
            {
                MapBaseMarkerEntry m = _baseMarkers[_selIndex];
                ImGui.TextUnformatted("Selected base reference");
                UiText.Disabled("A size reference only - the game ignores it.");

                float x = m.X, z = m.Z;
                ImGui.SetNextItemWidth(-90);
                if (ImGui.DragFloat("X##bx", ref x, 0.25f, 0f, TW, "%.1f in")) m.X = x;
                ImGui.SetNextItemWidth(-90);
                if (ImGui.DragFloat("Y##bz", ref z, 0.25f, 0f, TH, "%.1f in")) m.Z = z;

                float across = m.Base.Shape == EBaseShapeKind.Rectangle
                    ? MathF.Max(m.Base.WidthInches, m.Base.HeightInches)
                    : m.Base.DiameterInches;
                UiText.Disabled($"{m.Label}  -  {across:0.##} in across ({across * 25.4f:0} mm)");

                if (UiButton.Back("Delete", new Vector2(120, 0))) DeleteSelected();
                break;
            }
            default:
                UiText.Disabled("SELECTED: NOTHING");
                UiText.Wrapped("Use Select / Move and click something on the board to edit it here.");
                break;
        }
    }

    private static void FlagBox(string label, ref ETerrainType type, ETerrainType flag)
    {
        bool on = type.HasFlag(flag);
        if (ImGui.Checkbox(label, ref on)) type = on ? type | flag : type & ~flag;
    }

    // ── Board ─────────────────────────────────────────────────────────────────────────────────────────
    private void DrawBoard()
    {
        Vector2 p0 = ImGui.GetCursorScreenPos();
        Vector2 avail = ImGui.GetContentRegionAvail();
        float fit = MathF.Max(0.1f, MathF.Min(avail.X / TW, avail.Y / TH));
        bool boardHovered = ImGui.IsWindowHovered();
        HandleCameraKeys(fit, boardHovered);
        float scale = fit * _boardZoom;
        int ox = (int)(p0.X + (avail.X - TW * scale) / 2f + _boardPan.X);
        int oy = (int)(p0.Y + (avail.Y - TH * scale) / 2f + _boardPan.Y);
        Vector2 P(float x, float z) => new(ox + x * scale, oy + (TH - z) * scale);
        Vector2 ToTable(Vector2 screen) => new((screen.X - ox) / scale, TH - (screen.Y - oy) / scale);

        // One invisible button over the whole board catches clicks and drags.
        ImGui.InvisibleButton("##boardInput", avail, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool hovered = ImGui.IsItemHovered();
        Vector2 mouse = ToTable(ImGui.GetIO().MousePos);
        Vector2 snapped = new(SnapClamp(mouse.X, TW), SnapClamp(mouse.Y, TH));
        HandleInput(hovered, mouse, snapped);

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        TableBackgroundStyle style = TableBackgrounds.For(ETableBackground.Forest);
        Vector2 tl = P(0, TH), br = P(TW, 0);
        dl.AddRectFilled(tl, br, U32(style.Surface));
        for (float x = 6; x < TW; x += 6) dl.AddLine(P(x, 0), P(x, TH), U32(x % 12 == 0 ? style.GridMajor : style.GridMinor));
        for (float z = 6; z < TH; z += 6) dl.AddLine(P(0, z), P(TW, z), U32(z % 12 == 0 ? style.GridMajor : style.GridMinor));

        dl.PushClipRect(tl, br, true);

        // Terrain.
        for (int i = 0; i < _pieces.Count; i++)
        {
            Piece p = _pieces[i];
            IZone shape = p.ToZone();
            (Color fill, Color outline) = TerrainColors.For(p.Type);
            ZoneRenderer.DrawFilled(shape, dl, scale, ox, oy, TH, U32(fill), U32(outline), 1.5f);
            if (_selKind == ESel.Piece && _selIndex == i)
                ZoneRenderer.DrawFilled(shape, dl, scale, ox, oy, TH, 0u, SelectColor, 3f);
            if (p.Name.Length > 0)
            {
                Vector2 ts = ImGui.CalcTextSize(p.Name);
                dl.AddText(P(p.X, p.Z) - ts / 2f, U32(255, 255, 255, 220), p.Name);
            }
        }

        // Objectives.
        for (int i = 0; i < _objectives.Count; i++)
        {
            Vector2 c = P(_objectives[i].X, _objectives[i].Y);
            dl.AddCircleFilled(c, 3f * scale, U32(235, 235, 235, 40), 40);
            dl.AddCircle(c, 3f * scale, U32(235, 235, 235, 150), 40, 1.5f);
            float r = MathF.Max(8f, 0.7f * scale);
            dl.AddCircleFilled(c, r, U32(235, 235, 235), 24);
            dl.AddCircle(c, r, _selKind == ESel.Objective && _selIndex == i ? SelectColor : U32(20, 20, 20), 24,
                _selKind == ESel.Objective && _selIndex == i ? 3f : 1.5f);
            string n = (i + 1).ToString();
            Vector2 ts = ImGui.CalcTextSize(n);
            dl.AddText(c - ts / 2f, U32(20, 20, 20), n);
        }

        // Ghost: the piece about to be placed.
        if (hovered && _tool == ETool.Place && _preset is Preset pr)
        {
            var ghost = new Piece { Circle = pr.Circle, X = snapped.X, Z = snapped.Y, W = pr.W, D = pr.D, R = pr.R };
            (Color fill, Color outline) = TerrainColors.For(pr.Type);
            ZoneRenderer.DrawFilled(ghost.ToZone(), dl, scale, ox, oy, TH, U32(fill, 0.5f), U32(outline, 0.8f), 1.5f);
        }
        // Base references: drawn LAST of the real content so they sit on top of the terrain they are
        // being measured against, and in a flat grey-blue so nobody mistakes one for a piece of the map.
        for (int i = 0; i < _baseMarkers.Count; i++)
        {
            MapBaseMarkerEntry m = _baseMarkers[i];
            bool sel = _selKind == ESel.BaseMarker && _selIndex == i;
            uint fill = U32(150, 170, 200, sel ? 190 : 130);
            uint line = sel ? U32(255, 255, 255, 255) : U32(220, 230, 245, 200);

            if (m.Base.Shape == EBaseShapeKind.Rectangle)
            {
                Vector2 h = new(m.Base.WidthInches / 2f, m.Base.HeightInches / 2f);
                Vector2 a = P(m.X - h.X, m.Z + h.Y), b = P(m.X + h.X, m.Z - h.Y);
                dl.AddRectFilled(a, b, fill);
                dl.AddRect(a, b, line, 0f, ImDrawFlags.None, sel ? 2.5f : 1.5f);
            }
            else
            {
                float r = MathF.Max(2f, m.Base.DiameterInches / 2f * scale);
                dl.AddCircleFilled(P(m.X, m.Z), r, fill, 28);
                dl.AddCircle(P(m.X, m.Z), r, line, 28, sel ? 2.5f : 1.5f);
            }

            // The label only once the base is big enough on screen to hold it; below that it is a smear.
            if (m.Label.Length > 0 && m.Base.DiameterInches * scale > 26f)
            {
                Vector2 ts = ImGui.CalcTextSize(m.Label);
                dl.AddText(P(m.X, m.Z) - ts / 2f, U32(20, 25, 35, 230), m.Label);
            }
        }

        if (hovered && _tool == ETool.Objective)
            dl.AddCircle(P(snapped.X, snapped.Y), MathF.Max(8f, 0.7f * scale), U32(255, 255, 255, 160), 24, 2f);
        if (hovered && _tool == ETool.BaseMarker)
        {
            // Ghost of the base about to be placed, at its real size.
            if (_basePreset.Circle)
            {
                float r = MathF.Max(2f, _basePreset.DiameterInches / 2f * scale);
                dl.AddCircle(P(snapped.X, snapped.Y), r, U32(255, 255, 255, 170), 28, 2f);
            }
            else
            {
                Vector2 h = new(_basePreset.WidthInches / 2f, _basePreset.HeightInches / 2f);
                dl.AddRect(P(snapped.X - h.X, snapped.Y + h.Y), P(snapped.X + h.X, snapped.Y - h.Y),
                    U32(255, 255, 255, 170), 0f, ImDrawFlags.None, 2f);
            }
        }
        dl.PopClipRect();

        dl.AddRect(tl, br, U32(style.Border), 0f, ImDrawFlags.None, 3f);

        // Cursor readout, bottom-left of the board.
        if (hovered)
            dl.AddText(new Vector2(tl.X + 6, br.Y - ImGui.GetTextLineHeight() - 4), U32(255, 255, 255, 200),
                $"{snapped.X:0.#}\", {snapped.Y:0.#}\"");
    }

    // Board camera: WASD pans, Shift+W / S (or the mouse wheel over the board) zooms.
    private float _boardZoom = 1f;
    private Vector2 _boardPan;

    private void HandleCameraKeys(float fit, bool hovered)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        float dt = MathF.Min(io.DeltaTime, 0.05f);
        float zoomStep = hovered ? io.MouseWheel * 0.15f : 0f;
        if (!io.WantTextInput && !io.KeyCtrl)
        {
            float k = ViewSettings.CameraSpeed;
            float panPx = 700f * dt * k;
            if (!io.KeyShift)
            {
                if (ImGui.IsKeyDown(ImGuiKey.W)) _boardPan.Y += panPx;
                if (ImGui.IsKeyDown(ImGuiKey.S)) _boardPan.Y -= panPx;
                if (ImGui.IsKeyDown(ImGuiKey.A)) _boardPan.X += panPx;
                if (ImGui.IsKeyDown(ImGuiKey.D)) _boardPan.X -= panPx;
            }
            else
            {
                if (ImGui.IsKeyDown(ImGuiKey.W)) zoomStep += dt * 1.2f * k;
                if (ImGui.IsKeyDown(ImGuiKey.S)) zoomStep -= dt * 1.2f * k;
            }
        }
        if (zoomStep != 0f) _boardZoom = Math.Clamp(_boardZoom * MathF.Exp(zoomStep), 1f, 4f);
        if (_boardZoom <= 1.001f) _boardPan = Vector2.Zero;
        float limX = TW * fit * _boardZoom / 2f, limY = TH * fit * _boardZoom / 2f;
        _boardPan = new Vector2(Math.Clamp(_boardPan.X, -limX, limX), Math.Clamp(_boardPan.Y, -limY, limY));
    }

    private static float SnapClamp(float v, float max) => Math.Clamp(MathF.Round(v / Snap) * Snap, 0f, max);

    private void HandleInput(bool hovered, Vector2 mouse, Vector2 snapped)
    {
        // Right-click: back to Select.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) SetTool(ETool.Select);

        bool pressed = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool down = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        switch (_tool)
        {
            case ETool.Place when pressed && _preset is Preset pr:
                _pieces.Add(new Piece
                {
                    Name = pr.Name, Type = pr.Type, Height = pr.Height, Points = pr.Points, Circle = pr.Circle,
                    X = snapped.X, Z = snapped.Y, W = pr.W, D = pr.D, R = pr.R,
                });
                _selKind = ESel.Piece; _selIndex = _pieces.Count - 1;
                break;

            case ETool.Objective when pressed:
                _objectives.Add(snapped);
                _selKind = ESel.Objective; _selIndex = _objectives.Count - 1;
                break;

            case ETool.BaseMarker when pressed:
                _baseMarkers.Add(new MapBaseMarkerEntry
                {
                    X = snapped.X, Z = snapped.Y, Label = _basePreset.Label,
                    Base = new BaseFileEntry
                    {
                        Shape = _basePreset.Circle ? EBaseShapeKind.Circle : EBaseShapeKind.Rectangle,
                        DiameterInches = _basePreset.DiameterInches,
                        WidthInches = _basePreset.WidthInches,
                        HeightInches = _basePreset.HeightInches,
                    },
                });
                _selKind = ESel.BaseMarker; _selIndex = _baseMarkers.Count - 1;
                break;

            case ETool.Select:
                if (pressed)
                {
                    PickAt(mouse);
                    _dragging = _selKind != ESel.None && !(_selKind == ESel.Piece && _pieces[_selIndex].Raw != null);
                    _dragOffset = SelectedAnchor() - mouse;
                }
                else if (_dragging && down)
                {
                    Vector2 target = mouse + _dragOffset;
                    MoveSelectedTo(new Vector2(SnapClamp(target.X, TW), SnapClamp(target.Y, TH)));
                }
                else if (!down) _dragging = false;
                break;
        }
    }

    /// <summary>Selects the topmost thing under the cursor: base references, objectives, then terrain
    /// (last drawn on top).</summary>
    private void PickAt(Vector2 m)
    {
        for (int i = _baseMarkers.Count - 1; i >= 0; i--)
        {
            MapBaseMarkerEntry b = _baseMarkers[i];
            // Picked by a generous radius rather than the exact footprint: a 25mm base is a quarter of
            // an inch of screen at default zoom, and an exact hit test would make it unclickable.
            float r = MathF.Max(BasePickRadius, BaseRadiusInches(b));
            if (Vector2.Distance(new Vector2(b.X, b.Z), m) <= r) { _selKind = ESel.BaseMarker; _selIndex = i; return; }
        }
        for (int i = _objectives.Count - 1; i >= 0; i--)
            if (Vector2.Distance(_objectives[i], m) <= ObjectivePickRadius) { _selKind = ESel.Objective; _selIndex = i; return; }
        for (int i = _pieces.Count - 1; i >= 0; i--)
            if (_pieces[i].ToZone().IsPointWithinZone(new Float2(m.X, m.Y))) { _selKind = ESel.Piece; _selIndex = i; return; }
        Deselect();
    }

    /// <summary>Half the widest dimension of a base, in inches.</summary>
    private static float BaseRadiusInches(MapBaseMarkerEntry m) =>
        m.Base.Shape == EBaseShapeKind.Rectangle
            ? MathF.Max(m.Base.WidthInches, m.Base.HeightInches) / 2f
            : m.Base.DiameterInches / 2f;

    private const float BasePickRadius = 1.0f;

    private Vector2 SelectedAnchor() => _selKind switch
    {
        ESel.Piece => new Vector2(_pieces[_selIndex].X, _pieces[_selIndex].Z),
        ESel.Objective => _objectives[_selIndex],
        ESel.BaseMarker => new Vector2(_baseMarkers[_selIndex].X, _baseMarkers[_selIndex].Z),
        _ => Vector2.Zero,
    };

    private void MoveSelectedTo(Vector2 t)
    {
        switch (_selKind)
        {
            case ESel.Piece:
                _pieces[_selIndex].X = t.X; _pieces[_selIndex].Z = t.Y;
                break;
            case ESel.Objective:
                _objectives[_selIndex] = t;
                break;
            case ESel.BaseMarker:
                _baseMarkers[_selIndex].X = t.X; _baseMarkers[_selIndex].Z = t.Y;
                break;
        }
    }
}
