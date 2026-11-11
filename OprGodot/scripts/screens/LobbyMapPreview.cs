using FDG;
using FDG.SaveLoad;
using FdgRaylib.Lobby;
using FdgRaylib.Rendering;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// The lobby's flat top-down picture of the table it is set up for: surface, 6" grid, each side's deployment
/// zone, the terrain (when the layout is known before the game) and objectives a map fixes. Same reading of
/// the settings as the Raylib preview.
/// </summary>
public partial class LobbyMapPreview : Control
{
    public LobbySession? Session;
    public string Note { get; private set; } = "";

    private string? _layoutPath;
    private TerrainLayoutFile? _layout, _default;
    private (float W, float H) _defaultSize;

    public override void _Draw()
    {
        if (Session?.Vm is not { } vm) return;
        (float tw, float th) = Session.TableWithoutMap();
        bool horde = GameSystemInfo.IsHordeLike(vm.GameMode);

        TerrainLayoutFile? layout = null;
        string terrainNote;
        switch (vm.TerrainPlacementMode)
        {
            case ETerrainPlacementMode.AutoFromLayout:
                if (_default == null || !TableSizing.Same(_defaultSize, (tw, th)))
                {
                    _default = FDG.Stages.DefaultTerrainPool.ForTable(tw, th, out _);
                    _defaultSize = (tw, th);
                }
                layout = _default;
                terrainNote = "random terrain";
                break;
            case ETerrainPlacementMode.LoadFromFile:
                if (_layoutPath != vm.TerrainLayoutPath)
                {
                    _layoutPath = vm.TerrainLayoutPath;
                    _layout = string.IsNullOrEmpty(_layoutPath) ? null : TerrainLayoutLoader.TryLoadFromFile(_layoutPath, out _);
                }
                layout = _layout;
                terrainNote = layout != null ? "map: " + (string.IsNullOrWhiteSpace(layout.Name) ? Path.GetFileNameWithoutExtension(_layoutPath) : layout.Name) : "no map chosen";
                if (layout is { TableWidthInches: > 0f, TableHeightInches: > 0f }) { tw = layout.TableWidthInches; th = layout.TableHeightInches; }
                break;
            default:
                terrainNote = "terrain placed by the players";
                break;
        }
        bool mapObjectives = !horde && layout != null && layout.Objectives is { Count: > 0 }
            && vm.TerrainPlacementMode == ETerrainPlacementMode.LoadFromFile;
        int sides = LobbyDeploymentPreview.Sides(vm.PlayerInfos.Select(p => (int)p.TeamNumber));
        LobbyDeploymentPreview.Plan plan = LobbyDeploymentPreview.For(vm.GameSystem, vm.GameMode, vm.DeploymentStyle, sides, tw, th);
        string objectives = horde ? "5 fixed objectives"
            : mapObjectives ? $"{layout!.Objectives.Count} objectives set by the map"
            : vm.ObjectivePlacementMode == EObjectivePlacementMode.PlayerPlaced ? "objectives placed by the players" : "objectives placed at the start";
        Note = $"{TableSizing.Label(tw, th)}  -  {terrainNote}, {objectives}" + (plan.Note.Length > 0 ? ". " + plan.Note : "");

        Vector2 size = Size;
        float scale = Mathf.Max(0.1f, Mathf.Min(size.X / tw, size.Y / th));
        Vector2 o = new((size.X - tw * scale) / 2f, (size.Y - th * scale) / 2f);
        Vector2 P(float x, float z) => o + new Vector2(x * scale, (th - z) * scale);

        var table = new Rect2(P(0, th), new Vector2(tw * scale, th * scale));
        DrawRect(table, new Color(0.115f, 0.14f, 0.125f));
        for (float x = 6; x < tw; x += 6) DrawLine(P(x, 0), P(x, th), new Color(1, 1, 1, x % 12 == 0 ? 0.12f : 0.05f));
        for (float z = 6; z < th; z += 6) DrawLine(P(0, z), P(tw, z), new Color(1, 1, 1, z % 12 == 0 ? 0.12f : 0.05f));

        Color zoneA = HudKit.Friendly with { A = 0.28f }, zoneB = HudKit.Enemy with { A = 0.28f };
        if (horde)
        {
            // The corner zones are quarter circles: clipped to the table.
            Vector2[] tableRect = { P(0, th), P(tw, th), P(tw, 0), P(0, 0) };
            void Zone(float cx, float cz, Color colour)
            {
                var circle = Enumerable.Range(0, 48).Select(i => P(cx, cz) + Vector2.FromAngle(i * Mathf.Tau / 48) * 9f * scale).ToArray();
                foreach (Vector2[] part in Geometry2D.IntersectPolygons(circle, tableRect)) DrawColoredPolygon(part, colour);
            }
            Zone(tw / 2, th / 2, zoneA);
            foreach (var (cx, cz) in new[] { (0f, 0f), (tw, 0f), (0f, th), (tw, th) }) Zone(cx, cz, zoneB);
        }
        else
            for (int k = 0; k < plan.Zones.Count; k++) Fill(plan.Zones[k], k % 2 == 0 ? zoneA : zoneB, P);

        if (layout != null)
            foreach (TerrainPieceEntry piece in layout.Pieces)
                if (piece.Shape != null && (vm.TerrainPlacementMode != ETerrainPlacementMode.AutoFromLayout
                        || FDG.Stages.PlaceTerrainStage.FitsTable(piece.Shape, tw, th)))
                    Fill(piece.Shape, TerrainColour(piece.TerrainType), P);

        void Objective(float x, float z, int n)
        {
            Vector2 c = P(x, z);
            float r = Mathf.Max(6f, 1.2f * scale);
            DrawCircle(c, r, new Color(0.92f, 0.92f, 0.92f));
            DrawArc(c, r, 0, Mathf.Tau, 24, Colors.Black, 2f, true);
            DrawArc(c, 3f * scale, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.35f), 1f, true);
            DrawString(HudKit.Bold, c + new Vector2(-4, 5), n.ToString(), HorizontalAlignment.Left, -1, 13, Colors.Black);
        }
        if (horde)
        {
            var spots = new[] { (tw / 2, th / 2), (tw / 4, th / 4), (tw * 3 / 4, th / 4), (tw / 4, th * 3 / 4), (tw * 3 / 4, th * 3 / 4) };
            for (int k = 0; k < spots.Length; k++) Objective(spots[k].Item1, spots[k].Item2, k + 1);
        }
        if (mapObjectives)
            for (int k = 0; k < layout!.Objectives.Count; k++) Objective(layout.Objectives[k].X, layout.Objectives[k].Z, k + 1);

