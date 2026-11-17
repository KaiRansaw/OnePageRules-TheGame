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
    /// <summary>The look this box was made in - picks the hologram skin's colourway (teal / gold).</summary>
    [Export] public Looks.ELook Look { get; set; } = Looks.Current.Id;

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
        if (Skin.Holo && DrawHolo(canvasItem, rect)) { DrawBars(canvasItem, rect); return; }
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

        DrawBorderAndAccent(canvasItem, rect);
    }

    private void DrawBorderAndAccent(Rid canvasItem, Rect2 rect)
    {
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

    // ================================================================== hologram skin (Skin.cs)

    private static bool Near(Color a, Color b) => Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < 0.12f;

    /// <summary>Which state a box stands for, read from its border (the HUD marks hover with EdgeHot and pressed /
    /// selected with the go colour).</summary>
    private string State()
    {
        Looks.Look l = Looks.Get(Look);
        if (Border.A < 0.45f) return "disabled";
        if (Near(Border, l.Go)) return "active";
        if (Near(Border, l.EdgeHot)) return "hover";
        return "normal";
    }

    private static readonly Dictionary<Color, StyleBoxFlat> Fills = new();

    /// <summary>A rounded fill matching the pack frames' corners (cached per colour).</summary>
    private static StyleBoxFlat RoundFill(Color c, int radius)
    {
        if (Fills.TryGetValue(c, out var f) && f.CornerRadiusTopLeft == radius) return f;
        f = new StyleBoxFlat { BgColor = c, AntiAliasing = true };
        f.SetCornerRadiusAll(radius);
        return Fills[c] = f;
    }

    /// <summary>
    /// The pack's art for this box, laid so the frame's bright line sits exactly on the box's edge (the glow spills
    /// outside, as the drawn glow did) - so content keeps the same padding it had with the cut-corner panels. Short
    /// boxes (buttons, chips, fields) use the pack's button frames; anything taller gets this box's own fill with
    /// rounded corners under the pack's glowing panel frame. False = nothing to draw with.
    /// </summary>
    private bool DrawHolo(Rid ci, Rect2 rect)
    {
        string state = State();
        float w = rect.Size.X, h = rect.Size.Y;
        if (w < 4 || h < 4) return false;
        bool filled = Fill.A > 0.12f && Fill.S > 0.35f;   // a lime/gold-filled box: the selected / commit look
        Color mod = state == "disabled" ? new Color(1, 1, 1, 0.55f) : Colors.White;
        if (h <= 60)
        {
            string kind = filled || state == "active" ? "fill" : "btn";
            string st = state == "disabled" ? (kind == "fill" ? "normal" : "disabled") : state;
            Texture2D? tex = Skin.Tex($"{kind}_{st}", Look) ?? Skin.Tex($"{kind}_normal", Look);
            if (tex == null) return false;
            // Button art: line ~2 px in; corners ~10 px. Fill buttons have a soft 0-7 px glow edge.
            float grow = kind == "fill" ? 3 : 1;
            Rect2 r = rect.Grow(grow);
            float mm = Mathf.Min(kind == "fill" ? 14 : 12, Mathf.Min(r.Size.X, r.Size.Y) / 2f - 1);
            RenderingServer.CanvasItemAddNinePatch(ci, r, new Rect2(Vector2.Zero, tex.GetSize()), tex.GetRid(),
                new Vector2(mm, mm), new Vector2(mm, mm), RenderingServer.NinePatchAxisMode.Stretch, RenderingServer.NinePatchAxisMode.Stretch, true, mod);
            return true;
        }
        Texture2D? frame = Skin.Tex("frame", Look);
        if (frame == null) return false;
        // The fill, rounded like the frame (radius 14), just inside the line.
        Color fill = filled ? Fill : Fill;
        if (state == "hover") fill = fill.Lightened(0.05f);
        if (fill.A > 0) RoundFill(fill, 14).Draw(ci, rect.Grow(-1));
        // The frame's line is 19 px in from its edge; grow by that so the line lands on our edge.
        const float lineIn = 19, corner = 38;
        Rect2 fr = rect.Grow(lineIn - 1);
        float m = Mathf.Min(corner, Mathf.Min(fr.Size.X, fr.Size.Y) / 2f - 1);
        Color fm = state is "active" ? mod : state == "hover" ? mod : mod with { A = mod.A * 0.85f };
        RenderingServer.CanvasItemAddNinePatch(ci, fr, new Rect2(Vector2.Zero, frame.GetSize()), frame.GetRid(),
            new Vector2(m, m), new Vector2(m, m), RenderingServer.NinePatchAxisMode.Stretch, RenderingServer.NinePatchAxisMode.Stretch, false, fm);
        if (state == "active")   // selected: a second pass makes the line brighter
            RenderingServer.CanvasItemAddNinePatch(ci, fr, new Rect2(Vector2.Zero, frame.GetSize()), frame.GetRid(),
                new Vector2(m, m), new Vector2(m, m), RenderingServer.NinePatchAxisMode.Stretch, RenderingServer.NinePatchAxisMode.Stretch, false, mod);
        return true;
    }

    /// <summary>The accent and left bars still mark headers and selected rows over the pack's frames.</summary>
    private void DrawBars(Rid ci, Rect2 rect)
    {
        if (LeftBar.A > 0) RenderingServer.CanvasItemAddRect(ci, new Rect2(rect.Position.X + 2, rect.Position.Y + 10, 3, Mathf.Max(0, rect.Size.Y - 20)), LeftBar);
        if (Accent.A > 0)
            RenderingServer.CanvasItemAddRect(ci, new Rect2(rect.Position.X + 16, rect.Position.Y - AccentThickness / 2f, Mathf.Min(AccentLength, rect.Size.X - 32), AccentThickness), Accent);
    }
}
