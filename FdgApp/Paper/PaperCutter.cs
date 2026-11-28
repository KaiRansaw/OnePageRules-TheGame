namespace FdgRaylib.Paper;

/// <summary>
/// Finds the figures on a page of paper miniatures and cuts each one out (Kai, 2026-11-27: OPR's free paper minis,
/// PDF sheets, not named - "I need to manually match them to their units").
///
/// <para><b>How a sheet looks.</b> Coloured figures on white or light paper. Most are a fold pair: the figure's front
/// and its back printed as one strip, the back upside down, with a fold line between and often a strip of base at
/// each outer end. When folded the two halves stand back to back.</para>
///
/// <para><b>What the cutter does.</b> Paper is anything close to the page's own colour (read off the page edges) that
/// joins up with the page edge; everything else is ink. Ink pixels close to each other (<see cref="Options.JoinMm"/>)
/// make one blob; blobs too small to be a figure (page titles, captions, crop marks) are dropped. Each blob is then
/// tried as a fold pair: the top half turned over is compared with the bottom half (and left with right, for sheets
/// that fold sideways); a close match is a pair. Of a pair, the copy printed upright is the front - "upright" being
/// the end with the straight edge (the feet / base strip) at the bottom. The back copy's picture is turned the way it
/// shows when folded, for the standee's rear.</para>
///
/// <para><b>See-through.</b> Paper outside the figure becomes transparent; white inside the figure (highlights, eyes)
/// stays, unless it is a big enclosed gap (between legs over a base strip, say).</para>
/// </summary>
public static class PaperCutter
{
    public sealed record Options
    {
        /// <summary>The page's resolution - every size below is in millimetres and turned into pixels with it.</summary>
        public int Dpi { get; init; } = PdfPages.DefaultDpi;
        /// <summary>Ink this close together is one figure (a gun barrel apart from a hand, a dashed fold line).</summary>
        public double JoinMm { get; init; } = 0.8;
        /// <summary>Anything shorter than this is text or a mark, not a figure.</summary>
        public double MinHeightMm { get; init; } = 10;
        public double MinWidthMm { get; init; } = 4;
        /// <summary>How alike the two halves must be (overlap of their outlines, 0-1) to count as a fold pair.</summary>
        public double PairMatch { get; init; } = 0.68;
        /// <summary>How far a paper-coloured pixel may be from the page colour (0-255, per colour channel).</summary>
        public int PaperTolerance { get; init; } = 40;

        public int Px(double mm) => Math.Max(1, (int)Math.Round(mm / 25.4 * Dpi));
    }

    public static readonly Options Default = new();

    // ================================================================== paper and ink

    /// <summary>The paper's colour: the median of a band round the page edge (where nothing is printed).</summary>
    public static (byte R, byte G, byte B) PaperColour(RgbaImage page)
    {
        int band = Math.Max(2, Math.Min(page.Width, page.Height) / 60);
        var rs = new List<byte>(); var gs = new List<byte>(); var bs = new List<byte>();
        for (int y = 0; y < page.Height; y += 2)
            for (int x = 0; x < page.Width; x += 2)
            {
                if (x >= band && y >= band && x < page.Width - band && y < page.Height - band) continue;
                var (r, g, b, _) = page.Get(x, y);
                rs.Add(r); gs.Add(g); bs.Add(b);
            }
        if (rs.Count == 0) return (255, 255, 255);
        static byte Med(List<byte> l) { l.Sort(); return l[l.Count / 2]; }
        var c = (Med(rs), Med(gs), Med(bs));
        // A page whose edge is all ink (full-bleed art) - fall back to white paper.
        return c.Item1 + c.Item2 + c.Item3 < 3 * 150 ? ((byte)255, (byte)255, (byte)255) : c;
    }

    /// <summary>Whether each pixel is ink (not paper-coloured).</summary>
    public static bool[] Ink(RgbaImage img, (byte R, byte G, byte B) paper, int tolerance)
    {
        var ink = new bool[img.Width * img.Height];
        byte[] p = img.Pixels;
        for (int i = 0, j = 0; j < ink.Length; i += 4, j++)
        {
            if (p[i + 3] < 128) continue; // already see-through
            ink[j] = Math.Abs(p[i] - paper.R) > tolerance || Math.Abs(p[i + 1] - paper.G) > tolerance || Math.Abs(p[i + 2] - paper.B) > tolerance;
        }
        return ink;
    }

