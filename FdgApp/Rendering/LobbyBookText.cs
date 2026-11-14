using FDG;
using FDG.Missions;

namespace FdgRaylib.Rendering;

/// <summary>
/// The books' own wording for the lobby's rule settings (wish list, "Everywhere": "Where it is a rule,
/// the tooltip uses the book's own wording").
///
/// <para>
/// <c>OfficialRuleText</c> is Army Forge's text for UNIT special rules and has no entry for any of these -
/// Fast-Play, Extra Actions, deployment styles, King of the Hill and the Mission Cards are game-setup
/// rules printed in the books, not unit rules. So the sentences are copied here from the current books,
/// word for word except that curly quotes and inch marks are made ASCII (CLAUDE.md):
/// </para>
/// <list type="bullet">
/// <item>Core Rules v3.6.0 (GF, GFF, AoF, AoFS) - Deployment, Mission. The four print the same words.</item>
/// <item>Advanced Rulebook v3.6.0 (GF, GFF, AoF, AoFS) - Fast-Play (GF/AoF only: p17), Extra Actions
/// (p18), Advanced Deployment (pp21-23 GF/AoF, pp19-21 GFF/AoFS), Multiplayer Games (GF p52). Checked
/// 2026-11-07: the four books print the same words except the Fast-Play army size (GF 4000pts, AoF
/// 3000pts) and the free-for-all corner area (2'x1', or 1'x1' in Firefight and Skirmish).</item>
/// <item>Mission Cards v3.6.0 - the one-line summary under each game type.</item>
/// </list>
/// <para>A line starting "In this game:" is the app's own note on how it plays the rule, in plain words.</para>
/// </summary>
internal static class LobbyBookText
{
    // ── Core Rules v3.6.0 ──

    internal const string CoreDeployment =
        "Deployment: Players roll-off, and the winner picks a long table edge as their deployment zone, with " +
        "their opponent taking the opposite. Then the players alternate in placing one unit each within 12\" of " +
        "their table edge, starting with the player that won the deployment roll-off.";

    internal const string CoreGameLength =
        "The game ends after 4 rounds, and the player that controls most markers wins.";

    /// <summary>The book line a shooting house rule changes. GF: Star Quest 2.1.0 prints its own; there is
    /// no current AoF: Quest core book in the project (CLAUDE.md), so it gets none.</summary>
    internal static string? CoreShooting(EGameSystem system) => system switch
    {
        EGameSystem.GFStarQuest => "Models in range and line of sight may fire all ranged weapons.",
        EGameSystem.AoFQuest => null,
        _ => "Models in range and line of sight may fire all ranged weapons, and may fire at up to two different " +
             "targets with different weapon types.",
    };

    /// <summary>The cover line (the four 3.6.0 core books and GF: Star Quest 2.1.0 print the same words).</summary>
    internal static string? CoreCover(EGameSystem system) => system == EGameSystem.AoFQuest ? null :
        "Cover Terrain: Units with most models fully inside Cover terrain or behind Sight Blockers, or that are " +
        "mostly inside Cover terrain or behind Sight Blockers (for single-model units), get +1 to Defense rolls when " +
        "blocking hits from shooting.";

    /// <summary>The unit-spacing line (same five books as <see cref="CoreCover"/>).</summary>
    internal static string? CoreUnitSpacing(EGameSystem system) => system == EGameSystem.AoFQuest ? null :
        "Units may only be within 1\" of other units when charging, and may only charge if at least one charging " +
        "model is within charge distance of the target unit.";

    /// <summary>"The book says: ..." appended to a house rule's plain description, when the book is in.</summary>
    internal static string WithBook(string plain, string? book) =>
        book == null ? plain : plain + "\n\nThe book says: " + book;

    // ── Advanced Rulebook v3.6.0 - Advanced Deployment ──

