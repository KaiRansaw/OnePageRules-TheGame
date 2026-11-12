using System.IO.Compression;
using FdgRaylib.Update;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// The main menu's Update button, minus the network: telling Kai's git copy from a friend's zip copy, reading the
/// "Latest build" release, comparing commits, unpacking the zip and the script that copies it over the game.
/// </summary>
[TestFixture]
public class GameUpdaterTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void RepoRoot_FindsTheCloneAboveTheGameFolder_AndNothingForAZipCopy()
    {
        string game = Path.Combine(_dir, "repo", "FdgRaylib");
        Directory.CreateDirectory(game);
        Assert.That(GameUpdater.RepoRoot(game), Is.Null);
        Assert.That(GameUpdater.KindFor(game), Is.EqualTo(GameUpdater.EKind.Packaged));
        Directory.CreateDirectory(Path.Combine(_dir, "repo", ".git"));
        Assert.That(GameUpdater.RepoRoot(game), Is.EqualTo(Path.Combine(_dir, "repo")));
        Assert.That(GameUpdater.KindFor(game), Is.EqualTo(GameUpdater.EKind.GitClone));
    }

    [Test]
    public void Commits_ShortAndLongFormsMatch_AndTheZipsCommitFileWins()
    {
        Assert.That(GameUpdater.SameCommit("8f3f5bb", "8f3f5bbd0123456789abcdef0123456789abcdef"), Is.True);
        Assert.That(GameUpdater.SameCommit("8f3f5bb", "7f46935aaaa"), Is.False);
        Assert.That(GameUpdater.SameCommit(null, "8f3f5bb"), Is.False, "unknown = offer the update");
        File.WriteAllText(Path.Combine(_dir, GameUpdater.CommitFile), "abcdef1234567890\n");
        Assert.That(GameUpdater.InstalledCommit(_dir), Is.EqualTo("abcdef1234567890"));
    }

    [Test]
    public void Release_ReadsCommitAndWindowsZip_EvenWhenTargetedAtABranch()
    {
        var r = GameUpdater.ParseRelease(JObject.Parse(
            "{\"name\":\"Latest build (8f3f5bb)\",\"target_commitish\":\"8f3f5bbd0123456789abcdef0123456789abcdef\"," +
            "\"assets\":[{\"name\":\"other.zip\"},{\"name\":\"OnePageRules-TheGame-Windows.zip\",\"browser_download_url\":\"https://x/y.zip\",\"size\":1048576}]}"))!;
        Assert.That(r.Commit, Does.StartWith("8f3f5bb"));
        Assert.That(r.WindowsZipUrl, Is.EqualTo("https://x/y.zip"));
        Assert.That(r.WindowsZipBytes, Is.EqualTo(1048576));
        var b = GameUpdater.ParseRelease(JObject.Parse("{\"name\":\"Latest build (abc1234)\",\"target_commitish\":\"campaign-360\",\"assets\":[]}"))!;
        Assert.That(b.Commit, Is.EqualTo("abc1234"));
        Assert.That(b.WindowsZipUrl, Is.Null);
    }

    [Test]
    public void Stage_UnpacksTheZipsTopFolder_AndInstallCopiesItOverTheGame()
    {
        string src = Path.Combine(_dir, "src", "OnePageRules-TheGame");
        Directory.CreateDirectory(Path.Combine(src, "Assets"));
        File.WriteAllText(Path.Combine(src, "OnePageRules.exe"), "new");
        File.WriteAllText(Path.Combine(src, "Assets", "a.json"), "{}");
        string zip = Path.Combine(_dir, "u.zip");
        ZipFile.CreateFromDirectory(Path.Combine(_dir, "src"), zip);

        string game = Path.Combine(_dir, "game");
        string staging = Path.Combine(game, GameUpdater.StagingFolder);
        string from = GameUpdater.Stage(zip, staging);
        Assert.That(Path.GetFileName(from), Is.EqualTo("OnePageRules-TheGame"));
        Assert.That(File.ReadAllText(Path.Combine(from, "OnePageRules.exe")), Is.EqualTo("new"));
        Assert.That(GameUpdater.Stage(zip, staging), Is.EqualTo(from), "a second try starts clean");

        // The game's own files are replaced, new ones added, and files the zip doesn't have (a saved army) kept.
        Directory.CreateDirectory(Path.Combine(game, "armies"));
        File.WriteAllText(Path.Combine(game, "OnePageRules.exe"), "old");
        File.WriteAllText(Path.Combine(game, "armies", "mine.fdgarmy"), "kai's army");
        Assert.That(GameUpdater.ApplyInPlace(from, game), Is.EqualTo(2));
        Assert.That(File.ReadAllText(Path.Combine(game, "OnePageRules.exe")), Is.EqualTo("new"));
        Assert.That(File.Exists(Path.Combine(game, "Assets", "a.json")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(game, "armies", "mine.fdgarmy")), Is.EqualTo("kai's army"));

        // Files moved aside while the game ran are deleted on the next start, with the unpacked update.
        string aside = Path.Combine(game, "OnePageRules.exe.old-update");
        File.WriteAllText(aside, "locked old exe");
        File.WriteAllLines(Path.Combine(game, GameUpdater.OldFilesList), new[] { aside });
        GameUpdater.CleanUpOldFiles(game);
        Assert.That(File.Exists(aside), Is.False);
        Assert.That(File.Exists(Path.Combine(game, GameUpdater.OldFilesList)), Is.False);
        Assert.That(Directory.Exists(staging), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(game, "OnePageRules.exe")), Is.EqualTo("new"));
    }

    [Test]
    public async Task GitCopy_SeesNewCommitsOnGitHub_AndPullsThem()
    {
        string origin = Path.Combine(_dir, "origin.git"), work = Path.Combine(_dir, "author"), kai = Path.Combine(_dir, "kai");
        async Task Git(string dir, string args)
        {
            var r = await GameUpdater.GitAsync(dir, args);
            Assume.That(r.ExitCode, Is.EqualTo(0), $"git {args}: {r.Output}");
        }
        Directory.CreateDirectory(origin);
        Directory.CreateDirectory(work);
        await Git(origin, "init --bare -q -b campaign-360");
        await Git(work, "init -q -b campaign-360");
        await Git(work, "-c user.name=t -c user.email=t@t commit -q --allow-empty -m first");
        await Git(work, $"remote add origin \"{origin}\"");
        await Git(work, "push -q origin campaign-360");
        await Git(_dir, $"clone -q \"{origin}\" kai");

        Assert.That((await GameUpdater.CheckGitAsync(kai)).Available, Is.False);
        await Git(work, "-c user.name=t -c user.email=t@t commit -q --allow-empty -m \"Build 9.99 - new stuff\"");
        await Git(work, "push -q origin campaign-360");
        var s = await GameUpdater.CheckGitAsync(kai);
        Assert.That(s.Available, Is.True);
        Assert.That(s.Summary, Does.Contain("1 update").And.Contain("Build 9.99"));

        var pulled = await GameUpdater.PullAsync(kai);
        Assert.That(pulled.Error, Is.Null, pulled.Summary);
        Assert.That((await GameUpdater.CheckGitAsync(kai)).Available, Is.False);
    }
}
