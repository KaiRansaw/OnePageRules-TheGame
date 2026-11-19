using FDG;
using FDG.Data;
using FdgRaylib.Game;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>Kai, 2026-11-19: Horde and Quest panels. The objective text is the Campaign Builder's own words.</summary>
[TestFixture]
public class ModePanelTests
{
    [Test]
    public void EveryQuestObjective_HasItsBookText_InPlainLetters()
    {
        foreach (bool secondary in new[] { false, true })
            for (int n = 1; n <= 6; n++)
            {
                string text = QuestObjectiveText.Of(n, secondary);
                Assert.That(text, Does.EndWith("."), $"{(secondary ? "secondary" : "primary")} {n}");
                Assert.That(text.All(c => c < 128), Is.True, $"{(secondary ? "secondary" : "primary")} {n}: game text is ASCII");
            }
        Assert.That(QuestObjectiveText.Of(2, false), Does.Contain("once the marker has 6 tokens"), "Target Defense: 6 tokens");
        Assert.That(QuestObjectiveText.Of(2, true), Does.Contain("once the marker has 3 tokens"), "Safeguard: 3 tokens");
        Assert.That(QuestObjectiveText.Of(5, false), Does.Contain("On a 5+"), "Area Search completes on a 5+");
        Assert.That(QuestObjectiveText.Of(5, true), Does.Contain("On a 4+"), "Scavenge completes on a 4+");
        Assert.That(QuestObjectiveText.Of(0, false), Is.Empty);
    }

    [Test]
    public void AStandardGame_HasNoModePanel() =>
        Assert.That(ModePanel.For(new TableState(GameDataStore.GameDataStoreBuilder.GetDefault()), EGameSystem.GrimdarkFuture, EGameMode.Standard), Is.Null);
}
