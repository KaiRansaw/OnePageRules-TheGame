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

// #442 - the Hold the Line family's distance band, in the data the game actually loads.
//
// The books, identical in every system that prints them: "If most models in this unit have this rule,
// they get +1 to Morale Test rolls when shot/charged from over 9"/6" away." Three of the four rules in
// that family are bundled (Hold the Line, Hive Bond, Battle-Hardened); all three shipped firing on
// `always`, so the band simply did not exist, and two of them had no "most models" gate either.
//
// The behaviour half is FutureOfDarkGrimness/Tests/HoldTheLineBandTests. This is the DATA half, and it is
// the half that would have caught the original defect: the definition a game loads is the book's embedded
// copy, so a supplement edit without the --apply-rules rebake leaves the books on the old condition while
// every behaviour test stays green. That is precisely how the Lustbound numbers sat wrong in 14 books.
[TestFixture]
public class HoldTheLineShippedDataTests
{
    /// <summary>The three bundled members of the family, each with the Enhanced rule that lifts its band.</summary>
    private static readonly (string Rule, string? Enhanced)[] Family =
    {
        ("Hold the Line", "Enhanced Hold the Line"),
        ("Hive Bond", "Enhanced Hive Bond"),
        ("Battle-Hardened", null),      // no book prints an Enhanced form
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Supplement(string ruleName) =>
        BookRuleSupplement.LoadDefinitions(
                File.ReadAllText(Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == ruleName);

    // -- The supplement -----------------------------------------------------------------------------

    /// <summary>
    /// The authored shape: <c>And(MostModelsHaveThisRule, MoraleAttackedFromBeyond(9, 6))</c>, optionally
    /// with the Enhanced unlock as the second arm of an <c>Or</c>. Asserted structurally rather than by
    /// behaviour because the two halves fail independently - a missing "most models" gate and a missing
    /// band are different defects, and this family shipped with one, the other, or both.
    /// </summary>
    [Test]
    public void Supplement_EveryFamilyMember_IsGatedOnMostModelsAndTheBand()
    {
        foreach ((string rule, string? enhanced) in Family)
        {
            SpecialRuleDefinition definition = Supplement(rule);
            HookEntry entry = definition.Passive.Single();

            Assert.That(entry.HookID, Is.EqualTo(EHookID.Morale_OnPreMoraleTest), rule);

            Assert.That(entry.Condition, Is.InstanceOf<Condition.And>(),
                $"{rule}: the book opens with \"if most models in this unit have this rule\" AND closes "
                + "with the distance band - an unconditional Always here is the original defect.");
            var and = (Condition.And)entry.Condition;

            Assert.That(and.Left, Is.InstanceOf<Condition.MostModelsHaveThisRule>(),
                $"{rule}: \"If MOST models in this unit have this rule\".");

            Condition band = enhanced == null ? and.Right : ((Condition.Or)and.Right).Left;
            Assert.That(band, Is.InstanceOf<Condition.MoraleAttackedFromBeyond>(), $"{rule}: the band.");

            var beyond = (Condition.MoraleAttackedFromBeyond)band;
            Assert.That(beyond.ShotInches, Is.EqualTo(9f), $"{rule}: \"shot ... from over 9\\\" away\".");
            Assert.That(beyond.ChargedInches, Is.EqualTo(6f), $"{rule}: \"charged from over 6\\\" away\".");

            var effect = (Effect.RollModifier)entry.Effect;
            Assert.That(effect.RollKind, Is.EqualTo(ERollKind.Morale), rule);
            Assert.That(effect.Delta, Is.EqualTo(+1), $"{rule}: +1, and only ever +1.");
        }
    }

    /// <summary>
    /// The Enhanced rules carry <b>no morale effect of their own</b>. Their text is "they always get +1
    /// ... <i>from Hold the Line</i> (instead of only when shot/charged from over 9"/6" away)" - one
    /// bonus, whose restriction is lifted. Shipped as a second +1 on top of an already-unconditional
    /// base, they gave <b>+2</b>.
    /// </summary>
    [Test]
    public void Supplement_TheEnhancedRules_CarryNoBonusOfTheirOwn()
    {
        foreach (string enhanced in Family.Select(f => f.Enhanced).Where(e => e != null)!)
        {
            SpecialRuleDefinition definition = Supplement(enhanced!);

            Assert.That(definition.Passive.Where(e => e.HookID == EHookID.Morale_OnPreMoraleTest),
                Is.Empty,
                $"{enhanced} must not add its own morale modifier - it unlocks the base rule's band. A "
                + "RollModifier here is the +2 defect: the base rule grants the +1 either way, so this "
                + "one would be a second copy.");
        }
    }

    /// <summary>
    /// And the base rule's unlock must be the <b>grant-aware</b> check. In several books the Enhanced
    /// rule reaches a unit only through <i>Enhanced Hold the Line Aura</i> or <i>... Buff</i>, which
    /// grant it as a RuleGrant token - and a granted rule is invisible to
    /// <see cref="Condition.UnitHasRule"/>, which reads static rules only. Gated that way, every
    /// aura-fed unit silently loses its bonus at close range, which is the opposite of what the Enhanced
    /// rule is for. Nothing else in either suite distinguishes the two conditions, so this is the only
    /// thing holding the right one in place.
    /// </summary>
    [Test]
    public void Supplement_TheEnhancedUnlock_SeesAnAuraGrant()
    {
        foreach ((string rule, string? enhanced) in Family.Where(f => f.Enhanced != null))
        {
            var or = (Condition.Or)((Condition.And)Supplement(rule).Passive.Single().Condition).Right;

            Assert.That(or.Right, Is.InstanceOf<Condition.UnitHasRuleOrGrant>(),
                $"{rule}: a plain UnitHasRule would not see {enhanced} arriving from its Aura or Buff.");
            Assert.That(((Condition.UnitHasRuleOrGrant)or.Right).RuleName, Is.EqualTo(enhanced));
        }
    }

    // -- Every bundled book ------------------------------------------------------------------------

    /// <summary>
    /// The real population. Walks every bundled book rather than a hand-picked one, so a book the
    /// --apply-rules pass missed fails here by name. 13 books carry one of these rules, across all six
    /// game systems.
    /// </summary>
    [Test]
    public void EveryShippedFamilyRule_CarriesTheBand()
    {
        var offenders = new List<string>();
        var carriers = new Dictionary<string, int>();

        foreach (string path in ShippedBooks.AllPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(path), RuleJson.Options)!;

            foreach ((string rule, string? _) in Family)
            {
                SpecialRuleDefinition? definition = book.RuleDefinitions
                    .FirstOrDefault(r => string.Equals(r.Name, rule, StringComparison.OrdinalIgnoreCase));
                if (definition is null) continue;

                carriers[rule] = carriers.GetValueOrDefault(rule) + 1;

                foreach (HookEntry entry in definition.Passive
                             .Where(e => e.HookID == EHookID.Morale_OnPreMoraleTest))
                {
                    if (!MentionsTheBand(entry.Condition))
                    {
                        offenders.Add($"{Path.GetFileName(path)}/{rule} "
                            + $"({entry.Condition.GetType().Name})");
                    }
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "these books still carry an ungated member of the family - re-run --apply-rules for them "
            + $"after editing GdfRuleSupplement.json: {string.Join(", ", offenders.Take(12))}");

        foreach ((string rule, string? _) in Family)
        {
            Assert.That(carriers.GetValueOrDefault(rule), Is.GreaterThan(0),
                $"expected {rule} in at least one bundled book, found none. Zero means the book walk "
                + "broke and this fixture stopped checking anything.");
        }
    }

    /// <summary>
    /// And no bundled book ships an Enhanced rule that still carries its own +1 - the per-book embedded
    /// copy is what a game loads, so the supplement being right is not enough.
    /// </summary>
    [Test]
    public void NoShippedEnhancedRule_StillCarriesItsOwnBonus()
    {
        var offenders = new List<string>();
        int found = 0;

        foreach (string path in ShippedBooks.AllPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(path), RuleJson.Options)!;

            foreach (string enhanced in Family.Select(f => f.Enhanced).Where(e => e != null)!)
            {
                SpecialRuleDefinition? definition = book.RuleDefinitions
                    .FirstOrDefault(r => string.Equals(r.Name, enhanced, StringComparison.OrdinalIgnoreCase));
                if (definition is null) continue;

                found++;
                if (definition.Passive.Any(e => e.HookID == EHookID.Morale_OnPreMoraleTest))
                    offenders.Add($"{Path.GetFileName(path)}/{enhanced}");
            }
        }

        Assert.That(offenders, Is.Empty,
            $"these books would grant +2 instead of +1: {string.Join(", ", offenders.Take(12))}");
        Assert.That(found, Is.GreaterThan(0),
            "expected the Enhanced rules in several bundled books, found none - the walk broke.");
    }

    /// <summary>
    /// Valiant is the family's fourth member and <b>Leader</b> and <b>Motivator</b> are two more morale
    /// rules OPR publishes text for; no bundled unit references any of them, and the engine defines none
    /// of them. Recorded as a test so the day a book refresh introduces one, this fails and says what to
    /// do - rather than the rule resolving to nothing and the unit quietly playing without it, which is
    /// how the Villain bug reached a release.
    /// </summary>
    [Test]
    public void TheUnimplementedMoraleRules_AreStillUnused()
    {
        string[] unimplemented = { "Valiant", "Enhanced Valiant", "Motivator", "Leader" };
        var appeared = new List<string>();

        foreach (string path in ShippedBooks.AllPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(path), RuleJson.Options)!;

            foreach (string name in unimplemented)
            {
                if (book.RuleDefinitions.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                    appeared.Add($"{Path.GetFileName(path)}/{name}");
            }
        }

        Assert.That(appeared, Is.Empty,
            "a book now ships one of these, so it needs a definition in GdfRuleSupplement.json before it "
            + "does anything. Valiant is \"+1 to Morale Test rolls when shot/charged from over 9\"/6\" "
            + "away\" (the same shape as Hold the Line); Leader is \"+1 to Morale Test rolls in melee\" "
            + "and Motivator \"+1 to Morale Test rolls triggered by shooting\", and BOTH are hero-and-unit "
            + "in GF/AoF but hero-ONLY in two other systems, so neither can be added without a per-system "
            + $"split: {string.Join(", ", appeared.Take(12))}");
    }

    private static bool MentionsTheBand(Condition condition) => condition switch
    {
        Condition.MoraleAttackedFromBeyond => true,
        Condition.And and => MentionsTheBand(and.Left) || MentionsTheBand(and.Right),
        Condition.Or or => MentionsTheBand(or.Left) || MentionsTheBand(or.Right),
        Condition.Not not => MentionsTheBand(not.Inner),
        _ => false,
    };
}
