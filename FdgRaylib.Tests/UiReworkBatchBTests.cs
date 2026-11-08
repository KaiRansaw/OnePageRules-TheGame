using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FDG;
using FDG.Missions;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// UI rework batch B (Kai's wish list, 2026-11-07): portrait unit cards, the tabbed lobby, Choose Army and
/// Select Map. The drawing is rental; what these pin is the information - which settings a game has, which
/// deployment zones are drawn, which maps are offered, what a card and its hover say.
/// </summary>
[TestFixture]
public class UiReworkBatchBTests
{
    // ── Portrait cards ───────────────────────────────────────────────────────────────────────────

    private static UnitFileEntry Unit() => new()
    {
        Name = "Assault Grunts",
        ModelCount = 10,
        Quality = 5,
        Defense = 5,
        PointCost = 90,
        SpecialRules = { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
        Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 10, RangeInches = 24, Attacks = 1 } },
    };

    [Test]
    public void ACardShowsTheNameAndModelsAndPoints()
    {
        (string name, string facts) = UnitPortraitCards.CardCaption(Unit());
        Assert.That(name, Is.EqualTo("Assault Grunts"));
        Assert.That(facts, Is.EqualTo("[10] 90 pts"));

        var hero = Unit();
        hero.ModelCount = 1;
        Assert.That(UnitPortraitCards.CardCaption(hero).Facts, Is.EqualTo("90 pts"));
    }

    [Test]
    public void TheHoverCarriesTheUnitsStatsWeaponsAndRules()
    {
        string tip = UnitPortraitCards.StatsTooltip(Unit());
        Assert.That(tip, Does.Contain("Assault Grunts [10] - 90 pts"));
        Assert.That(tip, Does.Contain("Quality 5+   Defense 5+   Tough 3"));
        Assert.That(tip, Does.Contain("10x Rifle (24\", A1)"));
        Assert.That(tip, Does.Contain("Tough(3)"));
    }

    [Test]
    public void ACardWithNoPictureShowsInitialsFromTheNamesWords()
    {
        Assert.That(UnitPortraitCards.Initials("Miners (Combined)"), Is.EqualTo("M"));
        Assert.That(UnitPortraitCards.Initials("Battle Brother Captain"), Is.EqualTo("BBC"));
        Assert.That(UnitPortraitCards.Initials(""), Is.EqualTo("?"));
    }

    // ── Lobby: which settings a game has ─────────────────────────────────────────────────────────

    [Test]
    public void TheHeaderIsGameModeAndPoints()
    {
        Assert.That(LobbyScreen.HeaderTitle(EGameSystem.GrimdarkFuture, EGameMode.Standard, 2000),
            Is.EqualTo("Grimdark Future  -  Army vs Army  -  2000 pts"));
        Assert.That(LobbyScreen.HeaderModeLabel(EGameMode.Horde), Is.EqualTo("Horde Mode"));
        Assert.That(LobbyScreen.HeaderModeLabel(EGameMode.StarQuestHorde), Is.EqualTo("Horde Mode"));
        Assert.That(LobbyScreen.HeaderModeLabel(EGameMode.StarQuestMission), Is.EqualTo("Play a Mission"));
    }

    [TestCase(EGameSystem.GrimdarkFuture, EGameMode.Standard, true)]
    [TestCase(EGameSystem.GFFirefight, EGameMode.Standard, true)]
    [TestCase(EGameSystem.AgeOfFantasy, EGameMode.Standard, true)]
    [TestCase(EGameSystem.AoFSkirmish, EGameMode.Standard, true)]
    [TestCase(EGameSystem.GrimdarkFuture, EGameMode.Horde, false)]
    [TestCase(EGameSystem.GFStarQuest, EGameMode.StarQuestMission, false)]
    [TestCase(EGameSystem.AoFQuest, EGameMode.StarQuestHorde, false)]
    public void DeploymentIsOnlyOfferedWhereTheGameUsesIt(EGameSystem system, EGameMode mode, bool offered) =>
        Assert.That(LobbyDeploymentPreview.DeploymentApplies(system, mode), Is.EqualTo(offered));

    [Test]
    public void SettingsAModeDoesNotHaveAreNotOffered()
    {
        Assert.That(LobbyDeploymentPreview.MissionCardsApply(EGameSystem.GrimdarkFuture, EGameMode.Horde), Is.False);
        Assert.That(LobbyDeploymentPreview.MissionCardsApply(EGameSystem.GrimdarkFuture, EGameMode.Standard), Is.True);
        Assert.That(LobbyDeploymentPreview.GameLengthApplies(EGameMode.Horde), Is.False);
        Assert.That(LobbyDeploymentPreview.FastPlayApplies(EGameSystem.GFFirefight), Is.False);
        Assert.That(LobbyDeploymentPreview.FastPlayApplies(EGameSystem.AgeOfFantasy), Is.True);
        Assert.That(LobbyDeploymentPreview.ExtraActionsApply(EGameSystem.GFStarQuest, EGameMode.StarQuestMission), Is.False);
        Assert.That(LobbyDeploymentPreview.KingOfTheHillApplies(EGameSystem.GrimdarkFuture, EGameMode.Horde), Is.False);
        // The Quest games have no Advanced Rulebook: no Advanced tab at all.
        Assert.That(LobbyScreen.HasAdvancedTab(EGameSystem.GFStarQuest, EGameMode.StarQuestMission), Is.False);
        Assert.That(LobbyScreen.HasAdvancedTab(EGameSystem.GFFirefight, EGameMode.Standard), Is.True);
    }

    // ── Deployment zones: each game family draws its own book's diagram ─────────────────────────

    [Test]
    public void GrimdarkFutureDrawsTheLongTablePageAndFirefightTheSquareOne()
    {
        // Ground War is two side columns on the GF/AoF page and an L each on the GFF/AoFS page.
        var gf = LobbyDeploymentPreview.For(EGameSystem.GrimdarkFuture, EGameMode.Standard,
            EDeploymentStyle.GroundWar, 2, 72, 48);
        var gff = LobbyDeploymentPreview.For(EGameSystem.GFFirefight, EGameMode.Standard,
            EDeploymentStyle.GroundWar, 2, 48, 48);

        var expectGf = AdvancedDeployment.LongTableZones(EDeploymentStyle.GroundWar, 72, 48);
        var expectGff = AdvancedDeployment.Zones(EDeploymentStyle.GroundWar, 48, 48);
        Assert.That(gf.Zones, Has.Count.EqualTo(2));
        Assert.That(Bounds(gf.Zones[0]), Is.EqualTo(Bounds(expectGf.A)));
        Assert.That(Bounds(gff.Zones[0]), Is.EqualTo(Bounds(expectGff.A)));
        Assert.That(Bounds(gf.Zones[0]), Is.Not.EqualTo(Bounds(gff.Zones[0])));
    }

    private static (float, float, float, float) Bounds(IZone zone)
    {
        var r = (RectangularZone)zone;
        return (r.Left, r.Right, r.Bottom, r.Top);
    }

    [Test]
    public void ARandomStyleShowsTheCoreZonesAndSaysItIsRolled()
    {
        var plan = LobbyDeploymentPreview.For(EGameSystem.AgeOfFantasy, EGameMode.Standard,
            EDeploymentStyle.RandomStrategic, 2, 72, 48);
        Assert.That(plan.Zones, Has.Count.EqualTo(2));
        Assert.That(Bounds(plan.Zones[1]), Is.EqualTo((0f, 72f, 0f, 12f)));
        Assert.That(plan.Note, Does.Contain("rolled"));
    }

    /// <summary>
    /// Batch B pinned "a map with a zone for every side keeps its own" on purpose; Kai reversed that on
    /// 2026-11-07 (deployment is ONLY picked in the lobby), so the preview no longer even takes the map -
    /// the pick is drawn whatever map is chosen, as the engine now deploys (MapZonesIgnoredTests).
    /// </summary>
    [Test]
    public void TheLobbysPickIsAlwaysDrawnAndThreeSidesUseTheCorners()
    {
        var picked = LobbyDeploymentPreview.For(EGameSystem.GrimdarkFuture, EGameMode.Standard,
            EDeploymentStyle.Spearhead, 2, 72, 48);
        Assert.That(picked.Zones, Has.Count.EqualTo(2));
        Assert.That(picked.Note, Does.Contain("Spearhead").Or.Contain(AdvancedDeployment.Name(EDeploymentStyle.Spearhead)));
        Assert.That(picked.Note, Does.Not.Contain("own deployment zones"));

        var ffa = LobbyDeploymentPreview.For(EGameSystem.GrimdarkFuture, EGameMode.Standard,
            EDeploymentStyle.Spearhead, 3, 72, 48);
        Assert.That(ffa.Zones, Is.Empty);
        Assert.That(ffa.Note, Does.Contain("corner"));
    }

    [Test]
    public void SidesAreDistinctTeams() =>
        Assert.That(LobbyDeploymentPreview.Sides(new[] { 1, 2, 1, 2 }), Is.EqualTo(2));

    // ── Select Map: only this game's table size ──────────────────────────────────────────────────

    [Test]
    public void OnlyMapsMadeForThisTableAreOffered()
    {
        var sixByFour = new TerrainLayoutFile { TableWidthInches = 72, TableHeightInches = 48 };
        var fourByFour = new TerrainLayoutFile { TableWidthInches = 48, TableHeightInches = 48 };

        (float gw, float gh) = GameWideConstants.TableSizeFor(EGameSystem.GrimdarkFuture);
        Assert.That(MapPickerScreen.FitsTable(sixByFour, gw, gh), Is.True);
        Assert.That(MapPickerScreen.FitsTable(fourByFour, gw, gh), Is.False);

        (float fw, float fh) = GameWideConstants.TableSizeFor(EGameSystem.GFStarQuest);
        Assert.That(MapPickerScreen.FitsTable(fourByFour, fw, fh), Is.True);
        Assert.That(MapPickerScreen.FitsTable(sixByFour, fw, fh), Is.False);
        Assert.That(MapPickerScreen.FitsTable(null, fw, fh), Is.False);
    }

    [Test]
    public void ATerrainPiecesHoverSaysWhatItIs()
    {
        string text = MapPickerScreen.PieceText(new TerrainPieceEntry
            { Name = "Forest", TerrainType = ETerrainType.Cover | ETerrainType.Difficult });
        Assert.That(text, Does.StartWith("Forest"));
        Assert.That(text, Does.Contain("Cover"));
        Assert.That(text, Does.Contain("Difficult"));
    }

    // ── Choose Army: the badge ───────────────────────────────────────────────────────────────────

    [Test]
    public void TheBadgeSaysLegalOverLimitOrProblems()
    {
        Assert.That(ArmyPickerScreen.Badge(1990, 2000, 0), Is.EqualTo(("LEGAL", true)));
        Assert.That(ArmyPickerScreen.Badge(2100, 2000, 0).Text, Does.StartWith("OVER LIMIT"));
        Assert.That(ArmyPickerScreen.Badge(1500, 2000, 2), Is.EqualTo(("2 PROBLEMS", false)));
    }

    // ── Book wording ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Every lobby hover built from the books, for every value it can be asked about.</summary>
    private static IEnumerable<string> AllBookText()
    {
        foreach (FieldInfo f in typeof(LobbyBookText).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (f.GetValue(null) is string s) yield return s;
        foreach (EDeploymentStyle style in Enum.GetValues<EDeploymentStyle>()) yield return LobbyBookText.Deployment(style);
        foreach (EMissionCardGame game in Enum.GetValues<EMissionCardGame>()) yield return LobbyBookText.MissionCards(game);
        foreach ((string name, EExtraActions _) in ExtraActions.All) yield return LobbyBookText.ExtraAction(name);
        foreach (EGameSystem system in Enum.GetValues<EGameSystem>())
        {
            yield return LobbyBookText.FastPlayIntro(system);
            yield return LobbyBookText.FreeForAll(system);
            yield return LobbyBookText.CoreShooting(system) ?? "";
            yield return LobbyBookText.CoreCover(system) ?? "";
        }
    }

    [Test]
    public void TheBookTextIsAsciiOnly()
    {
        foreach (string text in AllBookText())
            Assert.That(text.All(c => c < 128), Is.True, text);
    }

    [Test]
    public void EveryOptionHasBookWording()
    {
        foreach (EDeploymentStyle style in Enum.GetValues<EDeploymentStyle>())
            Assert.That(LobbyBookText.Deployment(style), Is.Not.Empty, style.ToString());
        foreach (EMissionCardGame game in Enum.GetValues<EMissionCardGame>())
            Assert.That(LobbyBookText.MissionCards(game), Is.Not.Empty, game.ToString());
        foreach ((string name, EExtraActions _) in ExtraActions.All)
            Assert.That(LobbyBookText.ExtraAction(name), Is.Not.Empty, name);
    }

    [Test]
    public void FastPlayQuotesEachBooksOwnArmySize()
    {
        Assert.That(LobbyBookText.FastPlayIntro(EGameSystem.GrimdarkFuture), Does.Contain("4000pts"));
        Assert.That(LobbyBookText.FastPlayIntro(EGameSystem.AgeOfFantasy), Does.Contain("3000pts"));
        Assert.That(LobbyBookText.FreeForAll(EGameSystem.GFFirefight), Does.Contain("1'x1'"));
        Assert.That(LobbyBookText.FreeForAll(EGameSystem.GrimdarkFuture), Does.Contain("2'x1'"));
    }
}