    internal const string StandardStyles =
        "These styles are meant to provide a balanced experience, with fairly standard gameplay.";
    internal const string StrategicStyles =
        "These styles are meant to provide a balanced experience, with interesting twists that require strategic thinking.";
    internal const string AsymmetricStyles =
        "These styles are meant to provide an unbalanced experience, with one player being surrounded or disadvantaged.";
    internal const string RandomStyle = "To select a random deployment style, simply roll one die:";
    internal const string FrontLineNote = "Note that this is the basic deployment from the core rules.";

    /// <summary>The hover for one entry of the Deployment list.</summary>
    internal static string Deployment(EDeploymentStyle style)
    {
        if (style == EDeploymentStyle.Core)
            return "Core Rules:\n" + CoreDeployment;
        EDeploymentFamily? random = style switch
        {
            EDeploymentStyle.RandomStandard => EDeploymentFamily.Standard,
            EDeploymentStyle.RandomStrategic => EDeploymentFamily.Strategic,
            EDeploymentStyle.RandomAsymmetric => EDeploymentFamily.Asymmetric,
            _ => null,
        };
        if (random is { } fam)
        {
            IReadOnlyList<EDeploymentStyle> six = AdvancedDeployment.InFamily(fam);
            return $"{FamilyName(fam)} deployment styles: {FamilyText(fam)}\n{RandomStyle}\n"
                + string.Join("\n", six.Select((s, i) => $"  {i + 1}. {AdvancedDeployment.Name(s)}"))
                + "\nIn this game: the die is rolled when deployment starts, and the map preview shows the core zones until then.";
        }
        EDeploymentFamily family = FamilyOf(style);
        string text = $"{FamilyName(family)} deployment styles: {FamilyText(family)}";
        if (style == EDeploymentStyle.FrontLine) text += "\n" + FrontLineNote;
        return text + "\nThe zones are the book's diagram for this game - see the map preview.";
    }

    internal static EDeploymentFamily FamilyOf(EDeploymentStyle style)
    {
        foreach (EDeploymentFamily f in Enum.GetValues<EDeploymentFamily>())
            if (AdvancedDeployment.InFamily(f).Contains(style)) return f;
        return EDeploymentFamily.Standard;
    }

    private static string FamilyName(EDeploymentFamily f) => f.ToString();

    private static string FamilyText(EDeploymentFamily f) => f switch
    {
        EDeploymentFamily.Standard => StandardStyles,
        EDeploymentFamily.Strategic => StrategicStyles,
        _ => AsymmetricStyles,
    };

    // ── Advanced Rulebook v3.6.0 - Fast-Play Rules (GF and AoF only) ──

    internal static string FastPlayIntro(EGameSystem system) =>
        "Fast-Play rules allow you to speed up regular games, as well as play epic battles of massive size quickly.\n" +
        $"We highly recommend using these rules when playing games with {FastPlay.RecommendedFromPoints(system) ?? 0}pts " +
        "armies or higher, so that you can enjoy massive battles without making the games too slow.\n" +
        "Unit Leaders: No matter which combination of Fast-Play rules you choose to use, all of them use Unit Leaders " +
        "to speed up gameplay. Before the game begins, you must clearly define a single model within each unit to be " +
        "its leader, and which will remain the unit's leader until the end of the game. Since the leader is essential, " +
        "it always has to be the last model that is removed when the unit takes wounds.";

    internal const string LeaderMovement =
        "Leader-Based Movement: When a unit wants to move, only measure distances from the leader, and move it first. " +
        "Once the leader has been moved, you must place all other models in the unit around the leader in any way you " +
        "like, as long as you maintain unit coherency. No distances are measured for any other models except the " +
        "leader, so you may freely move them to their new position.";

    internal const string LeaderMelee =
        "Leader-Based Melee: When a unit wants to charge, only measure distances from the leader and move it into base " +
        "contact first. Once the leader has been moved, you must place all other models in the unit in base contact " +
        "with enemy models, or as close as possible in any way you like, as long as you maintain unit coherency. Then " +
        "all models from the target that are not in base contact may move in the same way. When fighting in melee, all " +
        "models always count as being in range of the enemy, regardless of their position, so everyone can always " +
        "strike with all weapons. When separating units by 1\" after melee, first separate the leader, and then place " +
        "all other models around the leader in coherency, over 1\" away from other units.";

