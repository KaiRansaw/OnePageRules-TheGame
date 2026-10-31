using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #414 - the one link that makes Star Quest progression real.
//
// Everything a hero earns in a game - XP, a level, improved stats, bought items, injuries, credits - lives
// on its sheet inside the unit (UnitData.QuestHeroJson). StarQuestMissionRewardTests already pins the
// rewards as far as that sheet. QuestHeroSync.WriteBack is what takes the sheet back out to the .fdghero
// file on disk, and until this fixture it had NO test at all, while being called from exactly one place:
// the GUI game-over screen. So the single step that turns a played game into a hero that keeps what it
// won was the least verified thing in the mode, and it cannot be reached from a headless run
// (PartyLoader deliberately nulls SourceFile, so a scripted game never writes to anybody's files).
//
// What these tests do is call WriteBack against a real store with real hero sheets and real temp files,
// and then READ THE FILES BACK. Asserting on the in-memory hero would prove nothing: the whole question
// is whether the bytes on disk changed.
//
// Note WriteBack keeps a static "written this game" set keyed on table identity, so every test builds its
// own TableState - sharing one would make the second write a silent no-op.
[TestFixture]
public class QuestHeroSyncTests
{
    private string _dir = null!;
    private GameDataStore _store = null!;
    private TableState _table = null!;
    private PlayerID _player;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory(nameof(QuestHeroSyncTests)).FullName;
        _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        _table = new TableState(_store);
        _player = new PlayerID(Guid.NewGuid());
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ── Scene building ──────────────────────────────────────────────────────────────────────────────

    private QuestHero NewHeroOnDisk(string name, Action<QuestHero>? starting = null)
    {
        var hero = new QuestHero
        {
            Name = name, Faction = "Human Defense Force", BaseHeroName = "Leader", BaseCost = 50,
            Class = "Soldier", Quality = 4, Defense = 4, Tough = 6, Power = 4, Strength = 4,
        };
        starting?.Invoke(hero);
        hero.SourceFile = Path.Combine(_dir, name + ".fdghero");
        File.WriteAllText(hero.SourceFile, hero.ToJson());
        return hero;
    }

