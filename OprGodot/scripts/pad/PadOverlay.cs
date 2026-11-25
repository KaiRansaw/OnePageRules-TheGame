using Godot;
using OprGodot.Hud;

namespace OprGodot.Pads;

/// <summary>
/// What the controller puts on screen, over everything: the menu highlight's outline (every kind of control, not only
/// the buttons that draw their own), the pointer ring, the Y-held info box, a short toast, and the strip of button
/// hints along the bottom. Plain drawing - the decision is what shows, not how it looks.
/// </summary>
public partial class PadOverlay : Control
{
    private static PadDriver _pad => PadDriver.I!;
    private readonly PanelContainer _info, _toast;
    private readonly Label _infoText, _toastText;
    private double _toastLeft;

    public PadOverlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        (_info, _infoText) = Box(HudKit.Lime, 520);
        (_toast, _toastText) = Box(HudKit.Teal, 760);
    }

    private (PanelContainer, Label) Box(Color edge, float width)
    {
        var text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0), MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeFontSizeOverride("font_size", 17);
        var box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.03f, 0.04f, 0.94f), BorderColor = edge,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        box.AddChild(text);
        AddChild(box);
        return (box, text);
    }

    public void Toast(string text)
    {
        _toastText.Text = text;
        _toast.ResetSize();
        _toast.Visible = true;
        _toastLeft = 3.0;
    }

    public override void _Process(double delta)
    {
        if (_toastLeft > 0)
        {
            _toastLeft -= delta;
            if (_toastLeft <= 0) _toast.Visible = false;
            Vector2 size = GetViewportRect().Size;
            _toast.Position = new Vector2((size.X - _toast.Size.X) / 2, 70);
        }
        bool menus = PadDriver.Active && !_pad.PointerMode;
        string? tip = menus && _pad.InfoHeld ? _pad.FocusedInfo(out Rect2 at) : null;
        _info.Visible = tip != null;
        if (tip != null)
        {
            _infoText.Text = tip;
            _info.ResetSize();
            Rect2 r = _pad.FocusedInfo(out at) != null ? at : default;
            Vector2 size = GetViewportRect().Size;
            float y = r.End.Y + 10 + _info.Size.Y < size.Y - 60 ? r.End.Y + 10 : r.Position.Y - _info.Size.Y - 10;
            _info.Position = new Vector2(Math.Clamp(r.Position.X, 10, size.X - _info.Size.X - 10), Math.Max(10, y));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!PadDriver.Active || !Pad.Prefs.Enabled) return;
        if (_pad.PointerMode) DrawPointer(_pad.Pointer);
        // A list or table shows the highlight on its picked row - no glow round the whole box (Kai, 2026-11-24).
        else if (GetViewport().GuiGetFocusOwner() is { } f && f.IsVisibleInTree() && f is not (Tree or ItemList))
        {
            // Kai, 2026-11-25: in a battle the panel the highlight is in glows, so LB / RB show where you are.
            if (_pad.ActivePanelRect() is { } panel)
            {
                Rect2 p = panel.Grow(2);
                for (int i = 1; i <= 6; i++) DrawRect(p.Grow(i * 2), HudKit.Lime with { A = 0.16f / i }, false, 2f);
                DrawRect(p, HudKit.Lime with { A = 0.85f }, false, 2f);
            }
            Rect2 r = f.GetGlobalRect().Grow(3);
            DrawRect(r, HudKit.Lime with { A = 0.10f });
            DrawRect(r, HudKit.Lime, false, 2.5f);
        }
        if (Pad.Prefs.ShowHints) DrawHints();
    }

    /// <summary>The controller cursor (Kai, 2026-11-24, from his Destiny picture): a soft, pale blue-grey disc with a
    /// lighter rim, see-through enough to read what's under it, with a faint shadow so it shows on light backgrounds.</summary>
    private void DrawPointer(Vector2 p)
    {
        const float r = 13f;
        DrawCircle(p + new Vector2(1, 2), r + 2, new Color(0, 0, 0, 0.22f));             // shadow
        DrawCircle(p, r, new Color(0.72f, 0.80f, 0.84f, 0.62f));                         // the disc
        DrawCircle(p, r * 0.55f, new Color(0.86f, 0.92f, 0.95f, 0.35f));                 // brighter middle
        DrawArc(p, r, 0, Mathf.Tau, 48, new Color(0.93f, 0.97f, 1f, 0.95f), 2f, true);  // rim
    }

    // ---------------------------------------------------------------- hints

    private void DrawHints()
    {
        var hints = new List<(PadButton[] Buttons, string Text)>();
        PadButton B(string id) => Pad.All.First(b => b.Id == id).Button;
        if (!_pad.PointerMode && _pad.InBattle)
        {
            // Battle, menu highlight: the d-pad walks the HUD buttons, the battle's own buttons still work.
            if (_pad.TurnName() is { } who) hints.Add((Array.Empty<PadButton>(), $"{who}'s turn"));
            hints.Add((new[] { B("menu.press") }, "Select"));
            hints.Add((new[] { B("menu.back") }, "Step back"));
            hints.Add((new[] { B("bat.confirm") }, "Confirm"));
            hints.Add((new[] { B("bat.next") }, "Next unit"));
            hints.Add((new[] { B("menu.tabl"), B("menu.tabr") }, "Panels"));
            hints.Add((new[] { B("ptr.esc") }, "Menu"));
            hints.Add((new[] { B("menu.pointer") }, "Pointer"));
        }
        else if (!_pad.PointerMode)
        {
            hints.Add((new[] { B("menu.press") }, "Select"));
            hints.Add((new[] { B("menu.back") }, "Back"));
            hints.Add((new[] { B("menu.info") }, "Hold for info"));
            hints.Add((new[] { B("menu.tabl"), B("menu.tabr") }, "Tabs"));
            hints.Add((new[] { B("menu.pointer") }, "Pointer"));
        }
        else if (_pad.InBattle)
        {
            if (_pad.TurnName() is { } who) hints.Add((Array.Empty<PadButton>(), $"{who}'s turn"));
            hints.Add((new[] { B("ptr.click") }, "Click"));
            hints.Add((new[] { B("ptr.right") }, "Step back"));
            hints.Add((new[] { B("bat.confirm") }, "Confirm"));
            hints.Add((new[] { B("bat.next") }, "Next unit"));
            hints.Add((new[] { B("bat.tprev"), B("bat.tnext") }, "Targets"));
            hints.Add((new[] { PadButton.DUp }, "Move camera"));
            hints.Add((new[] { B("ptr.esc") }, "Menu"));
        }
        else
        {
            hints.Add((new[] { B("ptr.click") }, "Select"));
            hints.Add((new[] { B("ptr.right") }, "Back"));
            hints.Add((new[] { B("ptr.menus") }, "Highlight"));
        }

        Font font = GetThemeDefaultFont();
        const int size = 14;
        const float r = 11, gap = 18;
        float width = 0;
        foreach (var h in hints) width += h.Buttons.Length * (r * 2 + 3) + 5 + font.GetStringSize(h.Text, HorizontalAlignment.Left, -1, size).X + gap;
        Vector2 view = GetViewportRect().Size;
        float x = (view.X - width) / 2, y = view.Y - 15;
        var strip = new Rect2(x - 12, y - 14, width + 12, 29);
        // Never cover what the player is on: the strip steps aside while the highlight or pointer is under it.
        Rect2? on = _pad.PointerMode ? new Rect2(_pad.Pointer - new Vector2(16, 16), new Vector2(32, 32))
            : GetViewport().GuiGetFocusOwner()?.GetGlobalRect();
        if (on is { } o && o.Intersects(strip)) return;
        DrawRect(strip, new Color(0, 0, 0, 0.6f));
        foreach (var (buttons, text) in hints)
        {
            foreach (PadButton b in buttons) { PadGlyph.Draw(this, new Vector2(x + r, y), r, b, font); x += r * 2 + 3; }
            x += 5;
            DrawString(font, new Vector2(x, y + 5), text, HorizontalAlignment.Left, -1, size, HudKit.Fg);
            x += font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + gap;
        }
    }
}