    internal const string LeaderShooting =
        "Leader-Based Shooting: When a unit wants to shoot, only measure line of sight and distances from the leader " +
        "model, to the closest model from the target unit. If the leader has line of sight, then all weapons that are " +
        "in range of the target from the leader's position may shoot. No line of sight or distances are measured for " +
        "any other models except the leader, so models can freely shoot as if they were in the leader's position.";

    internal const string SimplifiedTerrain =
        "Simplified Terrain: When setting up terrain for the game, all pieces of terrain must be treated like solid " +
        "buildings, with clearly defined areas that can be entered and exited. When using these rules, the only way " +
        "for units to get Cover is by entering a piece of terrain.\n" +
        "In this game: buildings stay impassable (the book's rules for garrisoning buildings are not in the game).";

    // ── Advanced Rulebook v3.6.0 - Extra Actions ──

    internal const string ExtraActionsIntro =
        "Before the game, players can agree to use any or all of the following extra actions in the game. When using " +
        "these extra actions the game is played normally, however players may pick any of the selected extra actions " +
        "in addition to the regular ones.";

    internal static string ExtraAction(string name) => name switch
    {
        FDG.ExtraActions.LastStand =>
            "Units may use a Last Stand action even if they are Shaken. When using Last Stand actions, units may use a " +
            "Hold, Advance, Rush, or Charge action, but count as being Shaken for the purpose of special rules until " +
            "the beginning of their next activation. Units in Last Stand get -1 to Quality and Defense rolls, halve " +
            "their movement, can't contest or seize objectives, and can't use any special rules that require picking " +
            "a target (ex.: Caster).",
        FDG.ExtraActions.HunkerDown =>
            "When using a Hunker Down action, the unit uses a Hold action and remains idle, and it gets +1 to Defense " +
            "rolls when taking hits from shooting until its next activation. Shaken units may use Hunker Down actions " +
            "to get this bonus as well as stopping to be Shaken.",
        FDG.ExtraActions.DefensiveStance =>
            "When using a Defensive Stance action, the unit uses a Hold action, remains idle, and may not strike back " +
            "until its next activation, and it gets +1 to Defense rolls when taking hits in melee until its next " +
            "activation. Shaken units may use Defensive Stance actions to get this bonus as well as stopping to be Shaken.",
        FDG.ExtraActions.FocusedFire =>
            "When using a Focused Fire action, the unit uses a Hold action, and gets +1 to hit rolls when shooting at " +
            "targets within 12\".",
        FDG.ExtraActions.HeavyCharge =>
            "When using a Heavy Charge action, the unit uses a Charge action to charge a target that is between 3\" and " +
            "6\" away, and gets +1 to hit rolls in melee.",
        FDG.ExtraActions.StealthMove =>
            "When using a Stealth Move action, the unit uses an Advance action, but may not shoot, and enemies shooting " +
            "at it from over 12\" away get -1 to their hit rolls until its next activation.",
        FDG.ExtraActions.CoveringFire =>
            "When using a Covering Fire action, the unit uses a Hold action and shoots, but only hits on unmodified " +
            "results of 6. Then, roll one die per hit, and if you roll at least one 4+, then the target must take a " +
            "Morale Test (regardless of casualties). All hits are then discarded as misses.",
        FDG.ExtraActions.Overwatch =>
            "When using an Overwatch action, the unit uses a Hold action and remains idle, and until its next activation " +
            "it may react once to an enemy unit's activation at any point as that enemy unit moves or shoots. The unit " +
            "may react to two things:\n" +
            "- Movement: Shoot at an enemy at any point during its movement, but gets -1 to hit rolls.\n" +
            "- Shooting: Shoot back at an enemy within 24\" that shot at it, but gets -1 to hit rolls.",
        _ => "",
    };

