using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The whole app's look, built in code from the HUD pieces (Kai picked the tactical XCOM / Total War look on
/// 2026-11-10): Rajdhani everywhere, dark glass panels with cut corners, lime for "go", teal for normal
/// buttons. Set on the root window at start-up, so every screen - and every control a script makes - gets it
/// without asking. The type variations keep the names the step-1 screens already use.
/// </summary>
public static class HudTheme
{
    public static Theme Build()
    {
        var t = new Theme { DefaultFont = HudKit.Medium, DefaultFontSize = 19 };

        // ---- Labels
        t.SetColor("font_color", "Label", HudKit.Fg);
        Variation(t, "TitleLabel", "Label");
        t.SetFont("font", "TitleLabel", HudKit.Exo(800));
        t.SetFontSize("font_size", "TitleLabel", 64);
        t.SetColor("font_color", "TitleLabel", HudKit.Fg);
        Variation(t, "HeaderLabel", "Label");
        t.SetFont("font", "HeaderLabel", HudKit.Tracked(HudKit.Bold, 2));
        t.SetFontSize("font_size", "HeaderLabel", 26);
        t.SetColor("font_color", "HeaderLabel", Palette.Header);
        Variation(t, "DimLabel", "Label");
        t.SetFont("font", "DimLabel", HudKit.Semi);
        t.SetFontSize("font_size", "DimLabel", 16);
        t.SetColor("font_color", "DimLabel", HudKit.Dim);

        // ---- Buttons: normal (teal edge), Commit (lime), Back (quiet), Tile (big menu tiles), Mode (game screen)
        ButtonStyle(t, "Button", new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 6, ContentMarginBottom = 6 }, HudKit.Fg, 18);
        Variation(t, "CommitButton", "Button");
        ButtonStyle(t, "CommitButton", new ChamferBox { Fill = new Color(HudKit.Lime, 0.18f), Border = HudKit.Lime, Cut = 8, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 6, ContentMarginBottom = 6 }, HudKit.Lime, 20);
        Variation(t, "BackButton", "Button");
        ButtonStyle(t, "BackButton", new ChamferBox { Fill = new Color(0, 0, 0, 0.35f), Border = HudKit.Edge with { A = 0.6f }, Cut = 8, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 6, ContentMarginBottom = 6 }, HudKit.Dim, 18);
        Variation(t, "TileButton", "Button");
        ButtonStyle(t, "TileButton", new ChamferBox { Fill = new Color(0.05f, 0.08f, 0.09f, 0.82f), Border = HudKit.Edge, Cut = 18, Accent = HudKit.Lime, AccentLength = 80 }, HudKit.Fg, 40);
        Variation(t, "ModeButton", "Button");
        ButtonStyle(t, "ModeButton", new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge, Cut = 10, LeftBar = HudKit.Teal, ContentMarginLeft = 18, ContentMarginRight = 14 }, HudKit.Fg, 22);
        t.SetConstant("h_separation", "Button", 10);

        // ---- Panels
        var glass = new ChamferBox { Fill = HudKit.Glass, Border = HudKit.Edge, Cut = 12, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12 };
        t.SetStylebox("panel", "PanelContainer", glass);
        t.SetStylebox("panel", "Panel", glass);
        Variation(t, "CardPanel", "PanelContainer");
        t.SetStylebox("panel", "CardPanel", glass.With(x => { x.Accent = HudKit.Lime; x.AccentLength = 56; x.ContentMarginLeft = x.ContentMarginRight = 20; x.ContentMarginTop = x.ContentMarginBottom = 18; }));

        // ---- Tabs
        var tabOff = new ChamferBox { Fill = HudKit.GlassLight, Border = HudKit.Edge with { A = 0.6f }, Cut = 7, Corners = ChamferBox.ECorners.TopRight, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 6, ContentMarginBottom = 6 };
        t.SetStylebox("tab_unselected", "TabContainer", tabOff);
        t.SetStylebox("tab_hovered", "TabContainer", tabOff.With(x => x.Border = HudKit.EdgeHot));
        t.SetStylebox("tab_selected", "TabContainer", tabOff.With(x => { x.Fill = new Color(HudKit.Lime, 0.16f); x.Border = HudKit.Lime; }));
        t.SetStylebox("tab_disabled", "TabContainer", tabOff);
        t.SetStylebox("tab_focus", "TabContainer", new StyleBoxEmpty());
        t.SetStylebox("panel", "TabContainer", glass.With(x => { x.Corners = ChamferBox.ECorners.BottomRight; x.ContentMarginLeft = x.ContentMarginRight = 22; x.ContentMarginTop = 18; }));
        t.SetFont("font", "TabContainer", HudKit.Tracked(HudKit.Bold, 1));
        t.SetFontSize("font_size", "TabContainer", 18);
        t.SetColor("font_selected_color", "TabContainer", HudKit.Lime);
        t.SetColor("font_unselected_color", "TabContainer", HudKit.Dim);
        t.SetColor("font_hovered_color", "TabContainer", HudKit.Fg);
        t.SetConstant("side_margin", "TabContainer", 0);

        // ---- Text boxes, dropdowns, check boxes, sliders
        var field = new ChamferBox { Fill = new Color(0, 0, 0, 0.45f), Border = HudKit.Edge, Cut = 6, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 5, ContentMarginBottom = 5 };
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", field.With(x => x.Border = HudKit.Lime));
        t.SetStylebox("read_only", "LineEdit", field.With(x => x.Fill = x.Fill with { A = 0.2f }));
        t.SetColor("font_color", "LineEdit", HudKit.Fg);
        t.SetColor("caret_color", "LineEdit", HudKit.Lime);
        t.SetColor("selection_color", "LineEdit", new Color(HudKit.Teal, 0.5f));
        ButtonStyle(t, "OptionButton", field.With(x => x.ContentMarginRight = 30), HudKit.Fg, 18);
        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            t.SetStylebox("normal", type, new StyleBoxEmpty());
            t.SetStylebox("hover", type, new StyleBoxEmpty());
            t.SetStylebox("pressed", type, new StyleBoxEmpty());
            t.SetStylebox("hover_pressed", type, new StyleBoxEmpty());
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, HudKit.Fg);
            t.SetColor("font_hover_color", type, HudKit.Fg);
            t.SetColor("font_pressed_color", type, HudKit.Lime);
            t.SetColor("font_hover_pressed_color", type, HudKit.Lime);
        }
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.5f), ContentMarginTop = 3, ContentMarginBottom = 3 };
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", new StyleBoxFlat { BgColor = HudKit.Teal, ContentMarginTop = 3, ContentMarginBottom = 3 });
        t.SetStylebox("grabber_area_highlight", "HSlider", new StyleBoxFlat { BgColor = HudKit.Lime, ContentMarginTop = 3, ContentMarginBottom = 3 });

        // ---- Popups and tooltips
        var popup = new ChamferBox { Fill = new Color(0.035f, 0.05f, 0.058f, 0.98f), Border = HudKit.Edge, Cut = 8, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6 };
        t.SetStylebox("panel", "PopupMenu", popup);
        t.SetStylebox("hover", "PopupMenu", new StyleBoxFlat { BgColor = new Color(HudKit.Lime, 0.18f) });
        t.SetColor("font_color", "PopupMenu", HudKit.Fg);
        t.SetColor("font_hover_color", "PopupMenu", HudKit.Lime);
        t.SetFont("font", "PopupMenu", HudKit.Semi);
        t.SetFontSize("font_size", "PopupMenu", 18);
        t.SetStylebox("panel", "PopupPanel", popup);
        t.SetStylebox("panel", "TooltipPanel", popup.With(x => { x.Border = Palette.RuleGold with { A = 0.6f }; x.ContentMarginLeft = x.ContentMarginRight = 12; x.ContentMarginTop = x.ContentMarginBottom = 8; }));
        t.SetColor("font_color", "TooltipLabel", HudKit.Fg);
        t.SetFont("font", "TooltipLabel", HudKit.Medium);
        t.SetFontSize("font_size", "TooltipLabel", 17);

        // ---- Scroll bars: thin
        var bar = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.25f), ContentMarginLeft = 3, ContentMarginRight = 3 };
        var grab = new StyleBoxFlat { BgColor = HudKit.Edge, ContentMarginLeft = 3, ContentMarginRight = 3 };
        t.SetStylebox("scroll", "VScrollBar", bar);
        t.SetStylebox("grabber", "VScrollBar", grab);
        t.SetStylebox("grabber_highlight", "VScrollBar", new StyleBoxFlat { BgColor = HudKit.Teal });
        t.SetStylebox("grabber_pressed", "VScrollBar", new StyleBoxFlat { BgColor = HudKit.Lime });
        return t;
    }

    private static void Variation(Theme t, string name, string baseType) => t.SetTypeVariation(name, baseType);

    private static void ButtonStyle(Theme t, string type, ChamferBox normal, Color text, int size)
    {
        t.SetStylebox("normal", type, normal);
        t.SetStylebox("hover", type, normal.With(x => { x.Border = HudKit.EdgeHot; x.Fill = x.Fill.Lightened(0.08f); x.Glow = HudKit.EdgeHot with { A = 0.45f }; }));
        t.SetStylebox("pressed", type, normal.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(HudKit.Lime, 0.25f); }));
        t.SetStylebox("hover_pressed", type, normal.With(x => { x.Border = HudKit.Lime; x.Fill = new Color(HudKit.Lime, 0.25f); }));
        t.SetStylebox("disabled", type, normal.With(x => { x.Fill = x.Fill with { A = x.Fill.A * 0.5f }; x.Border = x.Border with { A = 0.3f }; x.Accent = Colors.Transparent; }));
        t.SetStylebox("focus", type, new StyleBoxEmpty());
        t.SetFont("font", type, HudKit.Tracked(HudKit.Bold, 1));
        t.SetFontSize("font_size", type, size);
        foreach (string c in new[] { "font_color", "font_focus_color" }) t.SetColor(c, type, text);
        t.SetColor("font_hover_color", type, text.Lightened(0.15f));
        t.SetColor("font_pressed_color", type, HudKit.Lime);
        t.SetColor("font_hover_pressed_color", type, HudKit.Lime);
        t.SetColor("font_disabled_color", type, HudKit.Dim with { A = 0.5f });
    }
}
