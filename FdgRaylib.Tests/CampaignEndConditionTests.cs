using System;
using System.IO;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #445 - a campaign's end condition has to survive the trip to disk and back.
///
/// <para>
/// The engine's <c>CampaignStandingTests</c> cover the rule: given two armies' blocks, is the campaign
/// decided and who took it. They cannot cover the thing most likely to break it, which is that
/// <see cref="StrikeTeamInfo.VpTarget"/> is a NEW property on a type that is serialized into every
/// campaign file. If it does not round-trip, every campaign reads back with no target the next time it
/// is opened, the race silently becomes Endless, and nothing anywhere fails - the game simply never
/// ends. So these go through the real <see cref="StrikeTeamFiles"/> save and load.
/// </para>
///
/// <para>
/// Written to the real campaign folder under a name nothing else would pick, and cleaned up afterwards.
/// A temp directory would be testing a copy of the path logic rather than the path logic.
/// </para>
/// </summary>
[TestFixture]
public class CampaignEndConditionTests
{
    [SetUp]
    public void RequireAnArmiesFolder() =>
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null,
            "these tests write to the real campaign folder; none was found from the test host.");

    private static BuiltArmyFile OneUnitArmy(string name) => new()
    {
        Name = name,
        Units =
        {
            new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 },
        },
    };

    /// <summary>Creates a campaign army on disk the way the screen does, and hands back its path.</summary>
    private static string Create(string name, ECampaignType type, int target, bool isBot = false)
    {
        CampaignNumbers n = CampaignNumbers.For(EGameSystem.GrimdarkFuture);
        BuiltArmyFile army = OneUnitArmy(name);
        string path = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, name);

        CampaignRules.Start(army, name, path, n, EGameSystem.GrimdarkFuture, type,
            noPermadeath: false, isBot: isBot, vpTarget: target);
        army.Selections = null;

        Assert.That(StrikeTeamFiles.Save(path, army), Is.True, "couldn't write the campaign file.");
        return path;
    }

    private static void Delete(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The load-bearing one. A target written at creation must still be there when the campaign is
    /// reopened - otherwise the race quietly becomes Endless between sessions.
    /// </summary>
    [Test]
    public void TheVpTargetSurvivesSaveAndLoad()
    {
        string name = "zz Target " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            path = Create(name, ECampaignType.PointLimited, 6);

            BuiltArmyFile? reloaded = StrikeTeamFiles.Load(path);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded!.StrikeTeam, Is.Not.Null);
            Assert.That(reloaded.StrikeTeam!.CampaignType, Is.EqualTo(ECampaignType.PointLimited));
            Assert.That(reloaded.StrikeTeam.VpTarget, Is.EqualTo(6),
                "the target did not round-trip, so every reopened campaign would behave as Endless.");

            CampaignPreGame.CampaignStanding standing =
                CampaignPreGame.Standing(reloaded.StrikeTeam, null);
            Assert.That(standing.HasTarget, Is.True);
            Assert.That(standing.Describe(), Is.EqualTo("First to 6 VP"));
            Assert.That(standing.IsOver, Is.False, "a brand new campaign is not finished.");
        }
        finally { Delete(path); }
    }

    [Test]
    public void AnEndlessCampaignRoundTripsAsEndless()
    {
        string name = "zz Endless " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            path = Create(name, ECampaignType.Endless, 7);

            BuiltArmyFile? reloaded = StrikeTeamFiles.Load(path);
            Assert.That(reloaded!.StrikeTeam!.CampaignType, Is.EqualTo(ECampaignType.Endless));

            CampaignPreGame.CampaignStanding standing =
                CampaignPreGame.Standing(reloaded.StrikeTeam, null);
            Assert.That(standing.HasTarget, Is.False,
                "a stored number must stay inert while the type is Endless.");
            Assert.That(standing.Describe(), Is.EqualTo("Endless"));
        }
        finally { Delete(path); }
    }

    /// <summary>
    /// Both armies of one campaign are separate files, and the end condition belongs to the campaign
    /// rather than to either of them. If the two disagree, whichever file a reader happens to open is
    /// the wrong answer.
    /// </summary>
    [Test]
    public void BothArmiesOfACampaignAgreeOnDisk()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string? mine = null, theirs = null;
        try
        {
            mine = Create($"zz Mine {tag}", ECampaignType.PointLimited, 5);
            theirs = Create($"zz Theirs {tag}", ECampaignType.PointLimited, 5, isBot: true);

            StrikeTeamInfo you = StrikeTeamFiles.Load(mine)!.StrikeTeam!;
            StrikeTeamInfo rival = StrikeTeamFiles.Load(theirs)!.StrikeTeam!;

            Assert.That(rival.IsBot, Is.True, "the rival is the bot's army.");
            Assert.That(rival.VpTarget, Is.EqualTo(you.VpTarget));
            Assert.That(rival.CampaignType, Is.EqualTo(you.CampaignType));
        }
        finally { Delete(mine, theirs); }
    }

    /// <summary>
    /// A campaign army written before #445 has no VpTarget in its JSON at all. It must deserialize to
    /// a campaign that keeps opening, not one that reports itself won.
    /// </summary>
    [Test]
    public void ACampaignFileWithNoTargetFieldStillOpens()
    {
        string name = "zz Legacy " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            path = Create(name, ECampaignType.PointLimited, 7);

            // Remove the property outright, which is exactly what an older file looks like - not set it
            // to 0. That difference matters: this way the test proves the DEFAULT is safe on a missing
            // field, rather than only that an explicit zero is. Done by editing the saved JSON rather
            // than by constructing the object, so what is under test is deserialization.
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("vpTarget").IgnoreCase,
                "if the property stopped being written, the rest of this test would prove nothing.");

            string older = System.Text.RegularExpressions.Regex.Replace(
                json, "\\s*\"vpTarget\"\\s*:\\s*-?\\d+\\s*,", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Assert.That(older, Does.Not.Contain("vpTarget").IgnoreCase, "the field should be gone now.");
            File.WriteAllText(path, older);

            BuiltArmyFile? reloaded = StrikeTeamFiles.Load(path);
            Assert.That(reloaded, Is.Not.Null, "an older campaign file must still load.");

            CampaignPreGame.CampaignStanding standing =
                CampaignPreGame.Standing(reloaded!.StrikeTeam!, null);
            Assert.That(standing.IsOver, Is.False,
                "a campaign saved before targets existed must not open as already won.");
            Assert.That(standing.Describe(), Is.EqualTo("Endless"));
        }
        finally { Delete(path); }
    }
}
