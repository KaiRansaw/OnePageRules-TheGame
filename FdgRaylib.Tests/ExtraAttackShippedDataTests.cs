using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P16 - the one-shot extra-attack primitive and its two corpus rules:
//   Takedown Strike (OPR eyMkgYDVrP7C) - "Once per game, when it's this model's turn to strike in melee,
//     it may make X attacks at Quality 2+ with AP(2), Deadly(3), and Takedown."
//   Takedown Shot (OPR LPEKodkJ6xPS) - the same, "when this model shoots ... against the target".
// Both were dead no-definition until 2026-07-30. The mechanism is pinned engine-side by
// ExtraAttackRuleIntegrationTests; these pin the authored JSON, the corpus census the design leans on, the
// embedded book copies, and a real-book compile.
//
// #412, and the reason this fixture changed shape: OPR 3.6.0 gave BOTH rules a rating. 3.5.1 said "one
// attack"; 3.6.0 says "X attacks", `hasRating: true`, and the books now reference them as Takedown
// Strike(1) / Takedown Shot(1). Our definitions hard-coded a single attack, so the import started
// emitting coreNumeric entries the fixture's Core-only matcher could not see - which is why its censuses
// read 0 rather than merely moving. Effect.ExtraAttack.Attacks is a ValueSource now and both definitions
// read argument 0, so a Takedown Strike(3) makes three attacks instead of silently making one.
//
// The explicit book lists are gone with them: 5 refs in 5 books became 21 in 20, and 2 in 2 became 7 in
// 7, as Firefight and Skirmish arrived with their own copies. A reader cannot check a 20-name list, so
// what used to be asserted against those literals is now asserted against the census itself - every book
// that REFERENCES a rule must also EMBED it, which is the property the lists were standing in for.
[TestFixture]
public class ExtraAttackShippedDataTests
{
    private const string Strike = "Takedown Strike";
    private const string Shot = "Takedown Shot";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static BookFile LoadBook(string name) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(ShippedBooks.Path(name + BookFile.EXTENSION_WITH_PERIOD)),
        RuleJson.Options)!;

    private static SpecialRuleDefinition Supplement(string ruleName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == ruleName);

    // ---- The authored definitions ----------------------------------------------------------------------

    [TestCase(Strike, true)]
    [TestCase(Shot, false)]
    public void EachRule_IsAOncePerGameExtraAttackAtTheAttackWindow(string ruleName, bool melee)
    {
        SpecialRuleDefinition rule = Supplement(ruleName);

        Assert.That(rule.Passive, Is.Empty, "the whole rule is the activated ability - nothing fires passively");
        ActivatedAbility ability = rule.Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Combat_OnAttackWindow),
            "the only hook ResolveExtraAttackStage gathers offers at; anywhere else is dead data");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(), "'once per game'");

        // The combat-kind split is the ONLY difference between the two rules, and it is what keeps a melee
        // strike out of a shooting action and vice versa.
        if (melee)
        {
            Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.IsMelee>());
        }
        else
        {
            Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.Not>());
            Assert.That(((Condition.Not)ability.AvailableWhen).Inner, Is.InstanceOf<Condition.IsMelee>());
        }

        // Unit scope is exact here only because every carrier is a single-model unit - pinned below.
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(1),
            "3.6.0's rating: the books say Takedown Strike(1), and a 0-argument definition would drop the "
            + "number on the floor");
    }

    // The profile IS the rule. Each rider is an existing weapon rule rather than new vocabulary: "at
    // Quality 2+" is Reliable (a QualityFloor folded by minimum), and Deadly(3) / Takedown are themselves.
    // A dropped name here is a silent partial rule, so all three are named explicitly.
    [TestCase(Strike)]
    [TestCase(Shot)]
    public void TheProfile_IsXAttacksAtApTwoCarryingReliableDeadlyAndTakedown(string ruleName)
    {
        var effect = Supplement(ruleName).Activated.Single().Effect as Effect.ExtraAttack;

        Assert.That(effect, Is.Not.Null, "the effect must be ExtraAttack - nothing else runs at this hook");
        Assert.That(effect!.Attacks, Is.EqualTo(new ValueSource.Arg(0)),
            "'X attacks' - read from the rating, not a literal. A ValueSource.Literal here would make "
            + "Takedown Strike(3) deal one attack and say nothing about it.");
        Assert.That(effect.ArmorPenetration, Is.EqualTo(2), "AP(2)");
        Assert.That(effect.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }),
            "Reliable = 'at Quality 2+'; the argument on Deadly(3) has to survive as text");
        Assert.That(effect.WeaponName, Is.EqualTo(ruleName),
            "the synthetic weapon is named for the rule, so the log and dice rows self-attribute");
    }

    // ---- The corpus census the design leans on ---------------------------------------------------------

    private record Site(string Book, string Unit, int MinModels, int MaxModels, bool FromItem);

    private static IEnumerable<Site> Sites(string ruleName)
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                if (unit.Rules.Any(r => Names(r, ruleName)))
                {
                    yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels, false);
                }

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                    {
                        bool hit = option.RulesGained.Any(r => Names(r, ruleName))
                            || option.ItemsGained.Any(i => i.Rules.Any(r => Names(r, ruleName)));
                        if (hit)
                        {
                            yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels, true);
                        }
                    }
            }
        }
    }

    // #412: a rated reference deserialises as SpecialRuleEntry_CoreNumeric, not _Core, so matching on the
    // concrete type made every Takedown Strike(1) in the 3.6.0 corpus invisible and the censuses read 0.
    // Asked through the resolution ladder's own name extractor instead, which is what the load path uses.
    private static bool Names(SpecialRuleEntry entry, string ruleName) =>
        string.Equals(ArmyListRuleResolution.DescribeRuleEntry(entry).lookupName, ruleName,
            StringComparison.Ordinal);

    // The census that made unit scope safe. "Upgrade with one" grants the rule to ONE model, which would be
    // the per-model scoping problem Sergeant had - except every carrier is a 1-model hero, so unit scope and
    // model scope are the same thing. If a book update ever puts one of these on a multi-model unit, the
    // scoping has to be re-decided (a Unit-scoped rule would offer the ability once per unit, not per model).
    // Re-taken 2026-10-31 for the 3.6.0 import: 5 -> 21 and 2 -> 7, as Firefight and Skirmish arrived
    // with their own copies of the same heroes. Every carrier is still a 1-model unit, which is the only
    // reason unit scope reads correctly - that is what the loop below actually guards.
    [TestCase(Strike, 21)]
    [TestCase(Shot, 7)]
    public void EveryCarrier_IsASingleModelUnit(string ruleName, int expectedRefs)
    {
        List<Site> sites = Sites(ruleName).ToList();

        Assert.That(sites.Count, Is.EqualTo(expectedRefs),
            $"the audit's {expectedRefs} references to {ruleName} - a change here means the corpus moved");

        foreach (Site site in sites)
        {
            Assert.That(site.MinModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxModels, Is.EqualTo(1),
                $"{site.Book}/{site.Unit}: a multi-model carrier breaks the unit-scope reading");
        }
    }

    // What the two hard-coded book lists were standing in for, asserted directly: army load reads the
    // BOOK's embedded copy, so a book that names a rule and does not embed its definition fields nothing.
    // Derived from the census rather than listed, so a re-import that spreads a rule to another twenty
    // books widens the check instead of breaking it.
    [TestCase(Strike)]
    [TestCase(Shot)]
    public void EveryReferencingBook_EmbedsTheDefinition(string ruleName)
    {
        string[] books = Sites(ruleName).Select(s => s.Book).Distinct().OrderBy(b => b).ToArray();

        Assert.That(books, Is.Not.Empty, $"{ruleName} must still be referenced somewhere");
        foreach (string bookName in books)
        {
            Assert.That(LoadBook(bookName).RuleDefinitions.Select(d => d.Name), Does.Contain(ruleName),
                $"{bookName} references {ruleName} but embeds no definition for it - re-run --apply-rules");
        }
    }

    // #197 P16 lifted BuildTargetListStage's melee gate so Takedown Strike could pick its victim, and
    // noted the lift was a no-change for existing data only because no MELEE weapon in the corpus carried
    // Takedown - every Takedown sat on a ranged profile. The comment ended "if that ever stops being
    // true, the individual-model pick starts appearing in ordinary close combat and the ruling needs
    // revisiting."
    //
    // #412: it stopped being true. OPR 3.6.0 added exactly one - High Elf Fleets' Stingers may trade a
    // Shard Pistol and Stinger Blade for "Stinger Dagger (A2, AP(1), Bane, Takedown)", a range-0 weapon.
    // Revisited, and the lift stands: 3.6.0's Takedown text ("this model may pick any model in the target
    // unit as its individual target for this weapon's attacks ... Takedown attacks must be resolved
    // before other weapons") names no combat kind, so a melee Takedown picking a model is the rule
    // working, not leaking. The gate the lift removed would now be suppressing an intended behaviour.
    //
    // So this becomes a census rather than a prohibition: the melee carriers are a known population of
    // one, and a NEW one still fails here so the ruling gets revisited rather than assumed.
    [Test]
    public void TheOnlyMeleeTakedownCarriers_AreTheKnownPopulation()
    {
        var offenders = new List<string>();

        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    Record(bookName, unit.Name, weapon, offenders);
                }

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                        foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        {
                            Record(bookName, unit.Name, weapon, offenders);
                        }
            }
        }

        Assert.That(offenders, Is.EqualTo(new[] { "HighElfFleets/Stingers/Stinger Dagger" }),
            "a melee weapon carrying Takedown gets the individual-model pick in close combat - intended "
            + "for this one, so a new entry here needs the same deliberate look this one got");
    }

    private static void Record(string book, string unit, WeaponFileEntry weapon, List<string> offenders)
    {
        // Range 0 IS melee in this engine (IWeapon.IsMelee).
        if (weapon.RangeInches > 0f) return;
        if (weapon.SpecialRules.Any(r => Names(r, "Takedown")))
        {
            offenders.Add($"{book}/{unit}/{weapon.Name}");
        }
    }

    // ---- The embedded copies --------------------------------------------------------------------------

    [Test]
    public void TheEmbeddedBookCopies_CarryTheFullProfile()
    {
        foreach (string ruleName in new[] { Strike, Shot })
        {
            foreach (string bookName in Sites(ruleName).Select(s => s.Book).Distinct().OrderBy(b => b))
            {
                SpecialRuleDefinition embedded = LoadBook(bookName).RuleDefinitions
                    .Single(d => d.Name == ruleName);

                var effect = embedded.Activated.Single().Effect as Effect.ExtraAttack;
                Assert.That(effect, Is.Not.Null,
                    $"{bookName}/{ruleName}: the embedded copy lost its effect - re-run --apply-rules");
                Assert.That(effect!.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }),
                    $"{bookName}/{ruleName}: army load reads the BOOK's copy, so the riders must travel too");
                Assert.That(embedded.Activated.Single().Cost, Is.InstanceOf<Cost.OncePerGame>(), bookName);
            }
        }
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void ClanHandler_BuyingASpyAssassinSniper_ResolvesTakedownShotOnTheUnit()
    {
        BookFile book = LoadBook("RatmenClans");
        RosterUnit handler = book.Units.Single(u => u.Name == "Clan Handler");
        UpgradeSection section = handler.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains(Shot)));
        UpgradeOption option = section.Options.Single(o => o.Label.Contains(Shot));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = handler.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = option.Id!, Count = 1 } },
                },
            },
        });

        UnitFileEntry unit = army.Units.Single();
        // "Takedown Shot(1)", not "Takedown Shot": 3.6.0 rates the rule, so the rating rides in the
        // printable name the way Tough(3)'s does.
        Assert.That(unit.SpecialRules.Select(r => r.PrintableName), Does.Contain(Shot + "(1)"),
            "the item's rule flattens onto the unit, which is exact for a 1-model hero");
    }
}
