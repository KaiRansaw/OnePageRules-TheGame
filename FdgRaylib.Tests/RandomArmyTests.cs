using FDG;
using FDG.ArmyBuilding;
using FdgRaylib.Lobby;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #395 - Random Army with no saved army of the game: the lobby makes a legal one up from a random army book
/// instead of silently doing nothing. GF: Firefight and AoF: Skirmish ship with no saved armies at all, so before
/// this a Firefight or Skirmish bot sat in the lobby with no army.
/// </summary>
[TestFixture]
public class RandomArmyTests
{
    private static IEnumerable<TestCaseData> Games()
    {
        yield return new TestCaseData(EGameSystem.GrimdarkFuture, 2000);
        yield return new TestCaseData(EGameSystem.GFFirefight, 600);
        yield return new TestCaseData(EGameSystem.AgeOfFantasy, 2000);
        yield return new TestCaseData(EGameSystem.AoFSkirmish, 600);
    }

    [TestCaseSource(nameof(Games))]
    public void EveryBook_MakesALegalArmyNearTheLimit(EGameSystem system, int points)
    {
        string slug = GameSystems.CampaignBookSlug(system)!;
        var books = BookLibrary.Load().Where(b => GameSystems.SameSystem(b.GameSystem, slug)).ToList();
        Assert.That(books, Is.Not.Empty);
        var rng = new Random(395);
        int made = 0, total = 0;
        foreach (BookFile book in books.Take(12))
        {
            BuiltArmyFile? army = RandomArmyBuilder.Build(book, points, rng);
            if (army == null) continue;
            made++;
            total += army.TotalPoints;
            Assert.That(army.TotalPoints, Is.LessThanOrEqualTo(points), book.Name);
            Assert.That(army.TotalPoints, Is.GreaterThanOrEqualTo(points / 2), $"{book.Name}: {army.TotalPoints} of {points}");
            Assert.That(army.Units, Is.Not.Empty);
            var heroes = book.Units.Where(FDG.Campaign.CampaignBot.IsHeroEntry).Select(u => u.Name).ToHashSet();
            // A Hero first whenever the force org allows one (Firefight / Skirmish: one per 400 pts).
            if (heroes.Count > 0 && army.Units.Count > 3)
                Assert.That(army.Units.Any(u => heroes.Contains(u.Name)), Is.True, $"{book.Name}: a Hero first");
        }
        Assert.That(made, Is.GreaterThanOrEqualTo(Math.Min(10, books.Count)), "nearly every book can field an army");
        Assert.That(total / made, Is.GreaterThanOrEqualTo(points * 8 / 10), "on average close to the limit");
    }

    [Test]
    public void TheLobbyMakesOneUp_ForTheGamesWithNoSavedArmies()
    {
        Assert.That(LobbySession.MadeUpArmy(EGameSystem.GFFirefight, 300, new Random(1)), Is.Not.Null);
        Assert.That(LobbySession.MadeUpArmy(EGameSystem.AoFSkirmish, 300, new Random(2)), Is.Not.Null);
        Assert.That(LobbySession.MadeUpArmy(EGameSystem.GFStarQuest, 300, new Random(3)), Is.Null, "Quest enemies are whole collections");
    }
}
