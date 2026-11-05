using System;
using System.IO;
using System.Text.RegularExpressions;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #455 - between missions the player runs BOTH armies' books. campaign-flow-spec section 4, Kai's call,
/// confirmed 2026-11-05: "the bot never spends its own points."
/// </summary>
[TestFixture]
public class CampaignEnemyBooksTests
{
    private static string Source(params string[] parts)
    {
        for (DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        Assert.Ignore("Could not find " + string.Join("/", parts) + " beside the test run.");
        return "";
    }

    /// <summary>
    /// The after-mission write-back used to let the bot spend its own treasury, pick its own traits and
    /// recruit (#428). Kai ruled that it never does. The write-back needs a played table to call, so this
    /// guards its source: if the call comes back, this fails. Comments are stripped so the note explaining
    /// the removal cannot trip it.
    /// </summary>
    [Test]
    public void TheBotDoesNotSpendItsOwnTreasuryAfterAMission()
    {
        string source = Source("FdgRaylib", "Quest", "StrikeTeamFiles.cs");
        source = Regex.Replace(source, @"//[^\n]*", "");
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.That(source, Does.Not.Contain("CampaignBot.BetweenMissions("),
            "the player runs the enemy's economy (campaign-flow-spec section 4) - the bot must not spend.");
    }

    [Test]
    public void TheCampScreenCanManageTheEnemysArmy()
    {
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null, "writes to the real campaign folder.");
        string tag = Guid.NewGuid().ToString("N")[..6];
        CampaignNumbers n = CampaignNumbers.For(EGameSystem.GrimdarkFuture);
        string? mine = null, theirs = null;
        try
        {
            BuiltArmyFile Army(string name, bool bot, out string path)
            {
                var army = new BuiltArmyFile { Name = name };
                army.Units.Add(new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 });
                path = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, name);
                CampaignRules.Start(army, name, path, n, EGameSystem.GrimdarkFuture, isBot: bot);
                return army;
            }

            BuiltArmyFile enemy = Army($"zz Foe {tag}", true, out string enemyPath);
            theirs = enemyPath;
            Assert.That(StrikeTeamFiles.Save(theirs, enemy), Is.True);
            BuiltArmyFile player = Army($"zz Me {tag}", false, out string playerPath);
            mine = playerPath;
            player.StrikeTeam!.EnemyArmyFile = theirs;
            Assert.That(StrikeTeamFiles.Save(mine, player), Is.True);

            var screen = new StrikeTeamScreen();
            screen.Open(EGameSystem.GrimdarkFuture);
            screen.OnShown();
            screen.OpenCampForTest(mine);

            Assert.That(Path.GetFullPath(screen.ManagedPathForTest!), Is.EqualTo(Path.GetFullPath(mine)),
                "a campaign opens on the player's own army.");

            screen.SwitchSideForTest(true);
            Assert.That(screen.ManagingEnemyForTest, Is.True);
            Assert.That(Path.GetFullPath(screen.ManagedPathForTest!), Is.EqualTo(Path.GetFullPath(theirs)),
                "switching points the roster, unit panel and recruiting at the enemy's file.");
            Assert.That(Path.GetFullPath(screen.CurrentPathForTest!), Is.EqualTo(Path.GetFullPath(mine)),
                "the campaign itself - mission, journal - is still the player's.");

            // One new Hero per army per mission: buying the enemy one must not use up the player's.
            screen.HeroRecruitedForManagedForTest = true;
            screen.SwitchSideForTest(false);
            Assert.That(screen.HeroRecruitedForManagedForTest, Is.False);

            // Reopening the campaign always starts on the player's side.
            screen.SwitchSideForTest(true);
            screen.OpenCampForTest(mine);
            Assert.That(screen.ManagingEnemyForTest, Is.False);
        }
        finally
        {
            foreach (string? p in new[] { mine, theirs })
                try { if (p != null && File.Exists(p)) File.Delete(p); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
