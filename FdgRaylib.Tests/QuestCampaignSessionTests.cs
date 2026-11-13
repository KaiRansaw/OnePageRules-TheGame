using FDG;
using FDG.Quest;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Quest Campaigns without the drawing (Godot mockup approved 2026-11-12; Campaign Builder v2.1.0): the list's lines,
/// the mission marks, readying the next mission (the heroes carry the chapter's difficulty and the mission's
/// objectives, and remember their files so the game can write back), and the book's secondary-objective timing.
/// </summary>
[TestFixture]
public class QuestCampaignSessionTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-qcs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        QuestCampaignSession.Pending = null;
    }

    [TearDown]
    public void TearDown()
    {
        QuestCampaignSession.Pending = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Hero(string name, string faction = "Alien Hives", string baseHero = "Grunt Veteran")
    {
        var h = new QuestHero { Name = name, Faction = faction, BaseHeroName = baseHero, Class = "Berserker", Tough = 9, Power = 4, BaseCost = 55 };
        string path = Path.Combine(_dir, name + QuestHero.FileExtension);
        File.WriteAllText(path, h.ToJson());
        return path;
    }

    private (string Path, QuestCampaign C) Campaign(params string[] heroes)
    {
        var c = QuestCampaign.CreateEmpty("Hive Run", heroes);
        c.AddChapter();
        c.AddMission(0);
        c.AddMission(0);
        c.Chapters[0].MissionPlan[0].Primary = 3;
        string army = Path.Combine(_dir, "enemy.fdgarmy");
        File.WriteAllText(army, "{}");
        QuestCampaignSession.SetEnemyArmy(c.Chapters[0], army, "Battle Brothers list");
        QuestCampaignSession.SetEnemyArmy(c.Chapters[1], army, "Battle Brothers list");
        string path = Path.Combine(_dir, "Hive Run" + QuestCampaign.FileExtension);
        File.WriteAllText(path, c.ToJson());
        return (path, c);
    }

    [Test]
    public void Lines_AndMarks_FollowTheCampaign()
    {
        var (_, c) = Campaign(Hero("Krrek"), Hero("Vessa"));
        Assert.That(QuestCampaignSession.ProgressLine(c), Is.EqualTo("Between chapters - 0 of 2 done"));
        Assert.That(QuestCampaignSession.CantPlay(c), Is.EqualTo("Start a chapter first."));
        Assert.That(QuestCampaignSession.ShopOpen(c), Is.True, "book: the shop opens before a chapter");

        c.StartChapter(0, 2);
        Assert.That(QuestCampaignSession.ShopOpen(c), Is.False, "book: not between a chapter's missions");
        Assert.That(QuestCampaignSession.ProgressLine(c), Is.EqualTo("Chapter 1 of 2 - mission 1 of 3"));
        Assert.That(QuestCampaignSession.StatusLine(c), Is.EqualTo("Chapter 1 - Intermediate - mission 1 of 3 next"));
        Assert.That(QuestCampaignSession.NextLine(c), Is.EqualTo("Ch 1, mission 1 - Retrieval vs Battle Brothers list"));
        Assert.That(QuestCampaignSession.Marks(c).Take(4), Is.EqualTo(new[]
        {
            QuestCampaignSession.EMissionMark.Next, QuestCampaignSession.EMissionMark.ToPlay,
            QuestCampaignSession.EMissionMark.ToPlay, QuestCampaignSession.EMissionMark.ToPlay,
        }));

        c.RecordMission("success");
        Assert.That(QuestCampaignSession.Marks(c, 0), Is.EqualTo(new[]
        {
            QuestCampaignSession.EMissionMark.Won, QuestCampaignSession.EMissionMark.Next, QuestCampaignSession.EMissionMark.ToPlay,
        }));
    }

    [Test]
    public void PrepareNextMission_ReadiesTheHeroes_AndHandsTheLobbyTheMission()
    {
        string krrek = Hero("Krrek"), vessa = Hero("Vessa");
        var (path, c) = Campaign(krrek, vessa);
        c.StartChapter(0, 3);
        var s = new QuestCampaignSession(EGameSystem.GFStarQuest);

        QuestCampaignLaunch? launch = s.PrepareNextMission(path, c);

        Assert.That(launch, Is.Not.Null);
        Assert.That(QuestCampaignSession.Pending, Is.SameAs(launch));
        Assert.That(launch!.Title, Is.EqualTo("Hive Run - Chapter 1, mission 1"));
        Assert.That(launch.Line, Is.EqualTo("Expert - Retrieval"));
        Assert.That(launch.Party, Is.EqualTo(new[] { krrek, vessa }));
        Assert.That(launch.EnemyArmyFile, Does.EndWith("enemy.fdgarmy"));
        QuestHero sheet = CampaignPaths.LoadHero(krrek)!;
        Assert.That((sheet.CampaignName, sheet.CampaignDifficulty, sheet.CampaignPrimary), Is.EqualTo(("Hive Run", 3, 3)),
            "the engine's Mission mode reads these off the sheets");
    }

    [Test]
    public void PartyArmy_RemembersEachHerosFile_SoTheGameCanWriteBack()
    {
        string krrek = Hero("Krrek"), vessa = Hero("Vessa");
        var army = QuestCampaignSession.PartyArmy(EGameSystem.GFStarQuest, new[] { krrek, vessa });
        Assert.That(army, Is.Not.Null);
        Assert.That(army!.Units, Has.Count.EqualTo(2));
        var sheets = army.Units.Select(u => QuestHero.FromJson(u.QuestHeroJson!)!).ToList();
        Assert.That(sheets.Select(h => h.SourceFile), Is.EqualTo(new[] { Path.GetFullPath(krrek), Path.GetFullPath(vessa) }));
    }

    [Test]
    public void Generated_LeavesSecondariesToBeRolledInPlay_AsTheBookSays()
    {
        var s = new QuestCampaignSession(EGameSystem.GFStarQuest, new Random(7));
        QuestCampaign draft = s.Draft("Book Run", new[] { "a", "b" }, QuestCampaignSession.EBuild.Generate);
        var missions = draft.Chapters.SelectMany(c => c.MissionPlan).ToList();
        Assert.That(draft.Chapters.Count, Is.InRange(3, 5), "D3+2 chapters");
        Assert.That(draft.Chapters.All(c => c.MissionPlan.Count is >= 2 and <= 4), "D3+1 missions each");
        Assert.That(missions.All(m => m.Primary is >= 1 and <= 6));
        Assert.That(missions.All(m => m.Secondary == 0), "set up after the primary is done");
        s.RollMission(missions[0]);
        Assert.That(missions[0].Secondary, Is.EqualTo(0));
    }

    [Test]
    public void NewProblem_NeedsANameAndTwoToFourHeroes()
    {
        var s = new QuestCampaignSession(EGameSystem.GFStarQuest);
        Assert.That(s.NewProblem("", new[] { "a", "b" }), Is.EqualTo("Give the campaign a name."));
        Assert.That(s.NewProblem("X", new[] { "a" }), Is.EqualTo("Pick at least 2 heroes."));
        Assert.That(s.NewProblem("X", new[] { "a", "b", "c", "d", "e" }), Is.EqualTo("A party is 2 to 4 heroes."));
        Assert.That(s.NewProblem("X", new[] { "a", "b" }), Is.Null);
    }
}