    /// <summary>The mask grown by <paramref name="r"/> pixels each way (a square), so near-touching ink joins up.</summary>
    public static bool[] Grow(bool[] m, int w, int h, int r)
    {
        if (r <= 0) return (bool[])m.Clone();
        var rows = new bool[m.Length];
        for (int y = 0; y < h; y++)
        {
            int last = -1_000_000;
            for (int x = 0; x < w; x++) if (m[y * w + x]) last = x; else if (x - last <= r) rows[y * w + x] = true;
            for (int x = 0; x < w; x++) if (m[y * w + x]) rows[y * w + x] = true;
            last = 1_000_000_000;
            for (int x = w - 1; x >= 0; x--) if (m[y * w + x]) last = x; else if (last - x <= r) rows[y * w + x] = true;
        }
        var o = new bool[m.Length];
        for (int x = 0; x < w; x++)
        {
            int last = -1_000_000;
            for (int y = 0; y < h; y++) if (rows[y * w + x]) { last = y; o[y * w + x] = true; } else if (y - last <= r) o[y * w + x] = true;
            last = 1_000_000_000;
            for (int y = h - 1; y >= 0; y--) if (rows[y * w + x]) last = y; else if (last - y <= r) o[y * w + x] = true;
        }
        return o;
    }

    /// <summary>Labels the connected parts of a mask (8-connected); 0 = empty, 1.. = part. Returns the part count.</summary>
    public static int Label(bool[] m, int w, int h, int[] labels)
    {
        int n = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < m.Length; s++)
        {
            if (!m[s] || labels[s] != 0) continue;
            labels[s] = ++n;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        int j = yy * w + xx;
                        if (m[j] && labels[j] == 0) { labels[j] = n; stack.Push(j); }
                    }
            }
        }
        return n;
    }

    // ================================================================== finding figures on a page

    /// <summary>Every figure on the page, top to bottom, left to right (rows of figures read like text).</summary>
    public static List<PaperFigure> FindFigures(RgbaImage page, Options? o = null)
    {
        o ??= Default;
        var paper = PaperColour(page);
        int w = page.Width, h = page.Height;
        bool[] ink = Ink(page, paper, o.PaperTolerance);
        bool[] grown = Grow(ink, w, h, o.Px(o.JoinMm));
        var labels = new int[w * h];
        int n = Label(grown, w, h, labels);
        var box = new (int X0, int Y0, int X1, int Y1, int Count)[n + 1];
        for (int i = 1; i <= n; i++) box[i] = (int.MaxValue, int.MaxValue, -1, -1, 0);
        for (int i = 0; i < ink.Length; i++)
        {
            if (!ink[i]) continue;
            int l = labels[i];
            var b = box[l];
            int x = i % w, y = i / w;
            box[l] = (Math.Min(b.X0, x), Math.Min(b.Y0, y), Math.Max(b.X1, x), Math.Max(b.Y1, y), b.Count + 1);
        }
        var found = new List<PaperFigure>();
        int minH = o.Px(o.MinHeightMm), minW = o.Px(o.MinWidthMm);
        for (int l = 1; l <= n; l++)
        {
            var b = box[l];
            if (b.Count == 0) continue;
            var rect = PixelRect.FromCorners(b.X0, b.Y0, b.X1 + 1, b.Y1 + 1);
            if (rect.H < minH || rect.W < minW) continue;
            // A frame round the whole page (thin lines enclosing everything) is not a figure.
            if (rect.W > w * 0.85 && rect.H > h * 0.85 && b.Count < rect.Area * 0.12) continue;
            int label = l;
            var fig = Detect(page, rect, (x, y) => labels[y * w + x] == label, o, paper);
            if (fig != null) found.Add(fig);
        }
        // Reading order: rows (by top edge, grouped within half a figure's height), then left to right.
        found.Sort((a, b) => a.Box.Y.CompareTo(b.Box.Y));
        var ordered = new List<PaperFigure>();
        while (found.Count > 0)
        {
            var first = found[0];
            var row = found.Where(f => f.Box.Y < first.Box.Y + first.Box.H / 2).OrderBy(f => f.Box.X).ToList();
            ordered.AddRange(row);
            found.RemoveAll(row.Contains);
        }
        return ordered;
    }

    /// <summary>
    /// The figure inside <paramref name="area"/> (a found blob, or a box Kai drew): trimmed to its ink and tried as a
    /// fold pair. <paramref name="mine"/> says which ink belongs to it (others' ink poking into the box is left out);
    /// null = all ink in the box. Null when the box holds no ink.
    /// </summary>
    public static PaperFigure? Detect(RgbaImage page, PixelRect area, Func<int, int, bool>? mine = null, Options? o = null,
        (byte R, byte G, byte B)? paperColour = null)
    {
        o ??= Default;
        area = area.ClipTo(page.Width, page.Height);
        if (area.Area == 0) return null;
        var paper = paperColour ?? PaperColour(page);
        int w = area.W, h = area.H;
        var m = new bool[w * h];
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int px = area.X + x, py = area.Y + y;
                var (r, g, b, a) = page.Get(px, py);
                bool ink = a >= 128 && (Math.Abs(r - paper.R) > o.PaperTolerance || Math.Abs(g - paper.G) > o.PaperTolerance || Math.Abs(b - paper.B) > o.PaperTolerance);
                if (!ink || (mine != null && !mine(px, py))) continue;
                m[y * w + x] = true;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        if (x1 < 0) return null;
        var tight = PixelRect.FromCorners(area.X + x0, area.Y + y0, area.X + x1 + 1, area.Y + y1 + 1);
        // the trimmed mask
        int tw = tight.W, th = tight.H;
        var t = new bool[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++) t[y * tw + x] = m[(y + y0) * w + x + x0];

        var fig = new PaperFigure { Box = tight, Front = new PaperPart { Rect = tight } };
        var (vSplit, vScore) = BestSplit(t, tw, th, vertical: true);
        var (hSplit, hScore) = BestSplit(t, tw, th, vertical: false);
        // Side by side only with a clear gap between the copies: a figure drawn facing the front is its own mirror image.
        bool sideBySide = hScore >= o.PairMatch && HasGap(t, tw, th, hSplit);
        if (vScore >= o.PairMatch && (!sideBySide || vScore >= hScore))
        {
            var top = Trim(t, tw, th, new PixelRect(0, 0, tw, vSplit), o);
            var bottom = Trim(t, tw, th, new PixelRect(0, vSplit, tw, th - vSplit), o);
            if (top.Area > 0 && bottom.Area > 0)
            {
                PixelRect Page(PixelRect r) => new(tight.X + r.X, tight.Y + r.Y, r.W, r.H);
                // Which copy stands upright: the one whose straight edge (feet, base strip) is at its bottom.
                bool feetOutside = Straightness(t, tw, top, fromTop: true) + Straightness(t, tw, bottom, fromTop: false)
                                   >= Straightness(t, tw, top, fromTop: false) + Straightness(t, tw, bottom, fromTop: true);
                fig.Fold = PaperFold.TopBottom;
                if (feetOutside)
                {
                    fig.Front = new PaperPart { Rect = Page(bottom) };
                    fig.Back = new PaperPart { Rect = Page(top), Turned = true };
                }
                else
                {
                    fig.Front = new PaperPart { Rect = Page(top) };
                    fig.Back = new PaperPart { Rect = Page(bottom), Turned = true };
                }
            }
        }
        else if (sideBySide)
        {
            var left = Trim(t, tw, th, new PixelRect(0, 0, hSplit, th), o, vertical: false);
            var right = Trim(t, tw, th, new PixelRect(hSplit, 0, tw - hSplit, th), o, vertical: false);
            if (left.Area > 0 && right.Area > 0)
            {
                fig.Fold = PaperFold.LeftRight;
                fig.Front = new PaperPart { Rect = new PixelRect(tight.X + left.X, tight.Y + left.Y, left.W, left.H) };
                fig.Back = new PaperPart { Rect = new PixelRect(tight.X + right.X, tight.Y + right.Y, right.W, right.H) };
            }
        }
        return fig;
    }

    /// <summary>
    /// The split row (or column) where one side turned over best covers the other, and how well (the overlap of the
    /// two outlines, 0-1). Looked for in the middle 30-70 %. Worked on a half-size copy when the figure is big.
    /// </summary>
    public static (int At, double Score) BestSplit(bool[] m, int w, int h, bool vertical)
    {
        int len = vertical ? h : w, across = vertical ? w : h;
        if (len < 8) return (len / 2, 0);
        int step = Math.Max(1, len / 300), lo = (int)(len * 0.3), hi = (int)(len * 0.7);
        int[]? bestRow = null;
        double best = 0;
        int bestAt = len / 2;
        bool At(int along, int a) => vertical ? m[along * w + a] : m[a * w + along];
        for (int s = lo; s <= hi; s += step)
        {
            int reach = Math.Min(s, len - s);
            long both = 0, either = 0;
            for (int k = 0; k < reach; k += step)
            {
                int ra = s - 1 - k, rb = s + k;
                for (int a = 0; a < across; a += step)
                {
                    bool p = At(ra, a), q = At(rb, a);
                    if (p && q) both++;
                    if (p || q) either++;
                }
            }
            // Ink beyond the shorter half has nothing to match: it counts against.
            for (int k = reach; k < Math.Max(s, len - s); k += step)
            {
                int r = s >= len - s ? s - 1 - k : s + k;
                if (r < 0 || r >= len) continue;
                for (int a = 0; a < across; a += step) if (At(r, a)) either++;
            }
            double score = either == 0 ? 0 : both / (double)either;
            if (score > best) { best = score; bestAt = s; }
        }
        _ = bestRow;
        return (bestAt, best);
    }

    /// <summary>Whether a column near <paramref name="at"/> (within 3 % of the width) is all but empty - two copies side
    /// by side, not one figure that happens to be the same on both sides.</summary>
    private static bool HasGap(bool[] m, int w, int h, int at)
    {
        int reach = Math.Max(1, w * 3 / 100);
        for (int x = Math.Max(0, at - reach); x <= Math.Min(w - 1, at + reach); x++)
        {
            int n = 0;
            for (int y = 0; y < h; y++) if (m[y * w + x]) n++;
            if (n <= h * 0.12) return true;
        }
        return false;
    }

    /// <summary>
    /// One half of a pair trimmed to its ink - first dropping a thin line lying along the fold (the printed fold
    /// line), which belongs to neither picture.
    /// </summary>
    private static PixelRect Trim(bool[] m, int w, int h, PixelRect half, Options o, bool vertical = true)
    {
        int thin = Math.Max(2, o.Px(0.6));
        bool Full(int line)
        {
            int count = 0, n = vertical ? half.W : half.H;
            for (int a = 0; a < n; a++) if (vertical ? m[line * w + half.X + a] : m[(half.Y + a) * w + line]) count++;
            return count >= n * 0.8;
        }
        bool Empty(int line)
        {
            int n = vertical ? half.W : half.H;
            for (int a = 0; a < n; a++) if (vertical ? m[line * w + half.X + a] : m[(half.Y + a) * w + line]) return false;
            return true;
        }
        int start = vertical ? half.Y : half.X, end = vertical ? half.Bottom : half.Right; // [start, end)
        bool foldAtStart = vertical ? half.Y > 0 : half.X > 0;
        // Walk in from the fold side: skip a thin full-width run (the fold line) followed by a gap or the figure.
        int from = foldAtStart ? start : end - 1, dir = foldAtStart ? 1 : -1, i = from;
        while (InRange(i) && Empty(i)) i += dir;
        int run = 0, j = i;
        while (InRange(j) && Full(j)) { run++; j += dir; }
        bool InRange(int v) => v >= start && v < end;
        var mask = new HashSet<int>();
        if (run > 0 && run <= thin) for (int k = 0, v = i; k < run; k++, v += dir) mask.Add(v);

        int a0 = int.MaxValue, a1 = -1, b0 = int.MaxValue, b1 = -1;
        for (int y = half.Y; y < half.Bottom; y++)
            for (int x = half.X; x < half.Right; x++)
            {
                if (!m[y * w + x] || mask.Contains(vertical ? y : x)) continue;
                a0 = Math.Min(a0, x); a1 = Math.Max(a1, x); b0 = Math.Min(b0, y); b1 = Math.Max(b1, y);
            }
        return a1 < 0 ? new PixelRect(0, 0, 0, 0) : PixelRect.FromCorners(a0, b0, a1 + 1, b1 + 1);
    }

    /// <summary>How straight the top (or bottom) edge of the ink in <paramref name="r"/> is: the share of its columns whose
    /// first ink from that side lies within a few pixels of the outermost. A base strip or flat feet read near 1.</summary>
    private static double Straightness(bool[] m, int w, PixelRect r, bool fromTop)
    {
        if (r.Area == 0) return 0;
        var firsts = new List<int>();
        for (int x = r.X; x < r.Right; x++)
        {
            for (int k = 0; k < r.H; k++)
            {
                int y = fromTop ? r.Y + k : r.Bottom - 1 - k;
                if (m[y * w + x]) { firsts.Add(k); break; }
            }
        }
        if (firsts.Count == 0) return 0;
        int edge = firsts.Min(), slack = Math.Max(2, r.H / 40);
        return firsts.Count(f => f - edge <= slack) / (double)(r.W);
    }

    // ================================================================== the pictures

    /// <summary>
    /// A part's picture: the page under its rectangle, paper made see-through, turned upright when the part is printed
    /// upside down, scaled down to fit <paramref name="maxHeight"/> (and as wide again).
    /// </summary>
    public static RgbaImage CutOut(RgbaImage page, PaperPart part, int maxHeight = 512, Options? o = null, (byte R, byte G, byte B)? paperColour = null)
    {
        o ??= Default;
        var paper = paperColour ?? PaperColour(page);
        // a little margin so the soft edge has room
        var r = new PixelRect(part.Rect.X - 2, part.Rect.Y - 2, part.Rect.W + 4, part.Rect.H + 4).ClipTo(page.Width, page.Height);
        RgbaImage img = page.Crop(r);
        MakeSeeThrough(img, paper, o);
        if (part.Turned) img = img.FlipVertical().FlipHorizontal();
        if (part.Mirrored) img = img.FlipHorizontal();
        return Trimmed(img).ScaleDownToFit(maxHeight, maxHeight);
    }

    /// <summary>
    /// Paper round the figure becomes transparent. Kept: the biggest ink blob and any blob at least a sixth its size
    /// (so a neighbour's elbow poking into the box goes), white highlights inside the figure. Gone: paper joined to
    /// the box edge, and big or long enclosed paper gaps (1 % of the box, or a quarter of its height or width).
    /// </summary>
    public static void MakeSeeThrough(RgbaImage img, (byte R, byte G, byte B) paper, Options? o = null)
    {
        o ??= Default;
        int w = img.Width, h = img.Height;
        bool[] ink = Ink(img, paper, o.PaperTolerance);
        // the figure's own ink
        bool[] grown = Grow(ink, w, h, o.Px(o.JoinMm));
        var labels = new int[w * h];
        int n = Label(grown, w, h, labels);
        var size = new int[n + 1];
        for (int i = 0; i < ink.Length; i++) if (ink[i]) size[labels[i]]++;
        int biggest = size.Length > 1 ? size.Skip(1).Max() : 0;
        var keep = new bool[n + 1];
        for (int l = 1; l <= n; l++) keep[l] = size[l] > 0 && size[l] * 6 >= biggest;
        var solid = new bool[w * h];
        for (int i = 0; i < ink.Length; i++) solid[i] = ink[i] && keep[labels[i]];

        // paper regions = the not-solid parts; each is outside (touches the edge), a big gap, or a highlight
        var paperLabels = new int[w * h];
        var notSolid = solid.Select(s => !s).ToArray();
        int pn = Label4(notSolid, w, h, paperLabels);
        var touches = new bool[pn + 1];
        var count = new int[pn + 1];
        var minX = Enumerable.Repeat(int.MaxValue, pn + 1).ToArray(); var maxX = new int[pn + 1];
        var minY = Enumerable.Repeat(int.MaxValue, pn + 1).ToArray(); var maxY = new int[pn + 1];
        var nearPaper = new long[pn + 1];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, l = paperLabels[i];
                if (l == 0) continue;
                count[l]++;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) touches[l] = true;
                minX[l] = Math.Min(minX[l], x); maxX[l] = Math.Max(maxX[l], x);
                minY[l] = Math.Min(minY[l], y); maxY[l] = Math.Max(maxY[l], y);
                if (!ink[i]) nearPaper[l]++;
            }
        var clear = new bool[pn + 1];
        for (int l = 1; l <= pn; l++)
        {
            if (touches[l]) { clear[l] = true; continue; }
            // A gap, not paint: big (1 % of the box, and 4 % of its width or height), or long (a quarter of the figure's
            // height or width - the slit between two legs over a base strip).
            int bw = maxX[l] - minX[l] + 1, bh = maxY[l] - minY[l] + 1;
            bool big = count[l] >= w * h * 0.01 && (bw >= w * 0.04 || bh >= h * 0.04) || bh >= h * 0.25 || bw >= w * 0.25;
            bool paperish = nearPaper[l] >= count[l] * 0.9;
            clear[l] = big && paperish;
        }
        byte[] p = img.Pixels;
        for (int i = 0; i < solid.Length; i++)
        {
            int l = paperLabels[i];
            if (l != 0 && clear[l]) p[i * 4 + 3] = 0;
        }
        // Soft edge: an edge pixel that is half paper is half see-through.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (p[i * 4 + 3] == 0) continue;
                bool edge = false;
                for (int dy = -1; dy <= 1 && !edge; dy++)
                    for (int dx = -1; dx <= 1 && !edge; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        edge = xx >= 0 && yy >= 0 && xx < w && yy < h && p[(yy * w + xx) * 4 + 3] == 0;
                    }
                if (!edge) continue;
                int diff = Math.Max(Math.Abs(p[i * 4] - paper.R), Math.Max(Math.Abs(p[i * 4 + 1] - paper.G), Math.Abs(p[i * 4 + 2] - paper.B)));
                p[i * 4 + 3] = (byte)Math.Clamp(diff * 255 / 90, 90, 255);
            }
    }

    private static int Label4(bool[] m, int w, int h, int[] labels)
    {
        int n = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < m.Length; s++)
        {
            if (!m[s] || labels[s] != 0) continue;
            labels[s] = ++n;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                if (x > 0 && m[i - 1] && labels[i - 1] == 0) { labels[i - 1] = n; stack.Push(i - 1); }
                if (x < w - 1 && m[i + 1] && labels[i + 1] == 0) { labels[i + 1] = n; stack.Push(i + 1); }
                if (y > 0 && m[i - w] && labels[i - w] == 0) { labels[i - w] = n; stack.Push(i - w); }
                if (y < h - 1 && m[i + w] && labels[i + w] == 0) { labels[i + w] = n; stack.Push(i + w); }
            }
        }
        return n;
    }

    /// <summary>The picture cropped to its non-transparent pixels.</summary>
    public static RgbaImage Trimmed(RgbaImage img)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img.Pixels[img.Index(x, y) + 3] > 0) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        return x1 < 0 ? img : img.Crop(PixelRect.FromCorners(x0, y0, x1 + 1, y1 + 1));
    }
}

