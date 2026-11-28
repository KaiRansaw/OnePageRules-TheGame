using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Every army book carries its own copy of each rule it uses. Two copies with the same name AND the same Army Forge text
// are the same rule, so they must be built the same way - a copy that differs is a hand edit that reached some books and
// not others (Lane 1 deep hunt, 2026-11-28). It found two:
//   - Melee Slayer ("When charging, this model's weapons get AP(+2) if most models in the target have Tough(3) or
//     higher") was gated on any melee in AoF / AoFS Lust Disciples only, so it also worked on strike-backs there;
//   - Ravage Aura had two builds, both wrong (see RavageAuraShippedDataTests).
// The same name with DIFFERENT text (one game's wording against another's) is allowed to differ and is not compared.
[TestFixture]
public class ShippedRuleCopiesAgreeTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [Test]
    public void SameNameAndSameText_MeansTheSameDefinition()
    {
        var copies = new Dictionary<(string Name, string Text), Dictionary<string, List<string>>>();
        foreach (string path in Directory.GetFiles(BooksDirectory, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (SpecialRuleDefinition rule in book.RuleDefinitions)
            {
                // The whole definition through its own declared type, so the polymorphic conditions / effects serialize
                // in full (name and text are the key, so they are the same within a group anyway).
                string body = JsonSerializer.Serialize(rule, RuleJson.Options);
                var key = (rule.Name, rule.Description ?? "");
                if (!copies.TryGetValue(key, out var bodies)) copies[key] = bodies = new Dictionary<string, List<string>>();
                if (!bodies.TryGetValue(body, out var books)) bodies[body] = books = new List<string>();
                books.Add(Path.GetFileName(path));
            }
        }

        var differing = copies.Where(c => c.Value.Count > 1)
            .Select(c => $"{c.Key.Name}: " + string.Join(" / ",
                c.Value.OrderByDescending(b => b.Value.Count).Select(b => $"{b.Value.Count}x e.g. {b.Value[0]}")))
            .ToList();
        Assert.That(copies.Count, Is.GreaterThan(500), "the books were read");
        Assert.That(differing, Is.Empty, string.Join("\n", differing));
    }
}
