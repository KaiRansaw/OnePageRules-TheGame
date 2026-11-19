using FDG;
using FDG.Stages;
using FDG.StateMachine;

namespace FdgRaylib.Game;

/// <summary>
/// What the Mission panel says in Horde and the Quest games (Kai, 2026-11-19: "Build panels for them", with the list
/// "as is"). Those games are not won by holding objectives, so the standard panel had nothing true to say about them;
/// their state only showed up as lines in the battle report.
///
/// <para><b>Horde:</b> the wave and round; the priority objective; each objective's damage; when the next wave comes;
/// the points earned and left to spend. <b>Quest mission:</b> the objective being played, in the Campaign Builder's
/// words, and how far along it is; the round; the alertness that brings the next wave; the random event in play.
/// <b>Quest Horde:</b> the wave, the alertness that ends it, the party's XP and the random event.</para>
///
/// <para>Drawing-free on purpose: this is the decision about what the panel shows, and it survives any front end.
/// <see cref="Signature"/> changes whenever a row would.</para>
/// </summary>
public static class ModePanel
{
    public enum EStyle { Title, Line, Dim, Book, Good, Warn }

    public sealed record Row(string Text, EStyle Style, string? Tip = null);

    /// <summary>The panel's rows for this game, or null when the game is not one of these modes.</summary>
    public static List<Row>? For(ITableState table, EGameSystem system, EGameMode mode)
    {
        return mode switch
        {
            EGameMode.Horde => Horde(table),
            EGameMode.StarQuestMission => QuestMission(table, system),
            EGameMode.StarQuestHorde => QuestHorde(table, system),
            _ => null,
        };
    }

    /// <summary>A short text that changes whenever the rows would - the panel is rebuilt only then.</summary>
    public static string Signature(List<Row>? rows) => rows == null ? "" : string.Join("|", rows.Select(r => r.Text));

    // ------------------------------------------------------------------ Horde

    private const int HordeObjectives = 5;

    private static List<Row> Horde(ITableState table)
    {
        var rows = new List<Row> { new("Horde Mode", EStyle.Title) };
        IGameProgress progress = table.Progress;
        int last = progress.TotalRounds;
        int waves = (last + GameWideConstants.HORDE_ROUNDS_PER_WAVE - 1) / GameWideConstants.HORDE_ROUNDS_PER_WAVE;
        if (progress.RoundCount is not int round)
        {
            rows.Add(new("Setting up. Survive 12 rounds against the waves.", EStyle.Dim));
            return rows;
        }

        rows.Add(new($"Wave {HordeRules.WaveOfRound(round)} of {waves} - round {round} of {last}", EStyle.Line));

        int? nextWave = Enumerable.Range(round + 1, Math.Max(0, last - round)).Cast<int?>().FirstOrDefault(r => HordeRules.IsWaveRound(r!.Value));
        rows.Add(new(nextWave is int w ? $"Next wave: start of round {w}" : "No more waves - hold out to the end of round " + last, EStyle.Dim));
        int? reward = Enumerable.Range(round, Math.Max(0, last - round + 1)).Cast<int?>().FirstOrDefault(r => HordeRules.IsRewardRound(r!.Value));
        if (reward is int rr)
            rows.Add(new($"Points for objectives you hold: end of round {rr}", EStyle.Dim,
                $"{HordeRules.PointsPerSeizedObjective} points per objective a defending unit holds, +{HordeRules.PriorityObjectiveBonus} if the priority one is among them."));

        int earned = progress.HordePointsEarned ?? 0, left = progress.HordePointsAvailable ?? 0;
        rows.Add(new($"Points: {earned} earned, {left} to spend", EStyle.Good));

        // Objectives by number (1-5); a destroyed one is gone from the table.
        var byNumber = table.Objectives.Objects
            .Select(o => (Number: o.HordeNumber ?? HordeRules.ObjectiveNumber(o.Position, HordePlacement.TableWidth, HordePlacement.TableHeight), Objective: o))
            .GroupBy(x => x.Number).ToDictionary(g => g.Key, g => g.First().Objective);
        IObjective? priority = byNumber.Values.FirstOrDefault(o => o.IsHordePriority);
        int? priorityNumber = priority == null ? null : byNumber.First(kv => ReferenceEquals(kv.Value, priority)).Key;
        rows.Add(new(priorityNumber is int p ? $"Priority objective: {p}" : "Priority objective: none right now", EStyle.Warn,
            "The priority objective is set at the start of rounds 1, 4, 7 and 10. Holding it at a reward round is worth extra points."));
        for (int n = 1; n <= HordeObjectives; n++)
        {
            string text = !byNumber.TryGetValue(n, out IObjective? o) ? $"Objective {n}: destroyed"
                : o.HordeDamage == 0 ? $"Objective {n}: no damage"
                : $"Objective {n}: {o.HordeDamage} of {HordeRules.ObjectiveDamageToDestroy} damage";
            rows.Add(new(text + (n == priorityNumber ? "  (priority)" : ""), byNumber.ContainsKey(n) ? EStyle.Line : EStyle.Dim));
        }
        return rows;
    }

