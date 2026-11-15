using FDG;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;
using OprGodot.Hud;

namespace OprGodot.Maps;

/// <summary>
/// A map from above: the felt, each piece's footprint in its rules colour, and the numbered objectives - drawn the way the
/// battle table and Map Maker show it from above (the far edge, z = table depth, at the top). Optionally the two
/// deployment zones under it (Kai's todo, 2026-11-14: "Select Map: draw the deployment zones on the big preview").
/// </summary>
public partial class MapPreview : Control
{
    public MapDraft? Draft;

    /// <summary>Deployment zones to draw under the terrain: the first side's in blue, the second's in red. Null = none.</summary>
    public IReadOnlyList<FDG.IZone>? Zones;

    /// <summary>A point on the preview back to table inches (null off the table) - for "what's under the pointer".</summary>
    public (float X, float Z)? TableAt(Vector2 at)
    {
        if (Draft is not { } d) return null;
        float s = Mathf.Min(Size.X / d.TableW, Size.Y / d.TableH);
        var off = (Size - new Vector2(d.TableW, d.TableH) * s) / 2;
        Vector2 q = (at - off) / s;
        if (q.X < 0 || q.Y < 0 || q.X > d.TableW || q.Y > d.TableH) return null;
        return (q.X, d.TableH - q.Y);
    }

    public override void _Draw()
    {
        if (Draft is not { } d) return;
        float s = Mathf.Min(Size.X / d.TableW, Size.Y / d.TableH);
        var off = (Size - new Vector2(d.TableW, d.TableH) * s) / 2;
        // Engine z runs up the table (0 = the near edge), so it is flipped for the screen.
        Vector2 P(float x, float z) => off + new Vector2(x, d.TableH - z) * s;
        DrawRect(new Rect2(off, new Vector2(d.TableW, d.TableH) * s), new Color(0.115f, 0.14f, 0.125f));
        if (Zones != null)
            for (int k = 0; k < Zones.Count; k++)
            {
                Color zc = (k % 2 == 0 ? HudKit.Friendly : HudKit.Enemy) with { A = 0.32f };
                foreach (var poly in FdgRaylib.Rendering.ZoneOutlines.Polygons(Zones[k]))
                {
                    if (poly.Length < 3) continue;
                    var pts = poly.Select(q => P(q.X, q.Z)).ToArray();
                    if (Geometry2D.TriangulatePolygon(pts).Length == 0) continue;
                    DrawColoredPolygon(pts, zc);
                    DrawPolyline(pts.Append(pts[0]).ToArray(), (k % 2 == 0 ? HudKit.Friendly : HudKit.Enemy) with { A = 0.8f }, 1.5f);
                }
            }
        for (float x = 12; x < d.TableW; x += 12) DrawLine(P(x, 0), P(x, d.TableH), new Color(1, 1, 1, 0.06f));
        for (float z = 12; z < d.TableH; z += 12) DrawLine(P(0, z), P(d.TableW, z), new Color(1, 1, 1, 0.06f));
        foreach (MapPiece p in d.Pieces)
        {
            Color c = BattleTable.TerrainColour(p.Rules).Lightened(0.15f);
            if (p.Round) DrawCircle(P(p.X, p.Z), p.W / 2 * s, c);
            else DrawColoredPolygon(p.Corners().Select(q => P(q.X, q.Z)).ToArray(), c);
        }
        for (int i = 0; i < d.Objectives.Count; i++)
        {
            Vector2 c = P(d.Objectives[i].X, d.Objectives[i].Z);
            DrawCircle(c, Mathf.Max(4, 1.2f * s), new Color(0.94f, 0.76f, 0.31f));
        }
        DrawRect(new Rect2(off, new Vector2(d.TableW, d.TableH) * s), new Color(0.3f, 0.25f, 0.2f), false, 2);
    }
}
