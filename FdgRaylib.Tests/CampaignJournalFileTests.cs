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
/// #448 - the campaign journal has to survive the real campaign file. The engine's
/// <c>CampaignJournalTests</c> cover the rules for writing notes; this covers the one thing that would
/// lose the player's writing silently, which is the save and load the campaign screen actually uses.
/// </summary>
[TestFixture]
public class CampaignJournalFileTests
{
    [SetUp]
    public void RequireAnArmiesFolder() =>
        Assume.That(ArmyPaths.FolderPath, Is.Not.Null,
            "this test writes to the real campaign folder; none was found from the test host.");

    [Test]
    public void TheJournalSurvivesSaveAndLoad()
    {
        string name = "zz Journal " + Guid.NewGuid().ToString("N")[..8];
        string path = StrikeTeamFiles.NewPath(EGameSystem.GrimdarkFuture, name);
        try
        {
            var army = new BuiltArmyFile { Name = name };
            army.Units.Add(new UnitFileEntry { Name = "Warriors", ModelCount = 5, PointCost = 100 });
            CampaignRules.Start(army, name, path, CampaignNumbers.For(EGameSystem.GrimdarkFuture),
                EGameSystem.GrimdarkFuture, ECampaignType.PointLimited, vpTarget: 7);
            army.Selections = null;

            StrikeTeamInfo info = army.StrikeTeam!;
            info.Matches = 2;
            CampaignJournal.SetCampaignNote(info, "The Reavers swore an oath.\n\nThe Choir laughed.");
            CampaignJournal.SetNote(info, 1, "Lost the ridge.");
            CampaignJournal.SetNote(info, 3, "Briefing: take it back.");
            Assert.That(StrikeTeamFiles.Save(path, army), Is.True);

            StrikeTeamInfo back = StrikeTeamFiles.Load(path)!.StrikeTeam!;
            Assert.That(back.Journal, Is.EqualTo("The Reavers swore an oath.\n\nThe Choir laughed."),
                "paragraph breaks must survive the file.");
            Assert.That(CampaignJournal.NoteFor(back, 1), Is.EqualTo("Lost the ridge."));
            Assert.That(CampaignJournal.NoteFor(back, 3), Is.EqualTo("Briefing: take it back."),
                "a briefing for the next mission is saved before that mission is played.");
            Assert.That(back.History, Is.Empty, "the journal must never land in the game's own log.");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