    // ------------------------------------------------------------------ Quest

    private static QuestProgress? Quest(ITableState table) =>
        GameProgressUtilities.TryGetProgress(table.DataStore)?.Quest ?? GameProgressUtilities.PendingQuestFor(table.DataStore);

    private static List<Row> QuestMission(ITableState table, EGameSystem system)
    {
        QuestProgress? q = Quest(table);
        string difficulty = q is { Difficulty: >= 1 and <= 4 } ? " - " + StarQuestMissionRules.DifficultyNames[q.Difficulty] : "";
        var rows = new List<Row> { new($"{GameSystemInfo.QuestName(system)} Mission{difficulty}", EStyle.Title) };
        if (table.Progress.RoundCount is int round) rows.Add(new($"Round {round}", EStyle.Dim));
        if (q == null || q.Primary == 0)
        {
            rows.Add(new("The primary objective is rolled at the start of the mission.", EStyle.Dim));
            return rows;
        }

        bool secondary = q.PrimaryDone && q.Secondary > 0 && !q.SecondaryDone;
        if (!q.PrimaryDone || secondary)
        {
            int n = secondary ? q.Secondary : q.Primary;
            if (secondary) rows.Add(new($"Primary done: {StarQuestMissionRules.PrimaryNames[q.Primary]}", EStyle.Good));
            rows.Add(new($"{(secondary ? "Secondary" : "Primary")} objective: {(secondary ? StarQuestMissionRules.SecondaryNames[n] : StarQuestMissionRules.PrimaryNames[n])}", EStyle.Line));
            rows.Add(new(QuestObjectiveText.Of(n, secondary), EStyle.Book));
            rows.Add(new(ObjectiveProgress(table, q, n, secondary), EStyle.Warn));
        }
        else
        {
            rows.Add(new($"Primary done: {StarQuestMissionRules.PrimaryNames[q.Primary]}", EStyle.Good));
            if (q.Secondary == 0)
                rows.Add(new("A secondary objective is set up at the end of this round.", EStyle.Dim));
            else
                rows.Add(new($"Secondary done: {StarQuestMissionRules.SecondaryNames[q.Secondary]}", EStyle.Good));
        }
        if (q.PrimaryDone)
            rows.Add(new(QuestObjectiveText.MissionEnd, EStyle.Book));

        rows.Add(new($"Alertness {q.Alertness} of {StarQuestHordeRules.AlertnessToEndWave}"
            + (q.BossFight > 0 ? "" : " - reinforcements arrive at " + StarQuestHordeRules.AlertnessToEndWave), EStyle.Warn));
        AddEvent(rows, q);
        return rows;
    }

    private static List<Row> QuestHorde(ITableState table, EGameSystem system)
    {
        QuestProgress? q = Quest(table);
        var rows = new List<Row> { new($"{GameSystemInfo.QuestName(system)} Horde", EStyle.Title) };
        if (q == null) return rows;
        rows.Add(new($"Wave {q.Wave} of {StarQuestHordeRules.FinalWave}", EStyle.Line));
        rows.Add(new($"Alertness {q.Alertness} of {StarQuestHordeRules.AlertnessToEndWave} - at {StarQuestHordeRules.AlertnessToEndWave} the wave ends", EStyle.Warn));
        rows.Add(new($"Party XP this game: {q.PartyXp}", EStyle.Good));
        AddEvent(rows, q);
        return rows;
    }

    private static void AddEvent(List<Row> rows, QuestProgress q)
    {
        if (string.IsNullOrWhiteSpace(q.Event)) return;
        int dash = q.Event.IndexOf(" - ", StringComparison.Ordinal);
        rows.Add(new("Random event: " + (dash >= 0 ? q.Event[(dash + 3)..] : q.Event), EStyle.Line,
            "In play until the next random event (rolled " + q.Event + ")."));
    }

    /// <summary>How far along the objective is, from its markers.</summary>
    private static string ObjectiveProgress(ITableState table, QuestProgress q, int number, bool secondary)
    {
        var mine = q.Markers.Where(m => m.ForSecondary == secondary).ToList();
        var objectives = mine.Where(m => m.Kind == "objective").ToList();
        if (objectives.Count == 0 && number == 1)
            return $"The marker drops when the last model of the {(secondary ? "next" : "first")} reinforcement wave is killed.";
        QuestMarker? seize = objectives.FirstOrDefault(m => m.Action == "seize");
        if (seize != null)
            return seize.Activated ? $"Marker seized: {seize.Tokens} of {seize.TokensNeeded} tokens" : "Marker not yet activated (pass the test on it first).";
        QuestMarker? carried = objectives.FirstOrDefault(m => m.Action == "carry");
        if (carried != null)
        {
            string where = carried.CarriedBy.Length == 0 ? "on the table"
                : "carried by " + (table.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == carried.CarriedBy)?.Name ?? "a hero");
            int deliveries = mine.Count(m => m.Kind == "delivery");
            return "Marker " + where + (deliveries > 0 ? $" - {deliveries} delivery marker{(deliveries == 1 ? "" : "s")} left" : "");
        }
        return objectives.Count == 0 ? "No markers left." : $"{objectives.Count} marker{(objectives.Count == 1 ? "" : "s")} left";
    }
}

