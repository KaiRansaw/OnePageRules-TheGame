using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The shipped books after the 2026-11-13 Army Forge refresh: spells need line of sight only where their own words
/// say "in line of sight" (Caster(X) itself says only "Pick a target"), the two Mind-Control-shaped spells carry
/// "Each unit may only be moved once per round in this way.", and "If this model has Versatile Attack" counts a
/// unit that was given it (Versatile Attack Buff).
/// </summary>
[TestFixture]
public class SpellWordingShippedDataTests
{
    private static BookFile Book(string system, string file) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books", system, file)), RuleJson.Options)!;

    [TestCase("Grimdark Future", "SoulSnatcherCults.fdgbook", "Deep Hypnosis")]
    [TestCase("GF Firefight", "GFF-SoulSnatcherCults.fdgbook", "Deep Hypnosis")]
    [TestCase("Age of Fantasy", "AoF-VampiricUndead.fdgbook", "Seductive Invocation")]
    [TestCase("AoF Skirmish", "AoFS-VampiricUndead.fdgbook", "Seductive Invocation")]
    public void MindControlSpells_NeedSight_AndMoveAUnitOncePerRound(string system, string file, string spell)
    {
        SpellDefinition s = Book(system, file).Spells.Single(x => x.Name == spell);
        Assert.That(s.Target.RequireLineOfSight, Is.True, "\"within 18\\\" in line of sight\"");
        var move = (Effect.TriggeredMove)((Effect.MoraleTestThen)s.Effect).OnFailure;
        Assert.That(move.OncePerRound, Is.True);
    }

    [TestCase("Grimdark Future", "SoulSnatcherCults.fdgbook", "Psychic Onslaught")]
    [TestCase("Age of Fantasy", "AoF-VampiricUndead.fdgbook", "Cursed Strike")]
    public void DamageSpells_WithoutSightInTheirWords_NeedNone(string system, string file, string spell)
    {
        Assert.That(Book(system, file).Spells.Single(x => x.Name == spell).Target.RequireLineOfSight, Is.False,
            "\"Pick one enemy unit within 6\\\", which takes 3 hits...\" - no sight asked for");
    }

    [TestCase("Age of Fantasy", "AoF-DuchiesofVinci.fdgbook", "Enhanced Vinci Tech")]
    [TestCase("Grimdark Future", "WatchBrothers.fdgbook", "Enhanced Watchborn")]
    public void EnhancedVersatileRules_CountAGivenVersatileAttack(string system, string file, string rule)
    {
        SpecialRuleDefinition def = Book(system, file).RuleDefinitions.Single(r => r.Name == rule);
        Assert.That(def.Activated.Select(a => a.AvailableWhen), Is.All.InstanceOf<Condition.UnitHasRuleOrGrant>());
    }
}
