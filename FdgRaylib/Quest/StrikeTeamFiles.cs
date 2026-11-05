using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Serialization;

namespace FdgRaylib.Quest;

/// <summary>
/// Campaign armies on disk. A campaign army is an ordinary army file with a <c>StrikeTeam</c> block,
/// kept in its own game system's armies folder so the lobby lists it like any army of that system.
/// After a game the host writes each one's result back to its file (casualty rolls, XP, level ups, VP
/// and points). Only armies whose file is on this computer are updated.
/// <para>
/// #426: this used to serve one folder, <c>armies\GF Firefight</c>, because the campaign screen only
/// offered Firefight. All four systems with a Campaign Rules v3.6.0 book of their own are served now,
/// each from its own folder, and a campaign army's own <c>StrikeTeam.GameSystem</c> says which price
/// list it plays under.
/// </para>
/// </summary>
public static class StrikeTeamFiles
{
    private static readonly object WriteLock = new();
    private static ITableState? _lastTable;

    /// <summary>
    /// The systems the campaign screen offers: the four with a Campaign Rules v3.6.0 book. Star Quest
    /// and AoF: Quest have their own campaign (the Quest Campaign Builder, on its own screen) and are
    /// deliberately absent.
    /// </summary>
    public static IReadOnlyList<EGameSystem> CampaignSystems { get; } =
        Enum.GetValues<EGameSystem>().Where(CampaignNumbers.HasCampaignBook).ToList();

    /// <summary>A game system's ORDINARY armies folder, or null when there is no armies folder at all.
    /// This is where lists built in the Army Forge live, and where a campaign's starting army is picked
    /// from. Campaign armies go in <see cref="CampaignFolderFor"/>.</summary>
    public static string? FolderFor(EGameSystem system) =>
        ArmyPaths.FolderPath is { } root ? Path.Combine(root, ArmyPaths.FolderNameFor(system)) : null;

    /// <summary>
    /// #447 - a game system's CAMPAIGN armies folder (<c>armies/Campaigns/&lt;Game Type&gt;/</c>), where
    /// new campaign armies are written. See <see cref="ArmyPaths.CampaignsDirectoryName"/> for why they
    /// are kept apart.
    /// </summary>
    public static string? CampaignFolderFor(EGameSystem system) =>
        ArmyPaths.FolderPath is { } root ? Path.Combine(root, ArmyPaths.CampaignFolderNameFor(system)) : null;

    /// <summary>
    /// Both folders a campaign army of this system may be in: its own folder, and the system's ordinary
    /// folder, which is where every campaign army created before #447 still sits.
    ///
    /// <para>
    /// <b>Nothing is moved.</b> Campaigns already on disk keep working exactly where they are - they are
    /// read from the old location and only new ones are written to the new one. Auto-moving a player's
    /// files to tidy the layout is not worth the risk of doing it to the wrong file, and the behaviour
    /// Kai actually asked for (campaign armies staying out of other modes) is delivered by
    /// <see cref="ArmyCatalog.ReadEntry"/> regardless of where a file sits.
    /// </para>
    /// </summary>
    private static IEnumerable<string> CampaignSearchFolders(EGameSystem system)
    {
        if (CampaignFolderFor(system) is { } own) yield return own;
        if (FolderFor(system) is { } legacy) yield return legacy;
    }

