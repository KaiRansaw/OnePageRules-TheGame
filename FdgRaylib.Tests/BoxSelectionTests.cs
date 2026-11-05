using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FdgRaylib.Placement;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The drag-a-box gesture deployment and movement share (Kai, 2026-11-05: "the drag and highlight option
/// for movement/deployment doesnt work still").
/// </summary>
[TestFixture]
public class BoxSelectionTests
{
    private static readonly List<Position> Row =
        new() { new(1f, 1f), new(3f, 1f), new(5f, 1f), new(7f, 1f), new(9f, 1f) };

    [Test]
    public void ABox_CatchesTheModelsWhoseCentresAreInsideIt()
    {
        Assert.That(BoxSelection.Within(new Position(2f, 0f), new Position(6f, 2f), Row),
            Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void ABox_DrawnInAnyDirection_CatchesTheSameModels()
    {
        var forward  = BoxSelection.Within(new Position(2f, 0f), new Position(8f, 2f), Row);
        var backward = BoxSelection.Within(new Position(8f, 2f), new Position(2f, 0f), Row);
        var crossed  = BoxSelection.Within(new Position(2f, 2f), new Position(8f, 0f), Row);
        Assert.That(backward, Is.EqualTo(forward));
        Assert.That(crossed, Is.EqualTo(forward));
        Assert.That(forward, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void ABoxOverNothing_SelectsNothing()
    {
        Assert.That(BoxSelection.Within(new Position(20f, 20f), new Position(30f, 30f), Row), Is.Empty);
    }

    [Test]
    public void APressReleasedInPlace_IsAClick_NotABox()
    {
        Assert.That(BoxSelection.IsBox(new Position(4f, 4f), new Position(4.2f, 4.1f)), Is.False);
        Assert.That(BoxSelection.IsBox(new Position(4f, 4f), new Position(4.6f, 4f)), Is.True);
        Assert.That(BoxSelection.IsBox(new Position(4f, 4f), new Position(4f, 3f)), Is.True,
            "a tall thin box is still a box");
    }

    /// <summary>
    /// The bug half of the report: while models were still to place, a plain click on bare table started
    /// a box and swallowed the click, so in single mode only the first model could ever be clicked down.
    /// </summary>
    [Test]
    public void Deployment_PlainClickOnBareTable_WithModelsStillToPlace_DoesNotStartABox()
    {
        Assert.That(BoxSelection.DeploymentPressStartsBox(ctrlHeld: false, allPlaced: false, pressedOnModel: false),
            Is.False);
    }

    [Test]
    public void Deployment_PlainDragOnBareTable_OnceEveryModelIsDown_StillStartsABox()
    {
        Assert.That(BoxSelection.DeploymentPressStartsBox(ctrlHeld: false, allPlaced: true, pressedOnModel: false),
            Is.True);
    }

    [Test]
    public void Deployment_PlainPressOnAModel_PicksItUp_RatherThanBoxing()
    {
        Assert.That(BoxSelection.DeploymentPressStartsBox(ctrlHeld: false, allPlaced: true, pressedOnModel: true),
            Is.False);
    }

    [Test]
    public void Deployment_CtrlPress_AlwaysBoxes()
    {
        foreach (bool allPlaced in new[] { false, true })
        foreach (bool onModel in new[] { false, true })
            Assert.That(BoxSelection.DeploymentPressStartsBox(ctrlHeld: true, allPlaced, onModel), Is.True);
    }

    /// <summary>
    /// Movement never had a box at all. These pin that both screens route through the shared helper, so a
    /// later edit cannot quietly drop the gesture from one of them again.
    /// </summary>
    [TestCase("GuiDefineMovementResolver.cs", "BoxSelection.Within")]
    [TestCase("GuiDefineMovementResolver.cs", "BoxSelection.IsBox")]
    [TestCase("GuiPlaceObjectsResolver.cs", "BoxSelection.DeploymentPressStartsBox")]
    [TestCase("GuiPlaceObjectsResolver.cs", "BoxSelection.IsBox")]
    public void BothScreens_UseTheSharedGesture(string file, string call)
    {
        string path = Path.Combine(RepoRoot(), "FdgRaylib", "Rendering", "Resolvers", file);
        Assert.That(File.ReadAllText(path), Does.Contain(call));
    }

    /// <summary>The movement group step only moves the selected models when there is a selection.</summary>
    [Test]
    public void Movement_GroupStep_IsRestrictedToTheSelection()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "FdgRaylib", "Rendering", "Resolvers",
            "GuiDefineMovementResolver.cs"));
        Assert.That(src, Does.Contain("paths.Keys.Where(_boxSelected.Contains)"));
        Assert.That(src, Does.Contain("bool group = _formationMode.IsGroup || _boxSelected.Count > 0;"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "fdg-raylib.sln"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root not found");
        return dir!.FullName;
    }
}
