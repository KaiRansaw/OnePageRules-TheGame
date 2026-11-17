using System.Globalization;
using System.Text;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// Screens Kai can lay out himself in the Godot editor (2026-11-17: "make the screens editable in Godot" - the battle
/// screen, lobby, Army Forge, main menu and game screen). Their contents are built from game data in code, so what the
/// editor holds is each screen's <b>frame</b>: a <c>Frame</c> node in the screen's .tscn with one labelled box per panel
/// (a ReferenceRect named after the panel - "UnitCard", "ArmyList"...). Move or resize a box in the editor and the panel
/// sits there in the game; the code fills it and never moves it. A panel's box is its size; a panel whose content needs
/// more room grows past the box in the box's grow direction (set in the editor's Layout -> Grow Direction).
///
/// <para>
/// A screen without its box (or the whole Frame) falls back to the placement written in code, so a deleted box can't
/// lose a panel. <c>--press=...,framegen:&lt;file&gt;</c> (ScriptedRun) writes the boxes for the screen on display, from
/// where the code put its panels - that is how each screen's frame was first made, matching the screen as it was.
/// </para>
/// </summary>
public static class ScreenFrame
{
    public const string FrameNode = "Frame";

    /// <summary>Where a box hangs from: its left/top/right/bottom anchors (0 = left/top edge of the screen, 1 = right/bottom).</summary>
    public readonly record struct Anchors(float Left, float Top, float Right, float Bottom)
    {
        public static Anchors Of(Control.LayoutPreset p) { (float x, float y) = Anchor(p); return new(x, y, x, y); }
    }

    private sealed record Placed(Control Screen, string Name, Control Panel, Anchors At);
    private static readonly List<Placed> Fallbacks = new();

    /// <summary>The screen's box for a panel, if its scene has one.</summary>
    public static Control? Slot(Control screen, string name) => screen.GetNodeOrNull<Control>($"{FrameNode}/{name}");

    /// <summary>Is this screen laid out from its frame (has a Frame node)?</summary>
    public static bool Framed(Control screen) => screen.GetNodeOrNull<Control>(FrameNode) != null;

    /// <summary>
    /// Puts a panel in its box from the screen's frame, or - with no box - where the code says (<paramref name="preset"/>
    /// + <paramref name="offset"/> on <paramref name="fallbackParent"/>, as <see cref="HudKit.Place{T}"/>).
    /// </summary>
    public static T Place<T>(Control screen, string name, T panel, Control.LayoutPreset preset, Vector2 offset, Control? fallbackParent = null)
        where T : Control
    {
        if (Slot(screen, name) is { } slot)
        {
            Prepare(screen);
            slot.AddChild(panel);
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            panel.GrowHorizontal = slot.GrowHorizontal;
            panel.GrowVertical = slot.GrowVertical;
            return panel;
        }
        Fallbacks.Add(new Placed(screen, name, panel, Anchors.Of(preset)));
        return HudKit.Place(fallbackParent ?? screen, panel, preset, offset);
    }

    /// <summary>
    /// For a screen laid out by containers in code: puts the panel in its box when the frame has one (true), otherwise
    /// leaves it to the caller (false) and remembers it with the anchors its box should get, for <see cref="Generate"/>.
    /// </summary>
    public static bool TryPlace(Control screen, string name, Control panel, Anchors at)
    {
        if (Slot(screen, name) is { } slot)
        {
            Prepare(screen);
            panel.GetParent()?.RemoveChild(panel);
            slot.AddChild(panel);
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            panel.GrowHorizontal = slot.GrowHorizontal;
            panel.GrowVertical = slot.GrowVertical;
            return true;
        }
        Fallbacks.Add(new Placed(screen, name, panel, at));
        return false;
    }

