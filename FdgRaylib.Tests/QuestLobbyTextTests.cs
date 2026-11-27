using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Bug hunt 2026-11-27 (run 4): the AoF: Quest Horde lobby's Game tab said the waves are sized "from the heroes' total
/// points and the difficulty, which you pick when the mission starts". Quest Horde has no difficulty - Horde Mode
/// v2.0.0 sizes wave X at X*25% of the heroes' points - so the note now follows the mode.
/// </summary>
[TestFixture]
public class QuestLobbyTextTests
{
    [Test]
    public void QuestHorde_NoteQuotesTheHordeBooksWaveSize_AndSaysNothingAboutDifficulty()
    {
        string note = QuestLobbyText.GameNote(EGameMode.StarQuestHorde);
        Assert.That(note, Does.Contain("The total point value of all enemy units in a wave is X*25% of the total point value of the heroes, where X is the current wave number."));
        Assert.That(note, Does.Not.Contain("difficulty"));
    }

    [Test]
    public void QuestMission_NoteStillMentionsTheDifficulty()
    {
        Assert.That(QuestLobbyText.GameNote(EGameMode.StarQuestMission), Does.Contain("difficulty"));
    }

    [Test]
    public void Notes_AreAscii()
    {
        foreach (EGameMode m in new[] { EGameMode.StarQuestHorde, EGameMode.StarQuestMission })
            Assert.That(QuestLobbyText.GameNote(m).All(ch => ch < 128), Is.True);
    }
}
