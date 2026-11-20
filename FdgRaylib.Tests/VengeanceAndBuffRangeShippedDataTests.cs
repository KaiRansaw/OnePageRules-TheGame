using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #468: Vengeance and Extended Buff Range against the 3.6.0 wording, in the shipped books and through a real
// compile. Vengeance: "as many Vengeance tokens ... as models with this rule in this unit at the beginning of the
// game" (GF / GFF / AoF / AoFS; the Quest books give one marker per unconscious hero). Extended Buff Range: "within
// 24\" of another friendly unit with this rule that has a Hero in it" (GF / AoF) / "that is within 6\" of a
// friendly Hero" (GFF / AoFS / Quest), Quest adding "Only one use per game."
[TestFixture]
public class VengeanceAndBuffRangeShippedDataTests
{
    private static readonly string[] QuestSystems = { "AoF Quest", "GF Star Quest" };
    private static readonly string[] FullSystems = { "Grimdark Future", "Age of Fantasy" };

    private static IEnumerable<(string System, string File, SpecialRuleDefinition Rule)> Definitions(string name)
    {
        foreach (string path in ShippedBooks.AllPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string system = new DirectoryInfo(Path.GetDirectoryName(path)!).Name;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions.Where(r => r.Name == name))
                yield return (system, Path.GetFileName(path), rule);
        }
    }

    [Test]
    public void Vengeance_CountsTheStartingCarriers_ExceptInTheQuestGames()
    {
        var seen = Definitions("Vengeance").ToList();
        Assert.That(seen, Is.Not.Empty);
        foreach ((string system, string file, SpecialRuleDefinition rule) in seen)
        {
            Effect.GrantTokenToKiller grant = rule.Passive.Select(h => h.Effect).OfType<Effect.GrantTokenToKiller>().Single();
            if (QuestSystems.Contains(system))
                Assert.That(grant.Count, Is.EqualTo(new ValueSource.Literal(1)), $"{file}: one marker per unconscious hero");
            else
                Assert.That(grant.Count, Is.InstanceOf<ValueSource.StartingRuleCarrierCount>(), $"{file}: as many as models with the rule");
        }
    }

    [Test]
    public void ExtendedBuffRange_FollowsEachGamesWording()
    {
        var seen = Definitions("Extended Buff Range").ToList();
        Assert.That(seen, Is.Not.Empty);
        foreach ((string system, string file, SpecialRuleDefinition rule) in seen)
        {
            Effect.EnableBuffRelay relay = rule.Passive.Select(h => h.Effect).OfType<Effect.EnableBuffRelay>().Single();
            Assert.That(relay.RangeInches, Is.EqualTo(12f), file);
            Assert.That(relay.TargetWithinInches, Is.EqualTo(24f), file);
            Assert.That(relay.HeroWithinInches, Is.EqualTo(FullSystems.Contains(system) ? 0f : 6f), file);
            Assert.That(relay.OncePerGame, Is.EqualTo(QuestSystems.Contains(system)), file);
        }
    }

    [Test]
    public void ACombinedUnitThatBoughtVengeanceOnBothHalves_StartsWithTwoCarriers()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(ShippedBooks.Path("EternalDynasty" + BookFile.EXTENSION_WITH_PERIOD)), RuleJson.Options)!;
        RosterUnit warriors = book.Units.Single(u => u.Name == "Warriors");
        UpgradeSection section = warriors.Sections.Single(s => s.Options.Any(o => o.Label.StartsWith("Honor-Bound")));
        UpgradeOption honor = section.Options.Single(o => o.Label.StartsWith("Honor-Bound"));
        BuilderUnit Half(string id, string? with, bool buy) => new()
        {
            RosterUnitId = warriors.Id, ModelCount = warriors.BaseModelCount, Id = id, CombinedWithId = with,
            Choices = buy ? new List<UpgradeChoice> { new() { SectionId = section.Id, OptionId = honor.Id } } : new(),
        };

        BuiltArmyFile both = ListCompiler.Compile(book, new BuilderList
        {
            Name = "t", PointsLimit = 5000, Units = { Half("a", null, true), Half("b", "a", true) },
        });
        BuiltArmyFile one = ListCompiler.Compile(book, new BuilderList
        {
            Name = "t", PointsLimit = 5000, Units = { Half("a", null, true), Half("b", "a", false) },
        });

        Assert.That(both.Units.Single().RuleCarriers?["Vengeance"], Is.EqualTo(2));
        Assert.That(one.Units.Single().RuleCarriers?["Vengeance"], Is.EqualTo(1));

        var store = FDG.Data.GameDataStore.GameDataStoreBuilder.GetDefault();
        var unit = new FDG.UnitData(new PlayerID(Guid.NewGuid()), both.Units.Single(), store);
        Assert.That(unit.StartingRuleCarriers?["Vengeance"], Is.EqualTo(2), "the record reaches the unit in play");
    }
}
