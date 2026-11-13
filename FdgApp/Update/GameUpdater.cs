using System.Diagnostics;
using System.IO.Compression;
using Newtonsoft.Json.Linq;

namespace FdgRaylib.Update;

/// <summary>
/// The main menu's Update button (Kai, 2026-11-12: "instead of having to manually do git pull ... just click update").
///
/// <para><b>Two kinds of copy, two ways to update.</b>
/// <list type="bullet">
/// <item><b>Kai's copy</b> is a git clone run from the Godot editor: Update is <c>git pull</c> plus the engine
/// submodule, and the editor rebuilds on the next Play. Found by a <c>.git</c> above the game's folder.</item>
/// <item><b>A friend's copy</b> is the "Latest build" zip from GitHub Releases (rebuilt on every push by
/// <c>.github/workflows/latest-build.yml</c>): Update downloads the new zip, unpacks it beside the game and copies it
/// over the game from inside the game (<see cref="ApplyInPlace"/>); the player then opens the game again. Files the
/// zip doesn't carry (saved armies, downloaded models and pictures) are left alone; nothing is deleted. The zip carries
/// <see cref="CommitFile"/>, the commit it was built from, which is what "is there a newer one" compares.</item>
/// </list></para>
///
/// <para>The repo is public, so the release check needs no sign-in. Every call reports a plain sentence; nothing here
/// throws for a missing network or a missing git.</para>
/// </summary>
public static class GameUpdater
{
    public const string Repo = "KaiRansaw/OnePageRules-TheGame";
    public const string Branch = "campaign-360";
    public const string ReleaseTag = "latest";
    public const string WindowsAsset = "OnePageRules-TheGame-Windows.zip";
    /// <summary>Written into the friends' zip by the build: the full commit it was built from.</summary>
    public const string CommitFile = "build-commit.txt";
    public const string StagingFolder = "_update";
    public static string ReleasePage => $"https://github.com/{Repo}/releases/tag/{ReleaseTag}";

    public enum EKind { GitClone, Packaged }

    public sealed record Status(bool Available, string Summary, string? Error = null)
    {
        public static Status Failed(string error) => new(false, error, error);
    }

    public sealed record Release(string Commit, string Title, string? WindowsZipUrl, long WindowsZipBytes);

    // ================================================================== which copy

    /// <summary>The git clone the game runs from (the folder holding <c>.git</c>), or null for a packaged copy.</summary>
    public static string? RepoRoot(string appRoot)
    {
        var dir = new DirectoryInfo(appRoot);
        for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        return null;
    }

    public static EKind KindFor(string appRoot) => RepoRoot(appRoot) != null ? EKind.GitClone : EKind.Packaged;

