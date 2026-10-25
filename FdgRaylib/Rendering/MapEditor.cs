using System.Numerics;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.SaveLoad;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The 2D map maker, opened from the Play (lobby) screen's Custom Map picker. It takes over the screen
/// while open: tools and the selected item's settings on the left, the 6' x 4' board on the right.
///
/// <para>Tools: Select (click to pick, drag to move), a palette of terrain pieces (building, sandbags,
/// trees, rocks, rubble, mine field, plain rectangle / circle - click the board to drop one), Objective
/// (click to drop a marker) and Deployment Zone (drag a rectangle). Right-click goes back to Select;
/// Delete removes the selected item. Positions snap to half inches.</para>
///
/// <para>A map is saved as an ordinary terrain layout (.fdgterrain) in the Maps folder, plus its own
/// objectives and deployment zones (see <see cref="TerrainLayoutFile.Objectives"/>).</para>
/// </summary>
public sealed class MapEditor
{
    public bool IsOpen { get; private set; }

    /// <summary>Called with the saved file's path after a successful save.</summary>
    public Action<string>? OnSaved;

    private const float TW = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;
    private const float TH = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
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

    private sealed class Zone { public float L, R, B, T; }

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
    private readonly List<Zone> _zones = new();
    private string _name = "";
    private string? _path;
    private string _status = "";

    // ── Tools and selection ───────────────────────────────────────────────────────────────────────────
    private enum ETool { Select, Place, Objective, DeployZone }
    private enum ESel { None, Piece, Objective, Zone }
    private ETool _tool = ETool.Select;
    private Preset? _preset;
    private ESel _selKind = ESel.None;
    private int _selIndex = -1;
    private bool _dragging;
    private Vector2 _dragOffset;
    private Vector2? _zoneStart;

    // ── Opening ───────────────────────────────────────────────────────────────────────────────────────
    public void OpenNew()
    {
        Reset();
        _name = "New Map";
        // Start from the standard long-edge deployment zones so a new map is playable straight away.
        float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
        _zones.Add(new Zone { L = 0, R = TW, B = 0, T = d });
        _zones.Add(new Zone { L = 0, R = TW, B = TH - d, T = TH });
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
        foreach (TerrainPieceEntry e in layout.Pieces)
            if (e.Shape != null) _pieces.Add(FromEntry(e));
        foreach (MapObjectiveEntry o in layout.Objectives ?? new())
            _objectives.Add(new Vector2(o.X, o.Z));
        foreach (MapDeploymentZoneEntry z in layout.DeploymentZones ?? new())
            _zones.Add(new Zone { L = z.Left, R = z.Right, B = z.Bottom, T = z.Top });
        IsOpen = true;
    }

    private void Reset()
    {
        _boardZoom = 1f; _boardPan = Vector2.Zero;
        _pieces.Clear(); _objectives.Clear(); _zones.Clear();
        _path = null; _status = "";
        _tool = ETool.Select; _preset = null;
        _selKind = ESel.None; _selIndex = -1;
        _dragging = false; _zoneStart = null;
    }

    private static Piece FromEntry(TerrainPieceEntry e)
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

