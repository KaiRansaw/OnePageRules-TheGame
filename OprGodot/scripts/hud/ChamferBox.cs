using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The HUD's panel and button shape: a flat fill with cut (chamfered) corners, a thin border, an optional
/// soft glow and an optional accent bar along the top edge. Drawn as polygons, so it is sharp at any size
/// and any screen resolution - no textures to keep in step with the palette.
/// </summary>
[GlobalClass]
public partial class ChamferBox : StyleBox
{
    [Flags]
    public enum ECorners { None = 0, TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8, All = 15, Diagonal = TopLeft | BottomRight }

    [Export] public Color Fill { get; set; } = new(0.07f, 0.10f, 0.12f, 0.88f);
    [Export] public Color Border { get; set; } = new(0.28f, 0.38f, 0.42f, 0.9f);
    [Export] public float BorderWidth { get; set; } = 1f;
    [Export] public float Cut { get; set; } = 10f;
    [Export] public ECorners Corners { get; set; } = ECorners.Diagonal;
    /// <summary>A soft halo outside the border; transparent for none.</summary>
    [Export] public Color Glow { get; set; } = Colors.Transparent;
    [Export] public float GlowWidth { get; set; } = 6f;
    /// <summary>A short bright bar on the top edge, starting after the top-left cut; transparent for none.</summary>
    [Export] public Color Accent { get; set; } = Colors.Transparent;
    [Export] public float AccentLength { get; set; } = 48f;
    [Export] public float AccentThickness { get; set; } = 3f;
    /// <summary>A bar down the left edge (selected / active state); transparent for none.</summary>
    [Export] public Color LeftBar { get; set; } = Colors.Transparent;

    public ChamferBox()
    {
        ContentMarginLeft = ContentMarginRight = 12;
        ContentMarginTop = ContentMarginBottom = 8;
    }

    public ChamferBox With(Action<ChamferBox> change)
    {
        var copy = (ChamferBox)Duplicate();
        change(copy);
        return copy;
    }

    private Vector2[] Outline(Rect2 r, float inset)
    {
        float c = Mathf.Min(Cut, Mathf.Min(r.Size.X, r.Size.Y) * 0.45f);
        float x0 = r.Position.X + inset, y0 = r.Position.Y + inset, x1 = r.End.X - inset, y1 = r.End.Y - inset;
        var pts = new List<Vector2>(8);
        void Corner(ECorners which, Vector2 p, Vector2 a, Vector2 b)
        {
            if (Corners.HasFlag(which) && c > 0) { pts.Add(a); pts.Add(b); } else pts.Add(p);
        }
        Corner(ECorners.TopLeft, new(x0, y0), new(x0, y0 + c), new(x0 + c, y0));
        Corner(ECorners.TopRight, new(x1, y0), new(x1 - c, y0), new(x1, y0 + c));
        Corner(ECorners.BottomRight, new(x1, y1), new(x1, y1 - c), new(x1 - c, y1));
        Corner(ECorners.BottomLeft, new(x0, y1), new(x0 + c, y1), new(x0, y1 - c));
        return pts.ToArray();
    }

    public override void _Draw(Rid canvasItem, Rect2 rect)
    {
        if (Glow.A > 0)
            for (int i = 3; i >= 1; i--)
            {
                var ring = Outline(rect.Grow(GlowWidth * i / 3f), 0);
                var pts = ring.Append(ring[0]).ToArray();
                RenderingServer.CanvasItemAddPolyline(canvasItem, pts,
                    new[] { new Color(Glow, Glow.A * 0.22f * (4 - i) / 3f) }, GlowWidth / 2f, true);
            }

        var outline = Outline(rect, 0);
        if (Fill.A > 0)
            RenderingServer.CanvasItemAddPolygon(canvasItem, outline, new[] { Fill });

        if (LeftBar.A > 0)
        {
            float c = Corners.HasFlag(ECorners.TopLeft) ? Mathf.Min(Cut, rect.Size.Y * 0.45f) : 0;
            float cb = Corners.HasFlag(ECorners.BottomLeft) ? Mathf.Min(Cut, rect.Size.Y * 0.45f) : 0;
            RenderingServer.CanvasItemAddRect(canvasItem,
                new Rect2(rect.Position.X, rect.Position.Y + c, 3, rect.Size.Y - c - cb), LeftBar);
        }

        if (BorderWidth > 0 && Border.A > 0)
        {
            var line = Outline(rect, BorderWidth / 2f);
            RenderingServer.CanvasItemAddPolyline(canvasItem, line.Append(line[0]).ToArray(),
                new[] { Border }, BorderWidth, true);
        }

        if (Accent.A > 0)
        {
            float start = rect.Position.X + (Corners.HasFlag(ECorners.TopLeft) ? Mathf.Min(Cut, rect.Size.Y * 0.45f) : 0) + 4;
            float len = Mathf.Min(AccentLength, rect.Size.X - (start - rect.Position.X) - 8);
            RenderingServer.CanvasItemAddRect(canvasItem,
                new Rect2(start, rect.Position.Y - AccentThickness / 2f, len, AccentThickness), Accent);
        }
    }
}
