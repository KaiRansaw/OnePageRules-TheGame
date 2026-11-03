using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #445 - guards the CALL SITE, which no unit test can reach.
///
/// <para>
/// This project keeps paying for the same lesson: <b>a test that calls the new code cannot tell whether
/// the game reaches it.</b> <c>CampaignStandingTests</c> and <c>CampaignEndConditionTests</c> both call
/// <c>CampaignRules.Start</c> themselves, so both would pass in full while
/// <see cref="FdgRaylib.Rendering.StrikeTeamScreen"/> went on hard-coding
/// <c>ECampaignType.Endless</c> - which is exactly what it did before #445, for every campaign ever
/// created. Nothing would fail; campaigns would simply never end.
/// </para>
///
/// <para>
/// Creating a campaign runs through private screen state and reads the real armies folder, so there is
/// no seam to drive it from a test. Reading the source is the honest second best: it cannot prove the
/// screen is right, but it does fail the moment the wiring is reverted, which is the regression worth
/// catching. Same approach as <c>DisabledScopeBalanceTests</c>, and for the same reason - nothing in
/// the suite draws an ImGui frame.
/// </para>
/// </summary>
[TestFixture]
public class CampaignCreationCallSiteTests
{
    private static string ScreenSource()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "FdgRaylib", "Rendering", "StrikeTeamScreen.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        Assert.Ignore("Could not find FdgRaylib/Rendering/StrikeTeamScreen.cs beside the test run.");
        return "";
    }

    /// <summary>
    /// Every <c>CampaignRules.Start</c> call in the campaign screen, as its argument text. Comments and
    /// string literals are stripped first so prose about the old behaviour cannot trip the assertions
    /// below - this file's own doc comment mentions <c>ECampaignType.Endless</c>, and so does the
    /// screen's.
    /// </summary>
    private static MatchCollection StartCalls()
    {
        string source = ScreenSource();
        source = Regex.Replace(source, @"//[^\n]*", "");
        source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
        source = Regex.Replace(source, "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");

        MatchCollection calls = Regex.Matches(source, @"CampaignRules\.Start\s*\(([^;]*?)\)\s*;",
            RegexOptions.Singleline);
        Assert.That(calls, Is.Not.Empty,
            "no CampaignRules.Start call found - if creation moved, point this test at its new home "
            + "rather than deleting it.");
        return calls;
    }

    /// <summary>
    /// The player's army and the rival's are created by two separate calls. Both must be wired, because
    /// the two files have to agree on the campaign's end condition.
    /// </summary>
    [Test]
    public void BothCampaignArmiesAreCreatedThroughAWiredCall()
    {
        Assert.That(StartCalls(), Has.Count.EqualTo(2),
            "one call creates the player's army and one the rival's; a third would need wiring too.");
    }

    [Test]
    public void NoCampaignIsCreatedWithAHardCodedEndlessType()
    {
        foreach (Match call in StartCalls())
        {
            Assert.That(call.Groups[1].Value, Does.Not.Contain("ECampaignType."),
                "the campaign type must come from the player's choice, not a literal. A hard-coded "
                + "type is how every campaign ended up Endless and unwinnable before #445.\n"
                + "Call: " + call.Value);
        }
    }

    [Test]
    public void EveryCampaignIsCreatedWithAVpTarget()
    {
        foreach (Match call in StartCalls())
        {
            Assert.That(call.Groups[1].Value, Does.Contain("vpTarget"),
                "vpTarget defaults to 0, which reads as 'no target' - so a call that omits it creates a "
                + "campaign that can never be won, silently.\nCall: " + call.Value);
        }
    }
}
