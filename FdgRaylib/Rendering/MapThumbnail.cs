using System.Numerics;
using FDG;
using FDG.SaveLoad;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// A flat top-down picture of a table: surface, grid, deployment zones, terrain and objective markers.
/// The zones drawn are always the lobby's Deployment pick (or Horde's), never a map's own: deployment is
/// picked in the lobby (Kai, 2026-11-07), and zones an old map carries are ignored by the game.
///
/// <para>
/// Lifted out of the lobby's own preview so the <see cref="MapPickerScreen"/>'s tiles and the lobby's
/// big preview are the same picture at two sizes. A map that looks one way when you pick it and another
/// way in the lobby is a small thing that costs a lot of trust, and two drawing routines would drift
/// the first time a terrain colour changed.
/// </para>
///
/// <para>
/// Draw-list only: it takes a rectangle and paints inside it, touching no ImGui cursor state, so a
/// caller can put it in a grid cell, a child window or behind a button without it moving anything.
/// </para>
/// </summary>
public static class MapThumbnail
{
    private static uint U32(Raylib_cs.Color c, float alpha = 1f) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f * alpha));

    private static uint U32(int r, int g, int b, int a = 255) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));

    /// <summary>Which deployment zones to paint when the layout carries none of its own.</summary>
    public enum EFallbackZones
    {
        /// <summary>None - no zones drawn (the Map Maker's own pictures: a map holds no deployment).</summary>
        None,
        /// <summary>The books' two bands along the long edges.</summary>
        Standard,
        /// <summary>Horde's centre circle and four corners.</summary>
        Horde,
    }

    /// <summary>
    /// Paints the table into <paramref name="size"/> at <paramref name="topLeft"/>, letterboxed so the
    /// table keeps its shape whatever the box is.
    /// </summary>
    public static void Draw(ImDrawListPtr dl, Vector2 topLeft, Vector2 size,
        float tableW, float tableH, TerrainLayoutFile? layout, ETableBackground background,
        EFallbackZones fallbackZones = EFallbackZones.Standard, bool showObjectives = true,
        IReadOnlyList<IZone>? deploymentZones = null)
    {
        if (size.X <= 2f || size.Y <= 2f) return;

        float scale = MathF.Max(0.01f, MathF.Min(size.X / tableW, size.Y / tableH));
        Vector2 o = topLeft + new Vector2((size.X - tableW * scale) / 2f, (size.Y - tableH * scale) / 2f);
        Vector2 P(float x, float z) => o + new Vector2(x * scale, (tableH - z) * scale);

        TableBackgroundStyle style = TableBackgrounds.For(background);
        dl.AddRectFilled(P(0, tableH), P(tableW, 0), U32(style.Surface));

        // The 6" grid is the thing that makes a thumbnail readable as a TABLE rather than a blob, but at
        // tile size the lines merge into a haze - so they are dropped once a square would be under 4px.
        if (6f * scale >= 4f)
        {
            for (float x = 6; x < tableW; x += 6) dl.AddLine(P(x, 0), P(x, tableH), U32(style.GridMinor));
            for (float z = 6; z < tableH; z += 6) dl.AddLine(P(0, z), P(tableW, z), U32(style.GridMinor));
        }

        dl.PushClipRect(P(0, tableH), P(tableW, 0), true);

        uint zoneA = U32(80, 160, 255, 70), zoneB = U32(255, 110, 90, 70);
        if (deploymentZones is { Count: > 0 } && fallbackZones != EFallbackZones.Horde)
        {
            // The lobby's Deployment pick, drawn in its real shape (book diagrams for this game).
            for (int k = 0; k < deploymentZones.Count; k++)
                DrawShape(dl, deploymentZones[k], k % 2 == 0 ? zoneA : zoneB, P, scale);
        }
        else if (fallbackZones == EFallbackZones.Horde)
        {
            dl.AddCircleFilled(P(tableW / 2, tableH / 2), 9f * scale, zoneA, 40);
            foreach ((float cx, float cz) in new[]
                     { (0f, 0f), (tableW, 0f), (0f, tableH), (tableW, tableH) })
            {
                dl.AddCircleFilled(P(cx, cz), 9f * scale, zoneB, 40);
            }
        }
        else if (fallbackZones == EFallbackZones.Standard)
        {
            float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            dl.AddRectFilled(P(0, d), P(tableW, 0), zoneA);
            dl.AddRectFilled(P(0, tableH), P(tableW, tableH - d), zoneB);
        }

        if (layout != null)
        {
            foreach (TerrainPieceEntry piece in layout.Pieces)
            {
                if (piece.Shape != null)
                    DrawShape(dl, piece.Shape, TerrainColor(piece.TerrainType), P, scale);
            }
        }
        dl.PopClipRect();

        if (showObjectives && layout?.Objectives is { Count: > 0 } objectives)
        {
            for (int k = 0; k < objectives.Count; k++)
                DrawObjective(dl, P(objectives[k].X, objectives[k].Z), k + 1, scale);
        }

        dl.AddRect(P(0, tableH), P(tableW, 0), U32(style.Border), 0f, ImDrawFlags.None,
            MathF.Max(1f, 3f * MathF.Min(1f, scale / 6f)));
    }

    /// <summary>Horde's five fixed markers: the centre and the centre of each quarter.</summary>
    public static void DrawHordeObjectives(ImDrawListPtr dl, Vector2 topLeft, Vector2 size,
        float tableW, float tableH)
    {
        float scale = MathF.Max(0.01f, MathF.Min(size.X / tableW, size.Y / tableH));
        Vector2 o = topLeft + new Vector2((size.X - tableW * scale) / 2f, (size.Y - tableH * scale) / 2f);
        Vector2 P(float x, float z) => o + new Vector2(x * scale, (tableH - z) * scale);

        var spots = new[]
        {
            (tableW / 2, tableH / 2), (tableW / 4, tableH / 4), (tableW * 3 / 4, tableH / 4),
            (tableW / 4, tableH * 3 / 4), (tableW * 3 / 4, tableH * 3 / 4),
        };
        for (int k = 0; k < spots.Length; k++) DrawObjective(dl, P(spots[k].Item1, spots[k].Item2), k + 1, scale);
    }

    private static void DrawObjective(ImDrawListPtr dl, Vector2 centre, int number, float scale)
    {
        float r = MathF.Max(3f, 1.2f * scale);
        dl.AddCircleFilled(centre, r, U32(235, 235, 235), 20);
        dl.AddCircle(centre, r, U32(20, 20, 20), 20, MathF.Max(1f, 2f * MathF.Min(1f, scale / 6f)));
        // The number only fits once the marker is bigger than the glyph; below that it reads as a smudge.
        if (r < 7f) return;
        string n = number.ToString();
        Vector2 ts = ImGui.CalcTextSize(n);
        dl.AddText(centre - ts / 2f, U32(20, 20, 20), n);
    }

    public static uint TerrainColor(ETerrainType type)
    {
        if (type.HasFlag(ETerrainType.Dangerous)) return U32(150, 60, 40, 230);
        if (type.HasFlag(ETerrainType.Blocking) || type.HasFlag(ETerrainType.Impassible)) return U32(75, 75, 82, 240);
        if (type.HasFlag(ETerrainType.Cover) && type.HasFlag(ETerrainType.Difficult)) return U32(25, 70, 30, 230);
        if (type.HasFlag(ETerrainType.Cover)) return U32(120, 95, 60, 230);
        if (type.HasFlag(ETerrainType.Difficult)) return U32(95, 85, 55, 220);
        return U32(110, 110, 110, 200);
    }

    /// <summary>
    /// The same shape walk as the lobby's preview had - rectangles, circles, composites and rotated
    /// rectangles as quads. Kept as a copy of that logic rather than a new one, because a thumbnail that
    /// draws a piece differently from the big preview is worse than no thumbnail.
    /// </summary>
    public static void DrawShape(ImDrawListPtr dl, IZone shape, uint color,
        Func<float, float, Vector2> P, float scale)
    {
        switch (shape)
        {
            // Before RectangularZone: both derive from it, and that case would draw their bounding box.
            case MultiRectZone multi:
                foreach (RectangularZone part in multi.Parts) DrawShape(dl, part, color, P, scale);
                break;
            case PolygonZone poly:
                foreach (IReadOnlyList<Float2> part in poly.Vertices)
                    for (int i = 1; i + 1 < part.Count; i++)
                        dl.AddTriangleFilled(P(part[0].X, part[0].Y), P(part[i].X, part[i].Y),
                            P(part[i + 1].X, part[i + 1].Y), color);
                break;
            case RectangularZone r:
                dl.AddRectFilled(P(r.Left, r.Top), P(r.Right, r.Bottom), color);
                break;
            case CircularZone c:
                dl.AddCircleFilled(P(c.Center.X, c.Center.Y), MathF.Max(1f, c.Radius * scale), color, 24);
                break;
            case CompositeZone composite:
                foreach (IZone part in composite.Parts) DrawShape(dl, part, color, P, scale);
                break;
            case RotatedZoneWrapper rot when rot.Inner is RectangularZone rr:
            {
                float a = rot.AngleDegrees * MathF.PI / 180f, cos = MathF.Cos(a), sin = MathF.Sin(a);
                Vector2 Corner(float x, float z)
                {
                    float dx = x - rot.Pivot.X, dz = z - rot.Pivot.Y;
                    return P(rot.Pivot.X + dx * cos - dz * sin, rot.Pivot.Y + dx * sin + dz * cos);
                }
                dl.AddQuadFilled(Corner(rr.Left, rr.Bottom), Corner(rr.Right, rr.Bottom),
                    Corner(rr.Right, rr.Top), Corner(rr.Left, rr.Top), color);
                break;
            }
            case RotatedZoneWrapper other:
                foreach (IZone primitive in ((IZone)other).Primitives()) DrawShape(dl, primitive, color, P, scale);
                break;
        }
    }
}
