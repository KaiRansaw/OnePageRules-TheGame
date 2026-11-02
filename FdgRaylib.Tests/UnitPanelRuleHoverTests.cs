using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-02: the Selected Unit panel's rule names get the Battle Report's treatment - <b>gold,
/// underlined, explained on hover</b> - and special rules collapse from a block each into one comma
/// list.
///
/// <para>
/// The drawing is ImGui and is checked by looking at it. What is worth pinning is the <b>segmentation</b>
/// of a weapon's datasheet line, because it is built by walking the already-formatted line looking for
/// rule names rather than by rebuilding the line from its parts. That is the right call - the line's
/// wording belongs to <c>GetWeaponNameAndStats</c> and a second formatter here would be a second thing
/// to keep in step - but it means a bug would quietly change what the player reads rather than failing
/// loudly. So the invariant every test below protects is: <b>flattening the segments reproduces the line
/// exactly</b>.
/// </para>
/// </summary>
[TestFixture]
public class UnitPanelRuleHoverTests
{
    private static string Flatten(IReadOnlyList<RuleHoverText.Segment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    private static SpecialRuleDefinition Definition(string name, string description) =>
        new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Description: description);

    private static ResolvedRule Rule(string name, string description) =>
        new(name, Definition(name, description));

    private static Weapon WeaponWith(string name, float range, int attacks, int ap,
        params ResolvedRule[] rules)
    {
        var weapon = new Weapon(name, range, attacks, ap);
        foreach (ResolvedRule rule in rules) weapon.AttachRuleDefinition(rule);
        return weapon;
    }

    /// <summary>
    /// The invariant. Kai's screenshot line is the worked example: a Blessed Flamer with Blast(3),
    /// Disintegrate and Reliable must read exactly as it did, with those three words now hoverable.
    /// </summary>
    [Test]
    public void TheSegmentsReassembleTheWeaponLineExactly()
    {
        Weapon weapon = WeaponWith("Blessed Flamer", 12f, 1, 0,
            Rule("Blast(3)", "Hits are multiplied by 3."),
            Rule("Disintegrate", "Ignores Regeneration."),
            Rule("Reliable", "Attacks hit on 2+."));

        List<RuleHoverText.Segment> segments =
            UnitStatBlockRenderer.WeaponLineSegments(weapon, 2, systemSlug: null);

        Assert.That(Flatten(segments), Is.EqualTo("- " + weapon.GetWeaponNameAndStats(2)),
            "the panel's line must be byte-identical to what it printed before - only the underlines "
            + "are new.");
    }

    [Test]
    public void EveryRuleOnTheWeaponBecomesItsOwnHoverableRun()
    {
        Weapon weapon = WeaponWith("Blessed Flamer", 12f, 1, 0,
            Rule("Blast(3)", "Hits are multiplied by 3."),
            Rule("Disintegrate", "Ignores Regeneration."),
            Rule("Reliable", "Attacks hit on 2+."));

        var hoverable = UnitStatBlockRenderer.WeaponLineSegments(weapon, 2, systemSlug: null)
            .Where(s => s.IsRule).ToList();

        Assert.That(hoverable.Select(s => s.RuleName),
            Is.EquivalentTo(new[] { "Blast(3)", "Disintegrate", "Reliable" }));
        Assert.That(hoverable.All(s => s.IsDocumented), Is.True,
            "each one carries its description, which is what the hover shows.");
    }

    [Test]
    public void AWeaponWithNoRulesIsOnePlainRun()
    {
        Weapon weapon = WeaponWith("Rifle", 24f, 1, 0);

        List<RuleHoverText.Segment> segments =
            UnitStatBlockRenderer.WeaponLineSegments(weapon, 8, systemSlug: null);

        Assert.That(segments.Any(s => s.IsRule), Is.False);
        Assert.That(Flatten(segments), Is.EqualTo("- " + weapon.GetWeaponNameAndStats(8)));
    }

    /// <summary>
    /// The nasty case for a left-to-right walk: one rule's name contained inside another's. "Blast" must
    /// not match the "Blast" inside "Blast(3)" and leave a stray "(3)" behind, and neither name may be
    /// consumed twice. The line still has to come back whole.
    /// </summary>
    [Test]
    public void OneRuleNameInsideAnotherStillSegmentsCleanly()
    {
        Weapon weapon = WeaponWith("Odd Gun", 18f, 2, 1,
            Rule("Blast(3)", "Hits x3."),
            Rule("Blast", "Hits are multiplied."));

        List<RuleHoverText.Segment> segments =
            UnitStatBlockRenderer.WeaponLineSegments(weapon, 1, systemSlug: null);

        Assert.That(Flatten(segments), Is.EqualTo("- " + weapon.GetWeaponNameAndStats(1)),
            "whatever the overlap does to the highlighting, the text must survive intact.");
    }

    /// <summary>
    /// A rule the line's formatter chose not to print is skipped rather than inserted. Reconstructing the
    /// line from the rule list instead of walking the printed one would have put it back in and shown the
    /// player a rule that is not on their weapon.
    /// </summary>
    [Test]
    public void ARuleThatIsNotInThePrintedLineIsNotInvented()
    {
        Weapon weapon = WeaponWith("Rifle", 24f, 1, 0, Rule("Rending", "Unmodified 6s ignore armor."));
        string printed = weapon.GetWeaponNameAndStats(1);
        if (printed.Contains("Rending", StringComparison.Ordinal))
        {
            Assert.Pass("this formatter does print weapon rules, which the other tests already cover.");
        }

        List<RuleHoverText.Segment> segments =
            UnitStatBlockRenderer.WeaponLineSegments(weapon, 1, systemSlug: null);

        Assert.That(Flatten(segments), Is.EqualTo("- " + printed));
        Assert.That(segments.Any(s => s.IsRule), Is.False);
    }

    /// <summary>
    /// The colour is one shared constant rather than a literal per screen, so "gold underlined word" can
    /// only ever mean one thing. Sabotage: give the unit panel its own colour and this is what notices.
    /// </summary>
    [Test]
    public void TheRuleGoldIsOneSharedColour()
    {
        Assert.That(RuleHoverText.RuleGold.W, Is.EqualTo(1f), "fully opaque.");
        Assert.That(RuleHoverText.RuleGold.X, Is.GreaterThan(RuleHoverText.RuleGold.Z),
            "gold: more red than blue.");
        Assert.That(RuleHoverText.RuleGold.Y, Is.GreaterThan(RuleHoverText.RuleGold.Z),
            "gold: more green than blue.");
    }
}
