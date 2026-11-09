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
    internal const string Intro =
        "Challenge Bonuses (Solo & Co-Op Rules, optional): \"Players may give the AI any of the following\n" +
        "bonuses.\" The books' own way to make the bot harder. They change how the bot plays, not what it\n" +
        "brings, and only bot-controlled seats get them.";

    private static readonly (string Label, EChallengeBonuses Flag, string BookText)[] Bonuses =
    {
        ("Activates D3 units per turn", EChallengeBonuses.ExtraActivations,
            "\"When it's the AI's turn to activate a unit, it gets to activate D3 units instead.\"\n" +
            "The bot rolls a D3 each time its turn comes round and activates that many units in a row."),
        ("+1 to hit when tied", EChallengeBonuses.TiedHitBonus,
            "\"If the AI is tied with the players for objective control or VPs, then all of its units get\n" +
            "+1 to hit rolls.\"\nChecked at every roll. Counted by victory points when Mission Cards are on,\n" +
            "by objectives held otherwise."),
        ("+1 to hit and Defense when losing", EChallengeBonuses.LosingBonus,
            "\"If the AI is losing for objective control or VPs, then all of its units get +1 hit rolls and\n" +
            "+1 to Defense rolls.\"\nChecked at every roll, counted the same way as the tied bonus."),
    };

    /// <summary>Draws the header and the three boxes; returns the edited set (unchanged if nothing was clicked).</summary>
    internal static EChallengeBonuses Draw(EChallengeBonuses current, string idSuffix)
    {
        ImGui.TextUnformatted("Challenge Bonuses (bot)");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.TooltipWrapped(Intro);

        EChallengeBonuses edited = current;
        ImGui.Indent();
        foreach ((string label, EChallengeBonuses flag, string bookText) in Bonuses)
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
