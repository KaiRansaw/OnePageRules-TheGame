using System.Text;

namespace FdgRaylib.Audio;

/// <summary>Which mixer channel a sound slot plays on - one Settings slider each.</summary>
public enum ESoundChannel { Music, Announcer, Cues, Ambience, Menu }

/// <summary>
/// One place the game can make a sound (Kai, 2026-11-27: "the infrastructure for announcements, music, etc., built now,
/// before I have the audio"). A slot is a name the game asks for - <c>announcer/round-3</c>, <c>music/battle-grimdark</c> -
/// and the files that answer it come from Kai's own audio folder first (<see cref="SoundLibrary"/>), then the built-in
/// file shipped with the game, then the slot's <see cref="Fallback"/>. A slot with nothing behind it is silent, so every
/// slot can exist before its sound does.
/// </summary>
/// <param name="Id">The slot's name, which is also its path in the audio folder (<c>announcer/round-3</c> is
/// <c>Assets/Audio/announcer/round-3.ogg</c>, or any files inside <c>Assets/Audio/announcer/round-3/</c>).</param>
/// <param name="Channel">The slider it plays under.</param>
/// <param name="When">When it plays, in plain words (the audio folder's README and Settings -> Sound slots).</param>
/// <param name="BuiltIn">Files shipped with the game for this slot (paths under the Godot project, no extension:
/// <c>sounds/cues/victory</c>). Empty for slots that wait for Kai's audio.</param>
/// <param name="Fallback">Another slot to use when this one has no files - <c>announcer/round-3</c> falls back to
/// <c>announcer/round</c>, so one generic "Next round" line covers every round until numbered ones are added.</param>
public sealed record SoundSlot(string Id, ESoundChannel Channel, string When, IReadOnlyList<string> BuiltIn, string? Fallback = null);

/// <summary>Every sound slot in the game. The game asks only for slots listed here (a test holds it to that).</summary>
public static class SoundSlots
{
    /// <summary>How many numbered round lines exist (announcer/round-1 ... round-N). OPR games are 4 rounds; Prolonged
    /// Battle and big custom games can run longer.</summary>
    public const int NumberedRounds = 10;

    private static readonly string[] None = Array.Empty<string>();

    private static SoundSlot S(string id, ESoundChannel ch, string when, string? fallback = null, params string[] builtIn) =>
        new(id, ch, when, builtIn, fallback);

    /// <summary>All slots, grouped by channel in the order the README lists them.</summary>
    public static readonly IReadOnlyList<SoundSlot> All = Build();

    private static readonly Dictionary<string, SoundSlot> ById = All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    public static SoundSlot? Find(string id) => ById.TryGetValue(id, out SoundSlot? s) ? s : null;