        DrawRect(table, HudKit.Edge, false, 2f);
    }

    private void Fill(IZone shape, Color colour, Func<float, float, Vector2> P)
    {
        foreach (var poly in ZoneOutlines.Polygons(shape))
        {
            if (poly.Length < 3) continue;
            var pts = poly.Select(p => P(p.X, p.Z)).ToArray();
            if (Geometry2D.TriangulatePolygon(pts).Length == 0) continue; // a self-crossing outline Godot can't fill
            DrawColoredPolygon(pts, colour);
        }
    }

    private static Color TerrainColour(ETerrainType type)
    {
        if (type.HasFlag(ETerrainType.Dangerous)) return Color.Color8(150, 60, 40, 230);
        if (type.HasFlag(ETerrainType.Blocking) || type.HasFlag(ETerrainType.Impassible)) return Color.Color8(75, 75, 82, 240);
        if (type.HasFlag(ETerrainType.Cover) && type.HasFlag(ETerrainType.Difficult)) return Color.Color8(25, 70, 30, 230);
        if (type.HasFlag(ETerrainType.Cover)) return Color.Color8(120, 95, 60, 230);
        if (type.HasFlag(ETerrainType.Difficult)) return Color.Color8(95, 85, 55, 220);
        return Color.Color8(110, 110, 110, 200);
    }
}
