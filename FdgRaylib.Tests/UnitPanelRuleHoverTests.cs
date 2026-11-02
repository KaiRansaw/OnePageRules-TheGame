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
        // Attached shortest-first on purpose: the segmenter has to sort, not trust the order it is given.
        Weapon weapon = WeaponWith("Odd Gun", 18f, 2, 1,
            Rule("Blast", "Hits are multiplied."),
            Rule("Blast(3)", "Hits x3."));

        List<RuleHoverText.Segment> segments =
            UnitStatBlockRenderer.WeaponLineSegments(weapon, 1, systemSlug: null);

        Assert.That(Flatten(segments), Is.EqualTo("- " + weapon.GetWeaponNameAndStats(1)),
            "whatever the overlap does to the highlighting, the text must survive intact.");

        // And the longer name wins: "Blast" matching first would highlight three characters and leave
        // "(3)" as plain text beside them, which reads as a rule the player cannot hover.
        string printed = weapon.GetWeaponNameAndStats(1);
        if (printed.Contains("Blast(3)", StringComparison.Ordinal))
        {
            Assert.That(segments.Any(s => s.RuleName == "Blast(3)"), Is.True,
                "the longer rule name must be the one that matched.");
        }
    }

    /// <summary>
    /// The ordering trap, pinned on its own because a sabotage walked straight into it.
    ///
    /// <para>
    /// The obvious fix for "one name inside another" is to sort the names longest-first and walk them in
    /// that order. It is wrong, and the failure is silent: the cursor only moves forward, so consuming a
    /// long name that appears LATE in the line strands every shorter name before it, and those just stop
    /// being hoverable. Here Disintegrate is the longest and appears last, so sorting by length drops
    /// Blast(3) entirely.
    /// </para>
    /// </summary>
    [Test]
    public void ALongRuleNameLateInTheLineDoesNotStrandShorterOnesBeforeIt()
    {
        Weapon weapon = WeaponWith("Blessed Flamer", 12f, 1, 0,
            Rule("Blast(3)", "Hits are multiplied by 3."),
            Rule("Disintegrate", "Ignores Regeneration."),
            Rule("Reliable", "Attacks hit on 2+."));
        string printed = weapon.GetWeaponNameAndStats(1);
        if (!printed.Contains("Blast(3)", StringComparison.Ordinal)) Assert.Pass("formatter omits rules.");

        var names = UnitStatBlockRenderer.WeaponLineSegments(weapon, 1, systemSlug: null)
            .Where(s => s.IsRule).Select(s => s.RuleName).ToList();

        Assert.That(names, Does.Contain("Blast(3)"),
            "Blast(3) comes first in the line and must still be found after the longer Disintegrate.");
        Assert.That(names, Does.Contain("Disintegrate"));
        Assert.That(names, Does.Contain("Reliable"));
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
    /// Kai, 2026-11-02: <i>"in ANY instance of ANY rule being referenced ... all of them are gold,
    /// underscored and highlighted"</i>. That only holds if there is literally ONE colour - the two
    /// families of rule display (<see cref="RuleHoverText"/> for live rules in play,
    /// <c>RuleTextFlow</c> for army-file entries in the builders) used different ones, which is how the
    /// game ended up with a blue rule name in the Forge and a gold one in the Battle Report.
    ///
    /// <para>Sabotage: give either family its own literal and this fails.</para>
    /// </summary>
    [Test]
    public void EveryRuleDisplayUsesTheSameGold()
    {
        Assert.That(RuleHoverText.RuleGold, Is.EqualTo(ImGuiTheme.RuleGold),
            "the in-game panels and the theme must be the same constant, not two copies.");

        Assert.That(ImGuiTheme.RuleGold.W, Is.EqualTo(1f), "fully opaque.");
        Assert.That(ImGuiTheme.RuleGold.X, Is.GreaterThan(ImGuiTheme.RuleGold.Z),
            "gold: more red than blue.");
        Assert.That(ImGuiTheme.RuleGold.Y, Is.GreaterThan(ImGuiTheme.RuleGold.Z),
            "gold: more green than blue.");

        // The dim variant is the same hue for a rule on something switched off - darkened, not greyed,
        // so it still reads as a rule name rather than as disabled text.
        Assert.That(ImGuiTheme.RuleGoldDim.X, Is.GreaterThan(ImGuiTheme.RuleGoldDim.Z));
        Assert.That(ImGuiTheme.RuleGoldDim.X, Is.LessThan(ImGuiTheme.RuleGold.X), "and darker.");
    }
}
