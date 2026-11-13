using System;
using System.Collections.Generic;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering;

// #259 — name -> player-facing description for every special rule a book can reference, so the Army Forge
// can explain a rule on hover the way the real Army Forge does.
//
// No new data is authored here: SpecialRuleDefinition already carries Description (#151, written for the
// granted-rule token hovers), every CoreRuleCatalog definition has one, and every bundled .fdgbook embeds
// the faction-specific subset of GdfRuleSupplement.json (BookRuleSupplement.Apply) with descriptions
// attached. This class is just the lookup the screens were missing.
public sealed class RuleGlossary
{
    /// <summary>What the tooltip says about a rule the engine will not resolve. Same fact the #241 import
    /// modal reports as "RULES NOT ENFORCED BY THE ENGINE (inert in play)", surfaced per rule.</summary>
    public const string UnknownRuleText =
        "No description available - this rule is not enforced by the engine and does nothing in play.";

    private readonly Dictionary<string, string> _byName;

    private RuleGlossary(Dictionary<string, string> byName) => _byName = byName;

    /// <summary>An empty glossary (every lookup misses). For screens with no book in hand.</summary>
    public static RuleGlossary Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Core catalog first, then the book's embedded definitions overriding by name — the same
    /// core-then-embedded precedence army load uses when it registers rules (<c>RegisterOrReplace</c>),
    /// so what the tooltip describes is the definition that will actually fire in play.
    /// </summary>
    /// <remarks>
    /// Lookup is case-INSENSITIVE, matching <c>RuleResolver</c>'s own <c>OrdinalIgnoreCase</c> dictionary
    /// (#100). The corpus is not consistent about casing - the books say "Bane in Melee" where the catalog
    /// says "Bane in melee" - and the engine resolves those to the same rule, so the tooltip must describe
    /// it rather than claim the rule does nothing. Same comparer as the resolver means the glossary is
    /// silent exactly when army load would also fail to resolve the name.
    /// </remarks>
    public static RuleGlossary Build(BookFile? book) =>
        Build(book?.RuleDefinitions, book?.GameSystem);

    /// <summary>
    /// The same glossary from a bare definition list, for a source that carries rules but no book -
    /// a saved .fdgarmy opened in the Combat Calculator (#397), whose embedded definitions are all the
    /// hovers have to go on.
    /// </summary>
    public static RuleGlossary Build(IEnumerable<SpecialRuleDefinition>? definitions,
        string? gameSystem = null)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Lowest priority: the hand-written summary each definition was authored with. #420 measured
        // these against OPR's own text and 36 of the 38 that could be checked did not match - several
        // had dropped a whole clause - so they are now only ever the answer for a rule OPR publishes no
        // text for, which in practice means the engine's internally generated helper definitions
        // ("AP(+1) in melee", "Trait: Agile") that no player looks up.
        foreach (SpecialRuleDefinition definition in CoreRuleCatalog.All)
            byName[definition.Name] = definition.Description;

        // Then OPR's wording for this system, which beats the summary for every rule OPR publishes -
        // and which has to be keyed PER SYSTEM, because Star Quest's Fear, Scout, Blast, Tough and
        // Flying all read differently from Grimdark Future's and those are real rules differences.
        // Showing a Star Quest player Grimdark Future's Fear tells them to check the wrong thing at the
        // end of a melee.
        foreach (string name in byName.Keys.ToList())
        {
            string? official = OfficialRuleText.Text(name, gameSystem);
            if (official is not null) byName[name] = official;
        }

        // Last, and therefore highest: the book's OWN definition of a name. This is also OPR's text -
        // the importer takes it from the same place - but it is the more specific of the two, because
        // OPR does occasionally reword a rule for one book, and the per-system table above cannot hold
        // two answers for one name. A faction rule that only this book has is here and nowhere else.
        if (definitions is not null)
            foreach (SpecialRuleDefinition definition in definitions)
                if (!string.IsNullOrWhiteSpace(definition.Description))
                    byName[definition.Name] = definition.Description;

        // Deliberately NOT seeded with every name OPR publishes. The glossary's key set is also its
        // answer to "is this rule known here?", and a rule the engine has no definition for must keep
        // reading as unknown rather than acquiring a confident description of behaviour that will never
        // happen. That is the #420 fault in miniature: convincing text in front of nothing.
        return new RuleGlossary(byName);
    }

    /// <summary>The description for a canonical rule name, or null when the name resolves to nothing (or to
    /// a definition that was authored without a description).</summary>
    public string? Describe(string ruleName) =>
        _byName.TryGetValue(ruleName, out string? description) && !string.IsNullOrWhiteSpace(description)
            ? description
            : null;

    /// <summary>
    /// The description for a rule as an army/book file carries it. Looks up the CANONICAL name rather than
    /// the printable one, so "Tough(3)" finds Tough; an alias ("Medical Training (Regeneration)") falls
    /// through to the rule it renames, since that is what actually describes its behavior.
    /// </summary>
    public string? Describe(SpecialRuleEntry entry) => entry switch
    {
        SpecialRuleEntry_Core core => Describe(core.Name),
        SpecialRuleEntry_CoreNumeric numeric => Describe(numeric.Name),
        SpecialRuleEntry_Alias alias => Describe(alias.Name) ?? Describe(alias.AliasedRule),
        _ => Describe(entry.PrintableName),
    };

    /// <summary>Tooltip body for a rule: its description, or the inert-in-play note when it has none.
    /// Prefixed with the rule's own printable name so the tooltip stands alone when several rules sit on
    /// one line.</summary>
    public string Tooltip(SpecialRuleEntry entry) => Compose(entry.PrintableName, Describe(entry));

    /// <inheritdoc cref="Tooltip(SpecialRuleEntry)"/>
    public string Tooltip(string ruleName) => Compose(ruleName, Describe(ruleName));

    private static string Compose(string printableName, string? description) =>
        $"{printableName}\n{description ?? UnknownRuleText}";
}
