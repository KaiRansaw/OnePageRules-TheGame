using Godot;

namespace OprGodot;

/// <summary>
/// Looks for text that does not fit (lane 3, 2026-11-27: "fix text that's cut off, overflows, wraps badly, overlaps, or
/// doesn't fit its panel"). Walks every visible control on screen and reports, one line each:
/// <list type="bullet">
/// <item><b>cut</b> - a label or button whose text is wider than its box and is clipped mid-letter;</item>
/// <item><b>trimmed</b> / <b>trimmed-no-hover</b> - shortened with "...", with or without a hover that says it in full;</item>
/// <item><b>spills</b> - a label whose text is wider than its box and is drawn past it (nothing clips it);</item>
/// <item><b>hidden-lines</b> - a wrapping label or rich text box with lines that don't fit its height;</item>
/// <item><b>wraps-badly</b> - a wrapping label so narrow that a short text breaks onto many lines;</item>
/// <item><b>off-screen</b> - a control (with text) that runs past the edge of the window;</item>
/// <item><b>sticks-out</b> - a control (with text) that runs past its panel's edge;</item>
/// <item><b>overlap</b> - two pieces of text drawn over each other.</item>
/// </list>
/// Used by <see cref="ScriptedRun"/>'s "audit:&lt;tag&gt;" step; the report lines go to the console and to the file
/// given by --audit=&lt;file&gt; (appended, so one file collects a whole walk). A test tool - nothing in play calls it.
/// </summary>
public static class LayoutAudit
{
    public sealed record Finding(string Kind, string Path, string Text, string Detail, Control? Source = null);

    private const float Slack = 2f;

    public static List<Finding> Run(Node root, Rect2 viewport)
    {
        var found = new List<Finding>();
        var texts = new List<(Control Control, Rect2 Rect, string Text)>();
        Walk(root, viewport, found, texts, clip: null, inScroll: false);
        Func<Control?, bool> Hidden = Covered(root, viewport);
        found.RemoveAll(f => Hidden(f.Source));
        texts.RemoveAll(t => Hidden(t.Control));
        Overlaps(texts, found);
        return found;
    }

    /// <summary>A full-screen opaque backing (a picker opened over the lobby) hides everything drawn before it: the
    /// returned test says whether a control is hidden that way.</summary>
    private static Func<Control?, bool> Covered(Node root, Rect2 viewport)
    {
        var order = new Dictionary<Control, int>();
        Control? cover = null;
        int n = 0;
        void Number(Node node)
        {
            if (node is CanvasItem { Visible: false } || node is Window { Visible: false }) return;
            if (node is Control c)
            {
                order[c] = n++;
                if (IsCover(c, viewport)) cover = c;
            }
            foreach (Node ch in node.GetChildren()) Number(ch);
        }
        Number(root);
        return c => cover != null && c != null && c != cover && !IsAncestor(cover, c)
            && order.TryGetValue(c, out int i) && i < order[cover] && WindowOf(c) == WindowOf(cover) && LayerOf(c) == LayerOf(cover);
    }

    private static bool IsCover(Control c, Rect2 viewport)
    {
        Rect2 r = c.GetGlobalRect();
        if (r.Size.X * r.Size.Y < viewport.Size.X * viewport.Size.Y * 0.9f) return false;
        if (c is ColorRect cr) return cr.Color.A >= 0.9f;
        if (c is PanelContainer or Panel && c.GetThemeStylebox("panel") is StyleBoxFlat sb) return sb.BgColor.A >= 0.9f && sb.DrawCenter;
        return false;
    }

    /// <summary>Buttons a click-walk must not press: they quit, start a game, download, delete or overwrite.</summary>
    public static bool Risky(BaseButton b)
    {
        if (b.Name == "Back") return true;   // leaves the screen - checked by the walk itself
        string n = (b.Name + " " + (b as Button)?.Text).ToLowerInvariant();
        string[] words = { "quit", "closegame", "launch", "update", "download", "delete", "remove", "overwrite", "concede",
            "send my settings", "github", "clear download", "clear cache", "import", "rematch", "start", "begin", "play" };
        return words.Any(n.Contains);
    }

