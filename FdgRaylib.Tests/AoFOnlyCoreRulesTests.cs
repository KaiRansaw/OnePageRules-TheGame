using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #431 - the five core rules Age of Fantasy and AoF: Skirmish have and Grimdark Future does not:
/// <b>Banner, Musician, Sergeant, Brace(X) and Parry(X)</b>.
///
/// <para>
/// <b>Why this fixture exists, and what it is NOT.</b> `claude/fdg-todo.md` listed these five as rules
/// "AoF and Skirmish add that GF does not have at all", under a heading about the per-system rule gap.
/// Read quickly, that says they are missing from the game - and they ARE missing from
/// <see cref="CoreRuleCatalog"/>, which is the engine's built-in set, and they appear on 68-81 of the
/// bundled Age of Fantasy and Skirmish army books. It looks exactly like a large silent gap.
/// </para>
/// <para>
/// It is not one. Each of the five is <b>fully defined in the book data</b>, with real hooks, conditions
/// and effects, and the books' definitions reach play: <c>ListCompiler</c> copies
/// <c>book.RuleDefinitions</c> onto the compiled army, and <c>GameBootstrap.BuildRuleResolver</c>
/// registers every army's embedded definitions into the shared resolver on top of the core catalog. That
/// is the #059 embedded-rule design, the same mechanism every faction-specific rule rides.
/// </para>
/// <para>
/// So these tests pin the thing that was genuinely missing: <b>evidence</b>. Nothing proved the five
/// resolve, which is why the to-do entry could be read as a gap for weeks without anyone being able to
/// settle it in a minute. If a future book refresh drops one of the definitions, or a well-meaning
/// change makes the core catalog authoritative over embedded rules, these fail - and that is a real
/// regression, because 70-odd army books would quietly stop having Banners.
/// </para>
/// </summary>
[TestFixture]
public class AoFOnlyCoreRulesTests
{
    /// <summary>The five rules AoF and Skirmish have and Grimdark Future does not.</summary>
    private static readonly string[] AoFOnlyRules = { "Banner", "Musician", "Sergeant", "Brace", "Parry" };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<string> AoFBookPaths()
    {
        foreach (string folder in new[] { "Age of Fantasy", "AoF Skirmish" })
        {
            string dir = Path.Combine(BooksDirectory, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (string path in Directory
                         .EnumerateFiles(dir, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
                yield return path;
        }
    }

    private static BookFile Load(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    [SetUp]
    public void RequireBundledBooks() =>
        Assume.That(Directory.Exists(BooksDirectory), Is.True,
            "these tests read the bundled books beside the test binary; none were found.");

    /// <summary>
    /// None of the five is in the built-in catalog - which is the fact the to-do entry was describing,
    /// and is pinned here so the next reader can see at once that it is deliberate rather than an
    /// oversight. Grimdark Future does not have these rules, so the shared GF-worded catalog should not
    /// carry them.
    /// </summary>
    [Test]
    public void TheFiveAreNotInTheBuiltInCatalog_WhichIsCorrect()
    {
        var catalogNames = CoreRuleCatalog.All.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.That(AoFOnlyRules.Where(r => catalogNames.Contains(r)), Is.Empty,
            "these are Age of Fantasy rules; the built-in catalog is Grimdark Future's set and the books "
            + "supply them instead.");
    }

    /// <summary>
    /// And every one of them IS defined by the books that use it. This is the half that turns "missing
    /// from the catalog" into "not missing from the game".
    /// </summary>
    [Test]
    [TestCase("Banner")]
    [TestCase("Musician")]
    [TestCase("Sergeant")]
    [TestCase("Brace")]
    [TestCase("Parry")]
    public void EveryBookThatUsesTheRule_AlsoDefinesIt(string rule)
    {
        var usingBooks = new List<string>();
        var definingBooks = new List<string>();

        foreach (string path in AoFBookPaths())
        {
            BookFile book = Load(path);
            bool uses = book.Units.Any(u => NamesRule(u.Rules, rule)
                                            || u.Weapons.Any(w => NamesRule(w.SpecialRules, rule))
                                            || u.Sections.Any(s => s.Options.Any(o =>
                                                NamesRule(o.RulesGained, rule)
                                                || o.WeaponsGained.Any(w => NamesRule(w.SpecialRules, rule)))));
            bool defines = book.RuleDefinitions.Any(d =>
                string.Equals(d.Name, rule, StringComparison.OrdinalIgnoreCase));

            if (uses) usingBooks.Add(Path.GetFileName(path));
            if (uses && defines) definingBooks.Add(Path.GetFileName(path));
        }

        Assert.That(usingBooks, Is.Not.Empty,
            $"precondition: some bundled Age of Fantasy book must actually use {rule}.");
        Assert.That(definingBooks, Is.EquivalentTo(usingBooks),
            $"every book that puts {rule} on a unit must also carry its definition - a book that uses it "
            + "without defining it would drop the rule at launch, and the unit would simply not have it.");
    }

    /// <summary>
    /// The end of the chain, and the test that proves the rules actually REACH a game: take a real
    /// bundled book, compile an army from it exactly as the Army Forge does, build the resolver the
    /// game builds from that army, and resolve the rule by name. A non-null result with real hooks is
    /// the rule working.
    /// <para>
    /// The two steps here are the production path, not a reconstruction of it: <c>ListCompiler</c>
    /// copies <c>book.RuleDefinitions</c> onto the army (the assertion below), and
    /// <c>GameBootstrap.BuildRuleResolver</c> does core-catalog-then-embedded per slot, which is the
    /// <c>CreateResolver</c> + <c>RegisterEmbeddedDefinitions</c> pair used here.
    /// </para>
    /// <para>
    /// The rule's own definition decides how it is referenced - scope, and whether it takes a number -
    /// rather than this test hardcoding "Parry is numeric". If a book refresh changed Parry from
    /// Parry(X) to a plain Parry, a hardcoded test would fail for the wrong reason.
    /// </para>
    /// </summary>
    [Test]
    [TestCase("Banner")]
    [TestCase("Musician")]
    [TestCase("Sergeant")]
    [TestCase("Brace")]
    [TestCase("Parry")]
    public void ARealArmyBuiltFromABundledBook_ResolvesTheRuleAtLaunch(string rule)
    {
        (BookFile book, SpecialRuleDefinition definition)? found = FirstDefiner(rule);
        Assume.That(found, Is.Not.Null, $"no bundled Age of Fantasy book defines {rule}.");
        (BookFile book, SpecialRuleDefinition definition) = found!.Value;

        RosterUnit bearer = book.Units.First();
        var list = new BuilderList { Name = "Probe", BookName = book.Name };
        list.Units.Add(new BuilderUnit { RosterUnitId = bearer.Id, ModelCount = bearer.BaseModelCount });
        BuiltArmyFile army = ListCompiler.Compile(book, list);

        Assert.That(army.RuleDefinitions.Any(d => string.Equals(d.Name, rule, StringComparison.OrdinalIgnoreCase)),
            Is.True, "the compiled army must carry the book's definition of the rule.");

        // What GameBootstrap.BuildRuleResolver does for each slot: the core catalog, then this army's
        // embedded definitions on top.
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        Assert.DoesNotThrow(() => ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army),
            $"the army carrying {rule} must pass embedded-rule validation.");

        SpecialRuleEntry entry = SpecialRuleRegistry.DefinitionIsNumeric(definition)
            ? new SpecialRuleEntry_CoreNumeric(rule, 1)
            : new SpecialRuleEntry_Core(rule);
        ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(resolver, entry, definition.Scope,
            ownerDescription: $"test probe for {rule}");

        Assert.That(resolved, Is.Not.Null,
            $"{rule} must resolve at launch - if it does not, every Age of Fantasy unit with it has been "
            + "playing without it.");
        Assert.That(resolved!.Definition.Passive.Count + resolved.Definition.Activated.Count,
            Is.GreaterThan(0),
            $"{rule} must resolve to something with actual behaviour, not an inert marker.");
    }

    private static (BookFile Book, SpecialRuleDefinition Definition)? FirstDefiner(string rule)
    {
        foreach (string path in AoFBookPaths())
        {
            BookFile book = Load(path);
            SpecialRuleDefinition? definition = book.RuleDefinitions.FirstOrDefault(d =>
                string.Equals(d.Name, rule, StringComparison.OrdinalIgnoreCase));
            if (definition != null && book.Units.Count > 0) return (book, definition);
        }
        return null;
    }

    /// <summary>
    /// Whether this rule list names the rule. Numeric rules (Brace(X), Parry(X)) arrive as a different
    /// entry type carrying their value, so the name is compared rather than the entry.
    /// </summary>
    private static bool NamesRule(IEnumerable<SpecialRuleEntry> rules, string rule) =>
        rules.Any(r => string.Equals(RuleNameOf(r), rule, StringComparison.OrdinalIgnoreCase));

    private static string RuleNameOf(SpecialRuleEntry entry) => entry switch
    {
        SpecialRuleEntry_Core core => core.Name,
        SpecialRuleEntry_CoreNumeric numeric => numeric.Name,
        SpecialRuleEntry_Text text => text.Name,
        SpecialRuleEntry_Alias alias => RuleNameOf(alias.AliasedRule),
        _ => entry.PrintableName,
    };
}
