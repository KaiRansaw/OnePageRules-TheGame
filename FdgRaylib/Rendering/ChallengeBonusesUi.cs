using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The three Solo &amp; Co-Op Challenge Bonuses as tick boxes, shared by the lobby and campaign Mission Setup
/// so both say the same thing. Each hover quotes the book (Solo &amp; Co-Op Rules v3.6.0, identical in GF,
/// GF: Firefight, AoF and AoF: Skirmish).
/// </summary>
internal static class ChallengeBonusesUi
{
    internal const string Intro = ChallengeBonusesText.Intro;

    /// <summary>Draws the header and the three boxes; returns the edited set (unchanged if nothing was clicked).</summary>
    internal static EChallengeBonuses Draw(EChallengeBonuses current, string idSuffix)
    {
        ImGui.TextUnformatted(ChallengeBonusesText.Title);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.TooltipWrapped(Intro);

        EChallengeBonuses edited = current;
        ImGui.Indent();
        foreach ((string label, EChallengeBonuses flag, string bookText) in ChallengeBonusesText.Bonuses)
        {
            bool tick = (edited & flag) != 0;
            if (UiButton.Checkbox(label + idSuffix, ref tick))
                edited = tick ? edited | flag : edited & ~flag;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.TooltipWrapped(bookText);
        }
        ImGui.Unindent();
        return edited;
    }
}
