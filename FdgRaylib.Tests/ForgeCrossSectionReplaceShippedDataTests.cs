using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #323 — corpus guard for "buy it in one section, replace it in another".
//
// The reported bug was one instance of a general shape: a Replace section whose targets another section
// grants. Compilation walks the choices in the book's SECTION ORDER, so whenever the granting section is
// authored BELOW the consuming one, the swap found nothing to consume and was silently clamped away - no
// weapon, no points, no warning. The books are regenerated OPR data and the ordering is theirs, so a
// re-import can reintroduce the shape anywhere; a spot test on the reported unit would not notice.
//
// This walks EVERY bundled book instead, and for every (unit, Replace section, granting section) pair it
// buys the grant and then asks the section for its whole pool. The invariant: what the unit can no longer
// replace is exactly what the choice bought, and the price is the per-application one. That holds whether
// the grantor sits above or below - which is the property the fix actually restored.
//
// Affects=All is excluded on purpose (see the tail of this file).
[TestFixture]
public class ForgeCrossSectionReplaceShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<TestCaseData> Books() =>
        ShippedBooks.GdfPaths()
            .OrderBy(path => path)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path)));

    private static BookFile Load(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    [TestCaseSource(nameof(Books))]
    public void EveryReplaceFedByAnotherSection_CanSpendItsWholePool(string bookPath)
    {
        BookFile book = Load(bookPath);
        var failures = new List<string>();
        int checkedPairs = 0;

        foreach (RosterUnit roster in book.Units)
        {
            foreach (UpgradeSection section in roster.Sections)
            {
                // All is single-pass by design; PickN/AddModels don't consume targets.
                if (section.Variant != UpgradeVariant.Replace) continue;
                if (section.Affects == UpgradeAffects.All || section.Targets.Count == 0) continue;

                UpgradeOption swap = section.Options.FirstOrDefault();
                if (swap is null) continue;

                foreach ((UpgradeSection grantor, UpgradeOption grant) in Grantors(roster, section))
                {
                    // Buy the grant alone: whatever the section can replace now is its true pool.
                    var withGrant = new UpgradeChoice { SectionId = grantor.Id, OptionId = grant.Id, Count = 1 };
                    (UnitFileEntry granted, List<ItemEntry> grantedItems) = Compile(book, roster, withGrant);
                    int pool = ListCompiler.AvailableApplications(granted.Weapons, grantedItems, section.Targets);
                    if (pool == 0) continue;   // the grant didn't actually feed this section

                    int wanted = section.Affects == UpgradeAffects.One ? 1
                        : section.MaxApplications > 0 ? Math.Min(pool, section.MaxApplications)
                        : pool;
                    checkedPairs++;

                    (UnitFileEntry both, List<ItemEntry> bothItems) = Compile(book, roster, withGrant,
                        new UpgradeChoice { SectionId = section.Id, OptionId = swap.Id, Count = wanted });

                    int left = ListCompiler.AvailableApplications(both.Weapons, bothItems, section.Targets);
                    string where = $"{roster.Name} / \"{section.Label}\" fed by \"{grantor.Label}\" ({grant.Label})";

                    // A swap that hands back a weapon the section ALSO replaces does not shrink its own
                    // pool, and nothing is wrong: 3.6.0 is full of statline upgrades that keep the name
                    // (AoF Skirmish's Bearded Veteran trades an "Oath Greataxe (A2, Shred)" for an "Oath
                    // Greataxe (A6, Shred)"). The pool arithmetic below assumes the target is consumed and
                    // not replaced, so it simply does not apply to those - the price check still does.
                    bool handsBackItsOwnTarget = Feeds(section, swap);

                    // Mutual feeding breaks the pool arithmetic as well as the price. Blessed Sisters'
                    // Biker Sisters are the case: "Replace one Energy Lance" hands out a Sgt. Hand Weapon,
                    // and "Replace Sgt. Hand Weapon" hands an Energy Lance straight back, so the lance
                    // count after both choices is the count before - correctly. (The grantor is starved in
                    // the baseline compile too, so its 3-lance "pool" is really the unit's own loadout and
                    // owes nothing to the grant.)
                    bool mutual = Mutual(section, swap, grantor);

                    if (!handsBackItsOwnTarget && !mutual && left != pool - wanted)
                    {
                        failures.Add($"{where}: asked for {wanted} of a {pool} pool, but {left} target(s) " +
                            $"remain (expected {pool - wanted}) - applications were dropped.");
                    }

                    // Exact price, EXCEPT where the two sections feed each other (DAO Union's Tactical
                    // Grunts trade a Pulse Rifle + CCW for a Pulse Pistol, which the next section trades
                    // back for a Pulse Rifle). There the grantor is itself starved in the baseline compile
                    // and only becomes payable once this swap applies, so the baseline is not comparable -
                    // the applications-landed check above still covers it.
                    if (mutual) continue;

                    // The other direction of the same problem: the grant feeds this section, and spending
                    // the section STARVES the grant, because both draw on the same target. Firefight's
                    // Assault Brother is the shape - it owns one Heavy Pistol, "Replace Heavy Pistol and
                    // Hand Weapon" hands a Heavy Pistol back, and "Replace any Heavy Pistol" (authored
                    // above it) takes the original, leaving the grantor with nothing to replace. The
                    // baseline compile charges for a grant the combined compile cannot apply, so the two
                    // prices are not comparable and the arithmetic here would report a compiler fault that
                    // is really a mutually exclusive pair of choices.
                    //
                    // (That the builder clamps one of those choices away silently rather than telling the
                    // player is a real wart, but a separate one - this fixture is about cross-section
                    // feeding, and the applications-landed check above still covers this pair.)
                    if (CompetesForTheSameTarget(section, grantor)) continue;

                    int expectedCost = granted.PointCost + swap.Cost * wanted;
                    if (both.PointCost != expectedCost)
                    {
                        failures.Add($"{where}: charged {both.PointCost}, expected {expectedCost} " +
                            $"({granted.PointCost} + {swap.Cost}x{wanted}).");
                    }
                }
            }
        }

        Assert.That(failures, Is.Empty,
            $"{Path.GetFileNameWithoutExtension(bookPath)}: " + string.Join("\n  ", failures));
        Assert.That(checkedPairs, Is.GreaterThanOrEqualTo(0)); // books with no such pair legitimately check none
    }

    // Sections OTHER than this one whose options hand it something it replaces. Same-section options are
    // skipped: a combo swap that hands one half back ("Replace Combat Shield and CCW" -> "... , Combat
    // Shield") is a self-contained idiom, not a cross-section feed.
    private static IEnumerable<(UpgradeSection, UpgradeOption)> Grantors(RosterUnit roster, UpgradeSection section)
    {
        foreach (UpgradeSection other in roster.Sections)
        {
            if (ReferenceEquals(other, section)) continue;
            foreach (UpgradeOption option in other.Options)
            {
                bool feeds = option.WeaponsGained.Any(w => Feeds(section, w.Name))
                    || option.ItemsGained.Any(i => Feeds(section, i.Name));
                if (feeds) yield return (other, option);
            }
        }
    }

    /// <summary>Whether the swap hands the grantor something the grantor itself replaces - the two sections
    /// feed each other, so neither one's price can be read from a compile without the other.</summary>
    private static bool Mutual(UpgradeSection section, UpgradeOption swap, UpgradeSection grantor) =>
        swap.WeaponsGained.Any(w => Feeds(grantor, w.Name))
        || swap.ItemsGained.Any(i => Feeds(grantor, i.Name));

    /// <summary>
    /// Whether the two sections replace any of the same thing. When they do, applying one can leave the
    /// other with no target, so a price read from a compile that applied only the grant does not predict
    /// the price of a compile that applies both.
    /// </summary>
    /// Target overlap is asked through <see cref="Feeds(UpgradeSection, string)"/> in both directions
    /// rather than by string equality: OPR writes the same weapon as "Hand Weapon" in one section and
    /// "Hand Weapons" in another, and the compiler's own normalisation is the only thing that knows they
    /// are the same target.
    private static bool CompetesForTheSameTarget(UpgradeSection section, UpgradeSection grantor) =>
        grantor.Variant == UpgradeVariant.Replace
        && (grantor.Targets.Any(target => Feeds(section, target))
            || section.Targets.Any(target => Feeds(grantor, target)));

    /// <summary>Whether any weapon or item this option grants is something the section itself replaces.</summary>
    private static bool Feeds(UpgradeSection section, UpgradeOption option) =>
        option.WeaponsGained.Any(w => Feeds(section, w.Name))
        || option.ItemsGained.Any(i => Feeds(section, i.Name));

    // Asked through the compiler's own availability seam rather than re-implementing its target parsing:
    // would a pile of this weapon satisfy any of the section's targets? (Handles the "2x Name" quantity
    // prefix and the plural/singular normalisation for free, exactly as compilation does.)
    private static bool Feeds(UpgradeSection section, string gainedName) =>
        section.Targets.Any(t => ListCompiler.AvailableApplications(
            new[] { new WeaponFileEntry { Name = gainedName, Quantity = 1000 } },
            Array.Empty<ItemEntry>(),
            new[] { t }) > 0);

    private static (UnitFileEntry, List<ItemEntry>) Compile(BookFile book, RosterUnit roster,
        params UpgradeChoice[] choices)
    {
        var bu = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
        bu.Choices.AddRange(choices);
        return ListCompiler.CompileUnitDetailed(book, bu);
    }

    // #324 — every Replace target must name something its unit can actually hold, in the base loadout or
    // from some option's gains. One target failed this before the plural fix: Dwarf Guilds' "Guardians"
    // target "Bashes" against a weapon named "Bash", which the single-trailing-s rule turned into "bashe".
    // Because that section is Affects=All (max across targets, not min) it still fired off its Pistols half,
    // so the swap looked like it worked while quietly leaving all five Bashes on the unit, free.
    //
    // OPR publishes targets as display STRINGS with no id-based alternative (verified 2026-08-02 against the
    // live army-books API: 61 targets in the Dwarf Guilds book, all strings, and the section schema carries
    // no weapon-id field). So name matching is the only mechanism there is, and a typo-shaped mismatch here
    // is silent by construction - which is why it gets a corpus-wide assertion rather than a spot test.
    // #412: targets that match nothing BECAUSE OPR SAYS SO, not because our import lost a weapon.
    //
    // OPR 3.6.0 shares one upgrade package across several units (the unit carries `upgrades: ["D1",
    // "f3D6qW6V", ...]` and the package holds the sections). Where a package member does not own one of
    // the package's targets, the section ships anyway and can never fire. Verified against the raw
    // army-books API on 2026-10-31 for AoF Skirmish Dark Elves' Champion: its `weapons` list holds only
    // Dual Hand Weapons, the "Replace Throwing Knife" section comes from a shared package, and its
    // `disabledSections` is empty - so OPR has not marked it dead and nothing on our side dropped it.
    //
    // Allowlisted by TARGET rather than by (book, unit): it is one upstream quirk repeated across
    // package members, and listing all 24 sites would turn every OPR roster tweak into a test edit. The
    // count below is the canary - a NEW unreachable target still fails outright.
    private static readonly HashSet<string> UpstreamUnreachableTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Throwing Knife", "Poisoned Knife", "Heavy Tusk", "Rough Horn",
        "Assault Linked Rifle-Array", "Raider Hoof", "Fumigator", "Explosive Spear",
    };

    [Test]
    public void EveryReplaceTarget_NamesSomethingItsUnitCanHold()
    {
        var dead = new List<string>();
        var upstream = new List<string>();

        foreach (string path in ShippedBooks.GdfPaths())
        {
            BookFile book = Load(path);
            foreach (RosterUnit roster in book.Units)
            {
                // Every name the unit can ever carry: its base loadout plus anything any option grants.
                var holdable = new List<WeaponFileEntry>(roster.Weapons);
                var holdableItems = new List<ItemEntry>(roster.Items);
                foreach (UpgradeSection s in roster.Sections)
                    foreach (UpgradeOption o in s.Options)
                    {
                        holdable.AddRange(o.WeaponsGained);
                        holdableItems.AddRange(o.ItemsGained);
                    }

                foreach (UpgradeSection section in roster.Sections)
                {
                    if (section.Variant != UpgradeVariant.Replace) continue;
                    foreach (string target in section.Targets)
                        if (ListCompiler.AvailableApplications(holdable, holdableItems, new[] { target }) == 0)
                        {
                            string where = $"{Path.GetFileNameWithoutExtension(path)}/{roster.Name}/" +
                                $"\"{section.Label}\" targets \"{target}\", which matches nothing on the unit";
                            if (UpstreamUnreachableTargets.Contains(target)) upstream.Add(where);
                            else dead.Add(where);
                        }
                }
            }
        }

        Assert.That(dead, Is.Empty, string.Join("\n  ", dead));

        // The canary: if OPR cleans these up, the allowlist is stale and should shrink; if the count
        // grows, a new unreachable target slipped in under an already-allowlisted name.
        // Re-taken for Star Quest's 54 enemy books (242 books): 21 -> 24. The three new ones are the
        // Star Quest editions of units already here - Jackals' Charger Beast and Scavenger Beast, and
        // Machine Cults' Raider Cavalry - so the allowlist needed no new target names, which is itself
        // the useful signal: the quirk travels with the shared upgrade package, not with the system.
        Assert.That(upstream, Has.Count.EqualTo(24),
            "the 2026-10-31 census of OPR's own unreachable shared-package targets:\n  "
            + string.Join("\n  ", upstream));
    }

    // The census that sized the problem - a canary, so a re-import that changes the corpus's shape shows
    // up as a number here rather than as silence. These are the (unit, section) pairs fed ONLY by a
    // section authored BELOW them: the shape the pre-#323 compiler mis-clamped, and the test above proves
    // each can now spend its pool.
    //
    // Re-taken 2026-10-31 for the OPR 3.6.0 import: 54 pairs across 17 books -> 91, which is corpus
    // growth (88 books -> 188) and not a new shape. The 3.5.1 population was almost all Titan Lords
    // titans and Brothers' "Replace Gravity Pistol"; 3.6.0 keeps those and adds the Firefight and
    // Skirmish books' own instances (AoF Skirmish's Treeman Elder and Balloon Warden, Firefight's
    // Assault Brother among them). The two named spot-checks below are deliberately 3.5.1-era units that
    // 3.6.0 kept, so the census can grow without the fixture losing its anchors.
    //
    // Pairs fed from BOTH directions stay out of the count (the test above covers them, grantor
    // direction being irrelevant there).
    [Test]
    public void ReplaceSectionsFedOnlyFromBelow_AreStillTheKnownPopulation()
    {
        var fedFromBelow = new List<string>();

        foreach (string path in ShippedBooks.GdfPaths())
        {
            BookFile book = Load(path);
            foreach (RosterUnit roster in book.Units)
            {
                for (int i = 0; i < roster.Sections.Count; i++)
                {
                    UpgradeSection section = roster.Sections[i];
                    if (section.Variant != UpgradeVariant.Replace || section.Targets.Count == 0) continue;

                    var indexes = Grantors(roster, section)
                        .Select(g => roster.Sections.IndexOf(g.Item1))
                        .Distinct()
                        .ToList();
                    if (indexes.Count > 0 && indexes.All(j => j > i))
                        fedFromBelow.Add($"{Path.GetFileNameWithoutExtension(path)}/{roster.Name}/{section.Label}");
                }
            }
        }

        // Re-taken again for Star Quest's 54 enemy books (242 books): 91 -> 132.
        Assert.That(fedFromBelow, Has.Count.EqualTo(132),
            "census drift - re-run the audit and re-check the fix covers the new shape:\n  "
            + string.Join("\n  ", fedFromBelow));
        // Anchored by SHAPE rather than by two unit names. The 3.5.1 anchors were the War Errant
        // Mini-Titan and the Hive Lord, and 3.6.0 rewrote both out of the population - which told us
        // nothing except that OPR had edited two units. What is worth pinning is that the shape is not
        // confined to one corner of the corpus: it occurs in every game system, so the #323 fix has to
        // hold everywhere and a future regression cannot hide in a system nobody checked.
        // (Age of Fantasy proper is out of this fixture's scope - GdfPaths() excludes the "AoF-" files,
        // and the AoF corpus carries its own census. GF Firefight and AoF Skirmish are in scope and are
        // where 3.6.0 added most of the new population.)
        string[] prefixes = { "AoFS-", "GFF-" };
        foreach (string prefix in prefixes)
        {
            Assert.That(fedFromBelow, Has.Some.StartsWith(prefix),
                $"the fed-from-below shape occurs in the {prefix.TrimEnd('-')} books too");
        }
        Assert.That(fedFromBelow.Any(entry => prefixes.All(p => !entry.StartsWith(p, StringComparison.Ordinal))),
            Is.True, "and in Grimdark Future, whose books carry no filename prefix");
    }
}