    /// <summary>Controls a player uses that say nothing on hover (on themselves or on a parent up to the row).</summary>
    public static List<Finding> NoHover(Node root, Rect2 viewport)
    {
        var found = new List<Finding>();
        void Walk(Node n)
        {
            if (n is CanvasItem { Visible: false } || n is Window { Visible: false } || n.HasMeta("no_hover_needed")) return;
            if (n is Control c && c.IsVisibleInTree() && c.Size.X > 0 && c.GetGlobalRect().Intersects(viewport)
                && c is BaseButton or Godot.Range or LineEdit && c is not ScrollBar && c.MouseFilter != Control.MouseFilterEnum.Ignore)
            {
                bool has = false;
                int up = 0;
                for (Node? p = c; p is Control k && up < 3; p = p.GetParent(), up++)
                    if (!string.IsNullOrWhiteSpace(k.TooltipText) || k.HasMeta("pad_tip")) { has = true; break; }
                if (!has)
                {
                    string text = (c as Button)?.Text ?? (c as LineEdit)?.PlaceholderText ?? "";
                    found.Add(new("no-hover", PathOf(c), Short(text), c.GetClass(), c));
                }
            }
            foreach (Node ch in n.GetChildren()) Walk(ch);
        }
        Walk(root);
        Func<Control?, bool> hidden = Covered(root, viewport);
        found.RemoveAll(f => hidden(f.Source));
        return found;
    }

    private static string Short(string text)
    {
        text = text.Replace('\n', ' ').Trim();
        return text.Length > 50 ? text[..50] + "..." : text;
    }

