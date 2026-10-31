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

// #197 Strafing - the 12 corpus references, which were dead as a scope mismatch until the catalog rule was
// re-authored at weapon scope. Unlike most #197 slices this one changed NO book data: the books were right
// all along (every reference sits on a bomb weapon) and the catalog was the approximation.
//
// What is pinned here is that agreement, plus the two properties whose failure modes are silent:
//  - the rule attaches at Weapon scope, without which army load drops all 12 references again;
//  - it carries no fly-over passive, and its carriers therefore need Aircraft or Flying of their own. If a
//    future import adds a Strafing bomb to a grounded unit, the weapon can never be used - the engine warns,
//    but a shipped book should never reach that state.
// The mechanism (the attack, the pick, the weapon restriction) is pinned engine-side by
// StrafingRuleIntegrationTests.
[TestFixture]
public class StrafingShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [Test]
    public void TheCatalogRule_IsWeaponScoped_AndAttacksWithTheCarryingWeapon()
    {
        SpecialRuleDefinition strafing = CoreRuleCatalog.Strafing;

        Assert.That(strafing.Scope, Is.EqualTo(ERuleScope.Weapon),
            "every corpus reference sits on a weapon - at unit scope army load drops all 12");

        ActivatedAbility ability = strafing.Activated.Single();
        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Movement_OnMoveThroughEnemy));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>(), "'once per activation'");
        Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(1), "'pick ONE of them'");
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(ability.Effect, Is.InstanceOf<Effect.AttackWithThisWeapon>(),
            "'attack it with this weapon as if it was shooting' - a fixed hit count would ignore the " +
            "weapon's own Attacks, AP and Blast");
    }

    [Test]
    public void TheCatalogRule_GrantsNoFlyOver()
    {
        // The source rule presupposes a unit that can already move through enemies rather than granting it.
        // Keeping the passive here would also be inert: it is weapon-scoped now, and the movement hook that
        // MovementRuleQueries.CanMoveThroughEnemies fires never reads weapon rules.
        Assert.That(CoreRuleCatalog.Strafing.Passive, Is.Empty,
            "Aircraft and Flying grant the fly-over; Strafing only uses it");
    }

    // Every Strafing reference in the corpus, with the unit it belongs to and whether that unit can fly.
    private record StrafeSite(string Book, string Unit, string Weapon, bool CanFlyOver);

    private static IEnumerable<StrafeSite> StrafeSites() => Sites(IsStrafing);

    private static IEnumerable<StrafeSite> CrossingStrikeSites() => Sites(IsCrossingStrike);

    private static IEnumerable<StrafeSite> Sites(Func<SpecialRuleEntry, bool> names)
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                // A unit's own rules, plus any granted by wargear items it starts with.
                var baseRules = unit.Rules.Select(NameOf)
                    .Concat(unit.Items.SelectMany(item => item.Rules.Select(NameOf)))
                    .ToList();

                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    if (!weapon.SpecialRules.Any(names)) continue;
                    yield return new StrafeSite(bookName, unit.Name, weapon.Name, CanFly(baseRules));
                }

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                    {
                        foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        {
                            if (!weapon.SpecialRules.Any(names)) continue;

                            // The upgrade that grants the bomb may also grant the wings - Saurian's Gecko
                            // Champion is a footslogger until it buys the Pterodactyl, which brings both.
                            List<string> withOption = baseRules
                                .Concat(option.RulesGained.Select(NameOf))
                                .Concat(option.ItemsGained.SelectMany(item => item.Rules.Select(NameOf)))
                                .ToList();
                            yield return new StrafeSite(bookName, unit.Name, weapon.Name, CanFly(withOption));
                        }
                    }
            }
        }
    }

    private static string NameOf(SpecialRuleEntry rule) =>
        ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

    private static bool IsStrafing(SpecialRuleEntry rule) =>
        string.Equals(NameOf(rule), "Strafing", StringComparison.OrdinalIgnoreCase);

    // #412: OPR 3.6.0 deleted the Strafing special rule and published the same mechanic as Crossing
    // Strike ("once per activation, when this model moves through enemy units, pick one of them and
    // attack it with this weapon as if it was shooting"). The old name survives only inside weapon
    // names - Alien Hives' Rapacious Beast carries "Strafing Ripper Guns", whose special rules are now
    // just Fragment - so matching on the RULE name is what tells the two apart.
    private static bool IsCrossingStrike(SpecialRuleEntry rule) =>
        string.Equals(NameOf(rule), "Crossing Strike", StringComparison.OrdinalIgnoreCase);

    private static bool CanFly(IEnumerable<string> ruleNames) => ruleNames.Any(name =>
        string.Equals(name, "Flying", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Aircraft", StringComparison.OrdinalIgnoreCase));

    // Census re-taken for the OPR 3.6.0 import (2026-10-31, 188 books). Strafing's 12 corpus references
    // are GONE - 3.6.0 deleted the rule - so the census here is legitimately zero, and the two corpus
    // properties the fixture was built to protect have moved to Crossing Strike below.
    //
    // The CATALOG assertions stay, and are not dead weight: a saved list from before the import still
    // names Strafing, and BundledBookRulebook backfills the definition for exactly that case. If the
    // rule were weakened or re-scoped on the grounds that nothing references it any more, those old
    // lists would quietly lose the weapon.
    [Test]
    public void StrafingHasNoCorpusReferencesLeft_ButTheCatalogRuleStands()
    {
        Assert.That(StrafeSites().Count(), Is.EqualTo(0),
            "3.6.0 replaced Strafing with Crossing Strike - if a reference reappears, OPR reversed that "
            + "and the two rules need reconciling rather than both being carried");
        Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Strafing", out ResolvedRule resolved), Is.True,
            "old saved lists still name it");
        Assert.That(resolved.Definition.Scope, Is.EqualTo(ERuleScope.Weapon));
    }

    [Test]
    public void EveryCrossingStrikeReference_SitsOnAWeapon_AndResolvesCleanly()
    {
        List<StrafeSite> sites = CrossingStrikeSites().ToList();

        // Re-taken for Star Quest's 54 enemy books (242 books): 24 -> 41. Crossing Strike travelled to
        // the Star Quest editions of the same bombers, and every new carrier still has Aircraft or
        // Flying - which the fly-over assertion below is what actually checks.
        Assert.That(sites.Count, Is.EqualTo(41),
            "the 2026-10-31 census - a change here means the corpus moved, not the engine");

        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in BookRuleSupplement.LoadDefinitions(
                     File.ReadAllText(Path.Combine(BooksDirectory, "GdfRuleSupplement.json"))))
        {
            resolver.RegisterOrReplace(definition);
        }

        Assert.That(resolver.TryResolve("Crossing Strike", out ResolvedRule resolved), Is.True);
        Assert.That(resolved.Definition.Scope, Is.EqualTo(ERuleScope.Weapon),
            "every reference sits on a weapon - at unit scope army load would re-home it onto all of them");
    }

    // The inherited invariant, and the reason it still matters: Crossing Strike presupposes a model that
    // can already move through enemy units rather than granting it, so a carrier without Aircraft or
    // Flying owns a weapon it can never fire. All 24 carriers clear this today.
    [Test]
    public void EveryCrossingStrikeCarrier_CanMoveThroughEnemies()
    {
        List<string> grounded = CrossingStrikeSites()
            .Where(site => !site.CanFlyOver)
            .Select(site => $"{site.Book}: {site.Unit} ({site.Weapon})")
            .ToList();

        Assert.That(grounded, Is.Empty,
            "Crossing Strike grants no fly-over, so a carrier without Aircraft or Flying can never use "
            + "the weapon at all: " + string.Join(", ", grounded));
    }
}
