using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The tactical-HUD look (XCOM / Halo Wars style) Kai asked for on 2026-11-09: dark glass panels with cut
/// corners, a thin steel border, lime accents, Rajdhani for headings and numbers, white game-icons tinted
/// in code. Every HUD screen builds from these pieces so the look is changed in one place.
/// </summary>
public static class HudKit
{
    // ---- Colours: read from the current look (Hud.Looks) - Grimdark's teal-slate + lime, or Fantasy's red + gold.
    public static Color Glass => Looks.Current.Glass;
    public static Color GlassLight => Looks.Current.GlassLight;
    public static Color Edge => Looks.Current.Edge;
    public static Color EdgeHot => Looks.Current.EdgeHot;
    /// <summary>Fill of a picked box, in the look's colours (was a hard-coded lime tint on every screen).</summary>
    public static Color Picked => Looks.Current.Picked;
    /// <summary>A deep panel inside another, in the look's colours.</summary>
    public static Color Well => Looks.Current.Well;
    public static Color Lime => Palette.Lime;

    /// <summary>
    /// A side-menu or list row in the same framed style as every other button (so the UI theme's art applies): the
    /// plain button look when off, the selected look (go-coloured line and fill, a bar down the left) when on.
    /// </summary>
    public static void RowStyle(Button b, bool on, int leftPad = 18)
    {
        ChamferBox normal = on
            ? new ChamferBox { Fill = new Color(Lime, 0.18f), Border = Lime, Cut = 8, LeftBar = Lime, ContentMarginLeft = leftPad, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 }
            : new ChamferBox { Fill = GlassLight, Border = Edge, Cut = 8, ContentMarginLeft = leftPad, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", on ? normal : normal.With(x => { x.Border = EdgeHot; x.Fill = x.Fill.Lightened(0.08f); }));
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }
    public static Color Teal => Palette.AccentHot;
    public static Color Fg => Palette.Text;
    public static Color Dim => Palette.TextDim;
    public static readonly Color Enemy = new(0.93f, 0.36f, 0.30f);
    public static readonly Color Friendly = new(0.30f, 0.80f, 0.95f);
    public static readonly Color Gold = Palette.HeroGold;

    // ---- Fonts (per look: Rajdhani + Exo 2, or Cinzel + Alegreya Sans)
    private static FontFile? _bold, _semi, _med, _exo;
    public static FontFile Bold => _bold ??= GD.Load<FontFile>(Looks.Current.BoldFont);
    public static FontFile Semi => _semi ??= GD.Load<FontFile>(Looks.Current.SemiFont);
    public static FontFile Medium => _med ??= GD.Load<FontFile>(Looks.Current.MediumFont);
    public static Font Exo(int weight)
    {
        _exo ??= GD.Load<FontFile>(Looks.Current.TitleFont);
        if (Looks.Current.Id == Looks.ELook.Fantasy) return _exo;
        return new FontVariation { BaseFont = _exo, VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } } };
    }
    /// <summary>Wide-tracked caps for headings ("ACTIVE UNIT"). Cinzel is already wide, so it gets less.</summary>
    public static Font Tracked(FontFile f, int spacing) => new FontVariation
        { BaseFont = f, SpacingGlyph = Looks.Current.Id == Looks.ELook.Fantasy ? Math.Max(0, spacing / 2) : spacing };

    /// <summary>Is this the look's heading face (Bold, tracked Bold, or the title font)? Cinzel sizes are scaled down.</summary>
    private static bool IsHeadingFont(Font? f) => f != null && (f == _bold || f == _exo || (f is FontVariation v && (v.BaseFont == _bold || v.BaseFont == _exo)));
    internal static void ForgetFonts() { _bold = _semi = _med = _exo = null; }
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
        if (Looks.Current.HeadingScale != 1f && IsHeadingFont(font)) size = Mathf.RoundToInt(size * Looks.Current.HeadingScale);
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
        if (selected) box = box.With(x => { x.Border = Lime; x.Glow = Lime with { A = 0.55f }; x.Fill = HudKit.Picked; });
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
        // A label wider than the square (Cinzel's "RULES VIEW" in the Fantasy look) widens the button instead of running
        // over its edges (lane 3, 2026-11-27).
        float wide = lbl.GetThemeFont("font").GetStringSize(lbl.Text, HorizontalAlignment.Left, -1, labelSize).X + 12;
        if (wide > size) b.CustomMinimumSize = new Vector2(wide, size + 6);

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
    /// A unit's portrait: its official Army Forge picture from UnitImages/&lt;faction&gt;/Portraits first (what the Asset
    /// Lab's "Download official pictures" fetches; a loose UnitImages/Portraits picture works for every faction), else
    /// one made from its downloaded model (<see cref="FdgRaylib.Models3D.UnitPortraits"/>), or null. Kai, 2026-11-14:
    /// the official pictures win - he didn't like the model-made ones. Looked for next to the repo / the game, like the
    /// Raylib build. Folder and file names match ignoring case, spaces and punctuation.
    /// </summary>
    /// <summary>
    /// A unit's picture as its owner sees it: another player's units show the pictures that player sent
    /// (<see cref="SharedLooks"/>), else this computer's own lookup.
    /// </summary>
    public static Texture2D? UnitPicture(string faction, string unit, string? look, FDG.PlayerID? owner)
    {
        if (owner != null && SharedLooks.Picture(owner, unit, look, out bool made) is { } shared)
        {
            if (made) ModelMade.Add(shared);
            return shared;
        }
        return UnitPicture(faction, unit, look);
    }

    /// <summary>How a unit's picture is framed: its owner's framing for another player's unit, else this computer's.</summary>
    public static FdgRaylib.Models3D.PictureFraming.Frame FramingFor(string faction, string unit, FDG.PlayerID? owner = null)
        => SharedLooks.Framing(owner, unit) ?? FdgRaylib.Models3D.PictureFraming.For(faction, unit);

    /// <summary>A Quest hero's picture: its own portrait when one was taken, else its base hero's.</summary>
    public static Control HeroPortrait(FDG.Quest.QuestHero h, float size) =>
        Portrait(FdgRaylib.Models3D.UnitPortraits.FindHero(h.Faction, h.Name) != null ? UnitPicture(h.Faction, h.Name)
            : UnitPicture(h.Faction, h.BaseHeroName, h.Look ?? FDG.Quest.QuestArmy.QuestHeroLook(h)), "person", size, Friendly);

    public static Texture2D? UnitPicture(string faction, string unit, string? look = null)
    {
        string key = faction + "/" + unit + "|" + look;
        if (Pictures.TryGetValue(key, out var t)) return t;
        t = null;
        // A Quest hero's own portrait (Hero Builder -> Take portrait) - the battle names its unit after the hero.
        if (FdgRaylib.Models3D.UnitPortraits.FindHero(faction, unit) is { } own && Image.LoadFromFile(own) is { } oi)
        {
            t = ImageTexture.CreateFromImage(oi);
            ModelMade.Add(t);
            return Pictures[key] = t;
        }
        // A portrait made on purpose for this exact version (Army Forge -> Choose looks -> Make portrait) comes first
        // (Kai, 2026-11-14); then the official picture; then any older model-made one.
        if (FdgRaylib.Models3D.UnitPortraits.FindChosen(faction, unit, look) is { } chosen && Image.LoadFromFile(chosen) is { } ci)
        {
            t = ImageTexture.CreateFromImage(ci);
            ModelMade.Add(t);
            return Pictures[key] = t;
        }
        string Norm(string s) => FdgRaylib.Models3D.OfficialPictures.Normalise(s);
        string want = Norm(unit), army = Norm(faction);
        foreach (string root in new[] { System.IO.Path.Combine(FdgRaylib.AppAssets.Root, ".."), FdgRaylib.AppAssets.Root, System.Environment.CurrentDirectory })
        {
            string images = System.IO.Path.Combine(root, "UnitImages");
            if (!System.IO.Directory.Exists(images)) continue;
            var folders = System.IO.Directory.EnumerateDirectories(images)
                .Where(d => Norm(System.IO.Path.GetFileName(d)) == army)
                .Select(d => System.IO.Path.Combine(d, "Portraits"))
                .Append(System.IO.Path.Combine(images, "Portraits"));
            foreach (string folder in folders)
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
        if (t == null && FdgRaylib.Models3D.UnitPortraits.Find(faction, unit, look) is { } made && Image.LoadFromFile(made) is { } mi)
        {
            t = ImageTexture.CreateFromImage(mi);
            ModelMade.Add(t);
        }
        return Pictures[key] = t;
    }

    /// <summary>Pictures photographed from a 3D model (clear background) - drawn over <see cref="Backdrop"/>.</summary>
    private static readonly HashSet<Texture2D> ModelMade = new();

    public static bool IsModelMade(Texture2D? t) => t != null && ModelMade.Contains(t);

    private static GradientTexture2D? _backdrop;

    /// <summary>
    /// Kai, 2026-11-14: a model-made portrait has nothing behind the model, so the card looked empty around it. This is
    /// what stands behind it: a soft lit backdrop in the game's colours, brighter behind the model, darker at the edges.
    /// </summary>
    public static Texture2D Backdrop => _backdrop ??= new GradientTexture2D
    {
        Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.42f), FillTo = new Vector2(1.05f, 1.0f),
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.55f, 1f },
            Colors = new[] { new Color(0.44f, 0.54f, 0.56f), new Color(0.21f, 0.28f, 0.30f), new Color(0.08f, 0.11f, 0.12f) },
        },
    };

    /// <summary>Forgets the pictures already looked up, so newly made portraits show.</summary>
    public static void ForgetPictures() { Pictures.Clear(); ModelMade.Clear(); FdgRaylib.Models3D.UnitPortraits.Forget(); }

    /// <summary>A framed unit picture: the Army Forge portrait if downloaded, else <paramref name="fallbackIcon"/>.</summary>
    /// <remarks><paramref name="look"/> is the look picked for the unit in the army builder (its first picked model's), so
    /// the portrait shows the same version that stands on the table.</remarks>
    public static Control Portrait(string faction, string unit, string fallbackIcon, float size, Color team, float alpha = 1f, string? look = null,
        FDG.PlayerID? owner = null)
        => Portrait(UnitPicture(faction, unit, look, owner), fallbackIcon, size, team, alpha);

    /// <summary>The look a battle unit's portrait shows: the first one picked for any of its models.</summary>
    public static string? LookOf(FDG.IUnit u) => FdgRaylib.Models3D.UnitPortraits.LookOf(u.Models.Select(m => m.Look));

    /// <summary>A framed picture (any texture - the Asset Lab previews a new portrait with this), else the icon.</summary>
    public static Control Portrait(Texture2D? picture, string fallbackIcon, float size, Color team, float alpha = 1f)
    {
        Control inner;
        if (picture is { } tex)
        {
            inner = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(size, size), SelfModulate = Colors.White with { A = alpha }, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            if (IsModelMade(tex))
            {
                // The backdrop behind a model photographed on a clear background (Kai, 2026-11-14).
                var stack = new Control { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
                var back = new TextureRect { Texture = Backdrop, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                    SelfModulate = Colors.White with { A = alpha }, MouseFilter = Control.MouseFilterEnum.Ignore };
                back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                stack.AddChild(back);
                inner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                stack.AddChild(inner);
                inner = stack;
            }
        }
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

    // ---- Picture banners (Kai, 2026-11-14, mockup option C: "c, 100, 70, 220, square")

    /// <summary>
    /// How big unit pictures are drawn, in pixels on a 1080p screen - Kai's picks from the portrait mockup. A banner is
    /// this tall and 1.7x as wide (it fades out under the text); the unit card's banner runs the card's full width.
    /// </summary>
    public static class PictureSize
    {
        /// <summary>Settings -> Display -> Card size (50-150 %), as a factor.</summary>
        public static float Scale => Math.Clamp(FdgRaylib.Config.UserConfig.Current.View.CardSize, 50, 150) / 100f;
        /// <summary>Army Forge card picture height: 100 px before 2026-11-17; Kai wanted about 1.5x as many cards on screen.</summary>
        public static float ForgeCard => 68 * Scale;
        /// <summary>Lobby army-list portraits (Kai: larger - they were 44).</summary>
        public static float LobbyList => 64 * Scale;
        public const float ArmyList = 70;
        public const float UnitCard = 220;
        /// <summary>The same card shown as a hover / pinned tip over the table - smaller, so it doesn't cover the fight.</summary>
        public const float UnitTip = 140;
        public const float BannerAspect = 1.7f;
    }

    private static Shader? _fade;

    // The picture fades to nothing towards its right edge (or bottom). Measured in the control's own pixels, not the
    // texture's, so the crop "cover" makes doesn't move the fade.
    private static Shader Fade => _fade ??= new Shader
    {
        Code = """
            shader_type canvas_item;
            uniform vec2 size = vec2(100.0, 100.0);
            uniform bool down = false;
            uniform float from = 0.5;
            uniform float alpha = 1.0;
            varying vec2 local;
            void vertex() { local = VERTEX; }
            void fragment() {
                vec4 c = texture(TEXTURE, UV) * COLOR;
                float t = down ? local.y / max(size.y, 1.0) : local.x / max(size.x, 1.0);
                c.a *= alpha * (1.0 - smoothstep(from, 1.0, t));
                COLOR = c;
            }
            """,
    };

    /// <summary>
    /// A unit's picture as a banner: <paramref name="w"/> x <paramref name="h"/>, cropped to fill, fading out to the
    /// right (or downwards) so text can sit over its end. Without a picture: the kind icon on a plain tile. A width of 0
    /// fills whatever space the container gives it.
    /// </summary>
    public static Control Banner(string faction, string unit, string fallbackIcon, float w, float h, Color team, float alpha = 1f, string? look = null, bool down = false,
        FDG.PlayerID? owner = null)
        => Banner(UnitPicture(faction, unit, look, owner), fallbackIcon, w, h, team, alpha, down, FramingFor(faction, unit, owner));

    /// <summary><paramref name="frame"/>: where the picture is centred and how far it is zoomed (the Asset Lab's picture
    /// framing, Kai 2026-11-14); null = centred, just filling the space.</summary>
    public static Control Banner(Texture2D? picture, string fallbackIcon, float w, float h, Color team, float alpha = 1f, bool down = false,
        FdgRaylib.Models3D.PictureFraming.Frame? frame = null)
    {
        if (picture == null)
        {
            var tile = new CenterContainer { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
            tile.AddChild(Icon(fallbackIcon, Mathf.Min(w > 0 ? w : h, h) * 0.6f, team with { A = alpha * 0.9f }));
            return tile;
        }
        var mat = new ShaderMaterial { Shader = Fade };
        mat.SetShaderParameter("down", down);
        mat.SetShaderParameter("from", down ? 0.62f : 0.5f);
        mat.SetShaderParameter("alpha", alpha);
        mat.SetShaderParameter("size", new Vector2(w, h));
        // The picture's crop is worked out here (not left to "cover"), so the framing can move and zoom it.
        var crop = new AtlasTexture { Atlas = picture };
        var f = frame ?? FdgRaylib.Models3D.PictureFraming.Frame.Default;
        void Fit(Vector2 size)
        {
            if (size.X < 1 || size.Y < 1) return;
            var (x, y, rw, rh) = FdgRaylib.Models3D.PictureFraming.Region(picture.GetWidth(), picture.GetHeight(), size.X / size.Y, f);
            crop.Region = new Rect2(x, y, rw, rh);
        }
        Fit(new Vector2(w > 0 ? w : h, h));
        var tr = new TextureRect
        {
            Texture = crop, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(w, h), ClipContents = true, Material = mat, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        tr.Resized += () => { mat.SetShaderParameter("size", tr.Size); Fit(tr.Size); };
        if (!IsModelMade(picture)) return tr;

        // A model photographed on a clear background: the backdrop behind it, fading out the same way.
        var holder = new Control { CustomMinimumSize = new Vector2(w, h), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        var backMat = (ShaderMaterial)mat.Duplicate();
        var back = new TextureRect { Texture = Backdrop, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Material = backMat, MouseFilter = Control.MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        back.Resized += () => backMat.SetShaderParameter("size", back.Size);
        holder.AddChild(back);
        tr.CustomMinimumSize = Vector2.Zero;
        tr.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(tr);
        return holder;
    }

    /// <summary>Text that stays readable over a picture (a dark outline).</summary>
    /// <summary>
    /// Shrinks a one-line label's letters, a size at a time, until its text fits <paramref name="width"/> (never below
    /// <paramref name="minSize"/>). For a name that has to stay on one line - a unit's name on its card and in the army
    /// list: a long one ("Elite Weapon Masters") was simply cut off ("ELITE WEAPON MA").
    /// </summary>
    public static Label FitText(Label l, float width, int minSize = 12)
    {
        Font font = l.GetThemeFont("font");
        int size = l.GetThemeFontSize("font_size");
        if (font == null || size <= 0) return l;
        while (size > minSize && font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, size).X > width) size--;
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    /// <summary>How wide <paramref name="s"/> is in a label made by <see cref="Text"/> with that font and size.</summary>
    public static float TextWidth(string s, int size, Font? font = null)
    {
        if (Looks.Current.HeadingScale != 1f && IsHeadingFont(font)) size = Mathf.RoundToInt(size * Looks.Current.HeadingScale);
        return (font ?? Medium).GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
    }

    public static Label OverPicture(Label l)
    {
        l.AddThemeConstantOverride("outline_size", 5);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        return l;
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