    /// <summary>Puts a hero on the table carrying <paramref name="afterTheGame"/> as its sheet - i.e. the
    /// sheet as the mission left it, which is what WriteBack is supposed to persist.</summary>
    private void PlaceHero(QuestHero afterTheGame)
    {
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(12f, 12f), _store);
        DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
        var unit = new UnitData(_player, afterTheGame.Name, quality: afterTheGame.Quality,
            defense: afterTheGame.Defense,
            modelBindings: new List<DataBinding<ModelData>> { modelBinding })
        {
            QuestHeroJson = afterTheGame.ToJson(),
        };
        DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
        _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
    }

    private void SetGameMode(EGameMode mode)
    {
        GameSettings settings = GameSettings.GetDefault();
        settings.GameSystem = EGameSystem.GFStarQuest;
        settings.GameMode = mode;
        _store.Create(new GameProgressData(
            stage: EResumeStage.MainPhase, roundCount: 1,
            teamActivateOrder: new List<int>(), currentRoundTeamFinishOrder: new List<int>(),
            currentTeamIndex: 0, currentPlayerIndexPerTeam: new Dictionary<int, int>(),
            unactivatedUnits: new List<DataBinding<UnitData>>(), settings: settings));
    }

    private QuestHero ReadBack(QuestHero hero) =>
        QuestHero.FromJson(File.ReadAllText(hero.SourceFile!))
        ?? throw new AssertionException($"{hero.Name}'s file no longer parses as a hero sheet");

    // ── The round trip ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void EverythingAHeroEarned_ReachesItsFileOnDisk()
    {
        // One assertion per kind of progression, because they are stored differently and a serializer
        // change can drop one without touching the others. A hero that levelled, got hurt, spent money
        // and came home.
        SetGameMode(EGameMode.StarQuestHorde);
        QuestHero hero = NewHeroOnDisk("Vex");

        QuestHero afterTheGame = ReadBack(hero);
        afterTheGame.Xp = 14;
        afterTheGame.Level = 4;
        afterTheGame.Tough = 9;                       // the level-up choice
        afterTheGame.Credits = 55;
        afterTheGame.Items = new List<string> { "Med Kit", "Frag Grenade" };
        afterTheGame.Injuries = new List<string> { "Blinded Eye" };
        afterTheGame.Skills = new List<string> { "Overwatch" };
        PlaceHero(afterTheGame);

        QuestHeroSync.WriteBack(_table);

        QuestHero saved = ReadBack(hero);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Xp, Is.EqualTo(14), "XP");
            Assert.That(saved.Level, Is.EqualTo(4), "level");
            Assert.That(saved.Tough, Is.EqualTo(9), "the stat the level-up improved");
            Assert.That(saved.Credits, Is.EqualTo(55), "credits");
            Assert.That(saved.Items, Is.EquivalentTo(new[] { "Med Kit", "Frag Grenade" }), "items");
            Assert.That(saved.Injuries, Is.EquivalentTo(new[] { "Blinded Eye" }), "injuries");
            Assert.That(saved.Skills, Is.EquivalentTo(new[] { "Overwatch" }), "skills");
        });
    }

    [Test]
    public void TheMissionCount_GoesUpByExactlyOne()
    {
        // Stated separately because it is the one field WriteBack writes itself rather than copies, and
        // because double-writing it is the shape the static guard exists to prevent.
        SetGameMode(EGameMode.StarQuestHorde);
        QuestHero hero = NewHeroOnDisk("Vex", h => h.Missions = 3);
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);

        Assert.That(ReadBack(hero).Missions, Is.EqualTo(4));
    }

    [Test]
    public void WritingTwiceForOneGame_DoesNotCountTheMissionTwice()
    {
        // The GUI can reach the game-over path more than once (a redraw, a re-entered screen). Without
        // the guard a hero would gain a mission - and in a campaign, a mission would be recorded against
        // the chapter - every time the screen came back.
        SetGameMode(EGameMode.StarQuestHorde);
        QuestHero hero = NewHeroOnDisk("Vex", h => h.Missions = 3);
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);
        QuestHeroSync.WriteBack(_table);

        Assert.That(ReadBack(hero).Missions, Is.EqualTo(4), "the same game must only count once");
    }

    [Test]
    public void EveryHeroInThePartyIsWritten_NotJustTheFirst()
    {
        SetGameMode(EGameMode.StarQuestHorde);
        QuestHero vex = NewHeroOnDisk("Vex");
        QuestHero mara = NewHeroOnDisk("Mara");

        QuestHero vexAfter = ReadBack(vex); vexAfter.Xp = 5;
        QuestHero maraAfter = ReadBack(mara); maraAfter.Xp = 8;
        PlaceHero(vexAfter);
        PlaceHero(maraAfter);

        QuestHeroSync.WriteBack(_table);

        Assert.That(ReadBack(vex).Xp, Is.EqualTo(5));
        Assert.That(ReadBack(mara).Xp, Is.EqualTo(8));
    }

    [Test]
    public void AHeroWithNoSourceFile_IsSkippedWithoutThrowing()
    {
        // This is the headless case, and it is deliberate: PartyLoader nulls SourceFile so a scripted run
        // cannot write over the player's real heroes. It must be a quiet skip, not a crash on the way out
        // of the game.
        SetGameMode(EGameMode.StarQuestHorde);
        var sheet = new QuestHero { Name = "Ghost", Class = "Soldier", SourceFile = null };
        PlaceHero(sheet);

        Assert.That(() => QuestHeroSync.WriteBack(_table), Throws.Nothing);
    }

    [Test]
    public void AHeroWhoseFileWasDeleted_IsSkippedWithoutRecreatingIt()
    {
        // A hero deleted in the Hero Builder while a game was running. Re-creating the file would
        // resurrect a hero the player meant to be rid of.
        SetGameMode(EGameMode.StarQuestHorde);
        QuestHero hero = NewHeroOnDisk("Vex");
        string path = hero.SourceFile!;
        PlaceHero(ReadBack(hero));
        File.Delete(path);

        Assert.That(() => QuestHeroSync.WriteBack(_table), Throws.Nothing);
        Assert.That(File.Exists(path), Is.False, "a deleted hero must not come back");
    }

    [Test]
    public void AnOrdinaryUnitWithNoHeroSheet_IsIgnored()
    {
        SetGameMode(EGameMode.StarQuestHorde);
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(20f, 20f), _store);
        DataBinding<ModelData> mb = _store.GetDataBinding<ModelData>(_store.Create(model));
        var grunt = new UnitData(_player, "Raiders", quality: 5, defense: 5,
            modelBindings: new List<DataBinding<ModelData>> { mb });
        _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>>
            { _store.GetDataBinding<UnitData>(_store.Create(grunt)) }));

        Assert.That(() => QuestHeroSync.WriteBack(_table), Throws.Nothing);
        Assert.That(Directory.GetFiles(_dir), Is.Empty, "nothing on disk to write, so nothing written");
    }

    // ── Horde is not a campaign ─────────────────────────────────────────────────────────────────────

    [Test]
    public void AHordeGame_DoesNotTouchCampaignState()
    {
        // Horde and Mission share this one write-back. A Horde run must not record a mission against a
        // campaign the party happens to be in, or playing Horde between chapters would advance the
        // campaign without playing it.
        SetGameMode(EGameMode.StarQuestHorde);
        QuestHero hero = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = "Rimward";
            h.CampaignDifficulty = 2;
            h.LastOutcome = "success";
        });
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);

        QuestHero saved = ReadBack(hero);
        Assert.That(saved.CampaignName, Is.EqualTo("Rimward"), "still in the campaign");
        Assert.That(saved.LastOutcome, Is.EqualTo("success"),
            "a Horde run must not consume the campaign's pending result");
    }

    // ── A campaign mission's result ─────────────────────────────────────────────────────────────────
    //
    // These write a real .fdgcampaign, because CampaignPaths resolves its own folder (heroes/<game
    // type>/campaigns) and there is no seam to redirect it. The name below is deliberately one no player
    // would choose, and TearDownCampaign removes it whether the test passed or not.

    private const string TestCampaign = "zz-QuestHeroSyncTests-temp";

    /// <summary>The hero folder Star Quest heroes and campaigns live under, as the screens name it.</summary>
    private const string HeroGameType = FdgRaylib.Rendering.HeroBuilderScreen.GameType;

    [TearDown]
    public void TearDownCampaign()
    {
        try
        {
            string path = CampaignPaths.FileFor(HeroGameType, TestCampaign);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
    }

    /// <summary>A campaign on disk with chapter 0 started and <paramref name="missions"/> missions in it.</summary>
    private QuestCampaign StartedCampaign(int missions = 2)
    {
        var campaign = new QuestCampaign { Name = TestCampaign };
        campaign.Chapters.Add(new CampaignChapter { Number = 1, Missions = missions });
        campaign.StartChapter(0, difficulty: 2);
        Assert.That(CampaignPaths.Save(CampaignPaths.FileFor(HeroGameType, TestCampaign), campaign),
            Is.True, "could not write the test campaign - this checkout's heroes folder is read-only");
        return campaign;
    }

    private QuestCampaign ReloadCampaign() =>
        CampaignPaths.Find(HeroGameType, TestCampaign)?.Campaign
        ?? throw new AssertionException("the test campaign is gone from disk");

    [Test]
    public void AWonMission_IsRecordedInTheCampaignFile()
    {
        // The chapter has to move on, and it has to move on ONCE for a party of several heroes - each
        // hero carries the same pending outcome, so a naive loop would record the mission per hero and
        // finish a 2-mission chapter in a single game.
        StartedCampaign(missions: 2);
        SetGameMode(EGameMode.StarQuestMission);

        foreach (string name in new[] { "Vex", "Mara" })
        {
            QuestHero hero = NewHeroOnDisk(name, h =>
            {
                h.CampaignName = TestCampaign;
                h.CampaignDifficulty = 2;
                h.LastOutcome = "success";
            });
            PlaceHero(ReadBack(hero));
        }

        QuestHeroSync.WriteBack(_table);

        QuestCampaign saved = ReloadCampaign();
        Assert.That(saved.Chapters[0].MissionsWon, Is.EqualTo(1),
            "two heroes, one mission - the result is recorded per campaign, not per hero");
        Assert.That(saved.Chapters[0].Restarts, Is.EqualTo(0));
    }

    [Test]
    public void WritingTwiceForOneMission_DoesNotRecordTheMissionTwice()
    {
        // A GUI that reaches the game-over path twice - a redraw, a re-entered screen - must not finish a
        // 2-mission chapter in one game.
        //
        // Worth knowing WHICH guard does this, because it is not the obvious one. WriteBack has a
        // "campaign:<name>" entry in its written-this-game set that reads as the protection, and deleting
        // it changes nothing: the PER-HERO guard above it has already claimed every SourceFile, so the
        // second call finds no heroes at all and never reaches the campaign loop. The campaign entry is
        // belt-and-braces for a future where the hero guard moves. (Established by deleting each in turn
        // and re-running this fixture - the GroupBy, not either guard, is what makes a party of heroes
        // record one mission.) So this test asserts the BEHAVIOUR, which must hold whichever guard
        // provides it.
        StartedCampaign(missions: 2);
        SetGameMode(EGameMode.StarQuestMission);
        QuestHero hero = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = TestCampaign; h.CampaignDifficulty = 2; h.LastOutcome = "success";
        });
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);
        QuestHeroSync.WriteBack(_table);

        Assert.That(ReloadCampaign().Chapters[0].MissionsWon, Is.EqualTo(1),
            "one mission was played, so one mission is recorded however often the screen redraws");
    }

    [Test]
    public void AFailedMission_RestartsTheChapter()
    {
        StartedCampaign(missions: 2);
        SetGameMode(EGameMode.StarQuestMission);
        QuestHero hero = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = TestCampaign;
            h.CampaignDifficulty = 2;
            h.LastOutcome = "failed";
        });
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);

        QuestCampaign saved = ReloadCampaign();
        Assert.That(saved.Chapters[0].MissionsWon, Is.EqualTo(0));
        Assert.That(saved.Chapters[0].Restarts, Is.EqualTo(1), "a wipe restarts the chapter");
    }

    [Test]
    public void OneHeroDownOutOfTwo_StillCountsAsAWin()
    {
        // "If at the end of a mission any Hero is Unconscious, it counts as killed and suffers Injuries"
        // - the MISSION is still a success. Only a full wipe fails it. So a mixed party of outcomes
        // records a win, which is what the engine's "any success" reading means, stated out loud.
        StartedCampaign(missions: 3);
        SetGameMode(EGameMode.StarQuestMission);
        QuestHero won = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = TestCampaign; h.CampaignDifficulty = 2; h.LastOutcome = "success";
        });
        QuestHero down = NewHeroOnDisk("Mara", h =>
        {
            h.CampaignName = TestCampaign; h.CampaignDifficulty = 2; h.LastOutcome = "failed";
        });
        PlaceHero(ReadBack(won));
        PlaceHero(ReadBack(down));

        QuestHeroSync.WriteBack(_table);

        Assert.That(ReloadCampaign().Chapters[0].MissionsWon, Is.EqualTo(1));
    }

    [Test]
    public void TheRecordedOutcome_IsClearedSoItIsNotCountedAgain()
    {
        // LastOutcome is a pending flag on the sheet. Left set, the next game of any kind would record
        // this mission's result a second time.
        StartedCampaign(missions: 3);
        SetGameMode(EGameMode.StarQuestMission);
        QuestHero hero = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = TestCampaign; h.CampaignDifficulty = 2; h.LastOutcome = "success";
        });
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);

        Assert.That(ReadBack(hero).LastOutcome, Is.Empty);
    }

    [Test]
    public void FinishingTheLastMissionOfAChapter_SendsTheHeroesHomeFresh()
    {
        // The chapter ends, so the party leaves it: wounds and power reset (the shop opens between
        // chapters) and the hero is no longer in a campaign, which is what lets the campaign screen
        // offer the next chapter.
        StartedCampaign(missions: 1);
        SetGameMode(EGameMode.StarQuestMission);
        QuestHero hero = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = TestCampaign; h.CampaignDifficulty = 2; h.LastOutcome = "success";
            h.WoundsTaken = 4; h.PowerLeft = 1;
        });
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);

        QuestHero saved = ReadBack(hero);
        Assert.Multiple(() =>
        {
            Assert.That(saved.WoundsTaken, Is.EqualTo(0), "wounds");
            Assert.That(saved.PowerLeft, Is.EqualTo(-1), "power back to full");
            Assert.That(saved.CampaignName, Is.Empty, "out of the finished chapter");
        });
        Assert.That(ReloadCampaign().Chapters[0].Done, Is.True);
    }

    [Test]
    public void MidChapter_TheHeroKeepsItsWoundsAndPower()
    {
        // The other half, and the one a player feels: a chapter's missions are meant to be played by an
        // increasingly battered party. Resetting here would make every mission a fresh start.
        StartedCampaign(missions: 3);
        SetGameMode(EGameMode.StarQuestMission);
        QuestHero hero = NewHeroOnDisk("Vex", h =>
        {
            h.CampaignName = TestCampaign; h.CampaignDifficulty = 2; h.LastOutcome = "success";
            h.WoundsTaken = 4; h.PowerLeft = 1;
        });
        PlaceHero(ReadBack(hero));

        QuestHeroSync.WriteBack(_table);

        QuestHero saved = ReadBack(hero);
        Assert.That(saved.WoundsTaken, Is.EqualTo(4), "wounds carry into the next mission");
        Assert.That(saved.PowerLeft, Is.EqualTo(1), "and so does power");
        Assert.That(saved.CampaignName, Is.EqualTo(TestCampaign), "still in the chapter");
    }
}
