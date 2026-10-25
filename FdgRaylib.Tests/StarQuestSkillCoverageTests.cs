using System.Linq;
using FDG.Stages;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Every Star Quest skill and class feature in the shipped data is either playable in the game or named in
/// <see cref="QuestAdvancedSkills.NotPossible"/> with a reason - so a hero never learns a skill that silently
/// does nothing.
/// </summary>
[TestFixture]
public class StarQuestSkillCoverageTests
{
    [Test]
    public void EverySkillInTheData_IsPlayableOrExplained()
    {
        var skills = StarQuestData.Rules.Skills;
        Assert.That(skills, Is.Not.Empty, StarQuestData.LoadError ?? "no skills loaded");

        var missing = skills
            .Where(s => QuestSkill.Find(s.Name) == null && !QuestAdvancedSkills.NotPossible.ContainsKey(s.Name))
            .Select(s => $"{s.Name} (tier {s.Tier}, type {s.Type})")
            .Distinct()
            .ToList();

        Assert.That(missing, Is.Empty, "skills with no game code and no reason: " + string.Join(", ", missing));
    }

    [Test]
    public void SkillNames_AreUnique()
    {
        var names = QuestSkill.All.Select(s => s.Name)
            .Concat(QuestTreasure.SearchSkills.Select(s => s.Name))
            .Concat(QuestAdvancedSkills.All.Select(s => s.Name))
            .ToList();
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.That(duplicates, Is.Empty, "two skills share a name, so one of them can never be used");
    }
}
