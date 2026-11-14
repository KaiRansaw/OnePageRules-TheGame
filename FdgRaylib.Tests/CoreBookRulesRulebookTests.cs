using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Import;
using NUnit.Framework;
using static FDG.Rules.Dispatch.GameRulebook;

namespace FdgRaylib.Tests;

/// <summary>
/// Each game's rulebook (1.99) now covers the five core rules that live in the book data, not the engine's catalog
/// (#431): Banner, Musician, Sergeant, Brace and Parry - so every core rule a book prints is checked against that
/// game's Army Forge wording. Building this found Sergeant still playing the old "6s to hit deal 1 extra hit";
/// every current book prints "This model gets AP(+1) when attacking" (fixed 2026-11-14).
/// </summary>
[TestFixture]
public class CoreBookRulesRulebookTests
{
    private static string Books => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> BookDataCoreRules() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(Books, "GdfRuleSupplement.json")))
            .Where(d => GameRulebook.CoreRulesInBookData.Contains(d.Name)).ToList();

    private static IEnumerable<EGameSystem> AllGames() => GameRulebook.Games;

    [Test]
    public void TheFiveAreAllDefinedInTheSupplement() =>
        Assert.That(BookDataCoreRules().Select(d => d.Name), Is.EquivalentTo(GameRulebook.CoreRulesInBookData));

    [TestCaseSource(nameof(AllGames))]
    public void EveryGamesWordingOfTheFive_IsAccountedFor(EGameSystem game)
    {
        List<Entry> five = GameRulebook.For(game, BookDataCoreRules())
            .Where(e => GameRulebook.CoreRulesInBookData.Contains(e.Rule)).ToList();
        Assert.That(five, Has.Count.EqualTo(5));
        Assert.That(five.Where(e => e.Note.StartsWith("UNREVIEWED")).Select(e => $"{e.Rule}: {e.GameText}"), Is.Empty);
        Assert.That(five.Where(e => e.Reading == EReading.NotInThisGame).Select(e => e.Rule), Is.Empty,
            "every 3.6.0 / 2.1.0 core book prints all five");
    }

    /// <summary>"This model gets AP(+1) when attacking." - Army Forge, all six games.</summary>
    [Test]
    public void Sergeant_IsAPPlusOneWhenAttacking_EverywhereItIsDefined()
    {
        foreach (EGameSystem g in GameRulebook.Games)
            Assert.That(OfficialRuleText.OwnText("Sergeant", GameSystemInfo.Slug(g)), Is.EqualTo("This model gets AP(+1) when attacking."), g.ToString());

        var definitions = new List<(string Where, SpecialRuleDefinition Def)>();
        definitions.AddRange(BookDataCoreRules().Where(d => d.Name == "Sergeant").Select(d => ("supplement", d)));
        foreach (string path in Directory.EnumerateFiles(Books, "*.fdgbook", SearchOption.AllDirectories))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            definitions.AddRange(book.RuleDefinitions.Where(d => d.Name == "Sergeant").Select(d => (Path.GetFileName(path), d)));
        }
        Assert.That(definitions.Count, Is.GreaterThan(100), "the supplement and the books that use it");

        foreach ((string where, SpecialRuleDefinition d) in definitions)
        {
            HookEntry hook = d.Passive.Single();
            Assert.That(hook.Condition, Is.InstanceOf<Condition.Always>(), where + ": every attack, shooting or melee");
            Assert.That(hook.Effect, Is.EqualTo(new Effect.RollModifier(ERollKind.Save, -1)), where + ": AP(+1) = -1 to the target's Defense roll");
        }
    }
}
