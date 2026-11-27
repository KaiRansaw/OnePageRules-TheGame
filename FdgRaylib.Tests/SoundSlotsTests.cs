using FDG;
using FDG.Data;
using FDG.Presentation.Beats;
using FdgRaylib.Audio;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>2026-11-27: the sound slots, Kai's audio folder, the music director and the announcer.</summary>
[TestFixture]
[NonParallelizable] // SoundLibrary.Folder is process-wide
public class SoundSlotsTests
{
    private string _dir = "";
    private string? _oldFolder;

    private static string Repo => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));

    [SetUp]
    public void SetUp()
    {
        _oldFolder = SoundLibrary.Folder;
        _dir = Path.Combine(Path.GetTempPath(), "fdg-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SoundLibrary.Folder = _dir;
    }

    [TearDown]
    public void TearDown()
    {
        SoundLibrary.Folder = _oldFolder!;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void Drop(string relative)
    {
        string p = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        SoundLibrary.Rescan();
    }

    // ── The catalog ──

    [Test]
    public void EverySlot_IsUnique_ItsFallbackExists_AndNoChainLoops()
    {
        Assert.That(SoundSlots.All.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(SoundSlots.All.Count));
        foreach (SoundSlot s in SoundSlots.All)
        {
            Assert.That(s.Id, Does.Match("^[a-z]+/[a-z0-9-]+$"), "slot names are folder paths: lower case, dashes");
            if (s.Fallback != null) Assert.That(SoundSlots.Find(s.Fallback), Is.Not.Null, s.Id);
            var seen = new HashSet<string>();
            for (string? at = s.Id; at != null; at = SoundSlots.Find(at)?.Fallback)
                Assert.That(seen.Add(at), Is.True, $"{s.Id} loops");
        }
    }

    [Test]
    public void EveryBuiltInSound_IsShipped()
    {
        foreach (SoundSlot s in SoundSlots.All)
            foreach (string f in s.BuiltIn)
                Assert.That(File.Exists(Path.Combine(Repo, "OprGodot", f + ".ogg")), Is.True, $"{s.Id}: {f}");
    }

    [Test]
    public void EverySlotTheGameAsksFor_IsInTheCatalog()
    {
        foreach (GameCues.ECue cue in Enum.GetValues<GameCues.ECue>())
        {
            SoundSlot? slot = SoundSlots.Find(GameCues.SlotFor(cue));
            Assert.That(slot, Is.Not.Null, cue.ToString());
            Assert.That(slot!.BuiltIn, Does.Contain("sounds/" + GameCues.FileFor(cue, 0)), "the slot keeps the cue's old sound");
        }
        foreach (EGameSystem sys in Enum.GetValues<EGameSystem>())
            foreach (string bed in GameCues.AmbienceFor(sys, "storm"))
                Assert.That(SoundSlots.Find("ambience/" + bed), Is.Not.Null, bed);
        foreach (EMusicScene scene in Enum.GetValues<EMusicScene>())
            foreach (EGameSystem sys in Enum.GetValues<EGameSystem>())
                foreach (EGameMode mode in Enum.GetValues<EGameMode>())
                    foreach (bool? result in new bool?[] { true, false, null })
                        foreach (string id in MusicDirector.Candidates(new MusicContext(scene, sys, mode, FinalRound: true, Result: result)))
                            Assert.That(SoundSlots.Find(id), Is.Not.Null, id);
        for (int r = 1; r <= SoundSlots.NumberedRounds; r++) Assert.That(SoundSlots.Find($"announcer/round-{r}"), Is.Not.Null);
    }

    [Test]
    public void TheReadme_ListsEverySlot_AndTheCommittedCopyIsCurrent()
    {
        string text = SoundSlots.ReadmeText();
        foreach (SoundSlot s in SoundSlots.All) Assert.That(text, Does.Contain(s.Id));
        Assert.That(text.All(ch => ch < 128), Is.True, "plain ASCII");
        string committed = Path.Combine(Repo, "FdgRaylib", "Assets", "Audio", "README.txt");
        if (Environment.GetEnvironmentVariable("FDG_WRITE_AUDIO_README") == "1") File.WriteAllText(committed, text);
        Assert.That(File.ReadAllText(committed).ReplaceLineEndings(), Is.EqualTo(text.ReplaceLineEndings()),
            "FdgRaylib/Assets/Audio/README.txt is stale - copy SoundSlots.ReadmeText() into it");
        SoundLibrary.EnsureFolder();
        Assert.That(File.ReadAllText(Path.Combine(_dir, "README.txt")), Is.EqualTo(text));
    }

    // ── Kai's audio folder ──

    [Test]
    public void AnEmptyFolder_LeavesBuiltInsPlaying_AndNewSlotsSilent()
    {
        Assert.That(SoundLibrary.Resolve("cues/victory").Files.Single(), Is.EqualTo(new SoundFile("sounds/cues/victory", true)));
        Assert.That(SoundLibrary.HasSound("announcer/round-3"), Is.False);
        Assert.That(SoundLibrary.Pick("music/battle-grimdark"), Is.Null);
        Assert.That(SoundLibrary.Status(SoundSlots.Find("announcer/victory")!), Is.EqualTo("Empty (silent)"));
        Assert.That(SoundLibrary.Status(SoundSlots.Find("cues/victory")!), Is.EqualTo("Built-in sound"));
    }

    [Test]
    public void KaisFile_BeatsTheBuiltIn_AndOnlyAudioFilesCount()
    {
        Drop("cues/victory.ogg");
        Drop("cues/defeat.txt");
        var (slot, files) = SoundLibrary.Resolve("cues/victory");
        Assert.That(slot, Is.EqualTo("cues/victory"));
        Assert.That(files.Single().BuiltIn, Is.False);
        Assert.That(files.Single().Path, Does.EndWith("victory.ogg"));
        Assert.That(SoundLibrary.Resolve("cues/defeat").Files.Single().BuiltIn, Is.True, "a .txt is not a sound");
        Assert.That(SoundLibrary.Status(SoundSlots.Find("cues/victory")!), Is.EqualTo("Your file"));
    }

    [Test]
    public void AFolderOfVariants_AnswersTheSlot_AndNeverRepeatsBackToBack()
    {
        Drop("announcer/victory/a.ogg");
        Drop("announcer/victory/b.mp3");
        Drop("announcer/victory/c.wav");
        Assert.That(SoundLibrary.UserFiles("announcer/victory"), Has.Count.EqualTo(3));
        var rng = new Random(7);
        string? last = null;
        for (int i = 0; i < 50; i++)
        {
            string now = SoundLibrary.Pick("announcer/victory", rng)!.Value.Path;
            Assert.That(now, Is.Not.EqualTo(last));
            last = now;
        }
    }

    [Test]
    public void AnEmptySlot_UsesItsFallback()
    {
        Drop("announcer/round.ogg");
        var (slot, files) = SoundLibrary.Resolve("announcer/round-3");
        Assert.That(slot, Is.EqualTo("announcer/round"));
        Assert.That(files, Has.Count.EqualTo(1));
        Assert.That(SoundLibrary.Status(SoundSlots.Find("announcer/round-3")!), Is.EqualTo("Uses announcer/round"));
        Drop("announcer/round-3.ogg");
        Assert.That(SoundLibrary.Resolve("announcer/round-3").Slot, Is.EqualTo("announcer/round-3"), "the numbered line wins once it exists");
    }

    // ── Music ──

    [Test]
    public void Music_MenuHasItsBuiltIn_BattleIsSilentUntilKaiAddsTracks()
    {
        Assert.That(MusicDirector.Choose(new MusicContext(EMusicScene.Menu, EGameSystem.GrimdarkFuture)), Is.EqualTo("music/menu-grimdark"));
        Assert.That(MusicDirector.Choose(new MusicContext(EMusicScene.Lobby, EGameSystem.AoFQuest)), Is.EqualTo("music/menu-fantasy"));
        Assert.That(MusicDirector.Choose(new MusicContext(EMusicScene.Battle, EGameSystem.GrimdarkFuture)), Is.Null);
        Assert.That(MusicDirector.Choose(new MusicContext(EMusicScene.GameOver, EGameSystem.GrimdarkFuture, Result: true)), Is.Null);
    }

    [Test]
    public void Music_InBattle_TakesTheMostSpecificSlotWithTracks()
    {
        var gf = new MusicContext(EMusicScene.Battle, EGameSystem.GFStarQuest, EGameMode.StarQuestHorde, MyArmies: new[] { "Alien Hives" });
        Drop("music/battle-grimdark/one.ogg");
        Assert.That(MusicDirector.Choose(gf), Is.EqualTo("music/battle-grimdark"));
        Drop("music/quest-grimdark.ogg");
        Assert.That(MusicDirector.Choose(gf), Is.EqualTo("music/quest-grimdark"));
        Drop("music/horde-grimdark.ogg");
        Assert.That(MusicDirector.Choose(gf), Is.EqualTo("music/horde-grimdark"));
        Drop("music/army/alien-hives/theme.ogg");
        Assert.That(MusicDirector.Choose(gf), Is.EqualTo("music/army/alien-hives"));
        Assert.That(MusicDirector.Choose(gf with { FinalRound = true }), Is.EqualTo("music/army/alien-hives"),
            "the final-round slot's fallback must not pre-empt the army theme");
        Drop("music/battle-final-round-grimdark.ogg");
        Assert.That(MusicDirector.Choose(gf with { FinalRound = true }), Is.EqualTo("music/battle-final-round-grimdark"));
        Assert.That(MusicDirector.Choose(gf with { BattleMusicOn = false }), Is.Null);
        Assert.That(MusicDirector.Choose(gf with { System = EGameSystem.AgeOfFantasy, MyArmies = null, Mode = EGameMode.Standard }), Is.Null, "fantasy has no tracks yet");
        Assert.That(MusicDirector.ArmySlot("  Rebel Guerrillas (v2)"), Is.EqualTo("music/army/rebel-guerrillas-v2"));
    }

    [Test]
    public void Music_GameOver_PicksByResult()
    {
        Drop("music/victory.ogg");
        Assert.That(MusicDirector.Choose(new MusicContext(EMusicScene.GameOver, EGameSystem.AgeOfFantasy, Result: null)), Is.EqualTo("music/victory"), "a draw uses victory until it has its own");
        Assert.That(MusicDirector.Choose(new MusicContext(EMusicScene.GameOver, EGameSystem.AgeOfFantasy, Result: false)), Is.Null);
        Assert.That(MusicDirector.Loops(EMusicScene.GameOver), Is.False);
    }

    // ── The announcer ──

    private sealed class Table
    {
        public readonly GameDataStore Store = GameDataStore.GameDataStoreBuilder.GetDefault();
        public readonly TableState State;
        public readonly PlayerID Me = new(Guid.NewGuid()), Them = new(Guid.NewGuid());
        public GameProgressData? Progress;

        public Table() => State = new TableState(Store);

        public void Start(EGameSystem system)
        {
            var settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            Progress = new GameProgressData(EResumeStage.MainPhase, 0, new List<int>(), new List<int>(), 0,
                new Dictionary<int, int>(), new List<DataBinding<UnitData>>(), settings: settings);
            Store.Create(Progress);
        }

        public ModelData Unit(PlayerID owner) => UnitWithBinding(owner).Model;

        public (ModelData Model, DataBinding<UnitData> Unit) UnitWithBinding(PlayerID owner)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(0, 0), gameDataStore: Store);
            var binding = Store.GetDataBinding<ModelData>(Store.Create(model));
            var unit = Store.GetDataBinding<UnitData>(Store.Create(new UnitData(owner, "u", quality: 4, defense: 4, modelBindings: new List<DataBinding<ModelData>> { binding })));
            return (model, unit);
        }
    }

    private static string[] Drain(Announcer a, long now)
    {
        var said = new List<string>();
        while (a.Next(now, false) is { } l) said.Add(l.Key);
        return said.ToArray();
    }

    [Test]
    public void Announcer_CallsRounds_TheLastRound_AndTheResult()
    {
        var t = new Table();
        t.Start(EGameSystem.GrimdarkFuture);
        var a = new Announcer();
        var mine = new[] { t.Me };
        a.Update(t.State, mine, false, null, 0);
        Assert.That(Drain(a, 0), Is.Empty, "the first look only learns the table");

        int total = t.State.Progress!.TotalRounds;
        Assert.That(total, Is.GreaterThan(2));
        t.Progress!.RoundCount = 1;
        a.Update(t.State, mine, false, null, 10);
        Assert.That(a.Queued.Single().Slots, Is.EqualTo(new[] { "announcer/battle-start", "announcer/round-1" }));
        Drain(a, 10);
        t.Progress.RoundCount = 2;
        a.Update(t.State, mine, false, null, 20);
        Assert.That(Drain(a, 20), Is.EqualTo(new[] { "announcer/round-2" }));
        t.Progress.RoundCount = total;
        a.Update(t.State, mine, false, null, 30);
        Assert.That(a.Queued.Single().Slots, Is.EqualTo(new[] { "announcer/final-round", $"announcer/round-{total}" }));
        a.Update(t.State, mine, true, false, 40);
        Assert.That(Drain(a, 40), Is.EqualTo(new[] { "announcer/defeat" }), "the result clears what was waiting");
        a.Update(t.State, mine, true, false, 50);
        Assert.That(Drain(a, 50), Is.Empty, "once");

        var bots = new Announcer();
        bots.Update(t.State, Array.Empty<PlayerID>(), false, null, 0);
        bots.Update(t.State, Array.Empty<PlayerID>(), true, null, 1);
        Assert.That(Drain(bots, 1), Is.Empty, "watching bots to a draw: no 'draw' line for nobody");
    }

    [Test]
    public void Announcer_SaysWhoseObjectivesAndUnitsChanged()
    {
        var t = new Table();
        t.Start(EGameSystem.AgeOfFantasy);
        t.Store.Create(new ObjectiveData(new Position(10f, 20f), t.Store));
        ModelData mineModel = t.Unit(t.Me), theirs = t.Unit(t.Them);
        var a = new Announcer();
        var mine = new[] { t.Me };
        a.Update(t.State, mine, false, null, 0);

        var objective = (ObjectiveData)t.State.Objectives.Objects.Single();
        objective.SetOwner(t.Me);
        a.Update(t.State, mine, false, null, 1);
        Assert.That(Drain(a, 1), Is.EqualTo(new[] { "announcer/objective-seized" }));
        objective.SetOwner(t.Them);
        a.Update(t.State, mine, false, null, 2);
        Assert.That(Drain(a, 2), Is.EqualTo(new[] { "announcer/objective-lost" }));

        theirs.DealWounds(theirs.TotalWounds);
        a.Update(t.State, mine, false, null, 3);
        Assert.That(Drain(a, 3), Is.EqualTo(new[] { "announcer/enemy-unit-destroyed" }));
        mineModel.DealWounds(mineModel.TotalWounds);
        a.Update(t.State, mine, false, null, 4);
        Assert.That(Drain(a, 4), Is.EqualTo(new[] { "announcer/unit-lost" }));
        a.Update(t.State, mine, false, null, 5);
        Assert.That(Drain(a, 5), Is.Empty, "once per unit");
    }

    [Test]
    public void Announcer_InQuest_AFallenUnitOfMineIsAHeroDown()
    {
        var t = new Table();
        t.Start(EGameSystem.GFStarQuest);
        ModelData hero = t.Unit(t.Me);
        var a = new Announcer();
        a.Update(t.State, new[] { t.Me }, false, null, 0);
        hero.DealWounds(hero.TotalWounds);
        a.Update(t.State, new[] { t.Me }, false, null, 1);
        Assert.That(Drain(a, 1), Is.EqualTo(new[] { "announcer/hero-down" }));
    }

    [Test]
    public void Announcer_HearsTheEnginesHeadlines()
    {
        var a = new Announcer();
        var white = new TextColor(255, 255, 255, 255);
        a.OnBeat(new BannerBeat("Deployment", white), 0);
        a.OnBeat(new BannerBeat("Wave 3 incoming!", white), 0);
        a.OnBeat(new BannerBeat("Objective 2 destroyed!", white), 0);
        a.OnBeat(new BannerBeat("Prolonged Battle: the fight goes on - round 5!", white), 0);
        Assert.That(a.Queued.Select(l => l.Key), Is.EqualTo(new[] { "announcer/deployment", "announcer/wave-incoming", "announcer/objective-destroyed", "announcer/prolonged-battle" }));
        a.OnBeat(new BannerBeat("Wave 3: no enemies to send back.", white), 0);
        a.OnBeat(new BannerBeat("Round 2 mission cards dealt", white), 0);
        Assert.That(a.Queued, Has.Count.EqualTo(4), "only the headlines it has lines for");
        var b = new Announcer();
        b.OnBeat(new BannerBeat("Wave 1 arrives!", white), 0);
        b.OnBeat(new UnitRoutedBeat(new UnitID(Guid.NewGuid()), "Grunts", Array.Empty<RoutedModel>()), 0);
        Assert.That(b.Queued.Select(l => l.Key), Is.EqualTo(new[] { "announcer/wave-incoming", "announcer/unit-routed" }));
    }

    [Test]
    public void Announcer_OneLineAtATime_NoDuplicates_StaleLinesDropped()
    {
        var a = new Announcer();
        a.Say(0, false, "announcer/unit-lost");
        a.Say(0, false, "announcer/unit-lost");
        a.Say(0, false, "announcer/objective-lost");
        Assert.That(a.Queued, Has.Count.EqualTo(2), "the same line twice is said once");
        Assert.That(a.Next(100, speaking: true), Is.Null, "never over another line");
        Assert.That(a.Next(100, false)!.Key, Is.EqualTo("announcer/unit-lost"));
        Assert.That(a.Next(Announcer.StaleMs + 1, false), Is.Null, "a line that waited too long is dropped");
        for (int i = 0; i < 10; i++) a.Say(0, false, $"announcer/round-{i + 1}");
        Assert.That(a.Queued, Has.Count.EqualTo(Announcer.MaxQueued));
        a.Say(0, true, "announcer/victory");
        a.Say(0, false, "announcer/unit-lost");
        Assert.That(a.Queued.Select(l => l.Key), Is.EqualTo(new[] { "announcer/victory" }), "nothing after the result");
        Assert.That(a.Next(Announcer.StaleMs * 10, false)!.Key, Is.EqualTo("announcer/victory"), "the result is never stale");
    }

    [Test]
    public void Announcer_YourTurn_OnlyWhenAsked_AndOnlyOnTheHandOver()
    {
        var t = new Table();
        t.Start(EGameSystem.GrimdarkFuture);
        DataBinding<UnitData> myUnit = t.UnitWithBinding(t.Me).Unit, theirUnit = t.UnitWithBinding(t.Them).Unit;
        var a = new Announcer();
        var mine = new[] { t.Me };
        void Activate(DataBinding<UnitData> u) => t.Progress!.ActivatingUnit = u;
        Activate(theirUnit);
        a.Update(t.State, mine, false, null, 0, turns: true);
        Activate(myUnit);
        a.Update(t.State, mine, false, null, 1, turns: true);
        Assert.That(Drain(a, 1), Is.EqualTo(new[] { "announcer/your-turn" }));
        a.Update(t.State, mine, false, null, 2, turns: true);
        Assert.That(Drain(a, 2), Is.Empty);
        Activate(theirUnit);
        a.Update(t.State, mine, false, null, 3, turns: false);
        Activate(myUnit);
        a.Update(t.State, mine, false, null, 4, turns: false);
        Assert.That(Drain(a, 4), Is.Empty, "off in a game against bots");
    }
}
