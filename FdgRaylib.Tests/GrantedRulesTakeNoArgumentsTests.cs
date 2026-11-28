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
using NUnit.Framework;

namespace FdgRaylib.Tests;

// A rule handed over by name - an aura, an "add rule" grant, a spell's grant or mark - carries no (X). If the rule it
// names reads an argument (Ravage(X), Impact(X)...), the engine skips it at play time ("reads arguments, but grants carry
// none - skipped") and the aura / spell silently does nothing. That is how GF Ravage Aura was dead (Lane 1, 2026-11-28).
// This walks every shipped book: each name granted by any rule or spell must resolve (book first, then the core catalog)
// to a definition that reads no argument.
[TestFixture]
public class GrantedRulesTakeNoArgumentsTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<string> GrantedNames(object? node)
    {
        switch (node)
        {
            case Effect.Aura a: yield return a.RuleName; break;
            case Effect.AddRule r: yield return r.RuleName; break;
            case Effect.MarkTarget m: yield return m.RuleName; break;
        }
    }

    private static IEnumerable<Effect> Effects(SpecialRuleDefinition rule) =>
        rule.Passive.Select(h => h.Effect).Concat(rule.Activated.Select(a => a.Effect));

    [Test]
    public void EveryRuleGrantedByName_ReadsNoArgument()
    {
        RuleResolver catalog = CoreRuleCatalog.CreateResolver();
        var wrong = new SortedSet<string>();
        int grants = 0;
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            var own = book.RuleDefinitions.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            IEnumerable<(string From, Effect Effect)> effects = book.RuleDefinitions.SelectMany(r => Effects(r).Select(e => (r.Name, e)))
                .Concat(book.Spells.Select(s => ("spell " + s.Name, s.Effect)));
            foreach ((string from, Effect effect) in effects)
                foreach (string name in GrantedNames(effect))
                {
                    grants++;
                    SpecialRuleDefinition? target = own.TryGetValue(name, out SpecialRuleDefinition? d) ? d
                        : catalog.TryResolve(name, out ResolvedRule resolved) ? resolved.Definition : null;
                    if (target == null) continue;   // an unresolved name is RuleCoverage's business, not this test's
                    string json = JsonSerializer.Serialize(target, RuleJson.Options);
                    if (json.Contains("\"kind\": \"arg\"") || json.Contains("\"kind\":\"arg\""))
                        wrong.Add($"{Path.GetFileName(path)}: {from} grants '{name}', which reads an argument");
                }
        }
        Assert.That(grants, Is.GreaterThan(1000), "the grants were found");
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
    }
}
