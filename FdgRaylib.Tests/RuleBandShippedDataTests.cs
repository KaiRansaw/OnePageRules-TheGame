using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Kai's 2026-11-18 rules audit, held against every shipped book. 3.6.0 wording vs. what the data does:
//  - "shoots/charges ... over 9"/6" away" is two bands (9" shooting, 6" charging) - a single 9" gate made the
//    charge half almost never fire;
//  - "a unit where most models have this rule" is MOST, not every model;
//  - "(instead of only ...)" Enhanced rules apply at any range, so no distance gate at all.
// Walks the raw JSON so it sees exactly what ships, in every system folder.
[TestFixture]
public class RuleBandShippedDataTests
{
    private static readonly Dictionary<string, string> SlugByFolder = new()
    {
        ["Grimdark Future"] = "grimdark-future",
        ["GF Firefight"] = "grimdark-future-firefight",
        ["GF Star Quest"] = "grimdark-future-star-quest",
        ["Age of Fantasy"] = "age-of-fantasy",
        ["AoF Skirmish"] = "age-of-fantasy-skirmish",
        ["AoF Quest"] = "age-of-fantasy-quest",
    };

    private static IEnumerable<(string Book, string Slug, string Name, string Json)> Definitions()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        foreach ((string folder, string slug) in SlugByFolder)
        foreach (string path in Directory.GetFiles(Path.Combine(root, folder), "*.fdgbook"))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("ruleDefinitions", out JsonElement defs)) continue;
            foreach (JsonElement def in defs.EnumerateArray())
                yield return (Path.GetFileName(path), slug, def.GetProperty("name").GetString()!, def.GetRawText());
        }
    }

    private static List<string> Offenders(Func<string, string, bool> broken) =>
        Definitions()
            .Select(d => (d, text: OfficialRuleText.OwnText(d.Name, d.Slug)))
            .Where(x => x.text != null && broken(x.text, x.d.Json))
            .Select(x => $"{x.d.Slug} / {x.d.Book} / {x.d.Name}")
            .Distinct().ToList();

    [Test]
    public void ShootChargeBands_AreNineAndSix_NotOneNineInchGate()
    {
        List<string> bad = Offenders((text, json) =>
            text.Contains("over 9\"/6\"") && !text.Contains("instead of only") &&
            System.Text.RegularExpressions.Regex.IsMatch(json,
                "\"attackedFromOverInches\",\\s*\"distanceInches\":\\s*9\\b"));
        Assert.That(bad, Is.Empty, "3.6.0 says 9\"/6\"; these still gate both halves on 9\".");
    }

    [Test]
    public void EnhancedRules_ApplyAtAnyRange()
    {
        List<string> bad = Offenders((text, json) =>
            text.Contains("instead of only") && text.Contains("over 9\"/6\"") &&
            (json.Contains("\"attackedFromOverInches\"") ||
             (json.Contains("\"attackedFromBeyond\"") && !json.Contains("\"kind\": \"not\""))));
        Assert.That(bad, Is.Empty, "\"(instead of only when ... over 9\"/6\")\" means always - no band.");
    }

    [Test]
    public void MostModelsRules_AreNotGatedOnEveryModel()
    {
        List<string> bad = Offenders((text, json) =>
            (text.Contains("most models have this rule") || text.Contains("most models in this unit have")) &&
            json.Contains("\"allModelsHaveThisRule\"") && !json.Contains("\"mostModelsHaveThisRule\""));
        Assert.That(bad, Is.Empty, "The 3.6.0 text says MOST models.");
    }
}
