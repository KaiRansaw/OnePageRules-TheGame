using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Audit finding 3 - Banner's +1 is for combat morale only. Every current book and Army Forge's own text
// agree in all six systems: "+1 to Morale Test rolls triggered by shooting or melee." The shipped
// definition was authored with `always`, so a Banner also helped against a spell's morale test and
// against a campaign random event's (#432).
//
// The engine half is FutureOfDarkGrimness/Tests/BannerMoraleTriggerTests. This is the DATA half, and it
// is the one that would have caught the original defect: the definition the game actually loads is the
// book's embedded copy, so a supplement edit without the --apply-rules rebake leaves 114 books on the
// old condition while every behaviour test stays green.
[TestFixture]
public class BannerShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Supplement(string file, string ruleName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, file)))
            .Single(r => r.Name == ruleName);

    private static BookFile Book(string fileName) =>
        JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(ShippedBooks.Path(fileName + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

    // -- Banner -------------------------------------------------------------------------------------

    [Test]
    public void Supplement_Banner_IsGatedOnCombatTriggeredMorale()
    {
        SpecialRuleDefinition banner = Supplement("GdfRuleSupplement.json", "Banner");

        HookEntry entry = banner.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Morale_OnPreMoraleTest));
        Assert.That(entry.Condition, Is.InstanceOf<Condition.MoraleTriggeredByCombat>(),
            "the books say \"triggered by shooting or melee\" - an Always here is the finding-3 defect.");

        Effect.RollModifier effect = (Effect.RollModifier)entry.Effect;
        Assert.That(effect.RollKind, Is.EqualTo(ERollKind.Morale));
        Assert.That(effect.Delta, Is.EqualTo(+1));
    }

    /// <summary>
    /// The real population, across every system that ships a Banner. This is the assertion that pins the
    /// rebake: it walks every bundled book rather than a hand-picked one, so a book missed by the
    /// --apply-rules pass fails here by name.
    /// </summary>
    [Test]
    public void EveryShippedBanner_CarriesTheCombatGate()
    {
        var offenders = new List<string>();
        int carriers = 0;

        foreach (string path in ShippedBooks.AllPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(path), RuleJson.Options)!;

            SpecialRuleDefinition? banner = book.RuleDefinitions
                .FirstOrDefault(r => string.Equals(r.Name, "Banner", StringComparison.OrdinalIgnoreCase));
            if (banner is null) continue;

            carriers++;
            foreach (HookEntry entry in banner.Passive
                         .Where(e => e.HookID == EHookID.Morale_OnPreMoraleTest))
            {
                if (entry.Condition is not Condition.MoraleTriggeredByCombat)
                    offenders.Add($"{Path.GetFileName(path)} ({entry.Condition.GetType().Name})");
            }
        }

        Assert.That(offenders, Is.Empty,
            "these books still carry an ungated Banner - re-run --apply-rules for them after editing "
            + $"GdfRuleSupplement.json: {string.Join(", ", offenders.Take(12))}");
        Assert.That(carriers, Is.GreaterThan(100),
            $"expected Banner in ~114 bundled books, found {carriers}. A much smaller number means the "
            + "book walk broke and this fixture stopped checking anything.");
    }

    // -- Lustbound ----------------------------------------------------------------------------------

    // Found while rebaking for finding 3, and a separate defect: Lustbound is a MOVEMENT rule. Army
    // Forge prints "Moves +1\" when using Advance, and +2\" when using Rush/Charge" for all six systems,
    // and Enhanced Lustbound's own parenthetical ("instead of only +1\" and +2\"") confirms the base
    // independently. The supplement shipped +3 on Rush/Charge, and Enhanced Lustbound added +1 to land
    // on the correct +4 total - so the total was right and the base was wrong, which is why it survived.
    [Test]
    public void Supplement_Lustbound_MatchesArmyForgesMovementNumbers()
    {
        SpecialRuleDefinition rule = Supplement("GdfRuleSupplement.json", "Lustbound");

        Assert.That(MovementBonus(rule, EActionType.Advance), Is.EqualTo(1f), "Advance +1\"");
        Assert.That(MovementBonus(rule, EActionType.Rush), Is.EqualTo(2f), "Rush +2\", not +3\"");
        Assert.That(MovementBonus(rule, EActionType.Charge), Is.EqualTo(2f), "Charge +2\", not +3\"");

        Assert.That(rule.Passive.Any(e => e.HookID == EHookID.Shooting_OnRangeCheck), Is.False,
            "Lustbound grants no shooting range at all - that shape is the defect, not the rule.");
    }

    /// <summary>
    /// Enhanced Lustbound is additive on top of Lustbound, and the two together must reach Army Forge's
    /// stated total of +2 Advance / +4 Rush/Charge. Pinned as a SUM so a future edit cannot fix one half
    /// and silently break the pair, which is exactly how the original numbers passed review.
    /// </summary>
    [Test]
    public void Supplement_LustboundPlusEnhanced_ReachesTheStatedTotals()
    {
        SpecialRuleDefinition basic = Supplement("GdfRuleSupplement.json", "Lustbound");
        SpecialRuleDefinition enhanced = Supplement("GdfRuleSupplement.json", "Enhanced Lustbound");

        Assert.That(MovementBonus(basic, EActionType.Advance) + MovementBonus(enhanced, EActionType.Advance),
            Is.EqualTo(2f), "Army Forge: \"it moves +2\\\" on Advance ... from Lustbound\"");
        Assert.That(MovementBonus(basic, EActionType.Rush) + MovementBonus(enhanced, EActionType.Rush),
            Is.EqualTo(4f), "Army Forge: \"+4\\\" on Rush/Charge\"");
        Assert.That(MovementBonus(basic, EActionType.Charge) + MovementBonus(enhanced, EActionType.Charge),
            Is.EqualTo(4f), "Army Forge: \"+4\\\" on Rush/Charge\"");
    }

    /// <summary>
    /// And the rebaked books carry it. Lust Disciples is picked because it ships in all six systems and
    /// takes the plain supplement definition.
    ///
    /// <para>
    /// This used to carry a caveat: four books redefined Lustbound through a per-book override and were
    /// excluded, pending a check against the official builder. That check happened on 2026-11-01 - the
    /// official Army Forge API became reachable from the workspace - and all three books that print
    /// Lustbound in either system agree word for word: <i>"Moves +1" when using Advance, and +2" when
    /// using Rush/Charge."</i> No shooting range anywhere. The overrides were authored fiction, so they
    /// are gone and every book is covered by the all-books test below.
    /// </para>
    /// </summary>
    [TestCase("LustDisciples")]
    [TestCase("GFF-LustDisciples")]
    [TestCase("GFSQ-LustDisciples")]
    [TestCase("AoF-LustDisciples")]
    [TestCase("AoFS-LustDisciples")]
    [TestCase("AoFQ-LustDisciples")]
    public void EmbeddedBookCopy_CarriesTheCorrectedLustbound(string bookFileName)
    {
        SpecialRuleDefinition rule = Book(bookFileName).RuleDefinitions.Single(r => r.Name == "Lustbound");

        Assert.That(MovementBonus(rule, EActionType.Rush), Is.EqualTo(2f),
            $"{bookFileName}: the rebaked book must carry Rush +2\", not the old +3\".");
        Assert.That(rule.Passive.Any(e => e.HookID == EHookID.Shooting_OnRangeCheck), Is.False,
            $"{bookFileName}: Lustbound is not a shooting rule.");
    }

    /// <summary>
    /// Every bundled book that ships Lustbound, not a chosen six. This is the assertion that would have
    /// caught the four override-driven books: the six above were correct for weeks while those four
    /// granted +4" of shooting range and +8" with the Enhanced form, because a per-book override is
    /// applied LAST and wins (#375 C9), so a correct supplement proves nothing about a book that
    /// redefines the rule.
    /// </summary>
    [Test]
    public void EveryShippedLustbound_IsPurelyMovement()
    {
        var offenders = new List<string>();
        int carriers = 0;

        foreach (string path in ShippedBooks.AllPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(path), RuleJson.Options)!;

            SpecialRuleDefinition? rule = book.RuleDefinitions
                .FirstOrDefault(r => string.Equals(r.Name, "Lustbound", StringComparison.OrdinalIgnoreCase));
            if (rule is null) continue;

            carriers++;
            string name = Path.GetFileNameWithoutExtension(path);

            if (rule.Passive.Any(e => e.HookID == EHookID.Shooting_OnRangeCheck))
                offenders.Add($"{name}: grants shooting range");
            if (MovementBonus(rule, EActionType.Advance) != 1f)
                offenders.Add($"{name}: Advance {MovementBonus(rule, EActionType.Advance)}\" not 1\"");
            if (MovementBonus(rule, EActionType.Rush) != 2f)
                offenders.Add($"{name}: Rush {MovementBonus(rule, EActionType.Rush)}\" not 2\"");
            if (MovementBonus(rule, EActionType.Charge) != 2f)
                offenders.Add($"{name}: Charge {MovementBonus(rule, EActionType.Charge)}\" not 2\"");
        }

        Assert.That(offenders, Is.Empty,
            "Army Forge, verified live for every book that prints it: \"Moves +1\" when using Advance, "
            + $"and +2\" when using Rush/Charge.\" Offenders: {string.Join("; ", offenders.Take(12))}");
        Assert.That(carriers, Is.EqualTo(14),
            $"expected Lustbound in 14 bundled books, found {carriers}. A different number means the "
            + "book set changed and this fixture's population needs re-checking, not that the rule broke.");
    }

    private static float MovementBonus(SpecialRuleDefinition rule, EActionType actionType) =>
        rule.Passive
            .Select(e => e.Effect)
            .OfType<Effect.MovementBonus>()
            .Where(m => m.ActionType == actionType)
            .Sum(m => m.DistanceInches);
}
