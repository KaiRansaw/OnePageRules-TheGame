using System;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #447 - campaign armies must never turn up in a game that is not their campaign.
///
/// <para>
/// Kai, 2026-11-03: <i>"I want to have the Campaign Armies in a different folder than the
/// Skirmish/Firefight, Quest/StarQuest, GF/AoF... so NO Campaign armies will show up in any other game
/// modes, and vice versa."</i>
/// </para>
///
/// <para>
/// <b>This was a real bug, not untidiness.</b> <see cref="ArmyCatalog"/> scans <c>armies/</c> recursively
/// and nothing looked at the <c>strikeTeam</c> block, so a campaign army was offered in the lobby and
/// could be handed to a bot - fielding a list carrying XP, levels and injuries that the book never
/// priced, and quietly levelling up somebody's campaign army in an unrelated game.
/// </para>
///
/// <para>
/// The fix has two halves and these cover both: the <b>folder</b> (organisation - new campaign armies
/// live in <c>armies/Campaigns/&lt;Game Type&gt;/</c>) and the <b>content filter</b> (the guarantee -
/// a file with a <c>strikeTeam</c> block is not an ordinary army wherever it sits, which is what covers
/// the campaigns already on disk in the old location).
/// </para>
/// </summary>
[TestFixture]
public class CampaignArmyIsolationTests
{
    [SetUp]
    public void RequireAnArmiesFolder() =>
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null,
            "these tests write into the real armies folder layout; none was found from the test host.");

    private static BuiltArmyFile PlainArmy(string name)
    {
        var army = new BuiltArmyFile { Name = name, Faction = "Test Faction" };
        army.Units.Add(new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 });
        return army;
    }

    private static BuiltArmyFile CampaignArmy(string name)
    {
        BuiltArmyFile army = PlainArmy(name);
        CampaignRules.Start(army, name, name + ".fdgarmy",
            CampaignNumbers.For(EGameSystem.GrimdarkFuture), EGameSystem.GrimdarkFuture,
            ECampaignType.PointLimited, vpTarget: 7);
        army.Selections = null;
        return army;
    }

    private static void Delete(params string?[] paths)
    {
        foreach (string? path in paths)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    // -- The guarantee: contents, not location ------------------------------------------------------

    /// <summary>
    /// The load-bearing one. A campaign army sitting in the ORDINARY armies folder - which is where
    /// every campaign created before #447 still is - must not be catalogued as a playable army.
    /// </summary>
    [Test]
    public void ACampaignArmyInTheOldLocationIsNotOfferedAsAnOrdinaryArmy()
    {
        string name = "zz Isolation " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            string dir = StrikeTeamFiles.FolderFor(EGameSystem.GrimdarkFuture)!;
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, name + ".fdgarmy");
            Assert.That(StrikeTeamFiles.Save(path, CampaignArmy(name)), Is.True);

            Assert.That(ArmyCatalog.ReadEntry(path), Is.Null,
                "a campaign army must never be catalogued as an ordinary army - it would be offered in "
                + "the lobby and could be handed to a bot.");
        }
        finally { Delete(path); }
    }

    /// <summary>The other side of it: an ordinary army must still catalogue normally.</summary>
    [Test]
    public void AnOrdinaryArmyIsStillCatalogued()
    {
        string name = "zz Plain " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            string dir = StrikeTeamFiles.FolderFor(EGameSystem.GrimdarkFuture)!;
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, name + ".fdgarmy");
            Assert.That(StrikeTeamFiles.Save(path, PlainArmy(name)), Is.True);

            ArmyCatalogEntry? entry = ArmyCatalog.ReadEntry(path);
            Assert.That(entry, Is.Not.Null, "filtering campaign armies must not filter real ones.");
            Assert.That(entry!.Value.Name, Is.EqualTo(name));
            Assert.That(entry.Value.Points, Is.EqualTo(100));
        }
        finally { Delete(path); }
    }

    /// <summary>
    /// A campaign army in its NEW folder is equally invisible to the catalog. Belt and braces: the
    /// folder and the filter must agree, or one of them is doing nothing.
    /// </summary>
    [Test]
    public void ACampaignArmyInItsOwnFolderIsNotCataloguedEither()
    {
        string name = "zz NewFolder " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            path = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, name);
            Assert.That(StrikeTeamFiles.Save(path, CampaignArmy(name)), Is.True);
            Assert.That(ArmyCatalog.ReadEntry(path), Is.Null);
        }
        finally { Delete(path); }
    }

    // -- The folder ---------------------------------------------------------------------------------

    [Test]
    public void NewCampaignArmiesAreWrittenToTheCampaignFolder()
    {
        string path = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, "zz Where Does It Go");
        try
        {
            string expected = Path.GetFullPath(
                StrikeTeamFiles.CampaignFolderFor(EGameSystem.GrimdarkFuture)!);
            Assert.That(Path.GetFullPath(Path.GetDirectoryName(path)!), Is.EqualTo(expected));
            Assert.That(path, Does.Contain(ArmyPaths.CampaignsDirectoryName));
        }
        finally { Delete(File.Exists(path) ? path : null); }
    }

    /// <summary>
    /// Each campaign system gets its own folder, which is the second half of what Kai asked for: a
    /// Grimdark Future campaign must not list an Age of Fantasy campaign army.
    /// </summary>
    [Test]
    public void EachCampaignSystemHasItsOwnFolder()
    {
        var folders = StrikeTeamFiles.CampaignSystems
            .Select(s => Path.GetFullPath(StrikeTeamFiles.CampaignFolderFor(s)!))
            .ToList();

        Assert.That(folders.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Is.EqualTo(folders.Count), "two systems sharing a campaign folder would mix their armies.");

        foreach (EGameSystem system in StrikeTeamFiles.CampaignSystems)
        {
            string campaign = Path.GetFullPath(StrikeTeamFiles.CampaignFolderFor(system)!);
            string ordinary = Path.GetFullPath(StrikeTeamFiles.FolderFor(system)!);
            Assert.That(campaign, Is.Not.EqualTo(ordinary),
                $"{system}'s campaign armies must not share its ordinary armies folder.");
        }
    }

    /// <summary>
    /// The trap this nearly fell into. A campaign army's price list is decided by the folder it sits in,
    /// and the new folder is NOT under the ordinary one - so a path check that only knew the old layout
    /// would send every new campaign to the fallback, and a file with no GameSystem block would be paid
    /// Firefight's rewards for a Grimdark Future mission. That is the bug #426 fixed, re-introduced.
    /// </summary>
    [Test]
    public void TheCampaignFolderStillDecidesTheSystem()
    {
        foreach (EGameSystem system in StrikeTeamFiles.CampaignSystems)
        {
            string path = Path.Combine(StrikeTeamFiles.CampaignFolderFor(system)!, "Campaign - x.fdgarmy");

            Assert.That(StrikeTeamFiles.SystemForPath(path, null), Is.EqualTo(system),
                $"a campaign army in {system}'s own folder must resolve to {system}, with no help from "
                + "the file's contents.");
        }
    }

    [Test]
    public void TheOldLocationStillDecidesTheSystemToo()
    {
        foreach (EGameSystem system in StrikeTeamFiles.CampaignSystems)
        {
            string path = Path.Combine(StrikeTeamFiles.FolderFor(system)!, "Campaign - x.fdgarmy");
            Assert.That(StrikeTeamFiles.SystemForPath(path, null), Is.EqualTo(system),
                "campaigns in the old location must keep being paid correctly - nothing was moved.");
        }
    }

    // -- Reading both locations ---------------------------------------------------------------------

    /// <summary>
    /// Nothing is moved on the player's disk, so a campaign in the old location has to keep showing up
    /// on the campaign screen. It is found through <see cref="StrikeTeamFiles.LoadAll"/>, which is what
    /// that screen lists.
    /// </summary>
    [Test]
    public void ACampaignInEitherLocationIsStillListed()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string oldName = $"zz Old {tag}", newName = $"zz New {tag}";
        string? oldPath = null, newPath = null;
        try
        {
            string legacyDir = StrikeTeamFiles.FolderFor(EGameSystem.GrimdarkFuture)!;
            Directory.CreateDirectory(legacyDir);
            oldPath = Path.Combine(legacyDir, $"Campaign - {oldName}.fdgarmy");
            Assert.That(StrikeTeamFiles.Save(oldPath, CampaignArmy(oldName)), Is.True);

            newPath = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, newName);
            Assert.That(StrikeTeamFiles.Save(newPath, CampaignArmy(newName)), Is.True);

            var found = StrikeTeamFiles.LoadAll(EGameSystem.GrimdarkFuture)
                .Where(a => a.Army.StrikeTeam != null)
                .Select(a => Path.GetFullPath(a.Path))
                .ToList();

            Assert.That(found, Contains.Item(Path.GetFullPath(oldPath)),
                "a campaign in the old location must still be listed - nothing was migrated.");
            Assert.That(found, Contains.Item(Path.GetFullPath(newPath)));
        }
        finally { Delete(oldPath, newPath); }
    }

    /// <summary>
    /// And the reverse of Kai's "vice versa": an ordinary army must not be offered as a campaign to
    /// manage. The screen separates them by the block, so this pins that a plain army in the system
    /// folder is still seen as a plain army - which is what makes it a legal campaign STARTING army.
    /// </summary>
    [Test]
    public void AnOrdinaryArmyIsNotTreatedAsACampaign()
    {
        string name = "zz Starter " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            string dir = StrikeTeamFiles.FolderFor(EGameSystem.GrimdarkFuture)!;
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, name + ".fdgarmy");
            Assert.That(StrikeTeamFiles.Save(path, PlainArmy(name)), Is.True);

            var mine = StrikeTeamFiles.LoadAll(EGameSystem.GrimdarkFuture)
                .First(a => Path.GetFullPath(a.Path) == Path.GetFullPath(path));

            Assert.That(mine.Army.StrikeTeam, Is.Null,
                "a plain army has no campaign block, which is how it stays available as a starting army.");
        }
        finally { Delete(path); }
    }

    [Test]
    public void TheSameFileIsNeverListedTwice()
    {
        string name = "zz Once " + Guid.NewGuid().ToString("N")[..8];
        string? path = null;
        try
        {
            path = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, name);
            Assert.That(StrikeTeamFiles.Save(path, CampaignArmy(name)), Is.True);

            int times = StrikeTeamFiles.LoadAll(EGameSystem.GrimdarkFuture)
                .Count(a => Path.GetFullPath(a.Path) == Path.GetFullPath(path));

            Assert.That(times, Is.EqualTo(1),
                "two search folders must not list one army twice - it would offer two campaigns from "
                + "one file.");
        }
        finally { Delete(path); }
    }
}
