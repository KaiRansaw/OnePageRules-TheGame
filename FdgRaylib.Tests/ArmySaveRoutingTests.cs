using System;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FdgRaylib;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #421 - saving an army in game: the name it is filed under, and the folder it goes in.
///
/// <para>
/// Owner's workflow, 2026-10-31: "Save -&gt; in-game window appears 'Name Army' -&gt; you name army
/// -&gt; list is automatically saved into the folder for the game mode that was chosen. GF:F will auto
/// save into GF:F army lists, AoF will auto save into AoF."
/// </para>
/// <para>
/// The folder is not cosmetic. <see cref="ArmyPaths.EntriesFor"/> decides which armies the lobby offers
/// for a Game Type <b>by the folder the file is in</b>, so a Firefight list written into
/// <c>armies/GDF</c> is missing from the Firefight picker and offered in the Grimdark Future one, where
/// the launch gate refuses it. Choosing the destination from the army's own system removes a way to
/// misfile a list, rather than asking the player not to.
/// </para>
/// </summary>
[TestFixture]
public class ArmySaveRoutingTests
{
    /// <summary>Every system the lobby offers routes to its own folder, and no two share one.</summary>
    [Test]
    public void EverySystemHasItsOwnFolder()
    {
        string[] folders = GameSystemInfo.DisplayOrder
            .Select(ArmyPaths.FolderNameFor)
            .ToArray();

        Assert.That(folders, Is.Unique,
            "two systems sharing an armies folder would cross-list their armies in both pickers.");
        Assert.That(folders, Has.None.Empty);
    }

    /// <summary>
    /// The round trip the save depends on: an army carries a SLUG, the folders are keyed by
    /// <see cref="EGameSystem"/>, and <see cref="GameSystemInfo.FromSlug"/> is what joins them. If this
    /// ever disagrees, armies are filed somewhere the picker does not look.
    /// </summary>
    [Test]
    public void SlugRoundTripsBackToItsSystem()
    {
        foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
        {
            string slug = GameSystemInfo.Slug(system);

            Assert.That(GameSystemInfo.FromSlug(slug), Is.EqualTo(system),
                $"{GameSystemInfo.DisplayName(system)}'s slug ({slug}) must resolve back to it.");
        }
    }

    /// <summary>
    /// An army with no system field is a Grimdark Future army - every list written before the field
    /// existed. It must file into GDF rather than being dropped or landing somewhere arbitrary.
    /// </summary>
    [Test]
    public void AnAbsentSlugIsGrimdarkFuture()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameSystemInfo.FromSlug(null), Is.EqualTo(EGameSystem.GrimdarkFuture));
            Assert.That(GameSystemInfo.FromSlug(""), Is.EqualTo(EGameSystem.GrimdarkFuture));
            Assert.That(GameSystemInfo.FromSlug("   "), Is.EqualTo(EGameSystem.GrimdarkFuture));
        });
    }

    /// <summary>A slug this game does not play is null, not a wrong guess at a system.</summary>
    [Test]
    public void AnUnknownSlugIsNull()
    {
        Assert.That(GameSystemInfo.FromSlug("age-of-fantasy-regiments"), Is.Null,
            "Regiments is a real OPR system this game carries no data for - better to say so than to "
            + "file its armies under Age of Fantasy.");
    }

    /// <summary>The two named in the owner's report, spelled out so a rename cannot pass silently.</summary>
    [Test]
    public void FirefightAndAgeOfFantasyGoWhereTheOwnerExpects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ArmyPaths.FolderNameFor(EGameSystem.GFFirefight), Is.EqualTo("GF Firefight"));
            Assert.That(ArmyPaths.FolderNameFor(EGameSystem.AgeOfFantasy), Is.EqualTo("AoF"));
            Assert.That(ArmyPaths.FolderNameFor(EGameSystem.GrimdarkFuture), Is.EqualTo("GDF"));
        });
    }

    // ── File naming ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A typed name becomes a file name by stripping what the filesystem refuses, not by rejecting it.
    /// "Bob's Lads" is a perfectly good army name and bouncing it would be the app being pedantic about
    /// its own storage.
    /// </summary>
    [Test]
    public void AnOrdinaryNameIsKeptAsTyped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ArmyForgeScreen.SanitizeFileName("Bob's Lads"), Is.EqualTo("Bob's Lads"));
            Assert.That(ArmyForgeScreen.SanitizeFileName("2000pt Hive Swarm"), Is.EqualTo("2000pt Hive Swarm"));
        });
    }

    [Test]
    public void CharactersTheFilesystemRefusesAreStripped()
    {
        string sanitized = ArmyForgeScreen.SanitizeFileName("Orks / Goblins: v2");

        Assert.That(sanitized.IndexOfAny(Path.GetInvalidFileNameChars()), Is.EqualTo(-1),
            "the result has to be a legal file name on this platform.");
        Assert.That(sanitized, Does.Contain("Orks").And.Contain("Goblins"),
            "and still readable - stripping must not eat the name.");
    }

    /// <summary>
    /// Windows silently drops a trailing dot or space, so "Lads." and "Lads" would be the same file and
    /// the second save would overwrite the first without saying so.
    /// </summary>
    [Test]
    public void TrailingDotsAndSpacesAreTrimmed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ArmyForgeScreen.SanitizeFileName("Lads."), Is.EqualTo("Lads"));
            Assert.That(ArmyForgeScreen.SanitizeFileName("Lads   "), Is.EqualTo("Lads"));
            Assert.That(ArmyForgeScreen.SanitizeFileName("  Lads  "), Is.EqualTo("Lads"));
        });
    }

    /// <summary>
    /// A name that sanitizes away to nothing still has to produce a file, or Save would fail with
    /// nothing to show for it.
    /// </summary>
    [Test]
    public void ANameThatStripsToNothingStillYieldsAFileName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ArmyForgeScreen.SanitizeFileName("///"), Is.Not.Empty);
            Assert.That(ArmyForgeScreen.SanitizeFileName("   "), Is.Not.Empty);
            Assert.That(ArmyForgeScreen.SanitizeFileName(""), Is.Not.Empty);
        });
    }
}