/// <summary>A button's picture, drawn: Xbox face buttons as coloured letters, PlayStation as their shapes, the rest
/// as labelled pills. Used by the hint strip and the Controls tab.</summary>
public static class PadGlyph
{
    public static void Draw(CanvasItem ci, Vector2 c, float r, PadButton b, Font font)
    {
        bool ps = Pad.Style == Pad.EStyle.PlayStation;
        Color ring = new(0.85f, 0.88f, 0.9f);
        switch (b)
        {
            case PadButton.A or PadButton.B or PadButton.X or PadButton.Y:
                ci.DrawCircle(c, r, new Color(0.08f, 0.09f, 0.1f));
                ci.DrawArc(c, r, 0, Mathf.Tau, 24, ring with { A = 0.5f }, 1.5f, true);
                if (ps) Shape(ci, c, r * 0.5f, b);
                else
                {
                    Color col = b switch { PadButton.A => new(0.45f, 0.8f, 0.3f), PadButton.B => new(0.9f, 0.3f, 0.3f), PadButton.X => new(0.3f, 0.55f, 0.95f), _ => new(0.95f, 0.8f, 0.25f) };
                    Centered(ci, font, c, b.ToString(), (int)(r * 1.25f), col);
                }
                break;
            case PadButton.DUp or PadButton.DDown or PadButton.DLeft or PadButton.DRight:
            {
                float a = r * 0.36f;
                Color dim = ring with { A = 0.35f };
                ci.DrawRect(new Rect2(c.X - a, c.Y - r, a * 2, r * 2), dim);
                ci.DrawRect(new Rect2(c.X - r, c.Y - a, r * 2, a * 2), dim);
                Rect2 arm = b switch
                {
                    PadButton.DUp => new(c.X - a, c.Y - r, a * 2, r - a),
                    PadButton.DDown => new(c.X - a, c.Y + a, a * 2, r - a),
                    PadButton.DLeft => new(c.X - r, c.Y - a, r - a, a * 2),
                    _ => new(c.X + a, c.Y - a, r - a, a * 2),
                };
                ci.DrawRect(arm, ring);
                break;
            }
            default:
            {
                string label = b switch
                {
                    PadButton.LB => ps ? "L1" : "LB", PadButton.RB => ps ? "R1" : "RB",
                    PadButton.LT => ps ? "L2" : "LT", PadButton.RT => ps ? "R2" : "RT",
                    PadButton.LStick => "L3", PadButton.RStick => "R3",
                    PadButton.Back => ps ? "CR" : "VW", _ => ps ? "OPT" : "MENU",
                };
                var rect = new Rect2(c.X - r, c.Y - r * 0.72f, r * 2, r * 1.44f);
                ci.DrawRect(rect, new Color(0.08f, 0.09f, 0.1f));
                ci.DrawRect(rect, ring with { A = 0.6f }, false, 1.5f);
                Centered(ci, font, c, label, label.Length > 2 ? (int)(r * 0.62f) : (int)(r * 0.85f), ring);
                break;
            }
        }
    }

