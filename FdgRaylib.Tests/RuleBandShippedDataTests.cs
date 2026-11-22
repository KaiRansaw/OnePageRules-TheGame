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
    // Audit 468 (Kai, 2026-11-19: "Audit them next"), batch 1: the "before attacking, pick ..." buffs, debuffs and
    // marks. Many were built for GF's "pick one" wording and shipped unchanged to the games that print "pick up to 3"
    // and, in the Quest games, "Only one use per game".
    private static IEnumerable<(string Where, string Text, JsonElement Ability)> BeforeAttackPicks()
    {
        foreach (var d in Definitions())
        {
            string? text = OfficialRuleText.OwnText(d.Name, d.Slug);
            if (text == null) continue;
            using JsonDocument doc = JsonDocument.Parse(d.Json);
            if (!doc.RootElement.TryGetProperty("activated", out JsonElement acts)) continue;
            foreach (JsonElement a in acts.EnumerateArray())
                if (a.GetProperty("triggerHook").GetString() == "Activation_OnBeforeAttackAction"
                    && a.TryGetProperty("targetSelector", out JsonElement sel) && sel.ValueKind == JsonValueKind.Object
                    && sel.GetProperty("targetAffinity").GetString() != "Self")
                    yield return ($"{d.Slug} / {d.Book} / {d.Name}", text, a.Clone());
        }
    }

    [Test]
    public void BeforeAttackPicks_TakeAsManyUnitsAsTheBookSays()
    {
        var bad = BeforeAttackPicks().Where(x =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(x.Text, @"\bpick (one|up to (\d+))\b");
            if (!m.Success) return false;
            int want = m.Groups[1].Value == "one" ? 1 : int.Parse(m.Groups[2].Value);
            return x.Ability.GetProperty("targetSelector").GetProperty("maxCount").GetInt32() != want;
        }).Select(x => x.Where).Distinct().ToList();
        Assert.That(bad, Is.Empty, "the text says how many units it picks; these take a different number");
    }

    [Test]
    public void OnlyOneUsePerGame_IsOncePerGame()
    {
        var bad = BeforeAttackPicks().Where(x => x.Text.StartsWith("Only one use per game.")
            && x.Ability.GetProperty("cost").GetProperty("kind").GetString() != "oncePerGame")
            .Select(x => x.Where).Distinct().ToList();
        Assert.That(bad, Is.Empty, "\"Only one use per game\" but usable every activation");
    }

    [Test]
    public void EnemyPicksInLineOfSight_NeedLineOfSight()
    {
        var bad = BeforeAttackPicks().Where(x => x.Text.Contains("in line of sight")
            && x.Ability.GetProperty("targetSelector").GetProperty("targetAffinity").GetString() == "Foe"
            && !x.Ability.GetProperty("targetSelector").GetProperty("requireLineOfSight").GetBoolean())
            .Select(x => x.Where).Distinct().ToList();
        Assert.That(bad, Is.Empty, "the text says \"in line of sight\"; these pick through walls");
    }
    // Audit 468, batch 2: rule by rule against OPR's wording.
    [Test]
    public void ToughGates_UseTheNumberTheBookPrints()
    {
        List<string> bad = Offenders((text, json) =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"Tough\((\d+)\) or higher");
            if (!m.Success) return false;
            var gates = System.Text.RegularExpressions.Regex.Matches(json, "\"minToughValue\":\\s*(\\d+)");
            return gates.Count > 0 && gates.Any(g => g.Groups[1].Value != m.Groups[1].Value);
        });
        Assert.That(bad, Is.Empty, "the text says Tough(3), (9) or (27) - each game its own - and the data gates on another number");
    }

    [Test]
    public void MayRollPlusOneAttack_IsAnExtraAttackDie_NotALandedHit()
    {
        List<string> bad = Offenders((text, json) =>
            text.Contains("may roll +1 attack with that weapon") && text.Contains("roll of 6 to hit")
            && json.Contains("\"addExtraHit\""));
        Assert.That(bad, Is.Empty, "\"may roll +1 attack\" is a die that still has to hit; these score the hit outright");
    }

    [Test]
    public void FrenzyAndGrowthMarkers_StopAtTheBooksMaximum()
    {
        List<string> bad = Offenders((text, json) =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, @"max\. of (two|four|six) (tokens|markers)");
            if (!m.Success) return false;
            string want = m.Groups[1].Value switch { "two" => "2", "four" => "4", _ => "6" };
            return System.Text.RegularExpressions.Regex.Matches(json, "\"maxTotal\":\\s*(\\d+)").Any(g => g.Groups[1].Value != want);
        });
        Assert.That(bad, Is.Empty, "a Frenzy / Growth rule collects more or fewer markers than its text allows");
    }

    // 2026-11-22: "If most models in this unit are within 1" of terrain, THIS MODEL gets +1 to hit" - a per-model
    // attack bonus like Good Fighter, so a Hero carrying it inside a unit without it still gets it. An every-model
    // gate broke exactly that case. (Grounded Speed keeps its gate on purpose: a unit moves together, so the extra
    // move only helps when every model has it.)
    [Test]
    public void PerModelHitBonus_WithATerrainCheck_IsNotGatedOnEveryModel()
    {
        List<string> bad = Offenders((text, json) =>
            text.StartsWith("If most models in this unit are within", StringComparison.Ordinal)
            && text.Contains("this model gets +1 to hit")
            && json.Contains("allModelsHaveThisRule"));
        Assert.That(bad, Is.Empty, "a per-model hit bonus needs only the unit's terrain check.");
        Assert.That(Definitions().Count(d => d.Name == "Grounded Precision"), Is.GreaterThan(0), "control: the rule still ships");
    }
}
