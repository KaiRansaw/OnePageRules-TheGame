using System.Collections.Generic;
using Godot;

namespace OprGodot.Battle;

/// <summary>
/// Everything written on the table stays readable from the general's view (Kai's guiding principle; lane 4,
/// 2026-11-27): a unit's name, its status marks, the objectives' numbers and the markers' names grow when the camera
/// is far enough that they would shrink below a few pixels, and are their normal size up close. Each one is registered
/// with its own height in world inches and the smallest height on screen it may have.
/// </summary>
public partial class BattleTable
{
    private readonly List<(Node3D Node, float World, float MinPx)> _readable = new();

    /// <summary>Keeps <paramref name="node"/> at least <paramref name="minPx"/> pixels tall on screen
    /// (<paramref name="worldHeight"/> = its height in inches at scale 1). Never shrinks it below its own size.</summary>
    public void Readable(Node3D node, float worldHeight, float minPx) => _readable.Add((node, worldHeight, minPx));

    /// <summary>How much bigger than its own size a thing at <paramref name="at"/> must be drawn to stand
    /// <paramref name="minPx"/> pixels tall (1 = its own size is enough).</summary>
    public float ReadableScale(Vector3 at, float worldHeight, float minPx)
    {
        float h = GetViewport().GetVisibleRect().Size.Y;
        if (h <= 0 || worldHeight <= 0) return 1f;
        float perPixel = 2f * Camera.GlobalPosition.DistanceTo(at) * Mathf.Tan(Mathf.DegToRad(Camera.Fov) / 2f) / h;
        return Mathf.Clamp(minPx * perPixel / worldHeight, 1f, 6f);
    }

    private readonly Dictionary<FDG.UnitID, Vector3> _labelBase = new();
    private readonly Dictionary<FDG.UnitID, float> _labelLift = new();

    /// <summary>
    /// Names of units standing close together overlap once they are big enough to read from far away. Each frame the
    /// shown names are laid out on screen from the nearest to the camera back, and a name that would overlap one
    /// already placed is lifted (in world height) until it is clear - at most four lines up. Read by SyncModels on
    /// the next frame (the status marks follow their name).
    /// </summary>
    private void Declutter()
    {
        float h = GetViewport().GetVisibleRect().Size.Y;
        if (h <= 0) return;
        float tan = Mathf.Tan(Mathf.DegToRad(Camera.Fov) / 2f);
        var shown = new List<(FDG.UnitID Id, Label3D L, Vector3 At, float Depth)>();
        foreach (var (id, l) in _labels)
        {
            if (!l.Visible || !_labelBase.TryGetValue(id, out Vector3 at) || Camera.IsPositionBehind(at)) continue;
            shown.Add((id, l, at, Camera.GlobalPosition.DistanceTo(at)));
        }
        shown.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        var placed = new List<Rect2>();
        var lifts = new Dictionary<FDG.UnitID, float>();
        foreach (var (id, l, at, depth) in shown)
        {
            float pxPerInch = h / (2f * depth * tan);
            float k = l.Scale.X;
            float wide = l.Font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, l.FontSize).X * l.PixelSize * k * pxPerInch;
            float tall = l.FontSize * l.PixelSize * k * pxPerInch;
            Vector2 c = Camera.UnprojectPosition(at);
            float liftPx = 0;
            for (int tries = 0; tries < 4; tries++)
            {
                var r = new Rect2(c.X - wide / 2, c.Y - liftPx - tall / 2, wide, tall * 0.9f);
                if (!placed.Exists(p => p.Intersects(r))) { placed.Add(r); break; }
                liftPx += tall;
                if (tries == 3) placed.Add(r);
            }
            // A lift in world height shows foreshortened from above: measure what one inch up is on screen.
            float upPx = Mathf.Max(1f, c.Y - Camera.UnprojectPosition(at + Vector3.Up).Y);
            if (liftPx > 0) lifts[id] = liftPx / upPx;
        }
        _labelLift.Clear();
        foreach (var (id, lift) in lifts) _labelLift[id] = lift;
    }

    private void SyncReadable()
    {
        Declutter();
        for (int i = _readable.Count - 1; i >= 0; i--)
        {
            var (n, world, px) = _readable[i];
            if (!IsInstanceValid(n) || !n.IsInsideTree()) { _readable.RemoveAt(i); continue; }
            if (!n.Visible) continue;
            float s = ReadableScale(n.GlobalPosition, world, px);
            if (Mathf.Abs(n.Scale.X - s) > 0.02f * s) n.Scale = new Vector3(s, s, s);
        }
    }
}
