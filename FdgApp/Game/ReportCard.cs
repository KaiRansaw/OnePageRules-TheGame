using System.Globalization;
using System.Text.RegularExpressions;
using FDG;

namespace FdgRaylib.Game;

/// <summary>
/// What the battle report's hover card says about one activation (Kai, 2026-11-15, option A of the hover mockup:
/// "Headline and steps"). Built from the report's lines, so both front ends can draw it:
/// <list type="number">
/// <item><b>Headline</b> - who won the fight, or what the unit did.</item>
/// <item><b>Subline</b> - the move ("Charged 7&quot; into Assault Grunts") and what it led to ("Grunts routed").</item>
/// <item><b>One row per attack</b> - the numbers as steps: attacks -&gt; hits -&gt; saved -&gt; died, with the rules
/// that changed the dice as short notes (Rending, Regeneration...).</item>
/// <item><b>After</b> - morale tests and what they did (ROUTED, SHAKEN, HELD).</item>
/// </list>
/// The dice themselves stay in the lines' rolls, shown on demand.
/// </summary>
public static class ReportCard
{
    /// <summary>One number in an attack's chain. <see cref="Final"/>: the last one, what the attack did.</summary>
    public sealed record Step(string Value, string Label, bool Final = false, bool Hurt = false);

    public sealed record AttackRow(UnitID? By, string ByName, string Verb, UnitID? Target, string TargetName, bool Melee,
        IReadOnlyList<Step> Steps, IReadOnlyList<string> Notes);

    /// <summary>A line after the fighting. <see cref="Tag"/>: ROUTED, SHAKEN, HELD or null; <see cref="Bad"/> colours it.</summary>
    public sealed record AfterRow(string Text, string? Tag, bool Bad);

    public sealed record Card(string Headline, UnitID? HeadlineUnit, string Subline, IReadOnlyList<AttackRow> Attacks,
        IReadOnlyList<AfterRow> After);

    private static readonly Regex MeleeWon = new(@"^Melee won by (?<who>.+?), (?<a>[\d.]+) wounds? to (?<b>[\d.]+)\.?$", RegexOptions.Compiled);
    private static readonly Regex MoraleTest = new(@"^(?<who>.+?) - Morale Test \((?<n>\d+)\+\) - (?<result>.+)$", RegexOptions.Compiled);
    private static readonly Regex FailsMorale = new(@"^(?<who>.+?) fails morale - (?<what>Routed|Shaken)!?$", RegexOptions.Compiled);
    private static readonly Regex Routs = new(@"^(?<who>.+?) routs!?$", RegexOptions.Compiled);

    /// <summary>The card for one block of the report, or null when nothing was fought, shot or tested.</summary>
    /// <param name="nameOf">A unit's name, by id, for the headline when the line only has the id.</param>
    public static Card? For(BattleReport.Block block, Func<UnitID, string?>? nameOf = null)
    {
        UnitID? me = block.Unit;
        string myName = me is { } id ? nameOf?.Invoke(id) ?? NameIn(block.Activation) ?? "The unit" : NameIn(block.Activation) ?? "The unit";

        var attacks = new List<AttackRow>();
        foreach (BattleReport.Line l in block.Lines)
            if (l.Attack is { } a) attacks.Add(Row(l, a, me));

        var texts = block.Lines.Where(l => l.Kind == BattleReport.Kind.Line).Select(l => (Line: l, Text: TextOf(l).Trim())).ToList();
        var after = After(texts.Select(t => t.Text).ToList());
        if (attacks.Count == 0 && after.Count == 0) return null;
        if (me == null && attacks.Count == 0) return null;   // the setup (map, deployment rolls) - nobody activated

        // Headline: a fight's winner, else what the unit did.
        string headline;
        UnitID? headlineUnit = me;
        string? won = texts.Select(t => MeleeWon.Match(t.Text)).FirstOrDefault(m => m.Success)?.Groups["who"].Value;
        bool tie = texts.Any(t => t.Text.StartsWith("The melee is a tie", StringComparison.OrdinalIgnoreCase));
        if (won != null)
        {
            headline = $"{won} won the fight";
            headlineUnit = UnitNamed(block, won) ?? me;
        }
        else if (tie) { headline = "The fight was a tie"; headlineUnit = null; }   // nobody's colour
        else if (attacks.FirstOrDefault(a => a.By.Equals(me) && !a.Melee) is { } shot) headline = $"{myName} shot {shot.TargetName}";
        else if (attacks.Count > 0) headline = $"{attacks[0].ByName} {attacks[0].Verb} {attacks[0].TargetName}";
        else headline = myName;

        // Subline: the move, then what it led to.
        var sub = new List<string>();
        AttackRow? myFight = attacks.FirstOrDefault(a => a.By.Equals(me) && a.Melee);
        if (block.Charged && myFight != null) sub.Add($"Charged {block.Moved:0}\" into {myFight.TargetName}.");
        else if (block.Moved > 0.05f) sub.Add($"Moved {block.Moved:0}\".");
        foreach (AfterRow r in after.Where(r => r.Tag is "ROUTED" or "SHAKEN"))
            sub.Add($"{r.Text.Split(" failed", 2)[0].Split(" fails", 2)[0].Split(" was", 2)[0]} {(r.Tag == "ROUTED" ? "routed" : "is Shaken")}.");
        return new Card(headline, headlineUnit, string.Join(" ", sub.Distinct()), attacks, after);
    }

