using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FdgRaylib.Quest;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Kai, 2026-11-05: "during the AoFQ Hero building, a lot of the special rules on the hero sheet werent made
/// into tooltips ... make sure the tooltips are showing the correct AoFQ/GFSQ info, and not showing the other
/// gamemodes info." And: "when a hero/unit has a condition, make that condition in game a highlighted tooltip".
/// </summary>
[TestFixture]
public class QuestRuleTextTests
{
    /// <summary>Every rule on every shipped hero, on the hero and on its weapons, has text from its own game.</summary>
    [TestCase(EGameSystem.GFStarQuest)]
    [TestCase(EGameSystem.AoFQuest)]
    public void EveryShippedHeroRule_HasItsOwnGamesText(EGameSystem system)
    {
        QuestDataSet data = QuestDataSet.For(system);
        Assert.That(data.LoadError, Is.Null);
        var missing = new SortedSet<string>();
        int checkedCount = 0;
        foreach (SqFaction faction in data.Factions)
            foreach (SqHero hero in faction.Heroes)
            {
                IEnumerable<string> rules = hero.Rules.Concat(hero.Weapons.SelectMany(w => w.ToQuest().Rules));
                foreach (string rule in rules)
                {
                    checkedCount++;
                    if (string.IsNullOrWhiteSpace(QuestRuleText.For(rule, system, faction)))
                        missing.Add($"{faction.Faction}: {rule}");
                }
            }
        Assert.That(checkedCount, Is.GreaterThan(100), "the hero data must actually have been read");
        Assert.That(missing, Is.Empty, "rules with no text of their own game: " + string.Join("; ", missing.Take(20)));
    }

    [Test]
    public void OwnText_NeverFallsBackToTheParentGame()
    {
        // Find a rule Grimdark Future words that Star Quest does not - the strict lookup must say nothing,
        // where the general one hands back GF's wording.
        string? gfOnly = null;
        foreach (string candidate in new[] { "Destroyer", "Duellist", "Elite", "Explorer", "Chemist" })
            if (OfficialRuleText.OwnText(candidate, GameSystemInfo.Slug(EGameSystem.GrimdarkFuture)) != null
                && OfficialRuleText.OwnText(candidate, GameSystemInfo.Slug(EGameSystem.GFStarQuest)) == null)
            { gfOnly = candidate; break; }
        Assert.That(gfOnly, Is.Not.Null, "no GF-only rule among the candidates");
        Assert.That(OfficialRuleText.Text(gfOnly, GameSystemInfo.Slug(EGameSystem.GFStarQuest)), Is.Not.Null,
            "sanity: the general lookup does fall back");
        Assert.That(OfficialRuleText.OwnText(gfOnly, GameSystemInfo.Slug(EGameSystem.GFStarQuest)), Is.Null);
    }

    [Test]
    public void OwnText_IgnoresTheArgument()
    {
        string slug = GameSystemInfo.Slug(EGameSystem.AoFQuest);
        Assert.That(OfficialRuleText.OwnText("Tough(3)", slug), Is.EqualTo(OfficialRuleText.OwnText("Tough", slug)));
        Assert.That(OfficialRuleText.OwnText("Tough", slug), Is.Not.Null);
    }

    /// <summary>The books' own wording: GFSQ Core 2.1.0 "to remove status", AoFQ Beginners Guide 2.1.0 "to remove".</summary>
    [Test]
    public void Conditions_AreWordedForTheirOwnGame()
    {
        Assert.That(QuestTreasure.ConditionText(QuestTreasure.Afflicted, EGameSystem.GFStarQuest),
            Is.EqualTo("When activated, must pass a Str test to remove status. If failed, the model takes 1 wound."));
        Assert.That(QuestTreasure.ConditionText(QuestTreasure.Afflicted, EGameSystem.AoFQuest),
            Is.EqualTo("When activated, must pass a Str test to remove. If failed, the model takes 1 wound."));
        foreach (string c in new[] { QuestTreasure.Afflicted, QuestTreasure.Crippled, QuestTreasure.Diseased, QuestTreasure.Impaired })
            Assert.That(QuestTreasure.ConditionText(c, EGameSystem.AoFQuest), Is.Not.Null.And.Not.Empty, c);
        Assert.That(QuestTreasure.ConditionText("Shaken", EGameSystem.AoFQuest), Is.Null);
    }

    [TestCase("HeroBuilderScreen.cs", "RuleHoverText.DrawWrapped(RuleSegments(hero, hero.Rules)")]
    [TestCase("HeroBuilderScreen.cs", "RuleHoverText.DrawWrapped(RuleSegments(hero, wpn.Rules)")]
    [TestCase("RaylibRenderer.cs", "QuestTreasure.ConditionText(c, system)")]
    public void TheHoversAreDrawn(string file, string snippet)
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "fdg-raylib.sln"))) dir = dir.Parent;
        Assert.That(File.ReadAllText(Path.Combine(dir!.FullName, "FdgRaylib", "Rendering", file)), Does.Contain(snippet));
    }
}
