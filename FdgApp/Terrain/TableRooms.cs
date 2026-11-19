using Newtonsoft.Json.Linq;

namespace FdgRaylib.Terrain;

/// <summary>
/// Rooms around the battle table (Kai, 2026-11-14: "make it look like we're in a room" - "a few of each"): 360-degree
/// photos of real rooms from Poly Haven (CC0), drawn all around the table so it looks like it stands in that room from
/// every camera angle. Three kinds - a cozy room, a grimdark one, a fantasy one - three of each.
///
/// <para>Downloaded on the player's computer (each is about 20 MB) into a <c>Rooms</c> folder beside UnitImages; the
/// Settings screen picks one, or Auto: a grimdark room for the Grimdark Future games and a fantasy one for Age of Fantasy,
/// whichever of those is downloaded. Poly Haven's API asks that players can see the rooms come from Poly Haven - the
/// Settings screen and the Credits say so.</para>
/// </summary>
public static class TableRooms
{
    public const string FolderName = "Rooms";
    public const string Api = "https://api.polyhaven.com";
    public const string Resolution = "4k";
    public const string Auto = "auto";
    public const string None = "";
    /// <summary>No room: the land goes on past the table to the map's horizon (Kai, 2026-11-18 - the Total War look).</summary>
    public const string Battlefield = "battlefield";

    public enum EKind { Cozy, Grimdark, Fantasy }

    /// <summary>One room: its Poly Haven id (polyhaven.com/a/&lt;id&gt;), the name shown, and its kind.</summary>
    public sealed record Room(string Id, string Name, EKind Kind)
    {
        public string Page => "https://polyhaven.com/a/" + Id;
    }

    /// <summary>Every id checked against its Poly Haven page (2026-11-14).</summary>
    public static readonly IReadOnlyList<Room> All = new Room[]
    {
        new("lythwood_room", "Lythwood Room", EKind.Cozy),
        new("pine_attic", "Pine Attic", EKind.Cozy),
        new("workshop", "Workshop", EKind.Cozy),
        new("abandoned_factory_canteen_01", "Abandoned Factory Canteen", EKind.Grimdark),
        new("teufelsberg_inner", "Teufelsberg Ruin", EKind.Grimdark),
        new("peppermint_powerplant_2", "Powerplant", EKind.Grimdark),
        new("small_cathedral", "Small Cathedral", EKind.Fantasy),
        new("drachenfels_cellar", "Drachenfels Cellar", EKind.Fantasy),
        new("historic_cloister_passage", "Cloister Passage", EKind.Fantasy),
    };

    public static Room? ById(string? id) => All.FirstOrDefault(r => r.Id == id);

    /// <summary>The Rooms folder: beside UnitImages (the repo root in a dev build, the game folder when shipped).</summary>
    public static string Folder => Path.Combine(Path.GetDirectoryName(Models3D.OfficialPictures.Root)!, FolderName);

    public static string FileFor(Room r, string? folder = null) => Path.Combine(folder ?? Folder, $"{r.Id}_{Resolution}.hdr");

    public static bool Have(Room r, string? folder = null) => File.Exists(FileFor(r, folder));

    /// <summary>
    /// The room to draw: the one picked, or for Auto a downloaded room of the game's kind (grimdark for the Grimdark
    /// Future games, fantasy for Age of Fantasy). Null = no room (the plain dark background).
    /// </summary>
    public static Room? Pick(string? setting, FDG.EGameSystem? system, string? folder = null)
    {
        if (string.IsNullOrEmpty(setting) || setting == Battlefield) return null;
        if (setting != Auto) return ById(setting) is { } r && Have(r, folder) ? r : null;
        if (system == null) return null;
        bool fantasy = system.Value is FDG.EGameSystem.AgeOfFantasy or FDG.EGameSystem.AoFSkirmish or FDG.EGameSystem.AoFQuest;
        EKind kind = fantasy ? EKind.Fantasy : EKind.Grimdark;
        return All.FirstOrDefault(r => r.Kind == kind && Have(r, folder));
    }

    /// <summary>Downloads the rooms not on this computer yet. Returns (saved, failed, first failure).</summary>
    public static async Task<(int Saved, int Failed, string? Error)> DownloadAsync(IEnumerable<Room> rooms, Action<string>? progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // Poly Haven's API asks every app for its own User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OnePageRules-TheGame/1.0");
        Directory.CreateDirectory(Folder);
        int saved = 0, failed = 0;
        string? error = null;
        foreach (Room r in rooms)
        {
            ct.ThrowIfCancellationRequested();
            if (Have(r)) continue;
            try
            {
                progress?.Invoke($"{r.Name}: finding the file...");
                JObject files = JObject.Parse(await http.GetStringAsync($"{Api}/files/{r.Id}", ct));
                string url = (string?)files["hdri"]?[Resolution]?["hdr"]?["url"] ?? throw new IOException("Poly Haven has no " + Resolution + " file for it");
                progress?.Invoke($"{r.Name}: downloading...");
                string part = FileFor(r) + ".part";
                await using (var src = await http.GetStreamAsync(url, ct))
                await using (var dst = File.Create(part))
                    await src.CopyToAsync(dst, ct);
                File.Move(part, FileFor(r), true);
                saved++;
                progress?.Invoke($"{r.Name}: saved.");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                error ??= $"{r.Name}: {e.Message}";
                progress?.Invoke($"{r.Name}: failed - {e.Message}");
            }
        }
        return (saved, failed, error);
    }
}
