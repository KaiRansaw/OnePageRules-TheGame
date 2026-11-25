using FDG;
using FDG.Stages;
using FDG.StateMachine;

namespace FdgRaylib.Game;

/// <summary>
/// What the screens show about Command Points stratagems (Kai, 2026-11-25, "Doctrines and Stratagems Mockups" - the
/// doctrine pick screen and the in-battle drawer, option 2B). Names, costs and the book's text come from the engine
/// (<see cref="CommandPoints.Universal"/>, <see cref="CommandPoints.DoctrineStratagems"/>); this adds only the short
/// "when" tag - the moment each one can be used - and a few words on what each doctrine is about.
/// </summary>
public static class StratagemBook
{
    public sealed record Entry(string Name, int Cost, string When, string Text, int Doctrine);

    /// <summary>One stratagem as a player sees it now: can they pay for it, and have they used it this activation.</summary>
    public sealed record Line(Entry Stratagem, bool Affordable, bool UsedNow);

    private static readonly Dictionary<string, string> WhenTags = new()
    {
        [CommandPoints.HighCommand] = "Any die", [CommandPoints.SupremeCommand] = "Any roll",
        [CommandPoints.SeizeInitiative] = "Enemy's turn", [CommandPoints.WaiveInitiative] = "Your turn",
        [CommandPoints.DelayedDeployment] = "Deployment", [CommandPoints.HiddenDeployment] = "After deployment",
        [CommandPoints.PushForward] = "Advance / Rush", [CommandPoints.HinderedAdvance] = "Enemy moves",
        [CommandPoints.CoordinatedMove] = "Your turn", [CommandPoints.StrategicRelocation] = "Start of activation",
        [CommandPoints.EternalVigilance] = "Model would die", [CommandPoints.LightningReflexes] = "Being shot",
        [CommandPoints.ArmourBreaker] = "Your hits land", [CommandPoints.TacticalRetreat] = "Enemy within 12\"",
        [CommandPoints.SweepingMove] = "Charge", [CommandPoints.HitAndRun] = "After melee",
        [CommandPoints.CombatFatigue] = "Enemy strikes", [CommandPoints.KillingBlow] = "Melee",
        [CommandPoints.HeightenedSenses] = "Shooting at cover", [CommandPoints.DisruptedSight] = "Enemy shoots",
        [CommandPoints.FrenziedAttack] = "After shooting", [CommandPoints.ClosingFire] = "Being charged",
        [CommandPoints.MinimizeLosses] = "Became Shaken", [CommandPoints.StandStrong] = "Failed Morale",
        [CommandPoints.Terrorize] = "Enemy passed Morale", [CommandPoints.CodeOfHonor] = "Shaken / Routed",
        [CommandPoints.RushObjective] = "Rush", [CommandPoints.SupremeCaster] = "Caster",
        [CommandPoints.Vanish] = "End of round", [CommandPoints.TotalShutdown] = "End of round",
    };

    /// <summary>The moment a stratagem can be used, in two or three words.</summary>
    public static string When(string name) => WhenTags.TryGetValue(name, out string? w) ? w : "";

    /// <summary>What a doctrine's four stratagems are about (1-6).</summary>
    public static string Theme(int doctrine) => doctrine switch
    {
        1 => "Movement and position", 2 => "Keeping units alive", 3 => "Charges and melee",
        4 => "Shooting", 5 => "Morale", 6 => "Objectives and tricks", _ => "",
    };

    /// <summary>The doctrine's short name ("Shock" for the Shock Doctrine).</summary>
    public static string Short(int doctrine) => CommandPoints.DoctrineName(doctrine).Replace(" Doctrine", "");

    public static IReadOnlyList<Entry> Universal() =>
        CommandPoints.Universal.Select(u => new Entry(u.Name, u.Cost, When(u.Name), u.Text, 0)).ToList();

    public static IReadOnlyList<Entry> OfDoctrine(int doctrine) =>
        CommandPoints.DoctrineStratagems.Where(d => d.Doctrine == doctrine)
            .Select(d => new Entry(d.Name, d.Cost, When(d.Name), d.Text, doctrine)).ToList();

    /// <summary>A player's Command Points as the drawer shows them: their CP, their doctrine (0 = not picked yet) and
    /// every stratagem they have - their doctrine's first, then the Universal ones. Null when Command Points are off.</summary>
    public static (int Cp, int Doctrine, IReadOnlyList<Line> Lines)? For(ITableState table, PlayerID player)
    {
        CommandPointsState? s;
        try { s = CommandPoints.State(table.DataStore); }
        catch (Exception) { return null; }
        if (s == null || s.Playstyle == 0) return null;
        string key = player.ToString();
        int cp = s.Points.GetValueOrDefault(key);
        int doctrine = s.Doctrine.GetValueOrDefault(key);
        var lines = OfDoctrine(doctrine).Concat(Universal())
            .Select(e => new Line(e, e.Cost <= cp, s.Used.Contains($"{key}:{e.Name}")))
            .ToList();
        return (cp, doctrine, lines);
    }
}
