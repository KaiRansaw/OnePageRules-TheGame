using System.IO;
using System.Linq;
using FDG;
using FDG.Campaign;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #426 - which game a campaign army on disk belongs to, and therefore which Campaign Rules v3.6.0
/// price list pays it.
///
/// <para>This used to be one answer for everyone: the campaign screen only offered Firefight, and
/// <c>WriteBack</c> paid every campaign army Firefight's +80/+160 regardless. That is why opening the
/// campaign to the other three books needed more than a button - a Grimdark Future campaign paid
/// Firefight rewards would have been quietly, permanently wrong, and the engine's own
/// <c>OneLoopPaysEachBooksMissionReward</c> could not catch it because the mistake is in which numbers
/// get passed, not in the loop that spends them.</para>
///
/// <para><b>The rule under test: the folder decides.</b> A campaign army lives in its own system's
/// armies folder, which is also how the lobby decides which armies a game of that system accepts, so
/// the location is the one fact that cannot go stale. It is also the migration path for a campaign army
/// written before <c>StrikeTeamInfo.GameSystem</c> existed, which deserializes to Grimdark Future (0)
/// however it was really played.</para>
/// </summary>
[TestFixture]
public class CampaignFileSystemTests
{
    [SetUp]
    public void RequireAnArmiesFolder() =>
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null,
            "these tests read the real armies folder layout; none was found from the test host.");

    [Test]
    [TestCase(EGameSystem.GrimdarkFuture)]
    [TestCase(EGameSystem.GFFirefight)]
    [TestCase(EGameSystem.AgeOfFantasy)]
    [TestCase(EGameSystem.AoFSkirmish)]
    public void SystemForPath_ReadsTheFolderItSitsIn(EGameSystem system)
    {
        string path = Path.Combine(StrikeTeamFiles.FolderFor(system)!, "Campaign - Somebody.fdgarmy");

        Assert.That(StrikeTeamFiles.SystemForPath(path, new StrikeTeamInfo()), Is.EqualTo(system));
    }

    [Test]
    [TestCase(EGameSystem.GrimdarkFuture)]
    [TestCase(EGameSystem.AoFSkirmish)]
    public void SystemForPath_ReadsASubfolderToo(EGameSystem system)
    {
        // The folder scan recurses (#402), so a campaign army filed under armies\GDF\Chris\ is still a
        // Grimdark Future army.
        string path = Path.Combine(StrikeTeamFiles.FolderFor(system)!, "mine", "Campaign - Somebody.fdgarmy");

        Assert.That(StrikeTeamFiles.SystemForPath(path, new StrikeTeamInfo()), Is.EqualTo(system));
    }

    // The migration case, and the reason the folder is read FIRST rather than as a fallback: a campaign
    // army written before the GameSystem field existed loads with Grimdark Future on it whatever it
    // really is, and paying a Firefight strike team GF's +150/+300 would double its income.
    [Test]
    public void SystemForPath_PrefersTheFolderOverADisagreeingBlock()
    {
        string path = Path.Combine(StrikeTeamFiles.FolderFor(EGameSystem.GFFirefight)!, "Strike Team - Old.fdgarmy");
        var stale = new StrikeTeamInfo { GameSystem = EGameSystem.GrimdarkFuture };

        Assert.That(StrikeTeamFiles.SystemForPath(path, stale), Is.EqualTo(EGameSystem.GFFirefight),
            "a pre-3.6 strike team in the Firefight folder must be paid Firefight's rewards.");
        Assert.That(CampaignNumbers.For(StrikeTeamFiles.SystemForPath(path, stale)).WinPoints,
            Is.EqualTo(CampaignNumbers.Firefight.WinPoints));
    }

    [Test]
    public void SystemForPath_FallsBackToTheBlock_ForAFileKeptElsewhere()
    {
        string path = Path.Combine(Path.GetTempPath(), "Campaign - Loose.fdgarmy");
        var info = new StrikeTeamInfo { GameSystem = EGameSystem.AgeOfFantasy };

        Assert.That(StrikeTeamFiles.SystemForPath(path, info), Is.EqualTo(EGameSystem.AgeOfFantasy));
    }

    [Test]
    [TestCase(EGameSystem.GrimdarkFuture)]
    [TestCase(EGameSystem.GFFirefight)]
    [TestCase(EGameSystem.AgeOfFantasy)]
    [TestCase(EGameSystem.AoFSkirmish)]
    public void NewPath_PutsANewCampaignArmyInItsOwnSystemsFolder(EGameSystem system)
    {
        string path = StrikeTeamFiles.NewPath(system, "Test Army");

        Assert.That(StrikeTeamFiles.SystemForPath(path, null), Is.EqualTo(system),
            "a new campaign army must land where the lobby will serve it as that system's army.");
        Assert.That(Path.GetFileName(path), Does.StartWith("Campaign - "));
    }

    /// <summary>
    /// The campaign screen offers exactly the four books. Star Quest and AoF: Quest have their own
    /// campaign - the Quest Campaign Builder, a different book on its own screen - and must not appear
    /// here, which is the "every game mode has its own stats" trap in campaign clothing.
    /// </summary>
    [Test]
    public void CampaignSystems_AreTheFourWithACampaignBook()
    {
        Assert.That(StrikeTeamFiles.CampaignSystems, Is.EquivalentTo(new[]
        {
            EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight,
            EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish,
        }));
        Assert.That(StrikeTeamFiles.CampaignSystems, Does.Not.Contain(EGameSystem.GFStarQuest));
        Assert.That(StrikeTeamFiles.CampaignSystems, Does.Not.Contain(EGameSystem.AoFQuest));
        Assert.That(StrikeTeamFiles.CampaignSystems.All(CampaignNumbers.HasCampaignBook), Is.True);
    }
}
