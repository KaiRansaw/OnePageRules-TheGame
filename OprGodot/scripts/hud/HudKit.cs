using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The tactical-HUD look (XCOM / Halo Wars style) Kai asked for on 2026-11-09: dark glass panels with cut
/// corners, a thin steel border, lime accents, Rajdhani for headings and numbers, white game-icons tinted
/// in code. Every HUD screen builds from these pieces so the look is changed in one place.
/// </summary>
public static class HudKit
{
    // ---- Colours: the teal-slate + lime palette, pushed a little darker and glassier for over-the-table use.
    public static readonly Color Glass = new(0.045f, 0.07f, 0.08f, 0.86f);
    public static readonly Color GlassLight = new(0.08f, 0.12f, 0.135f, 0.92f);
    public static readonly Color Edge = new(0.25f, 0.36f, 0.39f, 0.95f);
    public static readonly Color EdgeHot = new(0.40f, 0.78f, 0.70f, 1f);
    public static readonly Color Lime = Palette.Lime;
    public static readonly Color Teal = Palette.AccentHot;
    public static readonly Color Fg = Palette.Text;
    public static readonly Color Dim = Palette.TextDim;
    public static readonly Color Enemy = new(0.93f, 0.36f, 0.30f);
    public static readonly Color Friendly = new(0.30f, 0.80f, 0.95f);
    public static readonly Color Gold = Palette.HeroGold;

