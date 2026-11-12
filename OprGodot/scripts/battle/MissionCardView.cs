using FDG.Missions;
using Godot;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// A mission card drawn the way Kai picked on 2026-11-11 (design C6 on the "Mission Card Designs" page): a cut-corner
/// panel edged in the card's family colour, a coloured stripe down the left with the card number, the family's
/// picture and its name, then the title and each objective as a row with its VP in a gold box on the right. The
/// objectives are the engine's own wording (MissionCardCatalog, from the Mission Cards v3.6.0 books); the text
/// shrinks until it all fits on the card.
/// </summary>
public static class MissionCardView
{
    // Family colours from the C6 mockup: Seize green, Hold blue, Position orange, Destroy red, Targets purple.
    public static Color Family(int n) => n < 30 ? new Color("2e8b57") : n < 40 ? new Color("2a62b0")
        : n < 50 ? new Color("e08a1e") : n < 60 ? new Color("c8382b") : new Color("7442a8");

    public static string FamilyName(int n) => n < 30 ? "SEIZE" : n < 40 ? "HOLD" : n < 50 ? "POSITION" : n < 60 ? "DESTROY" : "TARGETS";

    public static string FamilyIcon(int n) => n < 30 ? "flag-objective" : n < 40 ? "shield-reflect" : n < 50 ? "position-marker"
        : n < 60 ? n switch { 51 => "crosshair", 55 => "broken-shield", 56 => "magic-swirl", _ => "crossed-swords" }
        : n switch { 61 => "skull-crack", 63 => "flying-flag", _ => "targeting" };

    public static readonly Color StampRed = new("ec6a5c"), StampGold = new("f0c050"), StampDim = new("3a5258");

    /// <summary>How one card is drawn: its size, lit (picked), greyed, the tag on it, hover text.</summary>
    public sealed record Look(float Width, float Height, bool Lit = false, bool Dim = false, string? Stamp = null,
        Color? StampColour = null, string? Tooltip = null);

    private static readonly Color Face = new(0.086f, 0.141f, 0.153f, 0.98f);

