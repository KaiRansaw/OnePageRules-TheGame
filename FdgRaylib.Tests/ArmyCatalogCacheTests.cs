using System;
using System.IO;
using System.Linq;
using FdgRaylib;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #421 - the cached army scan has to notice the folder changing.
///
/// <para>
/// <b>The reported bug.</b> The owner built an army in game, saved it to <c>armies/GDF</c>, went to the
/// lobby and it was not in the picker. He could see the file on disk. Rebuilding the game made it
/// appear - which was the relaunch, not the rebuild. The catalog was a
/// <c>static readonly Lazy&lt;ArmyCatalog&gt;</c>: one scan per process, with a comment explaining that
/// re-reading it per lobby would be waste "because the armies folder doesn't change under us". True
/// when the only way to add an army was to drop a file in before launching; false from the moment the
/// in-game Army Forge could write one.
/// </para>
/// <para>
/// These cases are on the signature - the cheap "has it changed?" check that lets the lobby ask on every
/// entry without reparsing ~12 MB of lists. They run against a real temporary folder, because the whole
/// question is about the filesystem and a mocked one would prove nothing.
/// </para>
/// </summary>
[TestFixture]
public class ArmyCatalogCacheTests
{
    private string _root = null!;
    private string? _previousCwd;

    [SetUp]
    public void SetUp()
    {
        // ArmyPaths.FolderPath looks for an "armies" folder beside the exe or under the working
        // directory, so a temp working directory with one in it is how the real lookup gets exercised
        // rather than bypassed.
        _root = Path.Combine(Path.GetTempPath(), "fdg-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ArmyPaths.DirectoryName, "GDF"));

        _previousCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
        ArmyCatalogCache.Invalidate();
    }

    [TearDown]
    public void TearDown()
    {
        if (_previousCwd != null) Directory.SetCurrentDirectory(_previousCwd);
        ArmyCatalogCache.Invalidate();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// The headline: an army written while the app is running is seen without a restart. This is the
    /// owner's exact report, minus the UI.
    /// </summary>
    [Test]
    public void AnArmySavedWhileRunningIsPickedUpWithoutARestart()
    {
        // A first read caches whatever is there now (nothing).
        Assert.That(ArmyCatalogCache.Current.Entries, Is.Empty);
        ArmyCatalogCache.RefreshIfChanged();   // establishes the baseline signature

        WriteArmy("GDF", "Bobs Lads");

        Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.True,
            "a new .fdgarmy in the folder is a change the cache must notice.");
        Assert.That(ArmyCatalogCache.Current.Entries.Select(e => e.FileName),
            Does.Contain("Bobs Lads"),
            "and the rescan must actually find it - this is the bug the owner hit, where the only "
            + "workaround was restarting the game.");
    }

    /// <summary>
    /// The other half, and the reason this is a signature check rather than a blind invalidate: the
    /// lobby calls it on every entry, including every return from the army picker. If an unchanged
    /// folder reported a change, every pick would reparse the whole folder.
    /// </summary>
    [Test]
    public void AnUnchangedFolderReportsNoChange()
    {
        WriteArmy("GDF", "Steady");
        _ = ArmyCatalogCache.Current.Entries;
        ArmyCatalogCache.RefreshIfChanged();

        Assert.Multiple(() =>
        {
            Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.False);
            Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.False,
                "repeated checks on a still folder stay cheap.");
        });
    }

    [Test]
    public void ADeletedArmyIsAChange()
    {
        string path = WriteArmy("GDF", "Doomed");
        _ = ArmyCatalogCache.Current.Entries;
        ArmyCatalogCache.RefreshIfChanged();

        File.Delete(path);

        Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.True);
        Assert.That(ArmyCatalogCache.Current.Entries, Is.Empty);
    }

    /// <summary>
    /// An army re-saved in place keeps its path, so only its size or write time can give it away. Size
    /// is in the signature for exactly this: a quick edit inside one filesystem timestamp tick would
    /// otherwise look unchanged, which is the same invisible staleness in miniature.
    /// </summary>
    [Test]
    public void AnEditedArmyIsAChange()
    {
        string path = WriteArmy("GDF", "Edited", units: 1);
        _ = ArmyCatalogCache.Current.Entries;
        ArmyCatalogCache.RefreshIfChanged();

        WriteArmy("GDF", "Edited", units: 4);   // same path, different contents

        Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.True);
    }

    /// <summary>Armies in a Game Type subfolder count - the scan recurses, and so must the check.</summary>
    [Test]
    public void ASubfolderIsWatchedToo()
    {
        _ = ArmyCatalogCache.Current.Entries;
        ArmyCatalogCache.RefreshIfChanged();

        Directory.CreateDirectory(Path.Combine(_root, ArmyPaths.DirectoryName, "GF Firefight"));
        WriteArmy("GF Firefight", "Strike Team");

        Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.True);
        Assert.That(ArmyCatalogCache.Current.Entries.Select(e => e.FileName), Does.Contain("Strike Team"));
    }

    /// <summary>
    /// A file that is not an army must not trigger a rescan - otherwise a stray note or a backup in the
    /// folder would reparse every list on every lobby entry.
    /// </summary>
    [Test]
    public void ANonArmyFileIsNotAChange()
    {
        _ = ArmyCatalogCache.Current.Entries;
        ArmyCatalogCache.RefreshIfChanged();

        File.WriteAllText(Path.Combine(_root, ArmyPaths.DirectoryName, "notes.txt"), "shopping list");

        Assert.That(ArmyCatalogCache.RefreshIfChanged(), Is.False);
    }

    /// <summary>
    /// Invalidate is the unconditional version, used after a write when the app already knows it
    /// changed something. It must force a rescan even if the signature somehow matched.
    /// </summary>
    [Test]
    public void InvalidateForcesARescan()
    {
        _ = ArmyCatalogCache.Current.Entries;
        ArmyCatalogCache.Invalidate();
        WriteArmy("GDF", "After Invalidate");

        Assert.That(ArmyCatalogCache.Current.Entries.Select(e => e.FileName),
            Does.Contain("After Invalidate"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Writes a minimal but real .fdgarmy, since the scan parses the file rather than the name.</summary>
    private string WriteArmy(string folder, string fileName, int units = 1)
    {
        string dir = Path.Combine(_root, ArmyPaths.DirectoryName, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName + ".fdgarmy");

        string unitJson = string.Join(",", Enumerable.Range(0, units)
            .Select(i => $"{{\"name\":\"Unit{i}\",\"pointCost\":100}}"));
        File.WriteAllText(path,
            $"{{\"name\":\"{fileName}\",\"faction\":\"Testers\",\"units\":[{unitJson}]}}");
        return path;
    }
}
