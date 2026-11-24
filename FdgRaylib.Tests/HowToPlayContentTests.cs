using FDG;
using FDG.ArmyBuilding;
using FdgRaylib.Tutorial;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// How to Play (Kai, 2026-11-24): the practice battle's content has to hold together for both games it teaches -
/// the set armies build from this game's own current books, every rule a lesson shows exists in the book text, and
/// every coach line is filled in.
/// </summary>
[TestFixture]
public class HowToPlayContentTests
{
    private static IEnumerable<EGameSystem> Systems() => HowToPlay.Systems;

    [TestCaseSource(nameof(Systems))]
    public void TheSetArmies_BuildFromThisGamesOwnBooks(EGameSystem system)
    {
        var armies = HowToPlay.BuildArmies(system, out string? error);
        Assert.That(armies, Is.Not.Null, error);
        string slug = GameSystems.CampaignBookSlug(system)!;
        Assert.That(GameSystems.SameSystem(armies!.Value.Mine.GameSystem, slug), "your army is from this game's books");
        Assert.That(GameSystems.SameSystem(armies.Value.Foe.GameSystem, slug), "the enemy is from this game's books");

        var a = HowToPlay.ArmiesFor(system);
        Assert.That(armies.Value.Mine.Units.Select(u => u.Name), Is.SupersetOf(new[] { a.Name(HowToPlay.Role.MyShooters), a.Name(HowToPlay.Role.MyMelee), a.Name(HowToPlay.Role.MyHero) }));
        Assert.That(armies.Value.Foe.Units.Select(u => u.Name), Is.SupersetOf(new[] { a.Name(HowToPlay.Role.FoeShooters), a.Name(HowToPlay.Role.FoeMelee) }));
        var hero = armies.Value.Mine.Units.First(u => u.Name == a.Name(HowToPlay.Role.MyHero));
        var melee = armies.Value.Mine.Units.First(u => u.Name == a.Name(HowToPlay.Role.MyMelee));
        Assert.That(hero.JoinsUnitId, Is.EqualTo(melee.Id), "the hero leads the melee unit (lesson 10)");
    }

    [TestCaseSource(nameof(Systems))]
    public void EveryLessonsRules_AreInTheBookText(EGameSystem system)
    {
        RuleText.BookText book = RuleText.For(system);
        Assert.That(book.Sections, Is.Not.Empty, RuleText.FileFor(system));
        foreach (HowToPlay.Lesson lesson in HowToPlay.Lessons(system))
        {
            var ids = lesson.RuleIds.SelectMany(id => id == "special-rules" ? HowToPlay.SpecialRuleIds(system) : new[] { id });
            foreach (string id in ids) Assert.That(book[id], Is.Not.Null, $"lesson {lesson.Number} shows '{id}'");
        }
    }

    [TestCaseSource(nameof(Systems))]
    public void TwelveLessons_EveryCoachLineFilledIn(EGameSystem system)
    {
        var lessons = HowToPlay.Lessons(system);
        Assert.That(lessons.Select(l => l.Number), Is.EqualTo(Enumerable.Range(1, 12)));
        foreach (var step in lessons.SelectMany(l => l.Steps))
        {
            string text = HowToPlay.Fill(step.Coach, system) + HowToPlay.Fill(step.OrElseCoach ?? "", system);
            Assert.That(text, Does.Not.Contain("{"), text);
            Assert.That(text.All(c => c < 128), text);
        }
    }

    [TestCaseSource(nameof(Systems))]
    public void TheArmyRules_ComeFromTheBooks(EGameSystem system)
    {
        Assert.That(HowToPlay.ArmyRules(system), Has.Count.EqualTo(2));
    }

    [Test]
    public void TheRulesUsedByTheArmies_AreTaughtInLesson12()
    {
        // GF's armies carry these core rules (Battle Brothers: AP, Assault, Fearless; Alien Hives: Bane, Strider).
        Assert.That(HowToPlay.SpecialRuleIds(EGameSystem.GrimdarkFuture), Is.SupersetOf(new[] { "ap", "assault", "fearless", "bane", "strider" }));
        Assert.That(HowToPlay.SpecialRuleIds(EGameSystem.AgeOfFantasy), Is.SupersetOf(new[] { "ap", "parry" }));
    }

    [Test]
    public void SetDice_FavourYouInTheLessonsThatNeedIt()
    {
        var hit = FDG.Rules.Definitions.ERollKind.Hit;
        var morale = FDG.Rules.Definitions.ERollKind.Morale;
        Assert.That(HowToPlay.Faces(HowToPlay.Luck.Melee, morale, mine: false), Is.EqualTo(new[] { 1 }), "a 1 always fails: the enemy is Shaken");
        Assert.That(HowToPlay.Faces(HowToPlay.Luck.Melee, hit, mine: true)!.Count(f => f >= 5), Is.GreaterThanOrEqualTo(2), "a few hits, not a massacre");
        Assert.That(HowToPlay.Faces(HowToPlay.Luck.Melee, hit, mine: false)!.All(f => f <= 3), "their strikes back mostly miss");
        Assert.That(HowToPlay.Faces(HowToPlay.Luck.None, hit, mine: true), Is.Null, "no lesson, ordinary dice");
    }
}
