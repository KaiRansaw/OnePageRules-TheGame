using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;

namespace FdgRaylib.Game;

/// <summary>
/// The battle's own sound cues (lane 4, 2026-11-27) - not the shot effects (the battle effects own those): dice
/// rolled, the battle starting, a round over, an objective seized or lost, a unit destroyed, a morale test failed
/// (Shaken / Routed), victory / defeat. This decides WHEN a cue plays; the Godot screen plays the file.
/// <para>Two sources: the engine's presentation beats (dice, morale - <see cref="FromBeat"/>), and what changed on the
/// table since the last look (<see cref="Tracker"/>: the round number, who holds each objective, which units still
/// have models, the game's end).</para>
/// </summary>
public static class GameCues
{
    public enum ECue { Dice, BattleStart, RoundOver, ObjectiveSeized, ObjectiveLost, UnitDestroyed, MoraleShaken, MoraleRouted, Victory, Defeat }

    /// <summary>The cue a beat plays, if any. A routed unit's own beat is the Routed cue; a failed morale test's
    /// banner ("X fails morale - Shaken!") the Shaken one.</summary>
    public static ECue? FromBeat(PresentationBeat beat) => beat switch
    {
        DiceRolledBeat => ECue.Dice,
        UnitRoutedBeat => ECue.MoraleRouted,
        BannerBeat b when b.BannerText.Contains("fails morale - Shaken", StringComparison.Ordinal) => ECue.MoraleShaken,
        _ => null,
    };

    /// <summary>The battle's background beds: Grimdark Future games hear wind with distant rumbles of war, Age of
    /// Fantasy games the wind alone; rain, heavy rain and thunderstorms add rain. Names of files in sounds/ambience.</summary>
    public static IReadOnlyList<string> AmbienceFor(EGameSystem system, string? weather)
    {
        var beds = new List<string> { system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish or EGameSystem.AoFQuest ? "wind" : "war" };
        if (weather is "rain" or "heavy-rain" or "storm") beds.Add("rain");
        return beds;
    }

    /// <summary>Did this computer's side win? From the game's end message - the engine's win message names the winners
    /// ("Alpha and Bravo win!"). Null when nobody won (a draw, a fault, a disconnect) - the end cue is then the plain
    /// victory sting.</summary>
    public static bool? Result(string? ended, IEnumerable<string> mine)
    {
        if (string.IsNullOrEmpty(ended)) return null;
        int win = ended.IndexOf(" win", StringComparison.OrdinalIgnoreCase);
        if (win < 0) return null;
        string winners = ended[..win];
        return mine.Any(n => n.Length > 0 && winners.Contains(n, StringComparison.Ordinal));
    }

    /// <summary>The file a cue plays (sounds/cues, sounds/dice), picked with <paramref name="roll"/> where there are several.</summary>
    public static string FileFor(ECue cue, int roll) => cue switch
    {
        ECue.Dice => (roll % 5) switch { 0 => "dice/dice_1", 1 => "dice/dice_2", 2 => "dice/dice_3", 3 => "dice/die_1", _ => "dice/die_2" },
        ECue.BattleStart => "cues/round_start",
        ECue.RoundOver => "cues/round_over",
        ECue.ObjectiveSeized => "cues/objective_seized",
        ECue.ObjectiveLost => "cues/objective_lost",
        ECue.UnitDestroyed => "cues/unit_destroyed",
        ECue.MoraleShaken => "cues/morale_shaken",
        ECue.MoraleRouted => "cues/morale_routed",
        ECue.Victory => "cues/victory",
        _ => "cues/defeat",
    };

    /// <summary>Watches the table between frames and says what changed that has a sound.</summary>
    public sealed class Tracker
    {
        private int _round = -1;
        private readonly Dictionary<string, string?> _owners = new();
        private readonly HashSet<UnitID> _standing = new();
        private bool _ended;

        /// <summary>The cues since the last call. <paramref name="mine"/> = the players this computer plays (an
        /// objective is "seized" when one of them takes it, "lost" when another side does). <paramref name="result"/>:
        /// null while playing, then true (this computer's side won), false (lost) or null-with-<paramref name="ended"/>
        /// (a draw or watching bots - plays the Victory cue as the game's end).</summary>
        public List<ECue> Update(ITableState table, IReadOnlyCollection<PlayerID> mine, bool ended, bool? result)
        {
            var cues = new List<ECue>();
            int round = table.Progress?.RoundCount ?? 0;
            if (_round >= 0 && round > _round) cues.Add(_round == 0 ? ECue.BattleStart : ECue.RoundOver);
            _round = round;

            bool first = _owners.Count == 0;
            foreach (IObjective o in table.Objectives.Objects)
            {
                string key = $"{o.Position.x:0.0},{o.Position.z:0.0}";
                string? owner = o.OwnerID?.ID.ToString();
                if (_owners.TryGetValue(key, out string? was) && was != owner && owner != null && !first)
                    cues.Add(mine.Any(p => p.ID.ToString() == owner) ? ECue.ObjectiveSeized : mine.Any(p => p.ID.ToString() == was) ? ECue.ObjectiveLost : ECue.ObjectiveSeized);
                else if (was != null && owner == null && mine.Any(p => p.ID.ToString() == was))
                    cues.Add(ECue.ObjectiveLost);
                _owners[key] = owner;
            }

            foreach (IUnit u in table.Units.Objects)
            {
                bool standing = u.Models.Any(m => m.GetIsAlive());
                if (standing) _standing.Add(u.ID);
                else if (_standing.Remove(u.ID)) cues.Add(ECue.UnitDestroyed);
            }

            if (ended && !_ended) cues.Add(result == false ? ECue.Defeat : ECue.Victory);
            _ended = ended;
            // One of each per look: three objectives changing hands at the round's end is one chime, not three.
            return cues.Distinct().ToList();
        }
    }
}
