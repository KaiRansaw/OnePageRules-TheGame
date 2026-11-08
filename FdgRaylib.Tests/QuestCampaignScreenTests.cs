using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.Quest;
using FdgRaylib.Quest;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Batch E (2026-11-07) - the words the Hero Builder and Quest Campaigns screens show, pinned without a
// window: the Hero Builder's "what's still missing" line, the journal's "What happened" and export, the
// book's structure line (no more stale "D3 missions"), AoF: Quest's gold, and "a hero belongs to ONE
// campaign".
[TestFixture]
public class QuestCampaignScreenTests
{
    [Test]
    public void HeroBuilder_MissingLine_WalksTheFourSteps()
    {
        Assert.That(HeroBuilderScreen.SaveProblem("", false, false, null, 0), Is.EqualTo("Pick a base hero (step 1)."));
        Assert.That(HeroBuilderScreen.SaveProblem("", false, true, null, 0), Is.EqualTo("Give the hero a name (step 1)."));
        Assert.That(HeroBuilderScreen.SaveProblem("Vex", false, true, null, 0), Is.EqualTo("Pick a class (step 2)."));
        Assert.That(HeroBuilderScreen.SaveProblem("Vex", false, true, 2, 1), Is.EqualTo("Pick 1 more starter skill (step 3)."));
        Assert.That(HeroBuilderScreen.SaveProblem("Vex", false, true, 2, 0), Is.EqualTo("Pick 2 more starter skills (step 3)."));
        Assert.That(HeroBuilderScreen.SaveProblem("Vex", false, true, 2, 2), Is.Null, "gear (step 4) is optional");
        Assert.That(HeroBuilderScreen.SaveProblem("Vex", true, false, null, 0), Is.Null, "a hero that has played only needs its name");
        Assert.That(HeroBuilderScreen.StepNames, Is.EqualTo(new[] { "1. Hero & Name", "2. Class", "3. Starter Skills", "4. Gear" }));
    }

    [Test]
    public void StructureLine_IsTheBooksAndNotTheStaleD3()
    {
        string sq = QuestJournal.BookStructureLine(EGameSystem.GFStarQuest);
        Assert.That(sq, Does.Contain("D3+2 chapters, with chapters having D3+1 missions"));
        string aof = QuestJournal.BookStructureLine(EGameSystem.AoFQuest);
        Assert.That(aof, Does.Contain("D3+1 missions"));
        Assert.That(aof, Does.Not.Contain("GFSQ"), "AoF: Quest never quotes the Star Quest book");
        foreach (string line in new[] { sq, aof })
            Assert.That(line.All(ch => ch < 128), "game text is ASCII");
    }

    private static CampaignMissionRecord Record() => new()
    {
        Outcome = "success", Difficulty = 2, Primary = 3, PrimaryDone = true, Secondary = 0, Rounds = 5,
        KnockedOut = { "Vex" }, Injuries = { "Vex: Broken Arm" }, Xp = { "Vex +4" }, Money = { "Vex +10" },
        Loot = { "Vex: Health Potion" }, Events = { "33 - Dazing Spores" }, PlayedAt = "2026-11-07 20:30",
    };

    [Test]
    public void WhatHappened_SaysOnlyWhatTheGameRecorded_InTheSystemsMoney()
    {
        List<string> sq = QuestJournal.RecordLines(Record(), EGameSystem.GFStarQuest);
        Assert.That(sq[0], Is.EqualTo("Mission complete."));
        Assert.That(sq[1], Is.EqualTo("Primary: Retrieval - completed. Secondary: never came up."));
        Assert.That(sq, Does.Contain("Knocked out: Vex."));
        Assert.That(sq, Does.Contain("Injuries: Vex: Broken Arm."));
        Assert.That(sq, Does.Contain("XP: Vex +4."));
        Assert.That(sq, Does.Contain("Loot: Vex: Health Potion."));
        Assert.That(sq, Does.Contain("Credits: Vex +10c."));
        Assert.That(sq, Does.Contain("Events: 33 - Dazing Spores."));
        Assert.That(sq.Last(), Is.EqualTo("Difficulty: Intermediate. Rounds: 5. Played 2026-11-07 20:30."));

        List<string> aof = QuestJournal.RecordLines(Record(), EGameSystem.AoFQuest);
        Assert.That(aof, Does.Contain("Gold: Vex +10g."), "AoF: Quest counts gold");

        var quiet = new CampaignMissionRecord { Outcome = "failed", Primary = 1 };
        Assert.That(QuestJournal.RecordLines(quiet, EGameSystem.GFStarQuest).Count, Is.EqualTo(3),
            "nothing recorded, nothing shown - outcome, objectives and difficulty only");
    }

    [Test]
    public void Export_HasTheStoryEveryMissionAndTheAccount()
    {
        QuestCampaign c = QuestCampaign.Create("Rimward", new[] { "a.fdghero", "b.fdghero" }, new Random(2));
        c.Story = "Why we fight.";
        c.Chapters[0].Theme = "The sewers";
        c.Chapters[0].MissionPlan[0].Notes = "We went in.";
        c.StartChapter(0, 2);
        c.RecordMission("success", Record());
        string text = QuestJournal.Export(c, EGameSystem.GFStarQuest, new[] { "Vex", "Mara" });
        Assert.That(text, Does.Contain("Party: Vex, Mara"));
        Assert.That(text, Does.Contain("Why we fight."));
        Assert.That(text, Does.Contain("Theme: The sewers"));
        Assert.That(text, Does.Contain("What happened:"));
        Assert.That(text, Does.Contain("Your account:"));
        Assert.That(text, Does.Contain("We went in."));
        Assert.That(text.Split('\n').Count(l => l.StartsWith("CHAPTER ")), Is.EqualTo(c.Chapters.Count));
        Assert.That(text.All(ch => ch < 128), "game text is ASCII");
    }

    [Test]
    public void AHeroBelongsToOneUnfinishedCampaign()
    {
        string dir = Directory.CreateTempSubdirectory(nameof(QuestCampaignScreenTests)).FullName;
        try
        {
            string vex = Path.Combine(dir, "Vex.fdghero"), mara = Path.Combine(dir, "Mara.fdghero");
            var open = QuestCampaign.Create("Open", new[] { vex }, new Random(1));
            var done = QuestCampaign.Create("Done", new[] { mara }, new Random(1));
            foreach (CampaignChapter ch in done.Chapters) ch.Done = true;
            var all = new[] { open, done };
            Assert.That(CampaignPaths.CampaignOf("GF Star Quest", vex, all), Is.SameAs(open));
            Assert.That(CampaignPaths.CampaignOf("GF Star Quest", Path.Combine(dir, ".", "Vex.fdghero"), all), Is.SameAs(open),
                "the same file however the path is written");
            Assert.That(CampaignPaths.CampaignOf("GF Star Quest", mara, all), Is.Null, "a complete campaign lets its heroes go");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void ObjectiveChoices_AreTheBooksTablesPlusRollWhenItStarts()
    {
        Assert.That(QuestJournal.PrimaryName(0), Is.EqualTo("Roll when it starts"));
        Assert.That(Enumerable.Range(1, 6).Select(QuestJournal.PrimaryName),
            Is.EqualTo(new[] { "Clean Sweep", "Target Defense", "Retrieval", "Investigation", "Area Search", "Delivery" }));
        Assert.That(Enumerable.Range(1, 6).Select(QuestJournal.SecondaryName),
            Is.EqualTo(new[] { "Interrogate", "Safeguard", "Capture", "Sabotage", "Scavenge", "Escort" }));
    }
}