    /// <summary>The commit this packaged copy was built from: its <see cref="CommitFile"/>, else the one compiled in.</summary>
    public static string? InstalledCommit(string gameDir)
    {
        try
        {
            string f = Path.Combine(gameDir, CommitFile);
            if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: >= 7 } sha) return sha;
        }
        catch (IOException) { }
        return AppVersion.ShortCommit;
    }

    /// <summary>True when two commit names are the same commit (one may be the short form of the other).</summary>
    public static bool SameCommit(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        a = a.Trim().ToLowerInvariant(); b = b.Trim().ToLowerInvariant();
        int n = Math.Min(a.Length, b.Length);
        return n >= 7 && a[..n] == b[..n];
    }

    // ================================================================== Kai's copy: git

    public sealed record GitResult(int ExitCode, string Output);

    public static async Task<GitResult> GitAsync(string repo, string args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; // never sit waiting for a password nobody can type
        try
        {
            using var p = Process.Start(psi)!;
            Task<string> o = p.StandardOutput.ReadToEndAsync(ct), e = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return new GitResult(p.ExitCode, ((await o) + (await e)).Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new GitResult(-1, "git could not be run (is Git installed?): " + ex.Message); }
    }

    /// <summary>The branch to pull from: this branch's upstream, else origin's working branch.</summary>
    private static async Task<string> UpstreamAsync(string repo, CancellationToken ct)
    {
        var up = await GitAsync(repo, "rev-parse --abbrev-ref --symbolic-full-name @{u}", ct);
        return up.ExitCode == 0 && up.Output.Length > 0 ? up.Output.Split('\n')[0].Trim() : $"origin/{Branch}";
    }

    /// <summary>Fetches, then counts the commits on GitHub this copy doesn't have.</summary>
    public static async Task<Status> CheckGitAsync(string repo, CancellationToken ct = default)
    {
        var fetch = await GitAsync(repo, "fetch --quiet origin", ct);
        if (fetch.ExitCode != 0) return Status.Failed("Couldn't reach GitHub: " + FirstLine(fetch.Output));
        string up = await UpstreamAsync(repo, ct);
        var count = await GitAsync(repo, $"rev-list --count HEAD..{up}", ct);
        if (count.ExitCode != 0 || !int.TryParse(count.Output.Split('\n')[0].Trim(), out int behind))
            return Status.Failed("Couldn't compare with GitHub: " + FirstLine(count.Output));
        if (behind == 0) return new Status(false, "Up to date.");
        var log = await GitAsync(repo, $"log -1 --format=%s {up}", ct);
        return new Status(true, $"{behind} update{(behind == 1 ? "" : "s")} on GitHub - newest: {FirstLine(log.Output)}");
    }

    /// <summary><c>git pull</c> (fast-forward only, so it never makes a merge) and the engine submodule.</summary>
    public static async Task<Status> PullAsync(string repo, CancellationToken ct = default)
    {
        string up = await UpstreamAsync(repo, ct);
        string remoteBranch = up.Contains('/') ? up[(up.IndexOf('/') + 1)..] : Branch;
        var pull = await GitAsync(repo, $"pull --ff-only origin {remoteBranch}", ct);
        // Godot's editor writes its own .uid / .import files for new scripts; when the same file then arrives in an
        // update, git refuses to overwrite it (Kai, 2026-11-12). Those are Godot's bookkeeping, not anyone's work:
        // clear them and try once more.
        if (pull.ExitCode != 0 && InTheWay(pull.Output) is { Count: > 0 } blocking && blocking.All(IsGodotBookkeeping))
        {
            foreach (string file in blocking)
            {
                var tracked = await GitAsync(repo, $"ls-files --error-unmatch -- \"{file}\"", ct);
                if (tracked.ExitCode == 0) await GitAsync(repo, $"checkout -- \"{file}\"", ct);
                else { try { File.Delete(Path.Combine(repo, file)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            pull = await GitAsync(repo, $"pull --ff-only origin {remoteBranch}", ct);
        }
        if (pull.ExitCode != 0)
        {
            var inTheWay = InTheWay(pull.Output);
            return Status.Failed(inTheWay.Count > 0
                ? "Couldn't update: files changed on this computer are in the way. Tell Claude these names: " + string.Join(", ", inTheWay.Take(6))
                : "Couldn't update: " + FirstLine(pull.Output));
        }
        var sub = await GitAsync(repo, "submodule update --init --recursive", ct);
        if (sub.ExitCode != 0) return Status.Failed("Updated the game, but not the rules engine: " + FirstLine(sub.Output));
        return new Status(false, "Updated. Close the game and press Play in Godot again - it rebuilds with the new code.");
    }

    /// <summary>The files git lists under "...would be overwritten by merge:" (tab-indented lines).</summary>
    public static List<string> InTheWay(string gitOutput)
    {
        var files = new List<string>();
        bool listing = false;
        foreach (string raw in gitOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase)) { listing = true; continue; }
            if (!listing) continue;
            if (line.StartsWith('\t') || line.StartsWith("        ")) files.Add(line.Trim());
            else if (files.Count > 0) listing = false;
        }
        return files;
    }

    /// <summary>Files Godot's editor makes by itself next to scripts and assets.</summary>
    public static bool IsGodotBookkeeping(string path) =>
        path.EndsWith(".uid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".import", StringComparison.OrdinalIgnoreCase);

    // ================================================================== a friend's copy: the release zip

    /// <summary>The "Latest build" release: the commit it was built from and its Windows zip.</summary>
    public static async Task<Release?> LatestReleaseAsync(HttpClient http, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/tags/{ReleaseTag}");
        req.Headers.UserAgent.ParseAdd("OnePageRules-TheGame-Updater");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        var j = JObject.Parse(await res.Content.ReadAsStringAsync(ct));
        return ParseRelease(j);
    }

    public static Release? ParseRelease(JObject j)
    {
        string commit = (string?)j["target_commitish"] ?? "";
        if (!commit.All(Uri.IsHexDigit)) commit = ""; // a branch name, not a commit
        string title = (string?)j["name"] ?? "";
        if (commit.Length < 7) // a release targeted at a branch name: the commit is in the title, "Latest build (abc1234)"
        {
            int open = title.LastIndexOf('('), close = title.LastIndexOf(')');
            if (open >= 0 && close > open) commit = title[(open + 1)..close];
        }
        if (commit.Length < 7) return null;
        var asset = (j["assets"] as JArray)?.FirstOrDefault(a => (string?)a["name"] == WindowsAsset);
        return new Release(commit, title, (string?)asset?["browser_download_url"], (long?)asset?["size"] ?? 0);
    }

    public static async Task<Status> CheckReleaseAsync(HttpClient http, string gameDir, CancellationToken ct = default)
    {
        Release? r;
        try { r = await LatestReleaseAsync(http, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return Status.Failed("Couldn't reach GitHub: " + e.Message); }
        if (r == null) return Status.Failed("Couldn't read the newest build from GitHub.");
        string? mine = InstalledCommit(gameDir);
        if (SameCommit(mine, r.Commit)) return new Status(false, "Up to date.");
        return new Status(true, $"New build on GitHub ({r.Commit[..7]}){(r.WindowsZipBytes > 0 ? $", {r.WindowsZipBytes / 1048576} MB" : "")}.");
    }

    /// <summary>Downloads the zip to <paramref name="dest"/>, reporting 0..1.</summary>
    public static async Task DownloadAsync(HttpClient http, string url, string dest, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? 0, done = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await using var input = await res.Content.ReadAsStreamAsync(ct);
        await using (var output = File.Create(dest))
        {
            var buf = new byte[1 << 16];
            int n;
            while ((n = await input.ReadAsync(buf, ct)) > 0)
            {
                await output.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
    }

    /// <summary>
    /// Unpacks the zip into <paramref name="staging"/> (emptied first) and returns the folder that holds the game -
    /// the zip's single top folder when it has one.
    /// </summary>
    public static string Stage(string zip, string staging)
    {
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        string unpacked = Path.Combine(staging, "files");
        Directory.CreateDirectory(unpacked);
        // On Windows the zip is unpacked by Windows' own tar.exe, not by the game: Smart App Control blocked the new
        // build when the (unsigned) game wrote its program files itself (Kai, 1.76 -> 1.77), while the same kind of
        // files unpacked by Windows ran. ApplyInPlace then only RENAMES them into place, so they stay as Windows
        // wrote them. Elsewhere, or with no tar.exe, the game unpacks it itself.
        if (!WindowsTar(zip, unpacked)) ZipFile.ExtractToDirectory(zip, unpacked, true);
        var dirs = Directory.GetDirectories(unpacked);
        return dirs.Length == 1 && Directory.GetFiles(unpacked).Length == 0 ? dirs[0] : unpacked;
    }

    private static bool WindowsTar(string zip, string into)
    {
        if (!OperatingSystem.IsWindows()) return false;
        string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
        if (!File.Exists(tar)) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tar, $"-xf \"{zip}\" -C \"{into}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            })!;
            p.WaitForExit();
            return p.ExitCode == 0 && Directory.EnumerateFileSystemEntries(into).Any();
        }
        catch (Exception) { return false; }
    }

    /// <summary>Lists files moved aside during an update, so the next start can delete them.</summary>
    public const string OldFilesList = "_update-old-files.txt";
    private const string OldSuffix = ".old-update";

    /// <summary>
    /// Moves the new build over the game while it is still running - no script, no helper program (Kai, 2026-11-12:
    /// Windows' Smart App Control blocked the first version's script). Adds and replaces, never deletes. A file
    /// Windows won't let us overwrite because the game is using it (its own .exe and .dlls) is renamed out of the way
    /// first - Windows allows renaming a running program - and the new one put in its place; the next start deletes the
    /// renamed ones (<see cref="CleanUpOldFiles"/>). Returns how many files were written.
    /// </summary>
    public static int ApplyInPlace(string source, string gameDir)
    {
        int written = 0;
        var moved = new List<string>();
        try
        {
            foreach (string from in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(gameDir, Path.GetRelativePath(source, from));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                // Moved, not copied: a rename keeps the file exactly as it was unpacked (see Stage).
                try { File.Move(from, to, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    string aside = to + OldSuffix;
                    if (File.Exists(aside)) { try { File.Delete(aside); } catch (Exception) { aside = $"{to}.{Guid.NewGuid():N}{OldSuffix}"; } }
                    File.Move(to, aside);
                    moved.Add(aside);
                    File.Move(from, to, true);
                }
                written++;
            }
        }
        finally
        {
            if (moved.Count > 0) File.AppendAllLines(Path.Combine(gameDir, OldFilesList), moved);
        }
        return written;
    }

    /// <summary>At start-up: deletes the files an update moved aside (they are free now) and the unpacked update.</summary>
    public static void CleanUpOldFiles(string gameDir)
    {
        string list = Path.Combine(gameDir, OldFilesList);
        try
        {
            if (File.Exists(list))
            {
                var left = new List<string>();
                foreach (string f in File.ReadAllLines(list).Where(l => l.EndsWith(OldSuffix, StringComparison.Ordinal)))
                {
                    try { if (File.Exists(f)) File.Delete(f); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { left.Add(f); }
                }
                if (left.Count == 0) File.Delete(list); else File.WriteAllLines(list, left);
            }
            string staging = Path.Combine(gameDir, StagingFolder);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string FirstLine(string s)
    {
        string line = s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "(no message)";
        return line.Length > 200 ? line[..200] + "..." : line;
    }
}
