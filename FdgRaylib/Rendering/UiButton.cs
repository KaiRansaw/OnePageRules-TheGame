using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// ImGui.Button / Checkbox wrappers that voice a <see cref="UiSound"/> cue on click, so menu screens get
/// consistent audio feedback without every call site repeating the play call. Each method returns the same
/// bool the underlying ImGui call does; the cue fires only on the frame it returns true.
///
/// <para>Pick the method by ACTION CLASS, not by widget: <see cref="Navigate"/> for neutral menu
/// navigation, <see cref="Confirm"/> for a positive/forward commit (Launch, Connect, Create), <see
/// cref="Back"/> for back/cancel. In-game resolver panels are voiced centrally in
/// <c>ResolverButtons</c> instead.</para>
///
/// <para>#398: the class now COLOURS the button as well as voicing it - neutral grey undoes, muted blue
/// goes somewhere, the accent commits (<c>ImGuiTheme.ButtonBack/Go/Commit</c>). Every call site had
/// already declared its class here; until now that declaration was inaudible on screen, and a Back sat
/// beside a Choose unit in the same grey.</para>
/// </summary>
internal static class UiButton
{
    public static bool Navigate(string label, Vector2 size) => Press(label, size, EClass.Go);

    public static bool Navigate(string label) => Press(label, null, EClass.Go);

    public static bool Confirm(string label, Vector2 size) => Press(label, size, EClass.Commit);

    public static bool Confirm(string label) => Press(label, null, EClass.Commit);

    public static bool Back(string label, Vector2 size) => Press(label, size, EClass.Back);

    public static bool Back(string label) => Press(label, null, EClass.Back);

    /// <summary>
    /// A Back that is drawn as a neutral menu entry: the back tone on click, the navigation colour on
    /// screen. For a Back that sits in a list of PEERS rather than beside the thing it cancels - the
    /// main menu's Quit is one of seven identical entries, and tinting that one alone read as a bug
    /// rather than as meaning.
    /// </summary>
    public static bool BackInList(string label, Vector2 size) =>
        Press(label, size, EClass.Back, EClass.Go);

    /// <summary>SmallButton voiced with the neutral Navigate tone (roster/copy/pick actions).</summary>
    public static bool NavigateSmall(string label)
    {
        PushClass(EClass.Go);
        bool clicked = ImGui.SmallButton(label);
        ImGui.PopStyleColor(3);
        if (clicked) UiSound.Navigate();
        return clicked;
    }

    private enum EClass
    {
        Back,
        Go,
        Commit,
    }

    /// <param name="sound">Which cue the click voices.</param>
    /// <param name="tint">Which class's colour to wear, when that differs from the sound (see
    /// <see cref="BackInList"/>). Null means "the same class", which is the normal case.</param>
    private static bool Press(string label, Vector2? size, EClass sound, EClass? tint = null)
    {
        EClass cls = tint ?? sound;
        bool clicked;
        if (UiSkin.HasButtons)
        {
            // Skinned: the plate image is drawn behind a transparent ImGui button of the same size.
            var style = ImGui.GetStyle();
            string visible = label.Split("##")[0];
            Vector2 auto = new(ImGui.CalcTextSize(visible).X + style.FramePadding.X * 2f, ImGui.GetFrameHeight());
            Vector2 want = size ?? Vector2.Zero;
            float avail = ImGui.GetContentRegionAvail().X;
            Vector2 actual = new(want.X > 0 ? want.X : want.X < 0 ? MathF.Max(4f, avail + want.X) : auto.X,
                                 want.Y > 0 ? want.Y : auto.Y);
            Vector2 min = ImGui.GetCursorScreenPos();
            Vector2 max = min + actual;
            bool hovered = ImGui.IsMouseHoveringRect(min, max) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            UiSkin.DrawPlate(ImGui.GetWindowDrawList(),
                cls switch { EClass.Back => UiSkin.EPlate.Back, EClass.Commit => UiSkin.EPlate.Commit, _ => UiSkin.EPlate.Go },
                min, max, hovered, hovered && ImGui.IsMouseDown(ImGuiMouseButton.Left));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.04f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0f, 0f, 0f, 0.15f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
            clicked = ImGui.Button(label, actual);
            ImGui.PopStyleColor(4);
        }
        else
        {
            PushClass(cls);
            clicked = size is { } s ? ImGui.Button(label, s) : ImGui.Button(label);
            ImGui.PopStyleColor(3);
        }

        if (clicked)
        {
            switch (sound)
            {
                case EClass.Back: UiSound.Back(); break;
                case EClass.Commit: UiSound.Confirm(); break;
                default: UiSound.Navigate(); break;
            }
        }
        return clicked;
    }

    private static void PushClass(EClass cls)
    {
        (Vector4 idle, Vector4 hovered) = cls switch
        {
            EClass.Back => (ImGuiTheme.ButtonBack, ImGuiTheme.ButtonBackHovered),
            EClass.Commit => (ImGuiTheme.ButtonCommit, ImGuiTheme.ButtonCommitHover),
            _ => (ImGuiTheme.ButtonGo, ImGuiTheme.ButtonGoHovered),
        };

        ImGui.PushStyleColor(ImGuiCol.Button, idle);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiTheme.ButtonPressed);
    }

    /// <summary>Checkbox that ticks on flip (either direction).</summary>
    public static bool Checkbox(string label, ref bool value)
    {
        bool changed = ImGui.Checkbox(label, ref value);
        if (changed) UiSound.Toggle();
        return changed;
    }
}