    private static List<SoundSlot> Build()
    {
        var m = ESoundChannel.Music;
        var a = ESoundChannel.Announcer;
        var c = ESoundChannel.Cues;
        var list = new List<SoundSlot>
        {
            // ── Music. A slot with several files is a playlist: shuffled, one after another, crossfaded. ──
            S("music/menu-grimdark", m, "Menus while the Grimdark Future look is on.", null, "sounds/music/menu_grimdark"),
            S("music/menu-fantasy", m, "Menus while the Age of Fantasy look is on.", null, "sounds/music/menu_fantasy"),
            S("music/lobby-grimdark", m, "A Grimdark Future game's lobby, before launch.", "music/menu-grimdark"),
            S("music/lobby-fantasy", m, "An Age of Fantasy game's lobby, before launch.", "music/menu-fantasy"),
            S("music/battle-grimdark", m, "In battle - Grimdark Future, Firefight, Star Quest."),
            S("music/battle-fantasy", m, "In battle - Age of Fantasy, Skirmish, Quest."),
            S("music/battle-final-round-grimdark", m, "The last round of a Grimdark Future battle.", "music/battle-grimdark"),
            S("music/battle-final-round-fantasy", m, "The last round of an Age of Fantasy battle.", "music/battle-fantasy"),
            S("music/horde-grimdark", m, "Horde Mode battles (Grimdark Future games).", "music/battle-grimdark"),
            S("music/horde-fantasy", m, "Horde Mode battles (Age of Fantasy games).", "music/battle-fantasy"),
            S("music/quest-grimdark", m, "Star Quest missions.", "music/battle-grimdark"),
            S("music/quest-fantasy", m, "AoF: Quest missions.", "music/battle-fantasy"),
            S("music/campaign-grimdark", m, "Campaign screens between battles (Grimdark Future games).", "music/menu-grimdark"),
            S("music/campaign-fantasy", m, "Campaign screens between battles (Age of Fantasy games).", "music/menu-fantasy"),
            S("music/victory", m, "The game-over screen when your side won (plays once)."),
            S("music/defeat", m, "The game-over screen when your side lost (plays once)."),
            S("music/draw", m, "The game-over screen after a draw, or when watching bots (plays once).", "music/victory"),

            // ── Announcer. One line at a time; music dips while it speaks. ──
            S("announcer/battle-start", a, "The battle begins (round 1 starts)."),
            S("announcer/deployment", a, "Deployment starts."),
            S("announcer/round", a, "A new round starts (any round without its own numbered line)."),
        };
        for (int r = 1; r <= NumberedRounds; r++)
            list.Add(S($"announcer/round-{r}", a, $"Round {r} starts.", "announcer/round"));
        list.AddRange(new[]
        {
            S("announcer/final-round", a, "The last round starts (plays instead of the numbered line)."),
            S("announcer/your-turn", a, "Play passes to you from another player (hot-seat and online games)."),
            S("announcer/objective-seized", a, "Your side takes an objective."),
            S("announcer/objective-lost", a, "An enemy takes one of your objectives."),
            S("announcer/enemy-unit-destroyed", a, "An enemy unit is wiped out."),
            S("announcer/unit-lost", a, "One of your units is wiped out."),
            S("announcer/hero-down", a, "Star Quest / Quest: one of your heroes goes down.", "announcer/unit-lost"),
            S("announcer/unit-routed", a, "A unit fails morale and routs."),
            S("announcer/wave-incoming", a, "Horde Mode: a new wave arrives."),
            S("announcer/objective-destroyed", a, "Horde Mode: an objective is destroyed."),
            S("announcer/prolonged-battle", a, "Prolonged Battle: the fight goes on for another round."),
            S("announcer/victory", a, "Your side wins."),
            S("announcer/defeat", a, "Your side loses."),
            S("announcer/draw", a, "The game ends in a draw."),

            // ── Game cues (short sounds). ──
            S("cues/dice", c, "Dice are rolled.", null, "sounds/dice/dice_1", "sounds/dice/dice_2", "sounds/dice/dice_3", "sounds/dice/die_1", "sounds/dice/die_2"),
            S("cues/battle-start", c, "The battle begins.", null, "sounds/cues/round_start"),
            S("cues/round-over", c, "A round ends.", null, "sounds/cues/round_over"),
            S("cues/objective-seized", c, "An objective is taken.", null, "sounds/cues/objective_seized"),
            S("cues/objective-lost", c, "You lose an objective.", null, "sounds/cues/objective_lost"),
            S("cues/unit-destroyed", c, "A unit is wiped out.", null, "sounds/cues/unit_destroyed"),
            S("cues/morale-shaken", c, "A unit fails morale and is Shaken.", null, "sounds/cues/morale_shaken"),
            S("cues/morale-routed", c, "A unit fails morale and routs.", null, "sounds/cues/morale_routed"),
            S("cues/victory", c, "The game ends and your side won (or bots finished).", null, "sounds/cues/victory"),
            S("cues/defeat", c, "The game ends and your side lost.", null, "sounds/cues/defeat"),

            // ── Battle ambience (loops under everything). ──
            S("ambience/war", ESoundChannel.Ambience, "Grimdark Future battles: wind with distant rumbles of war.", null, "sounds/ambience/war"),
            S("ambience/wind", ESoundChannel.Ambience, "Age of Fantasy battles: wind.", null, "sounds/ambience/wind"),
            S("ambience/rain", ESoundChannel.Ambience, "Added on rain, heavy rain and storm maps.", null, "sounds/ambience/rain"),

            // ── Menu sounds (built-in ones are made by the game, not files). ──
            S("menu/hover", ESoundChannel.Menu, "The mouse or controller cursor moves onto a button. Empty: the game's own soft tick."),
            S("menu/click", ESoundChannel.Menu, "A button is pressed. Empty: the game's own click."),
        });
        return list;
    }

    /// <summary>The slots of one channel, in README order.</summary>
    public static IEnumerable<SoundSlot> Of(ESoundChannel channel) => All.Where(s => s.Channel == channel);

    public static string ChannelName(ESoundChannel c) => c switch
    {
        ESoundChannel.Music => "Music",
        ESoundChannel.Announcer => "Announcer",
        ESoundChannel.Cues => "Game cues",
        ESoundChannel.Ambience => "Ambience",
        _ => "Menu sounds",
    };

    /// <summary>The README written into the audio folder: what to name a file so it plays, and every slot.</summary>
    public static string ReadmeText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("YOUR AUDIO FOLDER");
        sb.AppendLine("=================");
        sb.AppendLine();
        sb.AppendLine("Drop sound files in here and the game plays them. No code, no restart needed for most of them");
        sb.AppendLine("(Settings -> Sound -> Sound slots... -> Rescan picks up new files straight away).");
        sb.AppendLine();
        sb.AppendLine("HOW TO NAME THEM");
        sb.AppendLine("  Each line below is a slot. Its name is where the file goes:");
        sb.AppendLine("    announcer/round-3      ->  announcer/round-3.ogg  (one file)");
        sb.AppendLine("                           or  announcer/round-3/<any names>.ogg  (several - one is picked at random,");
        sb.AppendLine("                               never the same one twice in a row; for music, a shuffled playlist)");
        sb.AppendLine("  Formats: .ogg (best), .mp3, .wav.");
        sb.AppendLine("  Your file wins over the game's built-in sound for the same slot.");
        sb.AppendLine("  An empty slot uses the slot named after \"else\", or stays silent.");
        sb.AppendLine();
        sb.AppendLine("These files ship with the game: Asset Lab -> Send my settings to GitHub sends them, and every download");
        sb.AppendLine("(your laptop, your friends) plays them.");
        sb.AppendLine();
        foreach (ESoundChannel ch in Enum.GetValues<ESoundChannel>())
        {
            sb.AppendLine($"{ChannelName(ch).ToUpperInvariant()}  (Settings -> Sound -> {ChannelName(ch)})");
            foreach (SoundSlot s in Of(ch))
            {
                string extra = s.BuiltIn.Count > 0 ? "  [has a built-in sound]" : "";
                string fb = s.Fallback != null ? $"  else {s.Fallback}" : "";
                sb.AppendLine($"  {s.Id,-38} {s.When}{fb}{extra}");
            }
            if (ch == ESoundChannel.Music)
                sb.AppendLine($"  {"music/army/<army name>",-38} An army's own battle music, played when YOU field that army (e.g. music/army/alien-hives/). Beats the battle slots above; lower case, spaces as -.");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
