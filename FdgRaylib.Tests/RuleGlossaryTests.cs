using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #259 — the name -> description lookup behind the Army Forge's rule hover tooltips.
[TestFixture]
public class RuleGlossaryTests
{
    private static SpecialRuleDefinition Definition(string name, string description) =>
        new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Description: description);

    private static BookFile BookWith(params SpecialRuleDefinition[] definitions) =>
        new() { Name = "Test Book", RuleDefinitions = new List<SpecialRuleDefinition>(definitions) };

    // #420 rewrote this test's EXPECTATION, not its subject. It still proves a core rule resolves from
    // a book that does not define it; what changed is the answer, which is now OPR's published wording
    // rather than the hand-written summary. The summary for Stealth was close, but 36 of the 38
    // summaries that could be checked against OPR's text were not, so the glossary no longer serves any
    // of them where real text exists.
    [Test]
    public void Describe_CoreRule_ReturnsTheOfficialText()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());

        Assert.That(glossary.Describe("Stealth"),
            Is.EqualTo(OfficialRuleText.Text("Stealth", GameSystems.GrimdarkFuture)));
        Assert.That(glossary.Describe("Stealth"), Is.Not.Empty);
    }

    // 2026-11-19: this used to assert the opposite - "the book's own definition overrides by name". The book's
    // description is a summary written beside the rule's effects, not OPR's text (2,907 of the 4,856 bundled
    // definitions OPR has wording for say something else), so a name OPR publishes reads as OPR's wording.
    [Test]
    public void Describe_ARuleOprPublishes_ReadsAsOprsWording_NotTheBooksOwnSummary()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Stealth", "This book's own Stealth.")));
        Assert.That(glossary.Describe("Stealth"), Is.EqualTo(OfficialRuleText.Text("Stealth", GameSystems.GrimdarkFuture)));
    }

    [Test]
    public void Describe_FactionRuleFromTheBook_Resolves()
    {
        // A rule only this book has (OPR publishes no text under the name): the definition's own description.
        Assume.That(OfficialRuleText.Text("Clockwork Zeal", GameSystems.GrimdarkFuture), Is.Null);
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Clockwork Zeal", "Moves +2\" when Advancing.")));
        Assert.That(glossary.Describe("Clockwork Zeal"), Is.EqualTo("Moves +2\" when Advancing."));
    }

    // The fault this guards (Kai's rule: rule text is the book's own words). Ratmen's Scurry was corrected in
    // play on 2026-11-18 (+1" Advance), but its description still said "+2 inches on Advance" - and the
    // description is what the Army Forge hovers and the Choose Army panel showed.
    [Test]
    public void BundledBooks_EveryRuleOprPublishes_ReadsAsOprsWording()
    {
        var wrong = new List<string>();
        int checkedRules = 0;
        foreach (BookFile book in FdgRaylib.Rendering.BookLibrary.Load())
        {
            RuleGlossary glossary = RuleGlossary.Build(book);
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                if (OfficialRuleText.OwnText(definition.Name, book.GameSystem) is not { } official) continue;
                checkedRules++;
                if (glossary.Describe(definition.Name) != official)
                    wrong.Add($"{book.GameSystem} / {book.Name} / {definition.Name}: {glossary.Describe(definition.Name)}");
            }
        }
        Assert.That(checkedRules, Is.GreaterThan(4000), "the bundled books were not read");
        Assert.That(wrong, Is.Empty, "rules described in words that are not OPR's:\n" + string.Join("\n", wrong.Take(20)));
    }

    // Kai, 2026-11-19 ("Clear them out"): the summaries themselves are gone from the book files - 3,608 stored
    // descriptions now hold OPR's wording, because the engine still reads Definition.Description directly for ability
    // offers and token tips. The old summaries are kept in WorkItems/468 for the effects audit. If this fails after a
    // re-harvest of OfficialRuleText.json, the books need the same in-place replacement again.
    [Test]
    public void BundledBooks_NoStoredDescriptionOfARuleOprPublishes_SaysAnythingButOprsWording()
    {
        var wrong = new List<string>();
        int checkedRules = 0;
        foreach (BookFile book in FdgRaylib.Rendering.BookLibrary.Load())
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                if (OfficialRuleText.OwnText(definition.Name, book.GameSystem) is not { } official) continue;
                checkedRules++;
                if (!string.IsNullOrEmpty(definition.Description) && definition.Description != official)
                    wrong.Add($"{book.GameSystem} / {book.Name} / {definition.Name}: {definition.Description}");
            }
        Assert.That(checkedRules, Is.GreaterThan(5000), "the bundled books were not read");
        Assert.That(wrong, Is.Empty, $"{wrong.Count} stored descriptions are not OPR's wording:\n" + string.Join("\n", wrong.Take(20)));
    }

    [Test]
    public void Scurry_InTheAgeOfFantasyRatmenBook_ReadsPlusOneOnAdvance()
    {
        BookFile ratmen = FdgRaylib.Rendering.BookLibrary.Load()
            .First(b => b.Name == "Ratmen" && GameSystems.SameSystem(b.GameSystem, GameSystems.AgeOfFantasy));
        Assert.That(RuleGlossary.Build(ratmen).Describe("Scurry"),
            Is.EqualTo("Moves +1\" when using Advance, and +2\" when using Rush/Charge."));
    }

    [Test]
    public void Describe_NumericEntry_LooksUpTheCanonicalNameNotThePrintableOne()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        var tough = new SpecialRuleEntry_CoreNumeric("Tough", 3);

        Assert.That(tough.PrintableName, Is.EqualTo("Tough(3)"));
        Assert.That(glossary.Describe("Tough(3)"), Is.Null, "the printable name is not a catalog key");
        Assert.That(glossary.Describe(tough),
            Is.EqualTo(OfficialRuleText.Text("Tough", GameSystems.GrimdarkFuture)));
    }

    [Test]
    public void Describe_Alias_FallsThroughToTheRuleItRenames()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        var alias = new SpecialRuleEntry_Alias("Medical Training", new SpecialRuleEntry_Core("Regeneration"));

        Assert.That(glossary.Describe(alias),
            Is.EqualTo(OfficialRuleText.Text("Regeneration", GameSystems.GrimdarkFuture)));
    }

    [Test]
    public void Describe_Alias_PrefersTheAliassOwnDefinitionWhenTheBookCarriesOne()
    {
        RuleGlossary glossary = RuleGlossary.Build(
            BookWith(Definition("Medical Training", "Battlefield surgery: as Regeneration, but fluffier.")));
        var alias = new SpecialRuleEntry_Alias("Medical Training", new SpecialRuleEntry_Core("Regeneration"));

        Assert.That(glossary.Describe(alias), Is.EqualTo("Battlefield surgery: as Regeneration, but fluffier."));
    }

    [Test]
    public void Describe_UnknownRule_ReturnsNull()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Describe("Repel Ambushers"), Is.Null);
        Assert.That(glossary.Describe(new SpecialRuleEntry_Core("Repel Ambushers")), Is.Null);
    }

    [Test]
    public void Describe_DefinitionWithoutDescription_CountsAsUnknown()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Silent Rule", "")));
        Assert.That(glossary.Describe("Silent Rule"), Is.Null);
    }

    // #260: the corpus is inconsistent about casing - the bundled books say "Bane in Melee" where the
    // catalog says "Bane in melee" - and RuleResolver has matched case-insensitively since #100, so these
    // rules DO fire in play. The glossary must describe them, not report them as inert.
    [Test]
    public void Describe_IsCaseInsensitive_LikeTheEnginesResolver()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        foreach (string bookSpelling in new[]
                 {
                     "Bane in Melee", "Rending in Melee", "Shred in Melee",
                     "Shred when Shooting", "Unstoppable in Melee",
                 })
            Assert.That(glossary.Describe(bookSpelling), Is.Not.Null, bookSpelling);
    }

    // The guard that keeps the two from drifting apart again: the glossary must be silent exactly when
    // army load would also fail to resolve the name, so a tooltip never contradicts what the engine does.
    [Test]
    public void Describe_IsSilentExactlyWhenTheResolverCannotResolve()
    {
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        RuleGlossary glossary = RuleGlossary.Build(BookWith());

        foreach (string name in new[]
                 {
                     "Stealth", "stealth", "STEALTH", "Bane in melee", "Bane in Melee",
                     "Shred when Shooting", "Repel Ambushers", "Not A Rule At All",
                 })
            Assert.That(glossary.Describe(name) is not null, Is.EqualTo(resolver.TryResolve(name, out _)),
                $"glossary and resolver disagree about '{name}'");
    }

    [Test]
    public void Build_BookDefinition_OverridesCoreEvenWhenItsCasingDiffers()
    {
        // RegisterOrReplace overrides through the same case-insensitive dictionary at army load, so the
        // glossary must not end up describing the core rule while the engine runs the book's version.
        // Checked on a core helper OPR publishes no wording for (for a published name OPR's wording is the
        // answer either way - see Describe_ARuleOprPublishes_ReadsAsOprsWording_NotTheBooksOwnSummary).
        SpecialRuleDefinition? helper = CoreRuleCatalog.All.FirstOrDefault(d =>
            OfficialRuleText.Text(d.Name, GameSystems.GrimdarkFuture) is null && d.Name.Any(char.IsLower));
        Assume.That(helper, Is.Not.Null, "every core rule has OPR wording now - nothing left to check here");
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition(helper!.Name.ToUpperInvariant(), "This book's own.")));
        Assert.That(glossary.Describe(helper.Name), Is.EqualTo("This book's own."));
    }

    [Test]
    public void Tooltip_LeadsWithThePrintableName_ThenTheDescription()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        string tooltip = glossary.Tooltip(new SpecialRuleEntry_CoreNumeric("Tough", 3));

        Assert.That(tooltip,
            Is.EqualTo($"Tough(3)\n{OfficialRuleText.Text("Tough", GameSystems.GrimdarkFuture)}"),
            "the tooltip is the printable name, a newline, then OPR's own wording for the rule (#420).");
    }

    [Test]
    public void Tooltip_UnknownRule_SaysItIsNotEnforced()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Tooltip("Repel Ambushers"),
            Is.EqualTo($"Repel Ambushers\n{RuleGlossary.UnknownRuleText}"));
    }

    [Test]
    public void Empty_ResolvesNothing()
    {
        Assert.That(RuleGlossary.Empty.Describe("Stealth"), Is.Null);
    }

    // The bundled books are the real input: every faction rule they reference should either resolve in the
    // core catalog or ride along in the book's own definitions. This pins the one book we control by hand.
    [Test]
    public void Build_DemoBook_DescribesItsUnitsRules()
    {
        BookFile demo = DemoBook.Build();
        RuleGlossary glossary = RuleGlossary.Build(demo);

        foreach (RosterUnit unit in demo.Units)
            foreach (SpecialRuleEntry rule in unit.Rules)
                Assert.That(glossary.Describe(rule), Is.Not.Null, $"no description for '{rule.PrintableName}'");
    }

    // #397: the Combat Calculator can be pointed at a saved .fdgarmy, which carries embedded rule
    // definitions but no book. The hovers have to come from those definitions alone.
    [Test]
    public void Build_FromBareDefinitions_DescribesThemAndStillKnowsTheCoreRules()
    {
        BookFile demo = DemoBook.Build();
        RuleGlossary glossary = RuleGlossary.Build(demo.RuleDefinitions);

        Assert.Multiple(() =>
        {
            Assert.That(glossary.Describe("Stealth"), Is.Not.Null, "core rules are always known");
            foreach (SpecialRuleDefinition definition in demo.RuleDefinitions)
                Assert.That(glossary.Describe(definition.Name), Is.Not.Null,
                    $"no description for embedded '{definition.Name}'");
        });
    }

    [Test]
    public void Build_FromNoDefinitions_IsStillTheCoreCatalog()
    {
        Assert.That(RuleGlossary.Build((IEnumerable<SpecialRuleDefinition>?)null).Describe("Stealth"),
            Is.Not.Null);
    }

    // Kai, 2026-11-17: "Furious Aura only says it gives the unit Furious, but it doesn't say what Furious is."
    [Test]
    public void Tooltip_ARuleThatGrantsAnother_AlsoExplainsTheGrantedRule()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        string furious = glossary.Describe("Furious")!;
        Assert.That(furious, Is.Not.Empty);

        string tip = glossary.Tooltip(new SpecialRuleEntry_Core("Furious Aura"));
        Assert.That(tip, Does.StartWith("Furious Aura\n"));
        Assert.That(tip, Does.EndWith("\n\nFurious\n" + furious));
        Assert.That(glossary.Grants("Furious Aura"), Is.EqualTo(new[] { "Furious" }));
    }

    [Test]
    public void Tooltip_ARuleThatGrantsNothing_IsUnchanged()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Tooltip("Stealth"), Is.EqualTo("Stealth\n" + glossary.Describe("Stealth")));
    }
}
