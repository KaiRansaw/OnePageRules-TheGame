using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 slice 0 — the regression guard for the scope-mismatch bug: 157 references to rules the engine
// fully implements never attached, because ArmyListRuleResolution refuses a definition whose declared
// ERuleScope differs from the site naming it, and the imported corpus disagreed with the catalog.
//
// Neither direction is a mismatch any more, and #412 closed the second one. A weapon-scoped rule named at
// UNIT level is re-homed by both attachment paths (ListCompiler onto the targeted weapon, army-load onto
// every weapon otherwise). A UNIT-scoped rule named on a WEAPON is PROMOTED onto the carrier by
// UnitData.ResolveWeaponRules - which the 3.6.0 corpus made unavoidable: Parry and Brace are shield and
// spear wargear but are unit-scoped defensive rules by nature, and Spell Amplifier is an item that grants
// a standing caster property. Dropping those would have silently disarmed 178 of the 188 books.
//
// So this fixture no longer asserts that the mismatch is ABSENT - that assertion was true only of the
// pre-3.6.0 corpus and would now fail on almost every book while nothing was actually wrong. It asserts
// the thing that matters and did not change: no rule reference in a book is DROPPED, whichever site names
// it. The check runs the real ArmyListRuleResolution ladder rather than comparing scopes by hand, so it
// cannot drift from what attaches at load the way the old scope comparison did.
//
// Without this, the next OPR import silently reintroduces the bug: the books are regenerated data, and a
// rule quietly doing nothing looks exactly like a rule working.
[TestFixture]
public class BookRuleScopeTests
{
    // A rule reference the load path still drops, with the reason it is tolerated. Each entry is a known
    // engine gap, not a licence for misauthored data - a stale entry (the reference starts resolving)
    // fails the fixture just as a new drop does.
    // Empty: #412 defined every rule name the 3.6.0 corpus uses, so nothing is dropped anywhere. Kept
    // (with its guard below) because the mechanism is what matters - the next re-import that introduces an
    // undefined or unattachable name has somewhere to be justified.
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>();

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<TestCaseData> Books() =>
        Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
            .OrderBy(path => path)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path)));

    /// <summary>
    /// Where a rule sits in a book, and the scope that site attaches at. The ENTRY rides along, not just
    /// its name: the load path checks argument arity too, so a Tough(3) reference reduced to the bare
    /// string "Tough" fails for a reason that exists only in the test.
    /// </summary>
    private record Reference(string Site, SpecialRuleEntry Entry, string Name, ERuleScope AttachesAt);

    [TestCaseSource(nameof(Books))]
    public void NoRuleReferenceInABookIsDroppedAtLoad(string bookPath)
    {
        BookFile book = LoadBook(bookPath);
        IRuleResolver resolver = ResolverFor(book);

        List<string> dropped = DropsIn(book, resolver)
            .Where(drop => !Allowlist.ContainsKey(drop.Name))
            .Select(drop => $"{drop.Site}: '{drop.Name}' is dropped at army load ({drop.Reason}).")
            .ToList();

        Assert.That(dropped, Is.Empty,
            $"{Path.GetFileName(bookPath)} names rules that do nothing in a game:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", dropped));
    }

    [Test]
    public void AllowlistedRulesAreStillDroppedSomewhere()
    {
        var stillDropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string bookPath in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories))
        {
            BookFile book = LoadBook(bookPath);
            IRuleResolver resolver = ResolverFor(book);
            foreach ((string _, string name, string _) in DropsIn(book, resolver)) stillDropped.Add(name);
        }

        List<string> stale = Allowlist.Keys.Where(name => !stillDropped.Contains(name)).ToList();
        Assert.That(stale, Is.Empty,
            "These rules are allowlisted as known load-path gaps but now attach cleanly - delete their " +
            "allowlist entries: " + string.Join(", ", stale));
    }

    /// <summary>
    /// Every reference in <paramref name="book"/> that the real load path would throw away, asked through
    /// <see cref="ArmyListRuleResolution.ResolveOrDescribeDrop"/> itself.
    /// <para>
    /// <c>attachmentScope: null</c> for BOTH site kinds, which is what the load paths now do and is the
    /// whole point: neither direction of scope mismatch is fatal any more. A unit site resolves through
    /// <c>ResolveAnyScope</c> and re-homes a weapon-scoped rule onto the unit's weapons (#197); a weapon
    /// site promotes a unit-scoped rule onto the carrier (#412). Passing the site's own scope here would
    /// re-impose a gate neither path applies and fail ~1,500 references that attach perfectly well -
    /// which is exactly the drift this fixture exists to catch, pointed the wrong way.
    /// </para>
    /// So what is left to report is what genuinely cannot attach anywhere: a name with no definition, and
    /// an (X) rule whose reference is missing its number.
    /// </summary>
    private static IEnumerable<(string Site, string Name, string Reason)> DropsIn(BookFile book,
        IRuleResolver resolver)
    {
        foreach (Reference reference in ReferencesIn(book))
        {
            ArmyListRuleResolution.ResolveOrDescribeDrop(resolver, reference.Entry,
                attachmentScope: null, reference.Site, out RuleDrop? drop);

            if (drop != null)
            {
                yield return (reference.Site, drop.Value.RuleName, drop.Value.Reason.ToString());
            }
        }
    }

    // The end-to-end case behind the owner's ruling (2026-07-09): a weapon rule applies only when THAT
    // weapon fires - it must not spread to the unit's other weapons, and must not fold onto the unit.
    //
    // This used to pin one hand-picked fixture (DAO Union's Sniper Drones buying a Drone Controller that
    // upgraded their Pulse Rifles, while their melee Taser had to stay clean). OPR 3.6.0 rewrote that
    // unit - no Pulse Rifles, no Drone Controller, no Taser - and the test died on a missing element
    // while nothing about the behaviour had changed. A fixture naming data OPR owns is a fixture that
    // breaks on every re-import, so the case is now DISCOVERED instead: one qualifying upgrade per
    // bundled book, found by shape rather than by name. Broader coverage than the single unit had, and
    // nothing to re-point next time the corpus moves.
    [TestCaseSource(nameof(Books))]
    public void AWeaponUpgradesRulesStayOnThatWeapon(string bookPath)
    {
        BookFile book = LoadBook(bookPath);

        // Candidates are found by SHAPE, then confirmed by COMPILING: section semantics vary (per-model
        // steppers, replace-all, sections fed from another section), and a shape that looks like a
        // one-for-one swap does not always compile into one from a single Count=1 choice. Whether it does
        // is the ListCompiler fixtures' business, not this one's - so the first candidate that actually
        // lands its weapon is the subject here, and only a book with NO such upgrade is skipped.
        foreach ((RosterUnit unit, UpgradeSection section, UpgradeOption option, WeaponFileEntry gained)
                 in SingleWeaponUpgrades(book))
        {
            var list = new BuilderList
            {
                Name = "Scope Test", BookName = book.Name, PointsLimit = 100000,
                Units =
                {
                    new BuilderUnit
                    {
                        RosterUnitId = unit.Id,
                        Choices = { new UpgradeChoice { SectionId = section.Id, OptionId = option.Id, Count = 1 } },
                    },
                },
            };

            UnitFileEntry compiled = ListCompiler.Compile(book, list).Units.Single();
            WeaponFileEntry? upgraded = compiled.Weapons.FirstOrDefault(w => w.Name == gained.Name);
            if (upgraded == null) continue;

            string[] granted = gained.SpecialRules.Select(r => r.PrintableName).ToArray();
            IEnumerable<string> RuleNames(IEnumerable<SpecialRuleEntry> rules) =>
                rules.Select(r => r.PrintableName);

            Assert.That(RuleNames(upgraded.SpecialRules), Is.SupersetOf(granted),
                $"{unit.Name} / '{gained.Name}' must carry the rules its upgrade granted.");

            foreach (WeaponFileEntry other in compiled.Weapons.Where(w => w.Name != gained.Name))
            {
                // Only rules the OTHER weapon did not already own in the roster entry: a unit can
                // legitimately carry two weapons that both have Rending from the start.
                string[] alreadyOwned = unit.Weapons
                    .Where(w => w.Name == other.Name)
                    .SelectMany(w => w.SpecialRules.Select(r => r.PrintableName))
                    .ToArray();

                Assert.That(RuleNames(other.SpecialRules).Intersect(granted.Except(alreadyOwned)), Is.Empty,
                    $"{unit.Name} / '{other.Name}' is not the upgrade's target and must not inherit its rules.");
            }

            Assert.That(RuleNames(compiled.SpecialRules).Intersect(granted), Is.Empty,
                "Rules placed on their target weapon do not also fold onto the unit.");
            return;
        }

        Assert.Ignore($"{Path.GetFileName(bookPath)} has no single-weapon upgrade that grants rules.");
    }

    /// <summary>
    /// Every upgrade in <paramref name="book"/> that replaces ONE named weapon with a weapon carrying
    /// special rules, on a unit that also holds at least one other weapon - the shape the owner's ruling
    /// is about.
    /// </summary>
    private static IEnumerable<(RosterUnit, UpgradeSection, UpgradeOption, WeaponFileEntry)>
        SingleWeaponUpgrades(BookFile book)
    {
        foreach (RosterUnit unit in book.Units)
        {
            if (unit.Weapons.Count < 2) continue;

            foreach (UpgradeSection section in unit.Sections)
            {
                if (section.Targets == null || section.Targets.Count != 1) continue;

                foreach (UpgradeOption option in section.Options)
                {
                    // Exactly one weapon gained, so the assertion has an unambiguous subject, and it must
                    // not share a name with a weapon the unit already has (otherwise "the other weapons"
                    // and "the upgraded weapon" are the same entry).
                    if (option.WeaponsGained.Count != 1) continue;

                    WeaponFileEntry gained = option.WeaponsGained[0];
                    if (gained.SpecialRules.Count == 0) continue;
                    if (unit.Weapons.Any(w => w.Name == gained.Name)) continue;
                    if (option.ItemsGained.Count > 0 || option.RulesGained.Count > 0) continue;

                    yield return (unit, section, option, gained);
                }
            }
        }
    }

    // #197 (Darkborn): OPR names two mechanically different rules bare "Darkborn" across armies, so the bare
    // name resolved to neither catalog variant and all 59 references were dead. The bundled books now name the
    // army's variant, and each resolves to the fully-built definition (its mechanics are proven by
    // RangeModifierRuleIntegrationTests). This is the end-to-end guard the reference-count fix rests on: a
    // regression to the bare name - or a re-import that reintroduces it without the importer's disambiguation -
    // would leave the rule silently doing nothing, which looks exactly like a rule working.
    [TestCase("DarkBrothers", "Darkborn (Defensive)")]
    [TestCase("DarkPrimeBrothers", "Darkborn (Offensive)")]
    public void Darkborn_ResolvesToTheArmyVariant(string bookFile, string expectedVariant)
    {
        BookFile book = LoadBook(ShippedBooks.Path(bookFile + BookFile.EXTENSION_WITH_PERIOD));
        IRuleResolver resolver = ResolverFor(book);

        List<Reference> darkbornRefs = ReferencesIn(book)
            .Where(r => r.Name.StartsWith("Darkborn", StringComparison.Ordinal)).ToList();

        Assert.That(darkbornRefs, Is.Not.Empty, $"{bookFile} should still reference Darkborn.");
        foreach (Reference reference in darkbornRefs)
        {
            Assert.That(reference.Name, Is.EqualTo(expectedVariant),
                $"{reference.Site}: bare 'Darkborn' resolves to nothing - it must name the army's variant.");
            Assert.That(resolver.TryResolve(reference.Name, out ResolvedRule resolved), Is.True,
                $"{reference.Site}: '{reference.Name}' must resolve to a definition.");
            Assert.That(resolved.Definition.Name, Is.EqualTo(expectedVariant));
        }
    }

    private static BookFile LoadBook(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    private static IRuleResolver ResolverFor(BookFile book)
    {
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions) resolver.RegisterOrReplace(definition);
        return resolver;
    }

    /// <summary>Every rule name a book states, paired with the scope its attachment site resolves at.
    /// Mirrors what army load does: weapon profiles attach at Weapon scope; a unit's own rules and its
    /// wargear rule-bundles (which ListCompiler flattens into the unit) attach at Unit scope.</summary>
    private static IEnumerable<Reference> ReferencesIn(BookFile book)
    {
        foreach (RosterUnit unit in book.Units)
        {
            foreach (Reference reference in RulesOf(unit.Rules, $"{unit.Name}", ERuleScope.Unit))
                yield return reference;

            foreach (ItemEntry item in unit.Items)
                foreach (Reference reference in RulesOf(item.Rules, $"{unit.Name} / item '{item.Name}'", ERuleScope.Unit))
                    yield return reference;

            foreach (WeaponFileEntry weapon in unit.Weapons)
                foreach (Reference reference in RulesOf(weapon.SpecialRules, $"{unit.Name} / weapon '{weapon.Name}'", ERuleScope.Weapon))
                    yield return reference;

            foreach (UpgradeSection section in unit.Sections)
                foreach (UpgradeOption option in section.Options)
                {
                    string where = $"{unit.Name} / option '{option.Label}'";

                    foreach (Reference reference in RulesOf(option.RulesGained, where, ERuleScope.Unit))
                        yield return reference;

                    foreach (ItemEntry item in option.ItemsGained)
                        foreach (Reference reference in RulesOf(item.Rules, $"{where} / item '{item.Name}'", ERuleScope.Unit))
                            yield return reference;

                    foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        foreach (Reference reference in RulesOf(weapon.SpecialRules, $"{where} / weapon '{weapon.Name}'", ERuleScope.Weapon))
                            yield return reference;
                }
        }
    }

    private static IEnumerable<Reference> RulesOf(IEnumerable<SpecialRuleEntry> rules, string site, ERuleScope scope) =>
        rules.Select(rule => new Reference(site, rule,
            ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName, scope));
}