    private static Vector2 ShapeCenter(IZone shape)
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
        Pieces = _pieces.Select(p => new TerrainPieceEntry
        {
            Name = p.Name,
            TerrainType = p.Type,
            Shape = p.ToZone(),
            HeightInches = p.Height,
            Points = p.Points,
        }).ToList(),
        Objectives = _objectives.Select(o => new MapObjectiveEntry { X = o.X, Z = o.Y }).ToList(),
        DeploymentZones = _zones.Select(z => new MapDeploymentZoneEntry
        {
            Left = MathF.Min(z.L, z.R), Right = MathF.Max(z.L, z.R),
            Bottom = MathF.Min(z.B, z.T), Top = MathF.Max(z.B, z.T),
        }).ToList(),
    };

    private bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(_name)) { _status = "Give the map a name first."; return false; }
        if (_zones.Count == 1) { _status = "Add a second deployment zone (or remove the only one to use the standard zones)."; return false; }
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

    private static readonly uint[] ZoneFill = { U32(80, 160, 255, 70), U32(255, 110, 90, 70), U32(90, 220, 120, 70), U32(240, 210, 80, 70) };
    private static readonly uint[] ZoneLine = { U32(80, 160, 255, 220), U32(255, 110, 90, 220), U32(90, 220, 120, 220), U32(240, 210, 80, 220) };
    private static readonly uint SelectColor = U32(255, 235, 60);

    public void Draw(int screenW, int screenH)
    {
        if (!IsOpen) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.SetNextWindowFocus();
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.09f, 0.11f, 1f));
        ImGui.Begin("##MapEditor", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
                                   ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
                                   ImGuiWindowFlags.NoScrollWithMouse);

        float margin = 12f;
        float panelW = MathF.Max(300f, screenW * 0.22f);

        // Left panel: name, tools, selected item, save/cancel.
        ImGui.SetCursorPos(new Vector2(margin, margin));
        ImGui.BeginChild("##mapTools", new Vector2(panelW, screenH - margin * 2f), ImGuiChildFlags.Borders);
        DrawToolPanel();
        ImGui.EndChild();

        // Right: the board.
        ImGui.SetCursorPos(new Vector2(margin * 2f + panelW, margin));
        ImGui.BeginChild("##mapBoard", new Vector2(screenW - panelW - margin * 3f, screenH - margin * 2f),
            ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawBoard();
        ImGui.EndChild();

        // Keyboard: Delete removes the selection (not while typing in a text box).
        if (!ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete, false)) DeleteSelected();

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void DrawToolPanel()
    {
        ImGui.TextUnformatted(_path == null ? "MAP MAKER - New map" : "MAP MAKER - Editing");
        ImGui.Separator();

        ImGui.TextUnformatted("Map name");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##mapName", ref _name, 64);
        ImGui.Spacing();

        // Tools.
        ImGui.TextDisabled("Tools  (right-click the board to go back to Select)");
        float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        Vector2 btn = new(half, 0f);
        if (ToolButton("Select / Move", _tool == ETool.Select, btn)) SetTool(ETool.Select);
        ImGui.SameLine();
        if (ToolButton("Objective", _tool == ETool.Objective, btn)) SetTool(ETool.Objective);
        if (ToolButton("Deploy Zone", _tool == ETool.DeployZone, btn)) SetTool(ETool.DeployZone);
        ImGui.SameLine();
        if (ToolButton("Clear Board", false, btn)) ImGui.OpenPopup("Clear the whole board?");

        ImGui.Spacing();
        ImGui.TextDisabled("Terrain  (click the board to place)");
        for (int i = 0; i < Presets.Length; i++)
        {
            if (i % 2 == 1) ImGui.SameLine();
            if (ToolButton(Presets[i].Label, _tool == ETool.Place && _preset == Presets[i], btn))
            {
                SetTool(ETool.Place);
                _preset = Presets[i];
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawSelectionProperties();

        // Counts + hints.
        ImGui.Separator();
        ImGui.TextDisabled($"{_pieces.Count} terrain, {_objectives.Count} objectives, {_zones.Count} deploy zones");
        if (_objectives.Count == 0)
            ImGui.TextWrapped("No objectives: the game rolls D3+2 and places them as usual.");
        if (_zones.Count == 0)
            ImGui.TextWrapped("No deploy zones: the standard zones along the long edges are used.");

        // Save / Cancel pinned to the bottom of the panel.
        float rowH = ImGui.GetFrameHeight() * 1.6f;
        float y = ImGui.GetWindowHeight() - rowH - ImGui.GetStyle().WindowPadding.Y - (_status.Length > 0 ? ImGui.GetTextLineHeightWithSpacing() * 2f : 0f);
        if (ImGui.GetCursorPosY() < y) ImGui.SetCursorPosY(y);
        if (_status.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.45f, 1f));
            ImGui.TextWrapped(_status);
            ImGui.PopStyleColor();
        }
        Vector2 big = new(half, rowH);
        if (UiButton.Back("Cancel", big)) { IsOpen = false; }
        ImGui.SameLine();
        if (UiButton.Confirm("Save Map", big)) TrySave();

        // Confirm popup for Clear Board.
        if (ImGui.BeginPopupModal("Clear the whole board?", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Remove all terrain, objectives and deployment zones?");
            if (UiButton.Back("No", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
            ImGui.SameLine();
            if (UiButton.Confirm("Yes, clear", new Vector2(120, 0)))
            {
                _pieces.Clear(); _objectives.Clear(); _zones.Clear();
                Deselect();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
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
        _zoneStart = null;
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
            case ESel.Zone when _selIndex < _zones.Count: _zones.RemoveAt(_selIndex); break;
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
                ImGui.TextUnformatted("Selected terrain");
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
                ImGui.TextDisabled("Points: its cost in 'Alternating: Points' terrain mode.");

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
            case ESel.Zone when _selIndex >= 0 && _selIndex < _zones.Count:
            {
                Zone z = _zones[_selIndex];
                ImGui.TextUnformatted($"Selected deploy zone {_selIndex + 1}");
                ImGui.SetNextItemWidth(-90); ImGui.DragFloat("Left", ref z.L, 0.25f, 0f, TW, "%.1f in");
                ImGui.SetNextItemWidth(-90); ImGui.DragFloat("Right", ref z.R, 0.25f, 0f, TW, "%.1f in");
                ImGui.SetNextItemWidth(-90); ImGui.DragFloat("Bottom", ref z.B, 0.25f, 0f, TH, "%.1f in");
                ImGui.SetNextItemWidth(-90); ImGui.DragFloat("Top", ref z.T, 0.25f, 0f, TH, "%.1f in");
                ImGui.TextDisabled("Zone 1 goes to the first team to pick, and so on.");
                if (UiButton.Back("Delete", new Vector2(120, 0))) DeleteSelected();
                break;
            }
            default:
                ImGui.TextDisabled("Nothing selected. Use Select / Move and click\nsomething on the board to edit it.");
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

        // Deployment zones (under everything else).
        for (int i = 0; i < _zones.Count; i++)
        {
            Zone z = _zones[i];
            Vector2 a = P(MathF.Min(z.L, z.R), MathF.Max(z.B, z.T)), b = P(MathF.Max(z.L, z.R), MathF.Min(z.B, z.T));
            dl.AddRectFilled(a, b, ZoneFill[i % ZoneFill.Length]);
            dl.AddRect(a, b, ZoneLine[i % ZoneLine.Length], 0f, ImDrawFlags.None, 2f);
            dl.AddText(a + new Vector2(6, 4), ZoneLine[i % ZoneLine.Length], $"Deploy {i + 1}");
            if (_selKind == ESel.Zone && _selIndex == i) dl.AddRect(a, b, SelectColor, 0f, ImDrawFlags.None, 3f);
        }

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

        // Ghosts: the piece about to be placed, or the zone being dragged out.
        if (hovered && _tool == ETool.Place && _preset is Preset pr)
        {
            var ghost = new Piece { Circle = pr.Circle, X = snapped.X, Z = snapped.Y, W = pr.W, D = pr.D, R = pr.R };
            (Color fill, Color outline) = TerrainColors.For(pr.Type);
            ZoneRenderer.DrawFilled(ghost.ToZone(), dl, scale, ox, oy, TH, U32(fill, 0.5f), U32(outline, 0.8f), 1.5f);
        }
        if (hovered && _tool == ETool.Objective)
            dl.AddCircle(P(snapped.X, snapped.Y), MathF.Max(8f, 0.7f * scale), U32(255, 255, 255, 160), 24, 2f);
        if (_zoneStart is Vector2 zs)
        {
            Vector2 a = P(MathF.Min(zs.X, snapped.X), MathF.Max(zs.Y, snapped.Y));
            Vector2 b = P(MathF.Max(zs.X, snapped.X), MathF.Min(zs.Y, snapped.Y));
            int k = _zones.Count;
            dl.AddRectFilled(a, b, ZoneFill[k % ZoneFill.Length]);
            dl.AddRect(a, b, ZoneLine[k % ZoneLine.Length], 0f, ImDrawFlags.None, 2f);
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

            case ETool.DeployZone:
                if (pressed) _zoneStart = snapped;
                else if (_zoneStart is Vector2 start && !down)
                {
                    if (MathF.Abs(snapped.X - start.X) >= 1f && MathF.Abs(snapped.Y - start.Y) >= 1f)
                    {
                        _zones.Add(new Zone
                        {
                            L = MathF.Min(start.X, snapped.X), R = MathF.Max(start.X, snapped.X),
                            B = MathF.Min(start.Y, snapped.Y), T = MathF.Max(start.Y, snapped.Y),
                        });
                        _selKind = ESel.Zone; _selIndex = _zones.Count - 1;
                    }
                    _zoneStart = null;
                }
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

    /// <summary>Selects the topmost thing under the cursor: objectives first, then terrain (last drawn on
    /// top), then deployment zones.</summary>
    private void PickAt(Vector2 m)
    {
        for (int i = _objectives.Count - 1; i >= 0; i--)
            if (Vector2.Distance(_objectives[i], m) <= ObjectivePickRadius) { _selKind = ESel.Objective; _selIndex = i; return; }
        for (int i = _pieces.Count - 1; i >= 0; i--)
            if (_pieces[i].ToZone().IsPointWithinZone(new Float2(m.X, m.Y))) { _selKind = ESel.Piece; _selIndex = i; return; }
        for (int i = _zones.Count - 1; i >= 0; i--)
        {
            Zone z = _zones[i];
            if (m.X >= MathF.Min(z.L, z.R) && m.X <= MathF.Max(z.L, z.R) && m.Y >= MathF.Min(z.B, z.T) && m.Y <= MathF.Max(z.B, z.T))
            { _selKind = ESel.Zone; _selIndex = i; return; }
        }
        Deselect();
    }

    private Vector2 SelectedAnchor() => _selKind switch
    {
        ESel.Piece => new Vector2(_pieces[_selIndex].X, _pieces[_selIndex].Z),
        ESel.Objective => _objectives[_selIndex],
        ESel.Zone => new Vector2(MathF.Min(_zones[_selIndex].L, _zones[_selIndex].R), MathF.Min(_zones[_selIndex].B, _zones[_selIndex].T)),
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
            case ESel.Zone:
                Zone z = _zones[_selIndex];
                float w = MathF.Abs(z.R - z.L), h = MathF.Abs(z.T - z.B);
                float l = Math.Clamp(t.X, 0f, TW - w), b = Math.Clamp(t.Y, 0f, TH - h);
                z.L = l; z.R = l + w; z.B = b; z.T = b + h;
                break;
        }
    }
}