    private static string PathOf(Node n)
    {
        var parts = new List<string>();
        for (Node? p = n; p != null && parts.Count < 6; p = p.GetParent())
        {
            if (p is Window && p.GetParent() == null) break;
            parts.Add(p.Name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void Walk(Node node, Rect2 viewport, List<Finding> found, List<(Control, Rect2, string)> texts, Rect2? clip, bool inScroll)
    {
        // A hidden window (a closed popup) and everything in it is not on screen.
        if (node is Window w && !w.Visible) return;
        if (node is CanvasItem ci && !ci.Visible) return;
        if (node is CanvasLayer { Visible: false }) return;
        // Popups are their own window: their contents measure against the popup, not the screen under it.
        if (node is Window { Visible: true } win && node.GetParent() != null)
        {
            viewport = new Rect2(Vector2.Zero, win.Size);   // a window's controls measure from its own corner
            clip = null;
        }
        if (node is Control c && c.IsVisibleInTree() && c.Size.X > 0 && c.Size.Y > 0 && c.Modulate.A > 0.05f && c.SelfModulate.A > 0.05f)
        {
            Rect2 rect = c.GetGlobalRect();
            // Rows scrolled out of a list's view are not drawn - nothing to check until they are scrolled to.
            bool shown = rect.Intersects(viewport) && (clip is not { } cr || cr.Intersects(rect));
            if (shown) Check(c, rect, viewport, clip, inScroll, found, texts);
            if (c.ClipContents || c is ScrollContainer) clip = clip is { } old ? old.Intersection(rect) : rect;
            if (c is ScrollContainer) inScroll = true;
        }
        foreach (Node child in node.GetChildren()) Walk(child, viewport, found, texts, clip, inScroll);
    }

    private static string? TextOf(Control c) => c switch
    {
        Label l => l.Text,
        Button b => b.Text,
        LinkButton lb => lb.Text,
        RichTextLabel r => r.GetParsedText(),
        _ => null,
    };

    private static void Check(Control c, Rect2 rect, Rect2 viewport, Rect2? clip, bool inScroll, List<Finding> found,
        List<(Control, Rect2, string)> texts)
    {
        string? text = TextOf(c);
        if (string.IsNullOrWhiteSpace(text)) return;
        string path = PathOf(c);
        int before = found.Count;

        switch (c)
        {
            case Label l:
            {
                Font font = l.LabelSettings?.Font ?? l.GetThemeFont("font");
                int size = l.LabelSettings?.FontSize ?? l.GetThemeFontSize("font_size");
                string shown = l.Uppercase ? l.Text.ToUpperInvariant() : l.Text;
                StyleBox box = l.GetThemeStylebox("normal");
                float room = rect.Size.X - box.GetMargin(Side.Left) - box.GetMargin(Side.Right);
                if (l.AutowrapMode == TextServer.AutowrapMode.Off)
                {
                    float wide = shown.Split('\n').Max(line => font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X);
                    if (wide > room + Slack)
                    {
                        bool ellipsis = l.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming;
                        found.Add(new(ellipsis ? Trimmed(c) : l.ClipText ? "cut" : "spills", path, Short(text), $"text {wide:0}px in {room:0}px"));
                    }
                }
                else
                {
                    int lines = l.GetLineCount(), visible = l.GetVisibleLineCount();
                    if (visible < lines && l.MaxLinesVisible < 0 && !inScroll)
                        found.Add(new("hidden-lines", path, Short(text), $"{visible} of {lines} lines fit"));
                    int words = shown.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    if (lines >= 3 && words > 1 && lines * 2 >= words + 2 && room < 160)
                        found.Add(new("wraps-badly", path, Short(text), $"{lines} lines for {words} words, {room:0}px wide"));
                }
                break;
            }
            case Button b when b.Text.Length > 0:
            {
                Font font = b.GetThemeFont("font");
                int size = b.GetThemeFontSize("font_size");
                StyleBox box = b.GetThemeStylebox("normal");
                float wide = b.Text.Split('\n').Max(line => font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X);
                float extra = box.GetMargin(Side.Left) + box.GetMargin(Side.Right);
                if (b.Icon != null && !b.ExpandIcon) extra += b.Icon.GetWidth() * (b.GetThemeConstant("icon_max_width") is > 0 and var mx ? Math.Min(1f, mx / (float)b.Icon.GetWidth()) : 1f) + b.GetThemeConstant("h_separation");
                if (b is OptionButton ob && ob.GetThemeIcon("arrow") is { } arrow) extra += arrow.GetWidth() + b.GetThemeConstant("arrow_margin");
                // A tick box draws its box before the text.
                if (b is CheckBox or CheckButton && b.GetThemeIcon(b.ButtonPressed ? "checked" : "unchecked") is { } tick)
                    extra += tick.GetWidth() + b.GetThemeConstant("h_separation");
                if (b.AutowrapMode == TextServer.AutowrapMode.Off && wide + extra > rect.Size.X + Slack)
                    found.Add(new(b.TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming ? Trimmed(c) : b.ClipText ? "cut" : "spills",
                        path, Short(text), $"text {wide:0}px + {extra:0}px in {rect.Size.X:0}px"));
                break;
            }
            case RichTextLabel r when !r.FitContent && !r.ScrollActive && !inScroll:
                if (r.GetContentHeight() > rect.Size.Y + Slack)
                    found.Add(new("hidden-lines", path, Short(text), $"content {r.GetContentHeight()}px in {rect.Size.Y:0}px"));
                break;
        }

        // Past the window's edge, or past the panel holding it (a scrolling list may run past - it scrolls).
        if (inScroll) { }
        else if (!viewport.Grow(Slack).Encloses(rect))
            found.Add(new("off-screen", path, Short(text), $"{rect.Position.X:0},{rect.Position.Y:0} {rect.Size.X:0}x{rect.Size.Y:0}"));
        else if (!inScroll && c.GetParent() is Control parent && parent is not ScrollContainer && !c.TopLevel)
        {
            Rect2 pr = parent.GetGlobalRect();
            if (pr.Size.X > 0 && pr.Size.Y > 0 && !pr.Grow(Slack + 1).Encloses(rect))
                found.Add(new("sticks-out", path, Short(text), $"{rect} outside {parent.Name} {pr}"));
        }
        // Clipped by a parent: part of the text is not drawn.
        if (!inScroll && clip is { } cl && !cl.Grow(Slack).Encloses(rect) && cl.Intersects(rect))
            found.Add(new("clipped", path, Short(text), $"{rect} clipped to {cl}"));

        for (int k = before; k < found.Count; k++) found[k] = found[k] with { Source = c };
        Rect2 tr = TextRect(c, rect);
        if (clip is { } seen) tr = tr.Intersection(seen);
        if (tr.Size.X > 0 && tr.Size.Y > 0) texts.Add((c, tr, text));
    }

    /// <summary>Text shortened with "..." is fine when a hover says it in full; without one the rest is lost.</summary>
    private static string Trimmed(Control c)
    {
        for (Node? p = c; p is Control k; p = p.GetParent())
            if (!string.IsNullOrEmpty(k.TooltipText)) return "trimmed";
        return "trimmed-no-hover";
    }

    /// <summary>The part of a control the text itself covers (a wide button with short text overlaps little).</summary>
    private static Rect2 TextRect(Control c, Rect2 rect)
    {
        if (c is Label { AutowrapMode: TextServer.AutowrapMode.Off } l)
        {
            Font font = l.LabelSettings?.Font ?? l.GetThemeFont("font");
            int size = l.LabelSettings?.FontSize ?? l.GetThemeFontSize("font_size");
            string shown = l.Uppercase ? l.Text.ToUpperInvariant() : l.Text;
            Vector2 s = font.GetMultilineStringSize(shown, HorizontalAlignment.Left, -1, size);
            float w = Math.Min(s.X, rect.Size.X + 400);
            float x = l.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => rect.Position.X + (rect.Size.X - w) / 2,
                HorizontalAlignment.Right => rect.End.X - w,
                _ => rect.Position.X,
            };
            // The line box has room above and below the letters; the letters themselves are about 70% of it.
            float h = Math.Min(s.Y * 0.7f, rect.Size.Y);
            float y = l.VerticalAlignment switch
            {
                VerticalAlignment.Center => rect.Position.Y + (rect.Size.Y - h) / 2,
                VerticalAlignment.Bottom => rect.End.Y - h,
                _ => rect.Position.Y,
            };
            return new Rect2(x, y, w, h);
        }
        return rect.Grow(-3);
    }

    private static bool IsAncestor(Node a, Node b)
    {
        for (Node? p = b.GetParent(); p != null; p = p.GetParent()) if (p == a) return true;
        return false;
    }

    private static Node? WindowOf(Node n)
    {
        for (Node? p = n; p != null; p = p.GetParent()) if (p is Window) return p;
        return null;
    }

    private static int LayerOf(Node n)
    {
        for (Node? p = n; p != null; p = p.GetParent()) if (p is CanvasLayer cl) return cl.Layer;
        return 0;
    }

    private static void Overlaps(List<(Control Control, Rect2 Rect, string Text)> texts, List<Finding> found)
    {
        for (int i = 0; i < texts.Count; i++)
            for (int j = i + 1; j < texts.Count; j++)
            {
                var (a, ra, ta) = texts[i];
                var (b, rb, tb) = texts[j];
                if (!ra.Intersects(rb)) continue;
                Rect2 both = ra.Intersection(rb);
                float area = both.Size.X * both.Size.Y, small = Math.Min(ra.Size.X * ra.Size.Y, rb.Size.X * rb.Size.Y);
                if (small <= 0 || area < small * 0.2f || both.Size.X < 6 || both.Size.Y < 6) continue;
                if (IsAncestor(a, b) || IsAncestor(b, a)) continue;
                // Something drawn on a higher layer or in a popup covers what is under it on purpose.
                if (WindowOf(a) != WindowOf(b) || LayerOf(a) != LayerOf(b)) continue;
                found.Add(new("overlap", PathOf(a), Short(ta), $"over '{Short(tb)}' ({PathOf(b)})", a));
            }
    }
}