    /// <summary>The frame only places: it never takes clicks, and its editor labels don't show in the game.</summary>
    private static void Prepare(Control screen)
    {
        if (screen.GetNodeOrNull<Control>(FrameNode) is not { } frame || frame.HasMeta("ready")) return;
        frame.SetMeta("ready", true);
        frame.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (Node n in frame.GetChildren())
            if (n is Control slot)
            {
                slot.MouseFilter = Control.MouseFilterEnum.Ignore;
                foreach (Node c in slot.GetChildren()) if (c is Label l && l.Name == "Label") l.Visible = false;
            }
    }

    /// <summary>
    /// Writes the Frame nodes (.tscn text) for a screen from where its panels are right now - the starting frame for a
    /// screen that has none. Anchors follow the preset each panel was placed with, so boxes keep to their corner when
    /// the window size changes.
    /// </summary>
    public static string Generate(Control screen)
    {
        var sb = new StringBuilder();
        string root = ".";
        sb.Append($"[node name=\"{FrameNode}\" type=\"Control\" parent=\"{root}\"]\nlayout_mode = 3\nanchors_preset = 15\nanchor_right = 1.0\nanchor_bottom = 1.0\ngrow_horizontal = 2\ngrow_vertical = 2\nmouse_filter = 2\n\n");
        Vector2 view = screen.GetViewportRect().Size;
        foreach (var p in Fallbacks.Where(f => f.Screen == screen && GodotObject.IsInstanceValid(f.Panel)))
        {
            Rect2 r = p.Panel.GetGlobalRect();
            Anchors a = p.At;
            float l = r.Position.X - a.Left * view.X, t = r.Position.Y - a.Top * view.Y;
            float rr = r.End.X - a.Right * view.X, b = r.End.Y - a.Bottom * view.Y;
            int G(float lo, float hi) => lo != hi ? 1 : lo == 0 ? 1 : lo == 1 ? 0 : 2;
            int gh = G(a.Left, a.Right), gv = G(a.Top, a.Bottom);
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            sb.Append($"[node name=\"{p.Name}\" type=\"ReferenceRect\" parent=\"{FrameNode}\"]\nlayout_mode = 1\n");
            sb.Append($"anchor_left = {F(a.Left)}\nanchor_top = {F(a.Top)}\nanchor_right = {F(a.Right)}\nanchor_bottom = {F(a.Bottom)}\n");
            sb.Append($"offset_left = {F(l)}\noffset_top = {F(t)}\noffset_right = {F(rr)}\noffset_bottom = {F(b)}\n");
            sb.Append($"grow_horizontal = {gh}\ngrow_vertical = {gv}\nmouse_filter = 2\nborder_color = Color(0.4, 0.9, 0.8, 1)\nborder_width = 2.0\n\n");
            sb.Append($"[node name=\"Label\" type=\"Label\" parent=\"{FrameNode}/{p.Name}\"]\nlayout_mode = 0\noffset_left = 6.0\noffset_top = 4.0\noffset_right = 300.0\noffset_bottom = 30.0\ntext = \"{p.Name}\"\n\n");
        }
        return sb.ToString();
    }

    private static (float, float) Anchor(Control.LayoutPreset p) => p switch
    {
        Control.LayoutPreset.TopLeft => (0, 0),
        Control.LayoutPreset.CenterTop => (0.5f, 0),
        Control.LayoutPreset.TopRight => (1, 0),
        Control.LayoutPreset.CenterLeft => (0, 0.5f),
        Control.LayoutPreset.Center => (0.5f, 0.5f),
        Control.LayoutPreset.CenterRight => (1, 0.5f),
        Control.LayoutPreset.BottomLeft => (0, 1),
        Control.LayoutPreset.CenterBottom => (0.5f, 1),
        Control.LayoutPreset.BottomRight => (1, 1),
        _ => (0, 0),
    };

    /// <summary>Grow direction as .tscn numbers: 0 = towards the start (left / up), 1 = end, 2 = both.</summary>
    private static (int, int) Grow(Control.LayoutPreset p)
    {
        (float ax, float ay) = Anchor(p);
        int G(float a) => a == 0 ? 1 : a == 1 ? 0 : 2;
        return (G(ax), G(ay));
    }
}
