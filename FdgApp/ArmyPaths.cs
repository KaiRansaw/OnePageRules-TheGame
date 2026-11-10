namespace FdgRaylib;

/// <summary>
/// Where the .fdgarmy file dialogs open by default. Every army Save/Load in the app (lobby, Army Builder,
/// Army Forge, share-link import) starts in the "armies" folder rather than dumping the user in the
/// application root, which is full of DLLs and nothing they'd ever pick.
///
/// <para>#402: the folder has subfolders (the shipped layout splits it into <c>GDF/</c> and <c>AoF/</c>),
/// so this is where the Load/Save dialogs OPEN, not where the armies necessarily are. The lobby's
/// <see cref="ArmyCatalog"/> scan recurses; nothing reads the folder names.</para>
///
/// <para>Two candidates, in order: beside the executable (the shipped layout - <c>scripts/build-dist.sh</c>
/// copies the repo's <c>armies/</c> next to the binary) and under the working directory (a
/// <c>dotnet run</c> from the repo root, where the output folder is several levels down from
/// <c>armies/</c>). Neither existing falls back to "", i.e. the previous behavior.</para>
/// </summary>
public static class ArmyPaths
{
    /// <summary>Folder name looked for beside the executable and under the working directory.</summary>
    public const string DirectoryName = "armies";

    /// <summary>
    /// A default path for the TinyDialogs open/save dialogs, or "" when no armies folder was found.
    /// tinyfiledialogs reads a trailing separator as "open this directory with no preset file name", so
    /// the Save dialogs still get an empty name box the user types into.
    /// </summary>
    public static string DefaultDialogPath =>
        FolderPath is { } dir ? dir + Path.DirectorySeparatorChar : "";

    /// <summary>The armies folder, or null if neither candidate location exists.</summary>
    public static string? FolderPath =>
        FindIn(AppAssets.Root, Directory.GetCurrentDirectory()) ?? FindAbove(AppAssets.Root);

    /// <summary>Walks up from <paramref name="start"/> looking for an armies folder. Covers running from
    /// Visual Studio (F5), where the exe and working directory are bin/Debug/net8.0, several levels below
    /// the repo's armies/ folder.</summary>
    internal static string? FindAbove(string start)
    {
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>First <paramref name="roots"/> entry with an armies subfolder, or null. Takes the roots as
    /// an argument so the search order is testable without moving the process working directory.</summary>
    internal static string? FindIn(params string[] roots)
    {
        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The armies sub-folder for a Game Type. Grimdark Future and Age of Fantasy keep the shipped
    /// GDF/ and AoF/ folders; the skirmish and quest games get their own.</summary>
    public static string FolderNameFor(FDG.EGameSystem system) => system switch
    {
        FDG.EGameSystem.AgeOfFantasy => "AoF",
        FDG.EGameSystem.GFFirefight => "GF Firefight",
        FDG.EGameSystem.AoFSkirmish => "AoF Skirmish",
        FDG.EGameSystem.GFStarQuest => "GF Star Quest",
        FDG.EGameSystem.AoFQuest => "AoF Quest",
        _ => "GDF",
    };

    /// <summary>
    /// #447 - the folder campaign armies live in, as a subfolder of <c>armies/</c>: campaign armies go
    /// in <c>armies/Campaigns/&lt;Game Type&gt;/</c> and ordinary armies stay in
    /// <c>armies/&lt;Game Type&gt;/</c>.
    ///
    /// <para>
    /// Kai, 2026-11-03: <i>"I want to have the Campaign Armies in a different folder... so NO Campaign
    /// armies will show up in any other game modes, and vice versa."</i> He is right, and it is a
    /// correctness point rather than tidiness: a campaign army carries XP, levels, injuries and a
    /// treasury, so dropping it into a one-off game plays a list the book never priced, and an ordinary
    /// army cannot be used mid-campaign because it has no sheets.
    /// </para>
    ///
    /// <para>
    /// <b>The folder is the organisation, not the guarantee.</b> A campaign army is kept out of ordinary
    /// games by its own contents - <see cref="ArmyCatalog.ReadEntry"/> rejects any file with a
    /// <c>strikeTeam</c> block - so one sitting in an old location, or moved by hand, is still invisible
    /// to the lobby. Relying on the path alone would have made every campaign army already on disk a
    /// bug that fixed itself only if the player tidied up.
    /// </para>
    ///
    /// <para>
    /// Standard and Horde deliberately SHARE a game's ordinary folder. Horde is a mode, not a system,
    /// and shares that mode's army-builder stats (CLAUDE.md), so a GF army is legal in both - splitting
    /// them would force every army to be built twice for no rules reason.
    /// </para>
    /// </summary>
    public const string CampaignsDirectoryName = "Campaigns";

    /// <summary>
    /// A Game Type's campaign-army subfolder, relative to <see cref="FolderPath"/> - e.g.
    /// <c>Campaigns/GDF</c>. Keeps each campaign system's armies apart from every other's, which is the
    /// second half of what Kai asked for: a Grimdark Future campaign must not list an AoF campaign army.
    /// </summary>
    public static string CampaignFolderNameFor(FDG.EGameSystem system) =>
        Path.Combine(CampaignsDirectoryName, FolderNameFor(system));

    /// <summary>Makes any missing Game Type folder under armies/, so there's an obvious place to put
    /// each game's lists. Silently does nothing when there is no armies folder or it can't be written.</summary>
    public static void EnsureGameTypeFolders()
    {
        if (FolderPath is not { } root) return;
        foreach (FDG.EGameSystem system in Enum.GetValues<FDG.EGameSystem>())
        {
            try { Directory.CreateDirectory(Path.Combine(root, FolderNameFor(system))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            // #447 - and the campaign subfolder, so the split is visible on disk before a campaign is
            // ever created rather than appearing the first time one is saved.
            if (!FDG.Campaign.CampaignNumbers.HasCampaignBook(system)) continue;
            try { Directory.CreateDirectory(Path.Combine(root, CampaignFolderNameFor(system))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The catalog entries that live in (or under) a Game Type's folder.</summary>
    public static IEnumerable<ArmyCatalogEntry> EntriesFor(FDG.EGameSystem system, IEnumerable<ArmyCatalogEntry> all)
    {
        if (FolderPath is not { } root) return Enumerable.Empty<ArmyCatalogEntry>();
        string folder = Path.GetFullPath(Path.Combine(root, FolderNameFor(system))) + Path.DirectorySeparatorChar;
        return all.Where(e => Path.GetFullPath(e.Path).StartsWith(folder, StringComparison.OrdinalIgnoreCase));
    }
}
