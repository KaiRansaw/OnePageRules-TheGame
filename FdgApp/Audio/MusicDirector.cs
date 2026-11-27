using FDG;

namespace FdgRaylib.Audio;

/// <summary>Where the player is, for picking music.</summary>
public enum EMusicScene { Menu, Lobby, Campaign, Battle, GameOver }

/// <summary>What the music director needs to know (2026-11-27). <paramref name="Result"/>: true won, false lost, null
/// draw / watching bots. <paramref name="MyArmies"/>: the army names this computer plays, for army themes.</summary>
public sealed record MusicContext(
    EMusicScene Scene,
    EGameSystem System,
    EGameMode Mode = EGameMode.Standard,
    bool FinalRound = false,
    bool? Result = null,
    IReadOnlyList<string>? MyArmies = null,
    bool BattleMusicOn = true);

/// <summary>
/// Which music slot plays where (2026-11-27). Asks the most specific slot first and takes the first one that has its own
/// files: in battle, the final round's music, then an army's own theme, then Horde / Quest music, then the family's
/// battle music. Nothing found = no music, which is how the game behaves until Kai adds tracks.
/// </summary>
public static class MusicDirector
{
    public static bool IsFantasy(EGameSystem s) => s is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish or EGameSystem.AoFQuest;

    public static bool IsQuest(EGameSystem s) => s is EGameSystem.GFStarQuest or EGameSystem.AoFQuest;

    /// <summary><c>music/army/alien-hives</c> from "Alien Hives".</summary>
    public static string ArmySlot(string army)
    {
        var chars = army.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        string slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return "music/army/" + slug;
    }

    /// <summary>The slots to try, most specific first (the last ones are the general fallbacks).</summary>
    public static List<string> Candidates(MusicContext c)
    {
        string fam = IsFantasy(c.System) ? "fantasy" : "grimdark";
        var list = new List<string>();
        switch (c.Scene)
        {
            case EMusicScene.Menu:
                list.Add($"music/menu-{fam}");
                break;
            case EMusicScene.Lobby:
                list.Add($"music/lobby-{fam}");
                list.Add($"music/menu-{fam}");
                break;
            case EMusicScene.Campaign:
                list.Add($"music/campaign-{fam}");
                list.Add($"music/menu-{fam}");
                break;
            case EMusicScene.Battle:
                if (!c.BattleMusicOn) break;
                if (c.FinalRound) list.Add($"music/battle-final-round-{fam}");
                foreach (string army in c.MyArmies ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(army)) list.Add(ArmySlot(army));
                if (c.Mode != EGameMode.Standard) list.Add($"music/horde-{fam}");
                if (IsQuest(c.System)) list.Add($"music/quest-{fam}");
                list.Add($"music/battle-{fam}");
                break;
            case EMusicScene.GameOver:
                list.Add(c.Result switch { true => "music/victory", false => "music/defeat", _ => "music/draw" });
                if (c.Result == null) list.Add("music/victory");
                break;
        }
        return list.Distinct().ToList();
    }

    /// <summary>The slot to play now: the first candidate with files of its own (Kai's, or built-in). Null = silence.</summary>
    public static string? Choose(MusicContext c) => Candidates(c).FirstOrDefault(HasOwn);

    /// <summary>Game-over music plays once; everything else loops as a playlist.</summary>
    public static bool Loops(EMusicScene scene) => scene != EMusicScene.GameOver;

    private static bool HasOwn(string id) =>
        SoundLibrary.UserFiles(id).Count > 0 || SoundSlots.Find(id) is { BuiltIn.Count: > 0 };
}