    private static void Shape(CanvasItem ci, Vector2 c, float s, PadButton b)
    {
        switch (b)
        {
            case PadButton.A: // cross, blue
                Color blue = new(0.45f, 0.65f, 1f);
                ci.DrawLine(c + new Vector2(-s, -s), c + new Vector2(s, s), blue, 2.5f, true);
                ci.DrawLine(c + new Vector2(-s, s), c + new Vector2(s, -s), blue, 2.5f, true);
                break;
            case PadButton.B: // circle, red
                ci.DrawArc(c, s, 0, Mathf.Tau, 20, new Color(1f, 0.4f, 0.4f), 2.5f, true);
                break;
            case PadButton.X: // square, pink
                ci.DrawRect(new Rect2(c.X - s * 0.85f, c.Y - s * 0.85f, s * 1.7f, s * 1.7f), new Color(1f, 0.5f, 0.8f), false, 2.5f);
                break;
            default: // triangle, green
                Vector2[] t = { c + new Vector2(0, -s), c + new Vector2(s, s * 0.75f), c + new Vector2(-s, s * 0.75f), c + new Vector2(0, -s) };
                ci.DrawPolyline(t, new Color(0.35f, 0.9f, 0.7f), 2.5f, true);
                break;
        }
    }

    private static void Centered(CanvasItem ci, Font font, Vector2 c, string text, int size, Color col)
    {
        Vector2 s = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        ci.DrawString(font, new Vector2(c.X - s.X / 2, c.Y + font.GetAscent(size) / 2 - 1), text, HorizontalAlignment.Left, -1, size, col);
    }
}

/// <summary>A control that shows one button's picture (the Controls tab's list).</summary>
public partial class PadGlyphView : Control
{
    public PadButton Button { get; set; }
    public PadGlyphView() { CustomMinimumSize = new Vector2(30, 30); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => PadGlyph.Draw(this, Size / 2, Math.Min(Size.X, Size.Y) / 2 - 1, Button, GetThemeDefaultFont());
}
