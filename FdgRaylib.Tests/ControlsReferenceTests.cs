using System;
using System.Linq;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The keybind list the in-game F1 panel and the Settings screen both read.
///
/// <para>
/// A keybind list that disagrees with the keys is worse than no list: a player trusts it, presses the
/// wrong thing, and concludes the game is broken or that they are. These pin the things that would make
/// it wrong silently.
/// </para>
/// </summary>
[TestFixture]
public class ControlsReferenceTests
{
    /// <summary>Game text is ASCII-only - the ImGui font atlas renders anything above U+00FF as "?".</summary>
    [Test]
    public void EveryLineIsAscii()
    {
        foreach ((string keys, string what, string group, string tip) in ControlsReference.All)
        {
            foreach (string text in new[] { keys, what, group, tip })
            {
                Assert.That(text.All(c => c <= 0x7F), Is.True, $"non-ASCII in \"{text}\".");
                Assert.That(text.Trim(), Is.Not.Empty);
            }
        }
    }

    /// <summary>
    /// Every line belongs to a named group, and the groups are the constants - not strings typed again
    /// at each row, which is how one row ends up in a group of its own with a typo for a name.
    /// </summary>
    [Test]
    public void EveryLineIsInAKnownGroup()
    {
        var known = new[]
        {
            ControlsReference.Camera, ControlsReference.Doing, ControlsReference.Looking,
            ControlsReference.App, ControlsReference.Menus,
        };
        foreach ((string _, string _, string group, string _) in ControlsReference.All)
            Assert.That(known, Does.Contain(group));
    }

    /// <summary>
    /// Rows of a group sit together. The Settings screen prints a heading whenever the group changes, so
    /// a stray row out of order would print its group's heading a second time further down the list.
    /// </summary>
    [Test]
    public void TheGroupsAreContiguous()
    {
        var seen = new List<string>();
        string? current = null;
        foreach ((string _, string _, string group, string _) in ControlsReference.All)
        {
            if (group == current) continue;
            Assert.That(seen, Does.Not.Contain(group),
                $"\"{group}\" appears again after another group - its heading would print twice.");
            seen.Add(group);
            current = group;
        }
    }

    /// <summary>
    /// The camera entries have to match what the camera actually does, and these four moved on
    /// 2026-11-02: the plain wheel zooms where it needed Alt, and in 3D middle-drag changes the angle
    /// while right-drag moves the board. Written as "the list says what the code does" rather than
    /// asserting the code, because the code is Raylib input polling and this is the documentation of it.
    /// </summary>
    [Test]
    public void TheCameraLinesDescribeTheCurrentGestures()
    {
        string What(string keys) => ControlsReference.All
            .First(c => c.Keys.Equals(keys, StringComparison.Ordinal)).What;

        // Kai, 2026-11-02: "make it so that the ONLY way to zoom in 2d is to hold Alt+scroll." The
        // plain wheel is the unit's rotation control, so the screen that teaches the controls has to
        // say which is which - a player who reads "Mouse wheel: Zoom" and then watches their unit spin
        // concludes the game is broken.
        Assert.That(What("Alt + mouse wheel"), Does.Contain("Zoom"));
        Assert.That(What("Alt + mouse wheel"), Does.Contain("2D"));
        Assert.That(What("Mouse wheel"), Does.Contain("turn"),
            "the plain wheel turns the unit being placed; only Alt+wheel zooms in 2D.");
        Assert.That(What("Middle-drag"), Does.Contain("angle"), "middle-drag changes the 3D angle.");
        Assert.That(What("Right-drag"), Does.Contain("Move"), "right-drag moves the board.");
        Assert.That(What("Right-click bare table"), Does.Contain("Deselect"));
    }

    /// <summary>
    /// Kai's In-Game list, 2026-11-07: "Controls list corrected (letter hotkeys, Ctrl+Z/X)". Take Back /
    /// Redo are Ctrl+Z / Ctrl+X (RaylibRenderer.HandleTakeBackKeys), so no row may claim plain Z / X; the
    /// decision panels' letter shortcuts are switched off, so they are not listed while they are; and the
    /// dead O key is gone.
    /// </summary>
    [Test]
    public void TheListMatchesTheKeysThatWorkToday()
    {
        var keys = ControlsReference.All.Select(c => c.Keys).ToList();
        Assert.That(keys, Does.Not.Contain("Z / X"), "Take Back / Redo are Ctrl+Z / Ctrl+X.");
        Assert.That(ControlsReference.All.Single(c => c.Keys == "Ctrl + Z / Ctrl + X").What, Does.Contain("Take Back"));
        Assert.That(keys, Does.Not.Contain("O"), "the O key's mission window is gone.");
        if (FdgRaylib.Rendering.Resolvers.ResolverHotkeys.LettersDisabled)
        {
            Assert.That(keys, Does.Not.Contain("W / C / S / A / X"), "letter shortcuts are switched off.");
            Assert.That(keys, Does.Not.Contain("Q E R T D F G Z V B"), "letter shortcuts are switched off.");
        }
    }

    /// <summary>Every row has a hover (Kai: "Every setting has a hover tooltip").</summary>
    [Test]
    public void EveryRowHasATooltip()
    {
        foreach (var row in ControlsReference.All)
            Assert.That(row.Tip.Trim().Length, Is.GreaterThan(10), $"\"{row.Keys}\" needs a hover.");
    }

    /// <summary>One row per key combination - a duplicate means two rows claiming the same key.</summary>
    [Test]
    public void NoKeyCombinationIsListedTwice()
    {
        var keys = ControlsReference.All.Select(c => c.Keys).ToList();
        Assert.That(keys, Is.Unique);
    }
}
