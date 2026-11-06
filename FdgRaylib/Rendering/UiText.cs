using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// #398: text that is DATA, not a format string.
///
/// <para><c>ImGui.Text</c>, <c>TextColored</c>, <c>TextDisabled</c> and friends take a printf FORMAT
/// string. Hand one a value carrying a '%' - "40.7%", a book that names a rule "50% cover" - and ImGui
/// reads the '%' as a conversion specifier and prints whatever happens to sit next on the varargs
/// stack. That is not a theoretical hazard: the percentage columns added in round 5 rendered as
/// "6.8??" followed by fragments of unrelated engine strings ("any remainder.", "0 skips terrain
/// placeme"), which is this bug reading the process's own memory and printing it on screen.</para>
///
/// <para><c>TextUnformatted</c> has no format pass, so it is the only safe way to print a string the
/// program did not write itself. These wrappers are it, with the colour handling that made the format
/// versions attractive in the first place.</para>
/// </summary>
internal static class UiText
{
    /// <summary>
    /// Text in <paramref name="color"/>, with no format interpretation. A line too long for the space
    /// left wraps at the content edge (2026-11-06: every coloured and dim line in the game was cut off at
    /// its panel's edge instead - cost warnings, hints, the camp's notes - while plain text wrapped).
    /// <paramref name="wrap"/> false keeps it to one line, for a caller that set its own wrap width with
    /// <c>PushTextWrapPos</c> (which this would otherwise replace).
    /// </summary>
    internal static void Colored(Vector4 color, string text, bool wrap = true)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        // Not on a window's first frame: a tooltip or popup sizes itself to its contents, and wrapping it
        // before it has a width would lock it narrow. After that frame it is as wide as its longest line,
        // so nothing in it is ever "too long" and it never wraps.
        bool pushWrap = wrap && !ImGui.IsWindowAppearing()
            && ImGui.CalcTextSize(text).X > ImGui.GetContentRegionAvail().X;
        if (pushWrap) ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        if (pushWrap) ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    /// <summary>The theme's disabled tone - <c>ImGui.TextDisabled</c> without the format pass. Wraps like
    /// <see cref="Colored"/>.</summary>
    internal static void Disabled(string text, bool wrap = true) =>
        Colored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text, wrap);

    /// <summary>A one-shot tooltip - exactly what <c>ImGui.SetTooltip</c> does (begin, one line, end),
    /// without the format pass. Deliberately unwrapped, like <c>SetTooltip</c>: the callers that need a
    /// wrap push their own width (see <c>RuleHoverText.ShowTooltip</c>).</summary>
    internal static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    /// <summary>Wrapped at the content edge - <c>ImGui.TextWrapped</c> without the format pass.</summary>
    internal static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>A bullet point - <c>ImGui.BulletText</c> without the format pass.</summary>
    internal static void Bullet(string text)
    {
        ImGui.Bullet();
        ImGui.TextUnformatted(text);
    }
}
