using System;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #409 (OPR 3.6.0) - formerly the shipped-data pin for Quick Readjustment.
//
// Quick Readjustment existed to cancel Indirect's "-1 to hit when shooting after moving". 3.6 deleted
// that penalty outright, so the rule has nothing to cancel; left in the supplement unchanged its +1 would
// stop being a cancellation and become a free after-moving hit bonus that no rulebook grants. It was
// therefore removed from GdfRuleSupplement.json with the rules change, and is on #409's list to revisit
// when the army books are re-imported against 3.6.
//
// This test now guards the removal rather than the authoring: the supplement must not carry the rule
// back in. The engine-side QuickReadjustmentRuleIntegrationTests shows what it would pay out if it did.
[TestFixture]
public class QuickReadjustmentShippedDataTests
{
    [Test]
    public void QuickReadjustment_IsNotShipped_BecauseIndirectHasNoPenaltyToCancel()
    {
        string json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json"));

        Assert.That(BookRuleSupplement.LoadDefinitions(json).Select(r => r.Name),
            Has.None.EqualTo("Quick Readjustment"),
            "3.6 removed Indirect's -1-after-moving, so a rule that cancels it would net a phantom +1 to "
            + "hit. If the 3.6 book re-import brings a replacement, author it against the new text rather "
            + "than restoring this one.");
    }
}
