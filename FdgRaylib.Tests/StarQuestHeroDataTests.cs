using System;
using System.Collections.Generic;
using System.Linq;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #415 - invariants the Hero Builder depends on, checked against the shipped Star Quest roster
/// (Assets/StarQuest/StarQuestHeroes.json, rebuilt by <c>--import-starquest</c>). A refresh that drops a
/// faction, breaks an upgrade reference or leaves an option unpriced fails here rather than in play.
/// </summary>
[TestFixture]
public class StarQuestHeroDataTests
{
    [Test]
    public void TheRoster_Loaded()
    {
        Assert.That(StarQuestData.LoadError, Is.Null, StarQuestData.LoadError);
        Assert.That(StarQuestData.Factions.Count, Is.GreaterThanOrEqualTo(50),
            "the official Star Quest catalog is 54 factions; far fewer means the refresh dropped books");
        Assert.That(StarQuestData.Factions.Sum(f => f.Heroes.Count), Is.GreaterThanOrEqualTo(190));
    }

    [Test]
    public void EveryFaction_IsOnTheCurrentRulesVersion()
    {
        var stale = StarQuestData.Factions
            .Where(f => f.Version is null || !f.Version.StartsWith("3.6", StringComparison.Ordinal))
            .Select(f => $"{f.Faction} (v{f.Version ?? "?"})")
            .ToList();
        Assert.That(stale, Is.Empty, "factions left on an older OPR version: " + string.Join(", ", stale));
    }

    [Test]
    public void EveryHero_HasASaneProfile()
    {
        var bad = new List<string>();
        foreach (SqFaction f in StarQuestData.Factions)
            foreach (SqHero h in f.Heroes)
            {
                if (string.IsNullOrWhiteSpace(h.Id)) bad.Add($"{f.Faction}/{h.Name}: no id");
                if (h.Cost <= 0) bad.Add($"{f.Faction}/{h.Name}: cost {h.Cost}");
                if (h.Q is < 2 or > 6) bad.Add($"{f.Faction}/{h.Name}: quality {h.Q}");
                if (h.D is < 2 or > 6) bad.Add($"{f.Faction}/{h.Name}: defense {h.D}");
                if (h.T < 1) bad.Add($"{f.Faction}/{h.Name}: tough {h.T}");
                if (h.Weapons.Count == 0) bad.Add($"{f.Faction}/{h.Name}: no weapons");
            }
        Assert.That(bad, Is.Empty, string.Join("; ", bad.Take(20)));
    }

    /// <summary>A hero's upgrades[] are package uids; an unresolvable one means a whole upgrade section
    /// silently disappears from the Hero Builder.</summary>
    [Test]
    public void EveryHeroUpgrade_ResolvesToAPackage()
    {
        var dangling = new List<string>();
        foreach (SqFaction f in StarQuestData.Factions)
            foreach (SqHero h in f.Heroes)
                foreach (string uid in h.Upgrades)
                    if (f.Package(uid) is null) dangling.Add($"{f.Faction}/{h.Name} -> {uid}");
        Assert.That(dangling, Is.Empty, "upgrade packages that don't exist: " + string.Join(", ", dangling.Take(20)));
    }

    [Test]
    public void EveryUpgradeOption_IsPricedAndHasAnEffect()
    {
        var unpriced = new List<string>();
        var empty = new List<string>();
        foreach (SqFaction f in StarQuestData.Factions)
            foreach (SqPackage p in f.Packages)
                foreach (SqSection s in p.Sections)
                    foreach (SqOption o in s.Opts)
                    {
                        if (o.Cost is null) unpriced.Add($"{f.Faction}/{p.Uid}: {o.Label}");
                        if (o.Gains.Count == 0) empty.Add($"{f.Faction}/{p.Uid}: {o.Label}");
                    }
        Assert.That(unpriced, Is.Empty, "options OPR published no price for: " + string.Join("; ", unpriced.Take(10)));
        Assert.That(empty, Is.Empty, "options that grant nothing: " + string.Join("; ", empty.Take(10)));
    }

    /// <summary>A "Replace X" section can only be offered once the hero actually has X. If no starting
    /// loadout in the faction can satisfy a replace target, the section is unreachable - usually a sign the
    /// importer matched the wrong package to the wrong hero.</summary>
    [Test]
    public void EveryReplaceSection_IsReachableFromSomeHero()
    {
        var unreachable = new List<string>();
        foreach (SqFaction f in StarQuestData.Factions)
            foreach (SqHero h in f.Heroes)
                foreach (string uid in h.Upgrades)
                {
                    SqPackage? p = f.Package(uid);
                    if (p is null) continue;
                    foreach (SqSection s in p.Sections)
                    {
                        if (s.Variant != "replace") continue;
                        var weapons = h.Weapons.Select(w => w.ToQuest()).ToList();
                        var rules = h.Rules.Where(r => r != "Hero").Concat(h.Gear).ToList();
                        // Only the hero's own starting kit is checked; a target an earlier upgrade grants
                        // is fine, so this only reports sections nothing in the faction can ever satisfy.
                        if (!StarQuestData.HasTargets(s, weapons, rules)
                            && !CanBeGranted(f, h, s.Targets ?? new List<string>()))
                            unreachable.Add($"{f.Faction}/{h.Name}/{uid}: \"{s.Label}\"");
                    }
                }
        Assert.That(unreachable.Count, Is.LessThanOrEqualTo(0),
            "replace sections no loadout can satisfy: " + string.Join("; ", unreachable.Take(15)));
    }

