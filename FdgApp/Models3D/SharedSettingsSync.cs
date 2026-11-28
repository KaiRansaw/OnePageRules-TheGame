using System.Diagnostics;
using FdgRaylib.Terrain;

namespace FdgRaylib.Models3D;

/// <summary>
/// Everything Kai sets up in the Asset Lab that a fresh download needs, copied from his PC into the game's own files
/// (Assets/Models3D) and sent to GitHub (Kai, 2026-11-17: "the model rotation and other things that were supposed to
/// be saved as config and pushed to the github wasnt"):
/// <list type="bullet">
/// <item>every model's facing / tilt / up / height / base - <see cref="SharedModelSettings"/></item>
/// <item>the facing new downloads get</item>
/// <item>picture framing - <see cref="PictureFraming"/></item>
/// <item>model matches - <see cref="ModelMatches"/></item>
/// <item>terrain matches - <see cref="WarhammerTerrainLibrary"/></item>
/// <item>Kai's audio folder (Assets/Audio) - <see cref="FdgRaylib.Audio.SoundLibrary"/></item>
/// </list>
/// Only when the game runs from the repo (<see cref="SharedModelSettings.IsAuthority"/>).
/// </summary>
public static class SharedSettingsSync
{
    /// <summary>The shipped files, relative to Assets.</summary>
    public static readonly string[] ShippedFiles =
    {
        SharedModelSettings.RelativePath, PictureFraming.ShippedRelativePath,
        ModelMatches.ShippedRelativePath, WarhammerTerrainLibrary.ShippedRelativePath,
        // Kai's hand-built terrain pieces (Asset Lab, Terrain Workshop) - a folder; git adds what is in it.
        Terrain.TerrainPieces.RelativeFolder,
        // Kai's own music, announcer lines and sounds (2026-11-27: "it's all going to be custom made by me") - a folder.
        "Audio",
        // Paper models (lane 5, 2026-11-28): the cut-out figures, each sheet's index and paper-matches.json - a folder.
        Paper.PaperStore.ShippedRelativeFolder,
    };

    /// <summary>Copies this PC's settings into the shipped files. Returns how many models' settings it holds (-1: not the repo).</summary>
    public static int Harvest()
    {
        if (!SharedModelSettings.IsAuthority) return -1;
        int n = SharedModelSettings.Harvest();
        PictureFraming.Harvest();
        CopyIfThere(ModelMatches.LocalPath(), ModelMatches.ShippedPath);
        CopyIfThere(Path.Combine(WarhammerTerrain.ModelsFolder, WarhammerTerrainLibrary.FileName), WarhammerTerrainLibrary.ShippedPath);
        Paper.PaperShipping.Harvest(Paper.PaperStore.LocalRoot, Paper.PaperStore.ShippedRoot);
        return n;
    }

    private static void CopyIfThere(string from, string to)
    {
        try
        {
            if (!File.Exists(from)) return;
            if (File.Exists(to) && File.ReadAllText(to) == File.ReadAllText(from)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Harvests, then commits just the shipped files and pushes them (the repo's git, on Kai's PC). Returns a line
    /// for the screen. Runs git, so call it off the main thread.
    /// </summary>
    public static string Publish()
    {
        if (!SharedModelSettings.IsSourceTree) return "Only works on the PC that runs the game from the repo - this copy is a download.";
        if (!SharedModelSettings.TryClaim(out string owner))
            return $"The settings come from {owner}. Send them from that computer (changes made here stay on this computer).";
        int n = Harvest();
        SharedModelSettings.Flush();
        string repo = Path.GetFullPath(Path.Combine(AppAssets.Root, ".."));
        string[] paths = ShippedFiles.Select(f => Path.GetRelativePath(repo, Path.Combine(AppAssets.AssetsFolder, f)).Replace('\\', '/'))
            .Where(p => File.Exists(Path.Combine(repo, p)) || Directory.Exists(Path.Combine(repo, p))).ToArray();
        if (paths.Length == 0) return "Nothing to send yet.";

        var add = Git(repo, new[] { "add", "--" }.Concat(paths));
        if (add.Code != 0) return "Couldn't add the files: " + add.Text;
        var diff = Git(repo, new[] { "diff", "--cached", "--quiet", "--" }.Concat(paths));
        if (diff.Code == 0) return $"Already up to date on GitHub ({n} models).";
        var commit = Git(repo, new[] { "commit", "-m", $"Asset Lab settings from {Environment.MachineName} ({n} models)", "--" }.Concat(paths));
        if (commit.Code != 0) return "Couldn't commit: " + commit.Text;
        var push = Git(repo, new[] { "push" });
        if (push.Code != 0)
        {
            // The other computer (or Claude) pushed something newer: bring it in, then push again.
            var pull = Git(repo, new[] { "pull", "--no-rebase", "--no-edit" });
            if (pull.Code != 0) return "Saved on this PC, but GitHub had newer changes and the pull stopped - send Claude a screenshot. " + pull.Text;
            push = Git(repo, new[] { "push" });
            if (push.Code != 0) return "Saved on this PC, but the push failed - send Claude a screenshot. " + push.Text;
        }
        return $"Sent to GitHub: settings for {n} models, picture framing, model and terrain matches, terrain pieces, paper models, your audio. Pull on the other computer to get them.";
    }

    private static (int Code, string Text) Git(string repo, IEnumerable<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(120_000);
            string last = o.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
            return (p.ExitCode, last);
        }
        catch (Exception e) { return (-1, "git isn't available: " + e.Message); }
    }
}
