using System;
using System.Linq;
using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Batch A of the screen-by-screen rework (Kai's wish list, 2026-11-07): the two-tile main menu, the game
/// screen with every mode as a button, and Back going to the previous screen everywhere.
/// </summary>
[TestFixture]
public class MenuFlowTests
{
    private sealed class Screen : IAppScreen
    {
        public string Name;
        public Screen(string name) => Name = name;
        public void Draw(int screenW, int screenH) { }
        public override string ToString() => Name;
    }

    [Test]
    public void TheMenuTitleIsTheGamesName() =>
        Assert.That(MainMenuScreen.Title, Is.EqualTo("ONEPAGERULES - THE GAME"));

    /// <summary>Each tile's three games, in the wish list's order - never the other family's.</summary>
    [Test]
    public void EachTileOpensItsOwnThreeGames()
    {
        Assert.That(GameScreen.Games(GameScreen.EFamily.GrimdarkFuture), Is.EqualTo(new[]
            { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest }));
        Assert.That(GameScreen.Games(GameScreen.EFamily.AgeOfFantasy), Is.EqualTo(new[]
            { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest }));
    }

    /// <summary>"GF: Army vs Army, Horde Mode, Campaign ... Star Quest: Play a Mission, Horde Mode,
    /// Campaign, Build Heroes" - and AoF the same.</summary>
    [Test]
    public void EachGameHasTheModesTheWishListNames()
    {
        foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
        {
            string[] labels = GameScreen.Modes(system).Select(GameScreen.ModeLabel).ToArray();
            string[] expected = GameSystemInfo.IsQuest(system)
                ? new[] { "Play a Mission", "Horde Mode", "Campaign", "Build Heroes" }
                : new[] { "Army vs Army", "Horde Mode", "Campaign" };
            Assert.That(labels, Is.EqualTo(expected), GameSystemInfo.DisplayName(system));
        }
    }

    [Test]
    public void EveryModeButtonHasAnAsciiHoverAndEveryGameABlurb()
    {
        foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
        {
            Assert.That(GameScreen.Blurb(system), Is.Not.Empty);
            Assert.That(GameScreen.Blurb(system).All(c => c <= 0x7F), Is.True);
            foreach (GameScreen.EMode mode in GameScreen.Modes(system))
            {
                string tip = GameScreen.ModeTip(system, mode);
                Assert.That(tip, Is.Not.Empty);
                Assert.That(tip.All(c => c <= 0x7F), Is.True, tip);
            }
        }
    }

    // ── Back history ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void BackWalksTheWayYouCame()
    {
        var menu = new Screen("menu");
        var games = new Screen("games");
        var campaigns = new Screen("campaigns");
        var lobby = new Screen("lobby");
        var history = new ScreenHistory(menu);

        history.Forward(menu, games);
        history.Forward(games, campaigns);
        history.Forward(campaigns, lobby);

        Assert.That(history.Back(), Is.SameAs(campaigns), "lobby Back -> the campaign that opened it");
        Assert.That(history.Back(), Is.SameAs(games), "campaign Back -> the game screen");
        Assert.That(history.Back(), Is.SameAs(menu));
        Assert.That(history.Back(), Is.SameAs(menu), "nothing left: Back is the main menu");
    }

    /// <summary>Coming back from a game (Continue / Return to Campaign) drops what was opened after it,
    /// so the campaign's Back still goes to the game screen and not into the dead lobby.</summary>
    [Test]
    public void ReturningToACampaignAfterAGameUnwindsTheLobby()
    {
        var menu = new Screen("menu");
        var games = new Screen("games");
        var campaigns = new Screen("campaigns");
        var lobby = new Screen("lobby");
        var history = new ScreenHistory(menu);
        history.Forward(menu, games);
        history.Forward(games, campaigns);
        history.Forward(campaigns, lobby);

        history.Unwind(campaigns);

        Assert.That(history.Previous, Is.SameAs(games));
    }

    /// <summary>Load Game and Join put the main menu under the lobby; a jump to the main menu clears all.</summary>
    [Test]
    public void ALobbyReachedByAJumpBacksOutToTheMainMenu()
    {
        var menu = new Screen("menu");
        var join = new Screen("join");
        var history = new ScreenHistory(menu);
        history.Forward(menu, join);

        history.Unwind(menu);
        Assert.That(history.Count, Is.EqualTo(0));

        var lobby = new Screen("lobby");
        history.Forward(menu, lobby);
        Assert.That(history.Back(), Is.SameAs(menu));
    }

    [Test]
    public void TheSameScreenTwiceIsNotAStep()
    {
        var menu = new Screen("menu");
        var history = new ScreenHistory(menu);
        history.Forward(menu, menu);
        Assert.That(history.Count, Is.EqualTo(0));
    }

    // ── Host ──────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void EveryGameHostsOnTheFixedPortFriendsJoin() =>
        Assert.That(HostStarter.Port, Is.EqualTo(FDG.Network.NetworkProtocol.DefaultPort));

    [Test]
    public void TheLobbyIsNamedAfterThePlayer()
    {
        Assert.That(HostStarter.TableName("Kai"), Is.EqualTo("Kai's Table"));
        Assert.That(HostStarter.TableName("  "), Is.EqualTo("The Table"));
    }
}