/// <summary>
/// The twelve mission objectives in the Campaign Builders' own words (GFSQ - Campaign Builder v2.1.0 p.2-3; AoF: Quest's
/// Campaign Builder and Beginners Guide v2.1.0 print the same text), plus the Mission End rule. ASCII inch marks.
/// </summary>
public static class QuestObjectiveText
{
    public static string Of(int number, bool secondary) =>
        number is >= 1 and <= 6 ? (secondary ? Secondary : Primary)[number - 1] : "";

    public const string MissionEnd =
        "Mission End: Once the Heroes have completed the primary objective, they must all be within 6\" of the same table corner at the end of any round in order to end the mission and collect rewards (the secondary objective doesn't need to be completed to end the mission).";

    private static readonly string[] Primary =
    {
        "The centre of the table counts as the AI Goal. When the last model from the first wave of Reinforcements is killed, it drops an objective marker within 1\". Heroes may use a Skill action whilst within 1\" of the marker, and if they pass a Str test, the marker is removed and the objective is completed.",
        "Place one objective marker randomly at the centre of a table quarter or the centre of the table, which counts as the AI Goal. Heroes may use a Skill action whilst within 1\" of the marker and take a Str test. If passed, at the end of each round, if any Hero is within 3\" of the marker while enemies aren't, then it's seized, and gets one token. The objective is completed once the marker has 6 tokens.",
        "Place one objective marker within 6\" of a random table corner, which counts as the AI Goal (if the Heroes are deployed in the same corner, move the marker to the centre of the table). Heroes within 1\" of the marker may pick it up by using a Skill action and passing a Dex test, and drop it within 1\" if they are Shaken. The objective is completed when the Hero carrying the marker ends its activation within 6\" of the Hero deployment corner.",
        "Place two objective markers randomly at the centre of two different table quarters or the centre of the table, and the marker closest to an enemy counts as its AI Goal. Heroes may use a Skill action whilst within 1\" of a marker, and if they pass a Dex test, the marker is removed. The objective is completed once both markers are removed.",
        "Place three objective markers randomly at the centre of three different table quarters or the centre of the table, and the marker closest to an enemy counts as its AI Goal. Heroes may use a Skill action whilst within 1\" of a marker, and if they pass a Wil test, remove the marker and roll one die. On a 5+, or if it was the last marker, the objective is completed.",
        "Place two delivery markers within 6\" of two different random table corners (if the Heroes are deployed in the same corner as a marker, move it to the centre of the table). When the Heroes deploy, place one objective marker in their deployment zone, which counts as the AI Goal. Heroes within 1\" of the marker may pick it up by using a Skill action and passing a Wil test, and drop it within 1\" if they are Shaken. When the Hero carrying the objective ends its activation within 3\" of a delivery marker, remove that marker. The objective is completed once both markers are removed.",
    };

    private static readonly string[] Secondary =
    {
        "The centre of the table counts as the AI Goal. When the last model from the next wave of Reinforcements is killed, it drops an objective marker within 1\". Heroes may use a Skill action whilst within 1\" of the marker, and if they pass a Wil test, the marker is removed and the objective is completed.",
        "Place one objective marker at the centre of the table, which counts as the AI Goal. Heroes may use a Skill action whilst within 1\" of the marker and take a Dex test. If passed, at the end of each round, if any Hero is within 3\" of the marker while enemies aren't, then it's seized, and gets one token. The objective is completed once the marker has 3 tokens.",
        "Place one objective marker at the centre of the table, which counts as the AI Goal. Heroes within 1\" of the marker may pick it up by using a Skill action and passing a Str test, and drop it within 1\" if they are Shaken. The objective is completed when the Hero carrying the marker ends its activation within 6\" of any corner.",
        "Place one objective marker within 6\" of the table corner furthest from one random Hero, which counts as the AI Goal. Heroes may use a Skill action whilst within 1\" of the marker, and if they pass a Wil test, the marker is removed and the objective is completed.",
        "Place two objective markers within 6\" of the two corners closest to one random Hero, and the marker closest to an enemy counts as its AI Goal. Heroes may use a Skill action whilst within 1\" of a marker, and if they pass a Dex test, remove the marker and roll one die. On a 4+, or if it was the last marker, the objective is completed.",
        "Place one objective marker within 3\" of one random Hero, which counts as the AI Goal, and place one extraction marker within 6\" of the table corner furthest from that Hero. Heroes within 1\" of the marker may pick it up by using a Skill action and passing a Str test, and drop it within 1\" if they are Shaken. When the Hero carrying the objective ends its activation within 3\" of the extraction marker the objective is completed.",
    };
}
