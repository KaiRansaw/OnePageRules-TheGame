using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Game Over "Save Battle Report" (Kai, 2026-11-07): "whole report to a file". The tab keeps only its
/// newest lines on screen; the file must still have the first line of the game.
/// </summary>
[TestFixture, NonParallelizable]
public class BattleReportExportTests
{
    [SetUp] public void SetUp() => DiceTray.Reset();
    [TearDown] public void TearDown() => DiceTray.Reset();

    [Test]
    public void TheExportKeepsEveryLine_EvenOnesThatScrolledOffTheTab()
    {
        for (int i = 0; i < 400; i++)
            DiceTray.OnBeat(new BannerBeat($"Line {i}", new TextColor(200, 200, 200, 255), EBannerTier.Notice));

        string text = DiceTray.ExportText();

        Assert.That(text, Does.Contain("Line 0\n").Or.Contain("Line 0\r\n"), "the first line of the game");
        Assert.That(text, Does.Contain("Line 399"));
        Assert.That(text.IndexOf("Line 0", System.StringComparison.Ordinal),
            Is.LessThan(text.IndexOf("Line 399", System.StringComparison.Ordinal)), "oldest first");
    }

    [Test]
    public void ALineKeepsItsBreakdown()
    {
        DiceTray.OnBeat(new BannerBeat("Poison bites", new TextColor(200, 200, 200, 255), EBannerTier.Notice,
            details: new[] { "Rolled a 2 - fails" }));

        Assert.That(DiceTray.ExportText(), Does.Contain("Poison bites").And.Contain("Rolled a 2 - fails"));
    }

    [Test]
    public void ResetForgetsTheLastGame()
    {
        DiceTray.OnBeat(new BannerBeat("Old game", new TextColor(200, 200, 200, 255), EBannerTier.Notice));
        DiceTray.Reset();
        Assert.That(DiceTray.ExportText(), Does.Not.Contain("Old game"));
    }
}