    public static BuiltArmyFile? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options) : null; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static bool Save(string path, BuiltArmyFile army)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(army, RuleJson.Options)); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Every army file belonging to one system: campaign armies and ordinary armies alike, from both the
    /// campaign folder and the system's ordinary one (#447). The caller separates them by whether the
    /// file has a <c>StrikeTeam</c> block, which it has always done.
    ///
    /// <para>
    /// De-duplicated by full path, because the two search folders are nested under the same root and a
    /// future layout change could overlap them - listing one army twice would offer it twice as a
    /// starting army and create two campaigns from one file.
    /// </para>
    /// </summary>
    public static List<(string Path, BuiltArmyFile Army)> LoadAll(EGameSystem system)
    {
        var list = new List<(string, BuiltArmyFile)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dir in CampaignSearchFolders(system))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.fdgarmy", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(Path.GetFullPath(file))) continue;
                if (Load(file) is { } army) list.Add((file, army));
            }
        }
        return list;
    }

    /// <summary>
    /// Every army file across all four campaign systems, each carrying the folder it came from. The
    /// folder decides the system, not the file: an ordinary army has nothing on it that says which game
    /// it was built for, and a campaign army's own <c>StrikeTeam.GameSystem</c> is set from this at
    /// creation.
    /// </summary>
    public static List<(string Path, BuiltArmyFile Army, EGameSystem System)> LoadAllSystems()
    {
        var list = new List<(string, BuiltArmyFile, EGameSystem)>();
        foreach (EGameSystem system in CampaignSystems)
            foreach ((string path, BuiltArmyFile army) in LoadAll(system))
            {
                // Stamp it, so everything downstream can read the block instead of re-deriving the
                // folder - and so a pre-3.6 campaign army is corrected the next time it is saved.
                if (army.StrikeTeam is { } info) info.GameSystem = system;
                list.Add((path, army, system));
            }
        return list;
    }

    /// <summary>
    /// A file name for a new campaign army of this system that isn't taken yet, in that system's own
    /// campaign folder (#447) rather than beside its ordinary armies.
    /// </summary>
    public static string NewPath(EGameSystem system, string teamName)
    {
        string dir = CampaignFolderFor(system) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(dir);
        string safe = string.Concat(teamName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safe.Length == 0) safe = "Campaign Army";
        string path = Path.Combine(dir, $"Campaign - {safe}.fdgarmy");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"Campaign - {safe} {n}.fdgarmy");
        return path;
    }

    /// <summary>
    /// Which campaign price list a campaign army file plays under: <b>the folder it sits in decides</b>.
    /// A campaign army lives in its own system's armies folder, which is also how the lobby decides
    /// which armies a game of that system accepts, so the location is the one fact that cannot go
    /// stale. The <c>StrikeTeam.GameSystem</c> block is the fallback for a file kept somewhere else -
    /// and reading the folder first is what migrates a campaign army written before that field
    /// existed, which would otherwise deserialize to Grimdark Future and be paid GF's rewards for a
    /// Firefight mission.
    /// </summary>
    public static EGameSystem SystemForPath(string path, StrikeTeamInfo? info)
    {
        // #447 - BOTH of a system's folders, its own campaign folder included. Checking only the
        // ordinary folder would leave every campaign army written under the new layout falling through
        // to the fallback below, and a campaign army with no GameSystem block would then be paid
        // Firefight's rewards for a Grimdark Future mission - which is exactly the bug #426 fixed.
        foreach (EGameSystem system in CampaignSystems)
        {
            foreach (string folder in CampaignSearchFolders(system))
            {
                string full = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(path).StartsWith(full, StringComparison.OrdinalIgnoreCase)) return system;
            }
        }
        return info?.GameSystem ?? EGameSystem.GFFirefight;
    }

    /// <summary>
    /// After a game (host only): every campaign army that played gets its result. It wins when its
    /// player is on the winning side; a tie counts as a loss for everyone. Returns a short summary for
    /// the game-over card, or null when no campaign army played.
    /// </summary>
    public static string? WriteBack(ITableState? table, GameResult result)
    {
        if (table == null || result.Outcome is EGameOutcome.Disconnect or EGameOutcome.Fault) return null;
        lock (WriteLock)
        {
            if (ReferenceEquals(table, _lastTable)) return null;   // once per game
            _lastTable = table;
        }
        var lines = new List<string>();
        var groups = table.Units.Objects.OfType<UnitData>()
            .Select(u => (Unit: u, Sheet: StrikeUnit.FromJson(u.StrikeUnitJson)))
            .Where(x => x.Sheet?.TeamFile != null)
            .GroupBy(x => x.Sheet!.TeamFile!)
            .ToList();

        // #428: the roster ceiling the bot spends against is the biggest NON-bot campaign army that
        // played - it will not build past what it could ever legally bring to a table. Read before any
        // army is written, so a bot does not measure itself against a rival that today's casualties
        // have already shrunk.
        int rivalValue = groups
            .Select(g => Load(g.Key))
            .Where(a => a is { StrikeTeam: { IsBot: false } })
            .Select(a => a!.TotalPoints)
            .DefaultIfEmpty(0)
            .Max();

        // #450 - every campaign army's name, so each one's journal can say who the mission was fought
        // against. Read before anything is written, like rivalValue above.
        var names = groups.ToDictionary(g => g.Key, g => Load(g.Key)?.StrikeTeam?.Name ?? "");

        foreach (var group in groups)
        {
            if (Load(group.Key) is not { StrikeTeam: not null } team) continue;
            bool won = result.Outcome == EGameOutcome.Win && group.Any(x => result.WinnerPlayers.Contains(x.Unit.PlayerID));
            var results = group.Select(x => new CampaignUnitResult(x.Sheet!.Id, !x.Unit.GetIsAlive(), x.Sheet.Kills, x.Sheet.HeroKills)).ToList();
            var rng = new Random();
            // #426: the army's own system's price list, not Firefight's for everyone. Paying a Grimdark
            // Future campaign Firefight's +80/+160 was the real reason the campaign screen only ever
            // offered Firefight - the write-back could not have served anything else correctly.
            CampaignNumbers n = CampaignNumbers.For(SystemForPath(group.Key, team.StrikeTeam));
            List<string> log = CampaignRules.AfterGame(team, results, won, n, () => rng.Next(1, 7));

            // #450 - who this mission was against, at the top of its journal record. Only campaign
            // armies are named: a one-off opponent the lobby rolled has no campaign file to name it by.
            int mission = team.StrikeTeam.Matches;
            var against = names.Where(kv => kv.Key != group.Key && kv.Value.Length > 0).Select(kv => kv.Value).ToList();
            CampaignJournal.AddToRecord(team.StrikeTeam, mission,
                against.Count > 0 ? $"Fought against {string.Join(" and ", against)}." : "Fought a one-off opponent.",
                first: true);

            // #430: the special objective it rolled before the mission, finally scored. Before this it
            // was shown to the player and then never checked, so the bonus could not be earned at all.
            // Scored before the objective is cleared, and paid by CompleteSpecialObjective so the
            // treasury write stays in one place.
            if (team.StrikeTeam.SpecialObjective is { Length: > 0 } objective)
            {
                PlayerID owner = group.First().Unit.PlayerID;
                CampaignObjectiveResult scored = CampaignObjectives.Score(table, owner, objective);
                string line = scored.Completed
                    ? CampaignPreGame.CompleteSpecialObjective(team, objective, n) + $" ({scored.Detail})"
                    : $"{team.StrikeTeam.Name} did not complete its special objective ({objective}): {scored.Detail}";
                log.Add(line);
                // AfterGame has ALREADY written this match's history entry from the log as it stood, so
                // anything added after it needs its own entry - otherwise the player would never see the
                // verdict, which is the whole point of scoring it.
                team.StrikeTeam.History.Add(line);
                CampaignJournal.AddToRecord(team.StrikeTeam, mission, line);
                // A new objective is rolled before the next mission, so this one is done either way.
                team.StrikeTeam.SpecialObjective = "";
            }

            // #428: the bot has nobody to sit at its campaign screen, so it does its own steps 5 and 6
            // here - take the choices waiting on its sheets, then spend its treasury. Run after
            // AfterGame so it is spending the reward it just earned, which is what a player would do.
            if (team.StrikeTeam.IsBot)
            {
                List<string> spent = CampaignBot.BetweenMissions(team, team.Book, n, rivalValue,
                    () => rng.Next(1, 7));
                log.AddRange(spent);
                // Its own history, for the same reason: AfterGame's entry was written before this ran.
                if (spent.Count > 0) team.StrikeTeam.History.Add(string.Join(" ", spent));
            }

            if (!Save(group.Key, team)) continue;
            if (team.StrikeTeam.IsBot)
            {
                lines.Add($"The enemy {team.StrikeTeam.Name} {(won ? "won" : "lost")} too: it banked "
                    + $"{team.StrikeTeam.BankedPoints} pts and is now worth {team.TotalPoints} pts.");
                continue;
            }

            int pending = team.Units.Sum(u => CampaignRules.Sheet(u)?.Pending.Count ?? 0);
            lines.Add($"Campaign army {team.StrikeTeam.Name}: {(won ? "won" : "lost")}, {log.Count(l => l.Contains("levels up"))} level up(s)"
                + (pending > 0 ? $", {pending} choice(s) to make" : "") + " - see Campaign.");
        }
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }
}