    // ── Advanced Rulebook v3.6.0 - Multiplayer Games ──

    internal static string FreeForAll(EGameSystem system) =>
        "Free-for-all: The simplest way to play games with 3-4 players is to have a free-for-all, where all players " +
        "can attack each other, and they are all competing for the same objectives.\n" +
        "Deployment: All players roll-off to determine the order in which they will deploy. Then the players " +
        "alternate in picking a table corner and deploying one unit each within a " +
        (GameSystemInfo.IsSkirmish(system) ? "1'x1'" : "2'x1'") + " area, starting with the player that got the " +
        "highest score, and then alternating in descending order.";

    /// <summary>The Generic terrain generator's last step (Advanced Rulebooks 3.6.0), word for word.</summary>
    internal const string DangerousTerrainPicks =
        "After terrain has been placed, roll-off to see who goes first, and then players alternate in picking D3 " +
        "pieces of terrain each, which count as Dangerous Terrain.";

    internal const string KingOfTheHill =
        "An alternative version of playing with 3-4 players is to play king of the hill, which works just like a " +
        "free-for-all, but players get a bonus for attacking the most powerful opponent, as they all compete for the " +
        "same objectives.\n" +
        "The King: Throughout the game, players must keep track of the total point cost of all units in their army " +
        "(including units that have not been deployed yet), and the player with the highest total counts as the king. " +
        "In case of a tie, all tied players count as the king. Note that at the beginning of the game, all players " +
        "are tied and all count as the king.\n" +
        "Combat Bonus Points: When a player picks one of the king's units as the target for an attack, they " +
        "immediately gain one combat bonus point. Once per activation, players may spend one point to add +1 to the " +
        "result of all dice in a single roll. Note that combat bonus points may be spent during the same activation " +
        "in which they were earned.";

    // ── Mission Cards v3.6.0 ──

    internal const string MissionCardsIntro =
        "Mission Cards: These rules were created to give players a dynamic way to play, with shifting mission " +
        "objectives. Objectives: Place 6 objective markers. Victory Conditions: The game ends after 4 rounds, and the " +
        "player that scored most victory points wins. Final Objectives: At the end of the game, players get 1 VP for " +
        "each objective that they control.";

    internal static string MissionCards(EMissionCardGame game) => game switch
    {
        EMissionCardGame.Off => "No mission cards: the core rules' mission.\n" + CoreGameLength,
        EMissionCardGame.BattleOfWits =>
            "Battle of Wits: Players compete in completing as many of their own objective cards as they can before the " +
            "end of the game. Personal Decks: Each player gets a personal deck of 36 mission cards.",
        EMissionCardGame.TotalDomination =>
            "Total Domination: Players compete in completing the same set of objective cards before the game ends, " +
            "racing to be the first to complete them. Shared Deck: Both players share a single deck of 36 mission cards.",
        EMissionCardGame.DirectChallenge =>
            "Direct Challenge: The players must challenge their opponent to complete specific missions. (Custom deck of " +
            "18 cards.)",
        EMissionCardGame.StrategicRuse =>
            "Strategic Ruse: The winning player must reveal all of their cards, whilst their opponent may keep them " +
            "hidden. (Custom deck of 18 cards.)",
        EMissionCardGame.PlannedGambit =>
            "Planned Gambit: The players may force their opponent to change their active mission cards. (Custom deck " +
            "of 18 cards.)",
        EMissionCardGame.CoordinatedRecovery =>
            "Coordinated Recovery: The players may shuffle discarded missions back into their deck. (Custom deck of 18 " +
            "cards.)",
        EMissionCardGame.LogisticalSupremacy =>
            "Logistical Supremacy: The winning player may hold additional mission cards in their hand. (Custom deck of " +
            "18 cards.)",
        EMissionCardGame.TacticalParagons =>
            "Tactical Paragons: The players may pick specific missions to be completed by their Heroes. (Custom deck " +
            "of 18 cards.)",
        _ => "",
    };
}
