using FDG;
using FDG.Quest;
using FdgRaylib.Quest;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>The Godot Hero Builder's non-drawing half (Kai's Hero Builder mockup, 2026-11-12, every item kept):
/// real Star Quest data, a Grunt Veteran made a Berserker with Rage and a gear swap, plus its 3D look.</summary>
[TestFixture]
public class HeroEditorTests
{
    private static HeroEditor BuildKrrek()
    {
        var e = new HeroEditor(EGameSystem.GFStarQuest);
        e.StartNew();
        SqFaction hives = e.Data.Factions.First(f => f.Faction == "Alien Hives");
        e.PickBase(hives, hives.Heroes.First(h => h.Name == "Grunt Veteran"));
        e.Name = "Krrek";
        e.PickClass(e.Data.Classes.First(c => c.Name == "Berserker"));
        return e;
    }

    [Test]
    public void Berserker_StatsAndMissingLine()
    {
        var e = BuildKrrek();
        QuestHero h = e.Compose()!;
        Assert.That((h.Quality, h.Defense, h.Tough, h.Power, h.Strength), Is.EqualTo((4, 6, 9, 4, 4)));
        Assert.That(e.SaveProblem(), Is.EqualTo("Pick 1 more starter skill (step 3)."));
        Assert.That(e.ToggleSkill("Rage"));
        Assert.That(e.ToggleSkill("Challenge"), Is.False, "the class allows one");
        Assert.That(e.SaveProblem(), Is.Null);
    }

    [Test]
    public void Gear_SwapAddsPoints_AndBlocksASectionWhoseTargetIsGone()
    {
        var e = BuildKrrek();
        var sections = e.GearSections();
        var both = sections.First(s => s.Entry.Section.Label == "Replace Spike-Shooter and Claws");
        e.TogglePick(both.Entry.Key, both.Entry.Section, both.Entry.Section.Opts.FindIndex(o => o.Label.StartsWith("Burst Bio-Spitter")));
        QuestHero h = e.Compose()!;
        Assert.That(h.Points, Is.EqualTo(60));
        Assert.That(h.Weapons.Select(w => w.Name), Does.Contain("Burst Bio-Spitter"));
        var spike = e.GearSections().First(s => s.Entry.Section.Label == "Replace Spike-Shooter");
        Assert.That(spike.Usable, Is.False);
        Assert.That(spike.Why, Does.Contain("Spike-Shooter"));
    }

    [Test]
    public void Look_IsSavedOnTheHero()
    {
        var e = BuildKrrek();
        e.Look = "Alien Hives/Grunt Veteran/gv_b.glb";
        Assert.That(e.Compose()!.Look, Is.EqualTo("Alien Hives/Grunt Veteran/gv_b.glb"));
        e.PickBase(e.Faction!, e.Faction!.Heroes.First(h => h.Name == "Snatcher Lord"));
        Assert.That(e.Look, Is.Null, "a different base hero drops the old look");
    }

    [Test]
    public void FolderLook_Parses()
    {
        Assert.That(FdgRaylib.Models3D.ModelLooks.Folder("Alien Hives/Grunt Veteran/gv_b.glb"), Is.EqualTo(("Alien Hives", "Grunt Veteran", (string?)"gv_b.glb")));
        Assert.That(FdgRaylib.Models3D.ModelLooks.Folder("Alien Hives/Grunt Veteran/"), Is.EqualTo(("Alien Hives", "Grunt Veteran", (string?)null)));
        Assert.That(FdgRaylib.Models3D.ModelLooks.Folder("gv_b.glb"), Is.Null);
    }
}
