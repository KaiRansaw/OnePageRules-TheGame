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
        foreach ((string keys, string what, string group) in ControlsReference.All)
        {
            foreach (string text in new[] { keys, what, group })
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
        foreach ((string _, string _, string group) in ControlsReference.All)
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
        foreach ((string _, string _, string group) in ControlsReference.All)
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

        Assert.That(What("Mouse wheel"), Is.EqualTo("Zoom"));
        Assert.That(What("Middle-drag"), Does.Contain("angle"), "middle-drag changes the 3D angle.");
        Assert.That(What("Right-drag"), Does.Contain("Move"), "right-drag moves the board.");
        Assert.That(What("Right-click bare table"), Does.Contain("Deselect"));
    }

    /// <summary>One row per key combination - a duplicate means two rows claiming the same key.</summary>
    [Test]
    public void NoKeyCombinationIsListedTwice()
    {
        var keys = ControlsReference.All.Select(c => c.Keys).ToList();
        Assert.That(keys, Is.Unique);
    }
}