    /// <summary>A face-up card. <paramref name="click"/> null = not clickable (context only).</summary>
    public static Button Card(MissionCardDef card, Look look, Action? click)
    {
        int n = card.Number;
        Color fam = Family(n);
        float w = look.Width, h = look.Height;
        Button b = Frame(look, fam, click);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = 3; row.OffsetTop = 3; row.OffsetRight = -3; row.OffsetBottom = -3;
        row.AddThemeConstantOverride("separation", 0);
        b.AddChild(row);

        // The stripe: number, family picture, family name up the side.
        float sw = Mathf.Round(w * 0.16f);
        var stripe = new PanelContainer { CustomMinimumSize = new Vector2(sw, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        stripe.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = fam.Darkened(0.15f), ContentMarginTop = Mathf.Round(h * 0.05f), ContentMarginBottom = 6 });
        var sv = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        sv.AddThemeConstantOverride("separation", 4);
        sv.AddChild(HudKit.Text(n.ToString(), Mathf.RoundToInt(w / 11f), Colors.White, HudKit.Exo(800), HorizontalAlignment.Center));
        var ic = HudKit.Icon(FamilyIcon(n), sw * 0.62f, Colors.White);
        ic.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        sv.AddChild(ic);
        sv.AddChild(HudKit.Grow());
        sv.AddChild(Vertical(FamilyName(n), Mathf.Max(8, Mathf.RoundToInt(w / 21f)), sw));
        stripe.AddChild(sv);
        row.AddChild(stripe);

        // The title and the objectives, fitted to the card.
        float mw = w - sw - 8;
        var main = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        main.AddThemeConstantOverride("separation", 0);
        row.AddChild(main);

        Font titleFont = HudKit.Exo(800), textFont = HudKit.Semi;
        float vpw = Mathf.Round(w * 0.15f);
        float textW = mw - vpw - 10;
        int titleSize = FitWords(card.Title.ToUpperInvariant(), titleFont, Mathf.RoundToInt(w / 11.5f), mw - 12);
        float titleH = titleFont.GetMultilineStringSize(card.Title.ToUpperInvariant(), HorizontalAlignment.Left, mw - 12, titleSize).Y + 12;
        int textSize = Mathf.RoundToInt(w / 11f);
        float Need(int s) => titleH + card.Conditions.Sum(c => Mathf.Max(textFont.GetMultilineStringSize(c.Text, HorizontalAlignment.Left, textW, s).Y, s * 1.9f) + 8)
            + (card.Note is { } note ? HudKit.Medium.GetMultilineStringSize(note, HorizontalAlignment.Left, mw - 10, Mathf.Max(8, s - 2)).Y + 8 : 0);
        // Measured text runs a little short of what the labels draw (line spacing), so leave a margin.
        while (textSize > 8 && Need(textSize) > (h - 12) * 0.86f) textSize--;

        var title = HudKit.Text(card.Title.ToUpperInvariant(), titleSize, fam.Lerp(Colors.White, 0.6f), titleFont);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.CustomMinimumSize = new Vector2(mw - 12, 0);
        main.AddChild(HudKit.Pad(title, 7, 5, 5, 5));
        main.AddChild(new ColorRect { Color = fam with { A = 0.6f }, CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore });

        foreach (MissionCondition c in card.Conditions)
        {
            var cr = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            cr.AddThemeConstantOverride("separation", 0);
            var t = HudKit.Text(c.Text, textSize, HudKit.Fg, textFont);
            t.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            t.CustomMinimumSize = new Vector2(textW, 0);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            t.VerticalAlignment = VerticalAlignment.Center;
            cr.AddChild(HudKit.Pad(t, 7, 4, 3, 4));
            var vp = new PanelContainer { CustomMinimumSize = new Vector2(vpw, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            vp.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.94f, 0.75f, 0.31f, 0.09f), BorderColor = new Color(1, 1, 1, 0.08f), BorderWidthLeft = 1 });
            var vv = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            vv.AddThemeConstantOverride("separation", -4);
            vv.AddChild(HudKit.Text(c.Vp.ToString(), Mathf.RoundToInt(textSize * 1.35f), HudKit.Gold, HudKit.Exo(800), HorizontalAlignment.Center));
            vv.AddChild(HudKit.Text("VP", Mathf.Max(7, textSize - 4), HudKit.Dim, HudKit.Bold, HorizontalAlignment.Center));
            vp.AddChild(vv);
            cr.AddChild(vp);
            main.AddChild(cr);
            main.AddChild(new ColorRect { Color = new Color(1, 1, 1, 0.07f), CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        if (card.Note is { } cardNote)
        {
            var nl = HudKit.Text(cardNote, Mathf.Max(8, textSize - 2), HudKit.Dim, HudKit.Medium);
            nl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            nl.CustomMinimumSize = new Vector2(mw - 10, 0);
            main.AddChild(HudKit.Pad(nl, 7, 4, 3, 4));
        }
        AddStamp(b, look);
        return b;
    }

    /// <summary>A card lying face down ("Hidden card 1" - only its owner knows what it is).</summary>
    public static Button FaceDown(string label, Look look, Action? click)
    {
        Button b = Frame(look, StampDim, click, new Color(0.10f, 0.16f, 0.18f, 0.98f));
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 8);
        var ic = HudKit.Icon("radar-sweep", look.Width * 0.3f, HudKit.Dim with { A = 0.5f });
        ic.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(ic);
        col.AddChild(HudKit.Text(label.ToUpperInvariant(), Mathf.RoundToInt(look.Width / 13f), HudKit.Dim, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center));
        b.AddChild(col);
        AddStamp(b, look);
        return b;
    }

    /// <summary>An empty place on the table where a card will go.</summary>
    public static Control EmptySlot(float w, float h)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new ChamferBox { Fill = new Color(0, 0, 0, 0.25f), Border = HudKit.Dim with { A = 0.45f }, Cut = w * 0.07f });
        p.AddChild(HudKit.Text("TABLE SLOT", Mathf.RoundToInt(w / 13f), HudKit.Dim with { A = 0.7f }, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center)
            .With(l => l.VerticalAlignment = VerticalAlignment.Center));
        return p;
    }

    private static Button Frame(Look look, Color edge, Action? click, Color? fill = null)
    {
        var b = new Button
        {
            CustomMinimumSize = new Vector2(look.Width, look.Height), FocusMode = Control.FocusModeEnum.None,
            TooltipText = look.Tooltip ?? "", MouseDefaultCursorShape = click != null ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow,
            MouseFilter = click != null || look.Tooltip != null ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore,
        };
        var box = new ChamferBox { Fill = fill ?? Face, Border = edge, BorderWidth = 2, Cut = Mathf.Round(look.Width * 0.07f), Corners = ChamferBox.ECorners.Diagonal };
        if (look.Lit) box = box.With(x => { x.Border = HudKit.Lime; x.BorderWidth = 3; x.Glow = HudKit.Lime with { A = 0.6f }; x.GlowWidth = 10; });
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", click != null ? box.With(x => { x.Border = look.Lit ? HudKit.Lime : x.Border.Lightened(0.35f); x.BorderWidth = 3; }) : box);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("disabled", box);
        if (look.Dim) b.Modulate = new Color(1, 1, 1, 0.4f);
        if (click != null) b.Pressed += click;
        return b;
    }

    private static void AddStamp(Button b, Look look)
    {
        if (look.Stamp is not { } text) return;
        Color c = look.StampColour ?? HudKit.Lime;
        var tag = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tag.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = c, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 2, ContentMarginBottom = 2,
        });
        Color ink = c == StampRed || c == StampDim ? Colors.White : new Color(0.08f, 0.1f, 0.04f);
        tag.AddChild(HudKit.Text(text, Mathf.Max(11, Mathf.RoundToInt(look.Width / 13f)), ink, HudKit.Tracked(HudKit.Bold, 2), HorizontalAlignment.Center));
        tag.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        tag.GrowHorizontal = Control.GrowDirection.Both;
        tag.GrowVertical = Control.GrowDirection.Begin;
        tag.OffsetBottom = -8;
        b.AddChild(tag);
    }

    /// <summary>A label turned to read up the stripe.</summary>
    private static Control Vertical(string text, int size, float width)
    {
        Font f = HudKit.Tracked(HudKit.Bold, 3);
        float len = f.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var holder = new Control { CustomMinimumSize = new Vector2(width, len + 4), MouseFilter = Control.MouseFilterEnum.Ignore };
        var l = HudKit.Text(text, size, new Color(1, 1, 1, 0.85f), f);
        l.Rotation = -Mathf.Pi / 2;
        l.Position = new Vector2((width - size * 1.3f) / 2, len + 2);
        holder.AddChild(l);
        return holder;
    }

    /// <summary>The largest size (down from <paramref name="start"/>) at which every word fits the width (no word broken).</summary>
    private static int FitWords(string text, Font f, int start, float width)
    {
        int s = start;
        while (s > 8 && text.Split(' ').Any(word => f.GetStringSize(word, HorizontalAlignment.Left, -1, s).X > width)) s--;
        return s;
    }
}