    private static AttackRow Row(BattleReport.Line l, BattleReport.Outcome a, UnitID? me)
    {
        bool mine = me == null || l.Attacker.Equals(me);
        string verb = a.Melee ? (mine ? "attacked" : "struck back at") : "shot";
        string by = l.Title.FirstOrDefault(p => p.Kind == BattleReport.PieceKind.Unit && p.Unit.Equals(l.Attacker))?.Text ?? "A unit";
        string target = l.TargetName ?? l.Title.LastOrDefault(p => p.Kind == BattleReport.PieceKind.Unit)?.Text ?? "the target";

        var hitRolls = l.Rolls.Where(r => r.WhoUnit.Equals(l.Attacker) && r.Label.StartsWith("Roll to Hit", StringComparison.Ordinal)).ToList();
        var saveRolls = l.Rolls.Where(r => !r.WhoUnit.Equals(l.Attacker) && r.Result.Contains("saved", StringComparison.Ordinal)).ToList();
        var steps = new List<Step> { new(Num(a.Attacks), "ATTACKS") };
        steps.Add(new Step(Num(a.Hits), "HIT" + Need(hitRolls)));
        if (a.HasSaves) steps.Add(new Step(Num(a.Saves), "SAVED" + Need(saveRolls)));
        if (a.Ignored > 0.01f) steps.Add(new Step(Num(a.Ignored), "SHRUGGED OFF"));
        steps.Add(a.ModelsLost > 0
            ? new Step(a.ModelsLost.ToString(CultureInfo.InvariantCulture), "DIED", true, true)
            : a.Wounds > 0.01f ? new Step(Num(a.Wounds), a.Wounds < 1.5f ? "WOUND" : "WOUNDS", true, true)
            : new Step("0", "DAMAGE", true));
        return new AttackRow(l.Attacker, by, verb, l.Target, target, a.Melee, steps, Notes(l, hitRolls, saveRolls));
    }