    private static bool CanBeGranted(SqFaction f, SqHero hero, IReadOnlyList<string> targets)
    {
        var grantable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string uid in hero.Upgrades)
            foreach (SqSection s in f.Package(uid)?.Sections ?? new List<SqSection>())
                foreach (SqOption o in s.Opts)
                    foreach (SqGain g in o.Gains)
                    {
                        if (g.W is not null) grantable.Add(g.W.N);
                        else if (g.Rule is not null) grantable.Add(Head(g.Rule));
                    }
        return targets.All(t => grantable.Contains(t) || grantable.Contains(t + "s"));
    }

    private static string Head(string rule)
    {
        int p = rule.IndexOf(" (", StringComparison.Ordinal);
        return p > 0 ? rule[..p] : rule;
    }

    /// <summary>CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only, so imported text has to
    /// come through the ASCII fold or it renders as '?' in game.</summary>
    [Test]
    public void AllRosterText_IsAscii()
    {
        var offenders = new List<string>();
        void Check(string where, string? text)
        {
            if (text is null) return;
            char[] bad = text.Where(c => c > 127).Distinct().ToArray();
            if (bad.Length > 0) offenders.Add($"{where}: {string.Join(" ", bad.Select(c => $"U+{(int)c:X4}"))}");
        }

        foreach (SqFaction f in StarQuestData.Factions)
        {
            Check(f.Faction, f.Faction);
            foreach (SqHero h in f.Heroes)
            {
                Check($"{f.Faction}/{h.Name}", h.Name);
                foreach (SqWeapon w in h.Weapons) Check($"{f.Faction}/{h.Name} weapon", w.N);
                foreach (string g in h.Gear) Check($"{f.Faction}/{h.Name} gear", g);
            }
            foreach (SqNamedText r in f.Rules) { Check($"{f.Faction} rule {r.N}", r.N); Check($"{f.Faction} rule {r.N}", r.D); }
            foreach (SqSpell s in f.Spells) { Check($"{f.Faction} spell {s.N}", s.N); Check($"{f.Faction} spell {s.N}", s.D); }
            foreach (SqPackage p in f.Packages)
                foreach (SqSection s in p.Sections)
                {
                    Check($"{f.Faction}/{p.Uid} section", s.Label);
                    foreach (SqOption o in s.Opts) Check($"{f.Faction}/{p.Uid} option", o.Label);
                }
        }
        Assert.That(offenders, Is.Empty, "non-ASCII text renders as '?' in game: " + string.Join("; ", offenders.Take(15)));
    }

    /// <summary>Every rule a hero or upgrade mentions should have OPR's wording in the faction glossary, so
    /// the Hero Builder's hover has something to show.</summary>
    [Test]
    public void FactionGlossaries_CoverTheRulesTheirHeroesUse()
    {
        var missing = new List<string>();
        foreach (SqFaction f in StarQuestData.Factions)
        {
            var known = new HashSet<string>(f.Rules.Select(r => r.N), StringComparer.OrdinalIgnoreCase);
            foreach (SqHero h in f.Heroes)
                foreach (string r in h.Rules)
                {
                    string name = Head(r);
                    int paren = name.IndexOf('(');
                    if (paren > 0) name = name[..paren];
                    if (name is "Hero") continue;
                    if (!known.Contains(name)) missing.Add($"{f.Faction}: {name}");
                }
        }
        Assert.That(missing.Distinct().ToList(), Is.Empty,
            "rules with no description: " + string.Join("; ", missing.Distinct().Take(20)));
    }

    /// <summary>Every faction lands in one of the picker's real groups; "Other" means the grouping table
    /// missed a rename or a new faction and the picker shows it in a catch-all bucket.</summary>
    [Test]
    public void EveryFaction_HasAPickerGroup()
    {
        var ungrouped = StarQuestData.Factions
            .Where(f => StarQuestData.GroupOf(f) == "Other")
            .Select(f => f.Faction)
            .ToList();
        Assert.That(ungrouped, Is.Empty, "factions falling into Other: " + string.Join(", ", ungrouped));
    }
}
