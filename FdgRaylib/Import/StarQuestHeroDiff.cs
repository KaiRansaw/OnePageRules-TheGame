using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FdgRaylib.Quest;

namespace FdgRaylib.Import;

// #415 - the review report for a StarQuestHeroes.json refresh. The importer rewrites the whole file, so
// the only way to know what OPR actually changed is to compare the old copy against the new one: factions
// and heroes added or dropped, points/quality/defense/tough moves, and loadout changes.
public static class StarQuestHeroDiff
{
    public static string Report(SqHeroFile? before, SqHeroFile after)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Star Quest heroes - refresh report");
        sb.AppendLine();
        sb.AppendLine($"- Source: {after.Source}");
        if (before is null)
        {
            sb.AppendLine("- No previous file to compare against.");
            return sb.ToString();
        }
        sb.AppendLine($"- Before: {before.Factions.Count} factions, {before.Factions.Sum(f => f.Heroes.Count)} heroes");
        sb.AppendLine($"- After:  {after.Factions.Count} factions, {after.Factions.Sum(f => f.Heroes.Count)} heroes");
        sb.AppendLine();

        var oldByName = before.Factions.ToDictionary(f => f.Faction, StringComparer.OrdinalIgnoreCase);
        var newByName = after.Factions.ToDictionary(f => f.Faction, StringComparer.OrdinalIgnoreCase);

        // Match by uid first - that survives OPR renaming a faction, which 3.6.0 did nine times.
        var oldByUid = before.Factions.Where(f => !string.IsNullOrEmpty(f.Uid))
            .GroupBy(f => f.Uid).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renames = new List<string>();
        var added = new List<string>();
        var body = new StringBuilder();

        foreach (SqFaction nf in after.Factions.OrderBy(f => f.Faction, StringComparer.OrdinalIgnoreCase))
        {
            if (!oldByName.TryGetValue(nf.Faction, out SqFaction? of))
            {
                oldByUid.TryGetValue(nf.Uid, out of);
                if (of is not null) renames.Add($"{of.Faction} -> {nf.Faction}");
            }
            if (of is null) { added.Add($"{nf.Faction} ({nf.Heroes.Count} heroes)"); continue; }
            matched.Add(of.Faction);

            string section = FactionSection(of, nf);
            if (section.Length > 0) body.Append(section);
        }

        var removed = before.Factions.Where(f => !matched.Contains(f.Faction) && !newByName.ContainsKey(f.Faction))
            .Select(f => f.Faction).ToList();

        if (renames.Count > 0)
        {
            sb.AppendLine("## Factions renamed");
            foreach (string r in renames) sb.AppendLine($"- {r}");
            sb.AppendLine();
        }
        if (added.Count > 0)
        {
            sb.AppendLine("## Factions added");
            foreach (string a in added) sb.AppendLine($"- {a}");
            sb.AppendLine();
        }
        if (removed.Count > 0)
        {
            sb.AppendLine("## Factions gone");
            foreach (string r in removed) sb.AppendLine($"- {r}");
            sb.AppendLine();
        }

        if (body.Length == 0) sb.AppendLine("No hero changed in any faction that exists in both files.");
        else { sb.AppendLine("## Hero changes"); sb.AppendLine(); sb.Append(body); }

        return sb.ToString();
    }

    private static string FactionSection(SqFaction of, SqFaction nf)
    {
        var lines = new List<string>();
        var oldById = of.Heroes.GroupBy(h => h.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var oldByName = of.Heroes.GroupBy(h => h.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (SqHero nh in nf.Heroes.OrderBy(h => h.Cost))
        {
            SqHero? oh = (oldById.TryGetValue(nh.Id, out SqHero? byId) ? byId : null)
                      ?? (oldByName.TryGetValue(nh.Name, out SqHero? byName) ? byName : null);
            if (oh is null) { lines.Add($"  + NEW  {nh.Name} - {nh.Cost}pts, Q{nh.Q}+ D{nh.D}+ T{nh.T}"); continue; }
            seen.Add(oh.Id);

            var moves = new List<string>();
            if (oh.Cost != nh.Cost) moves.Add($"{oh.Cost} -> {nh.Cost}pts");
            if (oh.Q != nh.Q) moves.Add($"Q{oh.Q}+ -> Q{nh.Q}+");
            if (oh.D != nh.D) moves.Add($"D{oh.D}+ -> D{nh.D}+");
            if (oh.T != nh.T) moves.Add($"Tough {oh.T} -> {nh.T}");

            string ow = Loadout(oh), nw = Loadout(nh);
            if (ow != nw) moves.Add("weapons changed");
            string og = string.Join(", ", oh.Gear.OrderBy(x => x, StringComparer.Ordinal));
            string ng = string.Join(", ", nh.Gear.OrderBy(x => x, StringComparer.Ordinal));
            if (og != ng) moves.Add("gear changed");
            string orr = Rules(oh), nr = Rules(nh);
            if (orr != nr) moves.Add("rules changed");

            if (moves.Count == 0) continue;
            lines.Add($"  * {nh.Name}: {string.Join("; ", moves)}");
            if (ow != nw)
            {
                lines.Add($"      was: {ow}");
                lines.Add($"      now: {nw}");
            }
            if (og != ng)
            {
                lines.Add($"      gear was: {(og.Length == 0 ? "-" : og)}");
                lines.Add($"      gear now: {(ng.Length == 0 ? "-" : ng)}");
            }
            if (orr != nr)
            {
                lines.Add($"      rules was: {orr}");
                lines.Add($"      rules now: {nr}");
            }
        }

        foreach (SqHero oh in of.Heroes.Where(h => !seen.Contains(h.Id)))
            if (!nf.Heroes.Any(n => string.Equals(n.Name, oh.Name, StringComparison.OrdinalIgnoreCase)))
                lines.Add($"  - GONE {oh.Name} ({oh.Cost}pts)");

        if (lines.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"### {nf.Faction}  (v{of.Version} -> v{nf.Version})");
        foreach (string l in lines) sb.AppendLine(l);
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Loadout(SqHero h) => string.Join(" / ", h.Weapons.Select(w =>
    {
        string rules = w.S.Count > 0 ? ", " + string.Join(", ", w.S) : "";
        string range = w.R > 0 ? $"{w.R}\", " : "";
        string count = w.C > 1 ? $"{w.C}x " : "";
        return $"{count}{w.N} ({range}A{w.A}{rules})";
    }));

    private static string Rules(SqHero h) =>
        string.Join(", ", h.Rules.OrderBy(x => x, StringComparer.Ordinal));
}