    /// <summary>The rules that changed the dice, in a few words each: Rending, Precise +1, Regeneration: 1 ignored...</summary>
    private static List<string> Notes(BattleReport.Line l, List<BattleReport.Roll> hits, List<BattleReport.Roll> saves)
    {
        var notes = new List<string>();
        foreach (BattleReport.Roll r in l.Rolls)
            foreach (string m in r.Modifiers)
                if (!m.StartsWith("Quality", StringComparison.Ordinal) && !m.StartsWith("Defense", StringComparison.Ordinal)
                    && !m.StartsWith("AP ", StringComparison.Ordinal))
                    notes.Add(m);
        foreach (BattleReport.Roll r in saves)
        {
            // "Slashing Claws: 3 hits, Rending AP+4" - the rule after the comma.
            int comma = r.Label.IndexOf(", ", StringComparison.Ordinal);
            if (comma > 0) notes.Add(r.Label[(comma + 2)..]);
            // "CCW: 2 hits +1 (Ferocious) = 3" - the bonus and its rule.
            Match bonus = Regex.Match(r.Label, @"(?<n>[+-]\d+) \((?<rule>[^)]+)\)");
            if (bonus.Success) notes.Add($"{bonus.Groups["rule"].Value} {bonus.Groups["n"].Value} hit{(bonus.Groups["n"].Value is "+1" or "-1" ? "" : "s")}");
        }
        // Any other roll inside the attack (Regeneration, a Bane re-roll): its name and what it did.
        // (A roll that ignored wounds is already its own step: SHRUGGED OFF.)
        foreach (BattleReport.Roll r in l.Rolls.Where(r => !hits.Contains(r) && !saves.Contains(r)
                     && !r.Result.Contains("ignored", StringComparison.Ordinal)))
            notes.Add($"{r.Label}: {r.Result}");
        return notes.Distinct().Take(5).ToList();
    }

    private static List<AfterRow> After(List<string> texts)
    {
        var rows = new List<AfterRow>();
        var fails = texts.Select(t => FailsMorale.Match(t)).Where(m => m.Success)
            .ToDictionary(m => m.Groups["who"].Value, m => m.Groups["what"].Value.ToUpperInvariant());
        foreach (string t in texts)
        {
            if (t.Length == 0 || t.EndsWith("'s Activation", StringComparison.Ordinal) || t.StartsWith("Activating", StringComparison.Ordinal)
                || MeleeWon.IsMatch(t) || t.StartsWith("The melee is a tie", StringComparison.OrdinalIgnoreCase)) continue;
            if (MoraleTest.Match(t) is { Success: true } test)
            {
                string who = test.Groups["who"].Value, need = test.Groups["n"].Value;
                bool passed = test.Groups["result"].Value.StartsWith("Pass", StringComparison.OrdinalIgnoreCase);
                if (passed) rows.Add(new AfterRow($"{who} passed morale ({need}+)", "HELD", false));
                else rows.Add(new AfterRow($"{who} failed morale ({need}+)", fails.GetValueOrDefault(who, "FAILED"), true));
                continue;
            }
            if (FailsMorale.Match(t) is { Success: true } fail)
            {
                // Already said by its morale test's row; on its own (no test line) it stands as one.
                if (!texts.Any(x => MoraleTest.Match(x) is { Success: true } m && m.Groups["who"].Value == fail.Groups["who"].Value))
                    rows.Add(new AfterRow($"{fail.Groups["who"].Value} failed morale", fail.Groups["what"].Value.ToUpperInvariant(), true));
                continue;
            }
            if (Routs.Match(t) is { Success: true } rout)
            {
                rows.Add(new AfterRow($"{rout.Groups["who"].Value} was wiped out", "ROUTED", true));
                continue;
            }
            rows.Add(new AfterRow(t, null, false));
        }
        return rows;
    }

    /// <summary>" 4+" when every roll needed the same, else nothing.</summary>
    private static string Need(List<BattleReport.Roll> rolls)
    {
        var needs = rolls.Select(r => r.Threshold).Distinct().ToList();
        return needs.Count == 1 ? $" {needs[0]}+" : "";
    }

    private static string Num(float f) => MathF.Abs(f - MathF.Round(f)) < 0.05f
        ? MathF.Round(f).ToString(CultureInfo.InvariantCulture)
        : f.ToString("0.#", CultureInfo.InvariantCulture);

    private static string TextOf(BattleReport.Line l) => string.Concat(l.Title.Select(p => p.Text));

    private static string? NameIn(BattleReport.Line? activation) =>
        activation?.Title.FirstOrDefault(p => p.Kind == BattleReport.PieceKind.Unit)?.Text
        ?? (activation == null ? null : TextOf(activation).TrimStart(':', ' ').Trim() is { Length: > 0 } t ? t : null);

    private static UnitID? UnitNamed(BattleReport.Block b, string name) =>
        b.Lines.SelectMany(l => l.Title).FirstOrDefault(p => p.Kind == BattleReport.PieceKind.Unit && p.Text == name)?.Unit;
}
