using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// One game family on the main menu (Kai, 2026-11-17, "Main Menu Mockups" option B): a framed card with Kai's backdrop
/// picture shown WHOLE (the box takes the picture's own shape - nothing is cut off), the family's name and its three
/// games under it, all in the family's own look. Laid out in scenes/screens/GameCard.tscn, so it can be rearranged in
/// the Godot editor; this script only fills it in and styles it.
/// </summary>
public partial class GameCard : Button
{
    [Export] public GameScreen.EFamily Family { get; set; }

    private AspectRatioContainer _frame = null!;
    private Control _art = null!;

    public override void _Ready()
    {
        _frame = GetNode<AspectRatioContainer>("%PictureFrame");
        _art = GetNode<Control>("%Art");
        var name = GetNode<Label>("%FamilyName");
        var games = GetNode<Label>("%Games");
        bool gf = Family == GameScreen.EFamily.GrimdarkFuture;
        name.Text = GameScreen.FamilyName(Family).ToUpperInvariant();
        games.Text = gf ? "Grimdark Future  -  Firefight  -  Star Quest" : "Age of Fantasy  -  Skirmish  -  Quest";
        TooltipText = gf
            ? "Sci-fi: Grimdark Future, GF: Firefight and GF: Star Quest."
            : "Fantasy: Age of Fantasy, AoF: Skirmish and AoF: Quest.";

        // Everything on the card is made in the family's look (frame art, colours, fonts), whatever look the menu is in.
        Looks.With(Looks.For(Family), () =>
        {
            var box = new ChamferBox { Fill = HudKit.Glass, Border = HudKit.Edge, Cut = 14, ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 0, ContentMarginBottom = 0 };
            AddThemeStyleboxOverride("normal", box);
            AddThemeStyleboxOverride("hover", box.With(x => { x.Border = HudKit.EdgeHot; x.Fill = x.Fill.Lightened(0.06f); x.Glow = HudKit.EdgeHot with { A = 0.45f }; }));
            AddThemeStyleboxOverride("pressed", box.With(x => x.Border = HudKit.Lime));
            AddThemeStyleboxOverride("focus", box.With(x => x.Border = HudKit.Lime));
            name.AddThemeFontOverride("font", HudKit.Exo(800));
            name.AddThemeFontSizeOverride("font_size", gf ? 52 : 44);
            name.AddThemeColorOverride("font_color", HudKit.Fg);
            games.AddThemeFontOverride("font", HudKit.Semi);
            games.AddThemeFontSizeOverride("font_size", 22);
            games.AddThemeColorOverride("font_color", HudKit.Lime);
            return 0;
        });
        Refresh();
        // The card hugs its picture: the picture takes the card's width at its own shape, unless that would not fit the
        // row's height - then it shrinks to fit (and sits centred).
        Resized += Fit;
        if (GetParent() is Control row) row.Resized += Fit;
        Resized += () => { if (GetParent() is Control r) foreach (GameCard c in r.GetChildren().OfType<GameCard>()) if (c != this && c._frame != null) c.Fit(); };
        Pressed += () => App.I.Open<GameScreen>(Scenes.GameScreen, s => s.Family = Family);
    }

    private void Fit()
    {
        if (GetParent() is not Control row || _frame.Ratio <= 0) return;
        var margin = GetNode<MarginContainer>("Margin");
        float sides = margin.GetThemeConstant("margin_left") + margin.GetThemeConstant("margin_right");
        float text = GetNode<Control>("%FamilyName").GetCombinedMinimumSize().Y + GetNode<Control>("%Games").GetCombinedMinimumSize().Y
                     + 2 * GetNode<VBoxContainer>("Margin/Column").GetThemeConstant("separation")
                     + margin.GetThemeConstant("margin_top") + margin.GetThemeConstant("margin_bottom");
        // Cards side by side share one height (the shortest any of them needs), so the row lines up; a picture of a
        // different shape keeps its shape and sits centred in its card.
        float want = row.GetChildren().OfType<GameCard>().Where(c => c.Visible && c._frame != null && c._frame.Ratio > 0)
            .Select(c => (c.Size.X - sides) / c._frame.Ratio).DefaultIfEmpty((Size.X - sides) / _frame.Ratio).Min();
        float h = Mathf.Floor(Mathf.Clamp(Mathf.Min(want, row.Size.Y - text), 80, 4000));
        if (!Mathf.IsEqualApprox(_frame.CustomMinimumSize.Y, h)) _frame.CustomMinimumSize = new Vector2(0, h);
        // A button does not grow to fit what is inside it, so the card's height is set here: picture + name + games.
        var card = new Vector2(CustomMinimumSize.X, Mathf.Ceil(h + text));
        if (!Mathf.IsEqualApprox(CustomMinimumSize.Y, card.Y)) CustomMinimumSize = card;
    }

    /// <summary>Shows the family's backdrop picture (Asset Lab -> Your pictures), or its colour and a hint while none is set.</summary>
    public void Refresh()
    {
        foreach (Node n in _art.GetChildren()) { _art.RemoveChild(n); n.QueueFree(); }
        var slot = FdgRaylib.Pictures.MenuPictures.Backdrop(Family == GameScreen.EFamily.AgeOfFantasy
            ? FdgRaylib.Pictures.MenuPictures.EFamily.AgeOfFantasy : FdgRaylib.Pictures.MenuPictures.EFamily.GrimdarkFuture);
        Control shown;
        if (MenuArt.Texture(slot) is { } tex)
        {
            // The frame takes the picture's own shape, so the whole picture shows.
            Vector2 size = tex.GetSize();
            _frame.Ratio = size.Y > 0 ? size.X / size.Y : 16f / 9f;
            shown = new PictureView { Picture = tex };
        }
        else
        {
            _frame.Ratio = 16f / 9f;
            Color wash = Looks.Get(Looks.For(Family)).Accent;
            var holder = new ColorRect { Color = wash with { A = 0.45f } };
            var hint = HudKit.Text("Set a picture in Asset Lab -> Your pictures", 18, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center);
            hint.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            hint.GrowHorizontal = GrowDirection.Both; hint.GrowVertical = GrowDirection.Both;
            hint.MouseFilter = MouseFilterEnum.Ignore;
            holder.AddChild(hint);
            shown = holder;
        }
        shown.MouseFilter = MouseFilterEnum.Ignore;
        shown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _art.AddChild(shown);
        if (IsInsideTree() && GetParent() is Control row)
            foreach (GameCard c in row.GetChildren().OfType<GameCard>()) if (c._frame != null) c.Fit();
    }
}
