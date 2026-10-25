using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Serialization;

namespace FdgRaylib.Quest;

/// <summary>
/// GF: Firefight Campaign strike teams on disk. A strike team is an army file in armies\GF Firefight with a
/// StrikeTeam block, so the lobby lists it like any Firefight army. After a game the host writes each strike
/// team's result back to its file (casualty rolls, XP, level ups, VP and points). Only teams whose file is on
/// this computer are updated.
/// </summary>
public static class StrikeTeamFiles
{
    private static readonly object WriteLock = new();
    private static ITableState? _lastTable;

    public static string? Folder =>
        ArmyPaths.FolderPath is { } root ? Path.Combine(root, ArmyPaths.FolderNameFor(EGameSystem.GFFirefight)) : null;

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

    /// <summary>Every army file in the Firefight folder: strike teams and ordinary armies.</summary>
    public static List<(string Path, BuiltArmyFile Army)> LoadAll()
    {
        var list = new List<(string, BuiltArmyFile)>();
        if (Folder is not { } dir || !Directory.Exists(dir)) return list;
        foreach (string file in Directory.EnumerateFiles(dir, "*.fdgarmy", SearchOption.AllDirectories).OrderBy(f => f))
            if (Load(file) is { } army) list.Add((file, army));
        return list;
    }

    /// <summary>A file name for a new strike team that isn't taken yet.</summary>
    public static string NewPath(string teamName)
    {
        string dir = Folder ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(dir);
        string safe = string.Concat(teamName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safe.Length == 0) safe = "Strike Team";
        string path = Path.Combine(dir, $"Strike Team - {safe}.fdgarmy");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"Strike Team - {safe} {n}.fdgarmy");
        return path;
    }

    /// <summary>
    /// After a game (host only): every strike team that played gets its result. A team wins when its player is
    /// on the winning side; a tie counts as a loss for everyone. Returns a short summary for the game-over card,
    /// or null when no strike team played.
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
            .GroupBy(x => x.Sheet!.TeamFile!);
        foreach (var group in groups)
        {
            if (Load(group.Key) is not { StrikeTeam: not null } team) continue;
            bool won = result.Outcome == EGameOutcome.Win && group.Any(x => result.WinnerPlayers.Contains(x.Unit.PlayerID));
            var results = group.Select(x => new StrikeUnitResult(x.Sheet!.Id, !x.Unit.GetIsAlive(), x.Sheet.Kills, x.Sheet.HeroKills)).ToList();
            var rng = new Random();
            List<string> log = StrikeTeamRules.AfterGame(team, results, won, () => rng.Next(1, 7));
            if (!Save(group.Key, team)) continue;
            int pending = team.Units.Sum(u => StrikeTeamRules.Sheet(u)?.Pending.Count ?? 0);
            lines.Add($"Strike team {team.StrikeTeam.Name}: {(won ? "won" : "lost")}, {log.Count(l => l.Contains("levels up"))} level up(s)"
                + (pending > 0 ? $", {pending} choice(s) to make" : "") + " - see Firefight Campaign.");
        }
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }
}