public enum PaperFold { None, TopBottom, LeftRight }

/// <summary>One printed copy of a figure on the page.</summary>
public sealed class PaperPart
{
    [Newtonsoft.Json.JsonProperty("rect")] public PixelRect Rect { get; set; }
    /// <summary>Printed upside down: turned half a turn to stand up (a top/bottom fold's back copy).</summary>
    [Newtonsoft.Json.JsonProperty("turned")] public bool Turned { get; set; }
    /// <summary>Flipped left-right on top (rarely needed; a fix tool).</summary>
    [Newtonsoft.Json.JsonProperty("mirrored")] public bool Mirrored { get; set; }

    public PaperPart Copy() => new() { Rect = Rect, Turned = Turned, Mirrored = Mirrored };
}

/// <summary>A figure cut from a sheet: its box on the page, its front, and (for a fold pair) its back.</summary>
public sealed class PaperFigure
{
    /// <summary>Its number on the sheet - the file name (&lt;n&gt;.png). Never reused once given.</summary>
    [Newtonsoft.Json.JsonProperty("n")] public int N { get; set; }
    /// <summary>The PDF page (0-based).</summary>
    [Newtonsoft.Json.JsonProperty("page")] public int Page { get; set; }
    [Newtonsoft.Json.JsonProperty("box")] public PixelRect Box { get; set; }
    [Newtonsoft.Json.JsonProperty("fold")] public PaperFold Fold { get; set; }
    [Newtonsoft.Json.JsonProperty("front")] public PaperPart Front { get; set; } = new();
    [Newtonsoft.Json.JsonProperty("back")] public PaperPart? Back { get; set; }
    /// <summary>The saved front picture's size (pixels) - for the standee's shape without loading it.</summary>
    [Newtonsoft.Json.JsonProperty("w")] public int PictureW { get; set; }
    [Newtonsoft.Json.JsonProperty("h")] public int PictureH { get; set; }
}
