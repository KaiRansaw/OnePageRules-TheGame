using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// The three Solo &amp; Co-Op Challenge Bonuses' names and hovers, shared by both front ends (the Raylib lobby
/// and Mission Setup, the Godot lobby). Each hover quotes the book (Solo &amp; Co-Op Rules v3.6.0, identical in
/// GF, GF: Firefight, AoF and AoF: Skirmish).
/// </summary>
internal static class ChallengeBonusesText
{
    internal const string Title = "Challenge Bonuses (bot)";

    internal const string Intro =
        "Challenge Bonuses (Solo & Co-Op Rules, optional): \"Players may give the AI any of the following\n" +
        "bonuses.\" The books' own way to make the bot harder. They change how the bot plays, not what it\n" +
        "brings, and only bot-controlled seats get them.";

    internal static readonly (string Label, EChallengeBonuses Flag, string BookText)[] Bonuses =
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
}
