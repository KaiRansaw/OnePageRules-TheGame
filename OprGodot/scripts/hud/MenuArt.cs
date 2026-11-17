using FDG;
using FdgRaylib.Pictures;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// Kai's menu pictures on screen (2026-11-16): loads the picture set for a slot (<see cref="MenuPictures"/>) and draws it
/// cropped around its focus point (<see cref="PictureView"/>). When no picture is set, an army falls back to a banner of
/// its first unit pictures over a colour of its own; backdrops and game tiles fall back to a plain gradient in their look.
/// </summary>
public static class MenuArt
{
    private static readonly Dictionary<string, Texture2D?> Cache = new();
    private static int _version = -1;

    /// <summary>The slot's picture, or null when none is set (or it can't be read).</summary>
    public static Texture2D? Texture(MenuPictures.Slot slot)
    {
        if (_version != MenuPictures.Version) { Cache.Clear(); _version = MenuPictures.Version; }
        if (MenuPictures.Get(slot) is not { } e) return null;
        string path = MenuPictures.PathOf(e);
        if (Cache.TryGetValue(path, out Texture2D? hit)) return hit;
        Texture2D? tex = null;
        var img = new Image();
        if (img.Load(path) == Error.Ok) tex = ImageTexture.CreateFromImage(img);
        Cache[path] = tex;
        return tex;
    }

    /// <summary>A colour that belongs to an army (same every time), for its plain tile.</summary>
    public static Color ArmyHue(string army)
    {
        uint h = 2166136261;
        foreach (char c in army) h = (h ^ c) * 16777619;
        return Color.FromHsv(h % 360 / 360f, 0.45f, 0.30f);
    }

    /// <summary>
    /// The picture for a slot filling <paramref name="size"/>, cropped around its focus point. No picture: an army gets
    /// its unit-picture banner (or a plain tile in its colour), anything else a gradient in the current look.
    /// </summary>
    public static Control View(MenuPictures.Slot slot, EGameSystem? unitsFrom = null)
    {
        Texture2D? tex = Texture(slot);
        if (tex != null)
        {
            var e = MenuPictures.Get(slot)!;
            return new PictureView { Picture = tex, FocusX = e.FocusX, FocusY = e.FocusY, Zoom = e.Zoom };
        }
        if (slot.Kind == MenuPictures.EKind.Army && unitsFrom is { } system)
            return UnitBanner(system, slot.Army!);
        return Fallback(slot.Kind == MenuPictures.EKind.Army ? ArmyHue(slot.Army!) : Palette.AccentDim);
    }

    /// <summary>A soft gradient in a colour - what a slot with no picture shows.</summary>
    public static Control Fallback(Color c)
    {
        var r = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { c.Lightened(0.15f), c.Darkened(0.55f) }, Offsets = new[] { 0f, 1f } },
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.7f, 0.3f), FillTo = new Vector2(0.1f, 1.1f), Width = 128, Height = 128,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        return r;
    }

    /// <summary>
    /// Made-from-units art for an army with no picture of Kai's (round 1, item "Tile art"): its first unit pictures side
    /// by side on the right, over its own colour. An army whose units have no pictures gets the plain colour.
    /// </summary>
    public static Control UnitBanner(EGameSystem system, string army)
    {
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        var bg = Fallback(ArmyHue(army));
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(bg);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", 0);
        holder.AddChild(row);
        var book = BookIndex.Books(system).FirstOrDefault(b => b.Name == army);
        int shown = 0;
        foreach (string unit in book?.Units ?? (IReadOnlyList<string>)Array.Empty<string>())
        {
            if (shown >= 3) break;
            Texture2D? pic = HudKit.UnitPicture(army, unit);
            if (pic == null) continue;
            row.AddChild(new TextureRect
            {
                Texture = pic, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                CustomMinimumSize = new Vector2(10, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            shown++;
        }
        if (shown > 0) row.AnchorLeft = 1f - 0.2f * shown;
        return holder;
    }
}

/// <summary>
/// Draws a picture covering the whole control, cropped around a focus point and zoomed - the same rule the picture
/// manager's previews show, so a picture framed once looks right in every shape it's used.
/// </summary>
public partial class PictureView : Control
{
    private Texture2D? _picture;
    public Texture2D? Picture { get => _picture; set { _picture = value; QueueRedraw(); } }
    private float _fx = 0.5f, _fy = 0.5f, _zoom = 1f;
    public float FocusX { get => _fx; set { _fx = value; QueueRedraw(); } }
    public float FocusY { get => _fy; set { _fy = value; QueueRedraw(); } }
    public float Zoom { get => _zoom; set { _zoom = value; QueueRedraw(); } }
    /// <summary>Darkens the picture (0 = as is) - a backdrop behind tiles.</summary>
    public float Dim { get; set; }

    public PictureView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        Resized += QueueRedraw;
    }

    /// <summary>The part of the picture (in its pixels) that fills a box of <paramref name="box"/> size.</summary>
    public static Rect2 Crop(Vector2 picture, Vector2 box, float fx, float fy, float zoom)
    {
        if (picture.X <= 0 || picture.Y <= 0 || box.X <= 0 || box.Y <= 0) return new Rect2(Vector2.Zero, picture);
        float scale = Math.Max(box.X / picture.X, box.Y / picture.Y) * Math.Max(1f, zoom);
        Vector2 size = box / scale;
        Vector2 at = new(fx * picture.X - size.X / 2, fy * picture.Y - size.Y / 2);
        at.X = Math.Clamp(at.X, 0, Math.Max(0, picture.X - size.X));
        at.Y = Math.Clamp(at.Y, 0, Math.Max(0, picture.Y - size.Y));
        return new Rect2(at, size);
    }

    public override void _Draw()
    {
        if (_picture == null) return;
        Rect2 src = Crop(_picture.GetSize(), Size, _fx, _fy, _zoom);
        DrawTextureRectRegion(_picture, new Rect2(Vector2.Zero, Size), src);
        if (Dim > 0) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, Dim));
    }
}
