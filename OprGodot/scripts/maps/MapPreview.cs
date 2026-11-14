using FDG;
using FdgRaylib.Terrain;
using Godot;
using OprGodot.Battle;

namespace OprGodot.Maps;

/// <summary>A map from above: the felt, each piece's footprint in its rules colour, and the numbered objectives.</summary>
public partial class MapPreview : Control
{
    public MapDraft? Draft;

    public override void _Draw()
    {
        if (Draft is not { } d) return;
        float s = Mathf.Min(Size.X / d.TableW, Size.Y / d.TableH);
        var off = (Size - new Vector2(d.TableW, d.TableH) * s) / 2;
        Vector2 P(float x, float z) => off + new Vector2(x, z) * s;
        DrawRect(new Rect2(off, new Vector2(d.TableW, d.TableH) * s), new Color(0.115f, 0.14f, 0.125f));
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