    // ---- Fonts
    private static FontFile? _bold, _semi, _med, _exo;
    public static FontFile Bold => _bold ??= GD.Load<FontFile>("res://fonts/Rajdhani-Bold.ttf");
    public static FontFile Semi => _semi ??= GD.Load<FontFile>("res://fonts/Rajdhani-SemiBold.ttf");
    public static FontFile Medium => _med ??= GD.Load<FontFile>("res://fonts/Rajdhani-Medium.ttf");
    public static Font Exo(int weight)
    {
        _exo ??= GD.Load<FontFile>("res://fonts/Exo2.ttf");
        return new FontVariation { BaseFont = _exo, VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } } };
    }
    /// <summary>Wide-tracked caps for headings ("ACTIVE UNIT").</summary>
    public static Font Tracked(FontFile f, int spacing) => new FontVariation { BaseFont = f, SpacingGlyph = spacing };

    // ---- Boxes
    public static ChamferBox PanelBox(float cut = 12) => new() { Fill = Glass, Border = Edge, Cut = cut };
    public static ChamferBox HeaderBox() => new()
    {
        Fill = GlassLight, Border = Edge, Cut = 12, Accent = Lime, AccentLength = 56,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
    };

    public static PanelContainer Panel(ChamferBox box, Control? content = null)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", box);
        if (content != null) p.AddChild(content);
        return p;
    }

    public static Label Text(string s, int size, Color? colour = null, Font? font = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = s, HorizontalAlignment = align };
        l.AddThemeFontOverride("font", font ?? Medium);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour ?? Fg);
        return l;
    }

    /// <summary>A section heading: tracked caps in the header teal, with a short lime tick in front.</summary>
    public static Control Heading(string s, int size = 17)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var tick = new ColorRect { Color = Lime, CustomMinimumSize = new Vector2(4, size * 0.75f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(tick);
        row.AddChild(Text(s.ToUpperInvariant(), size, Palette.Header, Tracked(Bold, 2)));
        return row;
    }

    public static Texture2D IconTex(string name) => GD.Load<Texture2D>($"res://icons/{name}.svg");

    public static TextureRect Icon(string name, float size, Color tint) => new()
    {
        Texture = IconTex(name), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size), SelfModulate = tint,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static void StyleButton(Button b, ChamferBox normal, Color text, int size = 18)
    {
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", normal.With(x => { x.Border = EdgeHot; x.Fill = x.Fill.Lightened(0.08f); x.Glow = EdgeHot with { A = 0.5f }; }));
        b.AddThemeStyleboxOverride("pressed", normal.With(x => { x.Border = Lime; x.Fill = new Color(Lime, 0.25f); }));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("disabled", normal.With(x => { x.Fill = x.Fill with { A = 0.5f }; x.Border = x.Border with { A = 0.35f }; }));
        b.AddThemeFontOverride("font", Tracked(Bold, 1));
        b.AddThemeFontSizeOverride("font_size", size);
        foreach (string c in new[] { "font_color", "font_hover_color", "font_focus_color" })
            b.AddThemeColorOverride(c, text);
        b.AddThemeColorOverride("font_pressed_color", Lime);
        b.AddThemeColorOverride("font_disabled_color", Dim with { A = 0.6f });
    }

    /// <summary>
    /// A square action button: icon on top, name under it, hotkey in the top-left corner. The XCOM-style bar
    /// is a row of these.
    /// </summary>
    public static Button ActionButton(string name, string icon, string label, string hotkey, Color iconTint, float size = 92, bool selected = false, bool disabled = false, int labelSize = 14, bool dim = false)
    {
        // dim: drawn greyed out but still clickable (Hold with nothing to shoot - Kai, 2026-11-11).
        bool grey = disabled || dim;
        var b = new Button { Name = name, CustomMinimumSize = new Vector2(size, size + 6), Disabled = disabled, FocusMode = Control.FocusModeEnum.None };
        var box = new ChamferBox { Fill = GlassLight, Border = Edge, Cut = 12, ContentMarginTop = 6, ContentMarginBottom = 6 };
        if (selected) box = box.With(x => { x.Border = Lime; x.Glow = Lime with { A = 0.55f }; x.Fill = new Color(0.12f, 0.17f, 0.10f, 0.95f); });
        StyleButton(b, box, Fg, 15);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 2);
        var ic = Icon(icon, size * 0.46f, grey ? Dim with { A = 0.4f } : selected ? Lime : iconTint);
        ic.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(ic);
        var lbl = Text(label.ToUpperInvariant(), labelSize, grey ? Dim with { A = 0.5f } : selected ? Lime : Fg, Tracked(Bold, 1), HorizontalAlignment.Center);
        lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(lbl);
        b.AddChild(col);

        var key = Text(hotkey, 13, grey ? Dim with { A = 0.4f } : Dim, Bold);
        key.Position = new Vector2(9, 4);
        key.MouseFilter = Control.MouseFilterEnum.Ignore;
        b.AddChild(key);
        return b;
    }

    /// <summary>Small chip: "Q4+" / "Fast" / "Tough(3)". Gold outline for rules, filled for stats.</summary>
    public static PanelContainer Chip(string s, Color colour, bool filled = false, int size = 15)
    {
        var box = new ChamferBox
        {
            Fill = filled ? new Color(colour, 0.22f) : new Color(0, 0, 0, 0.25f), Border = new Color(colour, 0.75f), Cut = 5,
            Corners = ChamferBox.ECorners.Diagonal, ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 1, ContentMarginBottom = 1,
        };
        return Panel(box, Text(s, size, colour, Semi));
    }

    public static Control Gap(float w, float h = 0) => new Control { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static Control Grow() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static MarginContainer Pad(Control c, int l, int t, int r, int b)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", l);
        m.AddThemeConstantOverride("margin_top", t);
        m.AddThemeConstantOverride("margin_right", r);
        m.AddThemeConstantOverride("margin_bottom", b);
        m.AddChild(c);
        return m;
    }

    /// <summary>
    /// Pins a control to a screen corner / edge centre at its own minimum size, growing away from the edge,
    /// so a panel keeps fitting its content. <paramref name="offset"/> moves it in from the edge.
    /// </summary>
    public static T Place<T>(Control parent, T c, Control.LayoutPreset preset, Vector2 offset = default) where T : Control
    {
        parent.AddChild(c);
        c.SetAnchorsPreset(preset);
        c.OffsetLeft = c.OffsetRight = offset.X;
        c.OffsetTop = c.OffsetBottom = offset.Y;
        c.GrowHorizontal = c.AnchorLeft >= 1 ? Control.GrowDirection.Begin : c.AnchorLeft > 0 ? Control.GrowDirection.Both : Control.GrowDirection.End;
        c.GrowVertical = c.AnchorTop >= 1 ? Control.GrowDirection.Begin : c.AnchorTop > 0 ? Control.GrowDirection.Both : Control.GrowDirection.End;
        return c;
    }

    // ---- Unit pictures

    private static readonly Dictionary<string, Texture2D?> Pictures = new();

    /// <summary>
    /// A unit's portrait: one made from its downloaded model (<see cref="FdgRaylib.Models3D.UnitPortraits"/>) first,
    /// else its Army Forge portrait from UnitImages/&lt;faction&gt;/Portraits (what the "Download Unit Pictures"
    /// tool fetches; a loose UnitImages/Portraits picture works for every faction), or null. Looked for next to
    /// the repo / the game, like the Raylib build. Name matching ignores case, spaces and punctuation.
    /// </summary>
    public static Texture2D? UnitPicture(string faction, string unit)
    {
        string key = faction + "/" + unit;
        if (Pictures.TryGetValue(key, out var t)) return t;
        t = null;
        // A portrait made from the unit's own downloaded model comes first: it matches what stands on the table.
        if (FdgRaylib.Models3D.UnitPortraits.Find(faction, unit) is { } made && Image.LoadFromFile(made) is { } mi)
            return Pictures[key] = ImageTexture.CreateFromImage(mi);
        static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        string want = Norm(unit);
        foreach (string root in new[] { System.IO.Path.Combine(FdgRaylib.AppAssets.Root, ".."), FdgRaylib.AppAssets.Root, System.Environment.CurrentDirectory })
        {
            foreach (string folder in new[] { System.IO.Path.Combine(root, "UnitImages", faction, "Portraits"), System.IO.Path.Combine(root, "UnitImages", "Portraits") })
            {
                if (!System.IO.Directory.Exists(folder)) continue;
                string? file = System.IO.Directory.EnumerateFiles(folder)
                    .FirstOrDefault(f => Norm(System.IO.Path.GetFileNameWithoutExtension(f)) == want
                        && new[] { ".png", ".jpg", ".jpeg" }.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()));
                if (file == null) continue;
                var img = Image.LoadFromFile(file);
                if (img != null) { t = ImageTexture.CreateFromImage(img); break; }
            }
            if (t != null) break;
        }
        return Pictures[key] = t;
    }

    /// <summary>Forgets the pictures already looked up, so newly made portraits show.</summary>
    public static void ForgetPictures() { Pictures.Clear(); FdgRaylib.Models3D.UnitPortraits.Forget(); }

    /// <summary>A framed unit picture: the Army Forge portrait if downloaded, else <paramref name="fallbackIcon"/>.</summary>
    public static Control Portrait(string faction, string unit, string fallbackIcon, float size, Color team, float alpha = 1f)
        => Portrait(UnitPicture(faction, unit), fallbackIcon, size, team, alpha);

    /// <summary>A framed picture (any texture - the Asset Lab previews a new portrait with this), else the icon.</summary>
    public static Control Portrait(Texture2D? picture, string fallbackIcon, float size, Color team, float alpha = 1f)
    {
        Control inner;
        if (picture is { } tex)
            inner = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(size, size), SelfModulate = Colors.White with { A = alpha }, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        else
            inner = Icon(fallbackIcon, size * 0.72f, team with { A = alpha * 0.9f });
        var frame = Panel(new ChamferBox
        {
            Fill = new Color(team, 0.12f * alpha), Border = team with { A = 0.55f * alpha }, Cut = 6,
            ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 2, ContentMarginBottom = 2,
        }, inner);
        frame.CustomMinimumSize = new Vector2(size + 4, size + 4);
        frame.MouseFilter = Control.MouseFilterEnum.Ignore;
        inner.SizeFlagsHorizontal = inner.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return frame;
    }

    /// <summary>A rough icon for a unit's kind, for when there is no picture.</summary>
    public static string KindIcon(string name, bool hero, bool shoots, int tough)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("artillery") || n.Contains("cannon") || n.Contains("catapult")) return "fire-ray";
        if (tough >= 6) return "gears";
        if (n.Contains("knight") || n.Contains("rider") || n.Contains("cavalry") || n.Contains("hound") || n.Contains("bike")) return "running-shoe";
        if (hero) return "rally-the-troops";
        return shoots ? "targeting" : "person";
    }
}
