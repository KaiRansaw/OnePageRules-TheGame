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

// #197 Inquisitorial Agent - 20 references, all in Human Inquisition, and the single largest name left in
// the audit. The mechanism is pinned engine-side by InquisitorialAgentRuleIntegrationTests; these pin the
// authored JSON, and one structural assumption the engine leans on.
[TestFixture]
public class InquisitorialAgentShippedDataTests
{
    private const string RuleName = "Inquisitorial Agent";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition() =>
        Supplement().Single(r => string.Equals(r.Name, RuleName, StringComparison.OrdinalIgnoreCase));

    [Test]
    public void ItIsAOncePerGameSelfReactivation_WithBothRiders()
    {
        ActivatedAbility ability = Definition().Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnNextActivatorRequested),
            "the hook DeterminePlayerTurnStage fires before the player picks their next unit");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(), "'once per game'");
        Assert.That(ability.TargetSelector!.TargetAffinity, Is.EqualTo(ETargetAffinity.Self));
        Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "'if ALL models in this unit have this rule'");
        Assert.That(ability.Effect, Is.EqualTo(
                new Effect.Reactivate(ClearsFatigue: true, ArmyRoundQuotaDivisor: 3)),
            "'stops being fatigued when activated for the second time', and 'only up to one THIRD of the " +
            "units in the army with this rule ... may use it in a single round'");
    }

    [Test]
    public void MartialProwess_CarriesTheSameTwoRiders()
    {
        // #422: this case used to assert the opposite - "Martial Prowess says nothing about fatigue ... and
        // has no army-wide cap" - as the control for both riders defaulting to off. That was true of the
        // 3.5.1 wording the engine was written against. The 3.6.0 rule is word for word the Inquisitorial
        // Agent's shape: "...(stops being Fatigued when activated again). Only up to one third of the units
        // in the army with this rule at the beginning of the game (rounding up) may use it in a single
        // round." The control for "neither rider" now lives in the engine suite, on a reactivation that
        // declares neither (ReactivateWithNoRiders_KeepsFatigue_AndIgnoresTheQuota).
        var effect = (Effect.Reactivate)CoreRuleCatalog.MartialProwess.Activated.Single().Effect;

        Assert.Multiple(() =>
        {
            Assert.That(effect.ClearsFatigue, Is.True,
                "'stops being Fatigued when activated again'");
            Assert.That(effect.ArmyRoundQuotaDivisor, Is.EqualTo(3),
                "'only up to one third of the units in the army with this rule ... in a single round'");
        });
    }

    // ---- The corpus ----------------------------------------------------------------------------------

    private record Site(string Book, string Unit);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (string name in RuleNamesOn(unit))
                    if (string.Equals(name, RuleName, StringComparison.OrdinalIgnoreCase))
                        yield return new Site(bookName, unit.Name);
        }
    }

    private static IEnumerable<string> RuleNamesOn(RosterUnit unit)
    {
        foreach (SpecialRuleEntry rule in unit.Rules) yield return NameOf(rule);
        foreach (ItemEntry item in unit.Items)
            foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);

        foreach (UpgradeSection section in unit.Sections)
            foreach (UpgradeOption option in section.Options)
            {
                foreach (SpecialRuleEntry rule in option.RulesGained) yield return NameOf(rule);
                foreach (ItemEntry item in option.ItemsGained)
                    foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);
            }
    }

    private static string NameOf(SpecialRuleEntry rule) =>
        ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

    [Test]
    public void EveryReference_ResolvesAgainstItsOwnBook()
    {
        List<Site> sites = Sites().ToList();

        // Census re-taken for the OPR 3.6.0 import (2026-10-31, 188 books): 20 references became 39 as
        // the Firefight edition of the Human Inquisition arrived. The per-book resolve loop below is what
        // this test is really for and it now covers both books automatically.
        // Re-taken again the same day, for Star Quest's 54 enemy books (242 books): 39 -> 60, as the
        // Star Quest edition of the Human Inquisition joined. The per-book resolve loop covers it too.
        Assert.That(sites.Count, Is.EqualTo(60),
            "the 2026-10-31 census - a change here means the corpus moved, not the engine");

        foreach (string bookName in sites.Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(ShippedBooks.Path(bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                resolver.RegisterOrReplace(definition);

            Assert.That(resolver.TryResolve(RuleName, out ResolvedRule resolved), Is.True, bookName);
            Assert.That(resolved.Definition.Activated, Is.Not.Empty, bookName);
        }
    }

    // The quota counts the roster off ArmyData.UnitBindings, which is append-only - so it equals the
    // GAME-START roster only as long as nothing adds units to the army mid-game. Spawn, Split and
    // Reinforcement all do. No book pairs one with Inquisitorial Agent today; if one ever does, the
    // quota would silently drift upward, so this fails loudly instead.
    [Test]
    public void NoBookPairsItWithARuleThatCreatesUnitsMidGame()
    {
        string[] unitCreating = { "Spawn", "Split", "Reinforcement" };

        var offenders = new List<string>();
        foreach (string bookName in Sites().Select(s => s.Book).Distinct())
        {
            string json = File.ReadAllText(
                ShippedBooks.Path(bookName + BookFile.EXTENSION_WITH_PERIOD));

            foreach (string creator in unitCreating)
            {
                if (json.Contains($"\"{creator}\"", StringComparison.Ordinal))
                    offenders.Add($"{bookName} carries {creator}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "a unit-creating rule in the same book would inflate the game-start roster the quota counts: "
            + string.Join(", ", offenders));
    }
}
