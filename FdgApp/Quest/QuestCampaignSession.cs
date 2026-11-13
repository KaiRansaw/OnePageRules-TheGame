using FDG;
using FDG.ArmyBuilding;
using FDG.Quest;
using FdgRaylib.Rendering;
using IOPath = System.IO.Path;

namespace FdgRaylib.Quest;

/// <summary>
/// A campaign mission waiting to be played: what the Quest Campaigns screen hands the lobby when PLAY NEXT MISSION
/// is pressed (Kai's decision 20, mockup approved 2026-11-12). The heroes are already readied on disk (the chapter's
/// difficulty and the mission's objectives written onto their sheets, which is how the engine's Mission mode reads
/// them); this record is the lobby's half - who sits in the party seat, which army the enemy seat gets, and the words
/// for the header and the locked settings.
/// </summary>
public sealed record QuestCampaignLaunch(
    EGameSystem System,
    string CampaignPath,
    string CampaignName,
    int ChapterNumber,
    int MissionNumber,
    int MissionsInChapter,
    int Difficulty,
    int Primary,
    int Secondary,
    string EnemyName,
    string? EnemyArmyFile,
    IReadOnlyList<string> Party)
{
    /// <summary>"Hive Run - Chapter 2, mission 2".</summary>
    public string Title => $"{CampaignName} - Chapter {ChapterNumber}, mission {MissionNumber}";

    /// <summary>"Intermediate - Retrieval".</summary>
    public string Line => $"{QuestJournal.DifficultyName(Difficulty)} - " +
                          (Primary > 0 ? QuestJournal.PrimaryName(Primary) : "primary rolled at the start");
}

/// <summary>
/// The Quest Campaigns screen without the drawing (Campaign Builder v2.1.0; GFSQ and AoFQ): the campaign list for a
/// Quest game, the party as it stands, starting chapters, readying the next mission, deleting, making a new
/// campaign (name, party, structure) and the journal export. Ported from the Raylib CampaignScreen so the Godot
/// screen and any later front end share one set of rules. The campaign's own rules (structure editing, recording
/// results) live on <see cref="QuestCampaign"/> in the engine.
/// </summary>
public sealed class QuestCampaignSession
{
    public EGameSystem System { get; }
    public string GameType => QuestGameTypes.For(System);
    public string MoneyWord => QuestItemSkin.CurrencyWord(System);
    public string MoneySuffix => QuestItemSkin.CurrencySuffix(System);

    public List<(string Path, QuestCampaign Campaign)> Campaigns { get; private set; } = new();
    private readonly Random _rng;

    /// <summary>The mission PLAY NEXT MISSION readied, for the next lobby to pick up (once).</summary>
    public static QuestCampaignLaunch? Pending { get; set; }

    public QuestCampaignSession(EGameSystem system, Random? rng = null)
    {
        System = system;
        _rng = rng ?? new Random();
        Reload();
    }

    public void Reload() => Campaigns = CampaignPaths.LoadAll(GameType);

    public static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    // ------------------------------------------------------------------ reading a campaign

    /// <summary>The party's hero files with their sheets as they are now (null = the file is gone).</summary>
    public static List<(string File, QuestHero? Hero)> Party(QuestCampaign c) =>
        c.HeroFiles.Select(f => (f, CampaignPaths.LoadHero(f))).ToList();

    /// <summary>"Chapter 2 of 4 - mission 2 of 3", "Between chapters - 1 of 3 done", "Complete - 5 chapters".</summary>
    public static string ProgressLine(QuestCampaign c)
    {
        if (c.Finished) return $"Complete - {Plural(c.Chapters.Count, "chapter")}";
        if (c.Active is { } a) return $"Chapter {a.Number} of {c.Chapters.Count} - mission {Math.Min(a.MissionsWon + 1, a.Missions)} of {a.Missions}";
        return $"Between chapters - {c.Chapters.Count(ch => ch.Done)} of {c.Chapters.Count} done";
    }

    /// <summary>The header line under a campaign's name.</summary>
    public static string StatusLine(QuestCampaign c)
    {
        if (c.Finished) return "Campaign complete";
        if (c.Active is { } a)
            return $"Chapter {a.Number} - {QuestJournal.DifficultyName(a.Difficulty)} - mission {Math.Min(a.MissionsWon + 1, a.Missions)} of {a.Missions} next";
        return "Between chapters - pick the next one";
    }

    /// <summary>Every mission in order, for the progress bar: won, the next one, or still to play.</summary>
    public static List<EMissionMark> Marks(QuestCampaign c)
    {
        var marks = new List<EMissionMark>();
        CampaignMission? next = c.NextMission;
        foreach (CampaignChapter ch in c.Chapters)
            for (int m = 0; m < ch.MissionPlan.Count; m++)
            {
                bool won = ch.Done || (ReferenceEquals(ch, c.Active) && m < ch.MissionsWon);
                marks.Add(won ? EMissionMark.Won : ReferenceEquals(ch.MissionPlan[m], next) ? EMissionMark.Next : EMissionMark.ToPlay);
            }
        return marks;
    }

    public enum EMissionMark { ToPlay, Won, Next }

    /// <summary>The marks for one chapter's missions.</summary>
    public static List<EMissionMark> Marks(QuestCampaign c, int chapter)
    {
        CampaignChapter ch = c.Chapters[chapter];
        CampaignMission? next = c.NextMission;
        return ch.MissionPlan.Select((m, i) =>
            ch.Done || (ReferenceEquals(ch, c.Active) && i < ch.MissionsWon) ? EMissionMark.Won
            : ReferenceEquals(m, next) ? EMissionMark.Next : EMissionMark.ToPlay).ToList();
    }

    public enum EChapterState { NotStarted, InProgress, Done }

    public static EChapterState StateOf(QuestCampaign c, int chapter) =>
        c.Chapters[chapter].Done ? EChapterState.Done : chapter == c.CurrentChapter ? EChapterState.InProgress : EChapterState.NotStarted;

    /// <summary>The chapter's enemy as words: "Battle Brothers (whole book)", a saved army's name, or "pick when it starts".</summary>
    public static string EnemyLabel(CampaignChapter ch)
    {
        if (ch.EnemyBook.Length > 0) return ch.EnemyName.Length > 0 ? ch.EnemyName : IOPath.GetFileNameWithoutExtension(ch.EnemyBook);
        if (ch.EnemyArmy.Length == 0) return "Pick when it starts";
        string name = ch.EnemyName.Length > 0 ? ch.EnemyName : IOPath.GetFileNameWithoutExtension(ch.EnemyArmy);
        return File.Exists(ch.EnemyArmy) ? name : name + " (file missing)";
    }

    /// <summary>Whether a chapter still needs its enemy picked before it can be played.</summary>
    public static bool NeedsEnemy(CampaignChapter ch) =>
        ch.EnemyBook.Length == 0 && (ch.EnemyArmy.Length == 0 || !File.Exists(ch.EnemyArmy));

    /// <summary>The shop opens before and after each chapter, never between its missions (book).</summary>
    public static bool ShopOpen(QuestCampaign c) => c.Active == null && !c.Finished;

    /// <summary>Why PLAY NEXT MISSION can't be pressed, or null.</summary>
    public static string? CantPlay(QuestCampaign c)
    {
        if (c.Finished) return "The campaign is complete.";
        if (c.Active is not { } ch) return "Start a chapter first.";
        if (NeedsEnemy(ch)) return "Pick this chapter's enemy army first (Structure).";
        if (Party(c).Any(p => p.Hero == null)) return "A hero's file is missing - the party can't be put together.";
        return null;
    }

    /// <summary>"Ch 2, mission 2 - Retrieval vs Battle Brothers".</summary>
    public static string NextLine(QuestCampaign c)
    {
        if (c.Active is not { } ch || c.NextMission is not { } m) return "";
        string obj = m.Primary > 0 ? QuestJournal.PrimaryName(m.Primary) : "objective rolled at the start";
        return $"Ch {ch.Number}, mission {ch.MissionsWon + 1} - {obj} vs {EnemyLabel(ch)}";
    }

    // ------------------------------------------------------------------ chapters and missions

    /// <summary>Starts a chapter at a difficulty (1-4): the party goes in fresh. Null, or why it could not be saved.</summary>
    public string? StartChapter(string path, QuestCampaign c, int index, int difficulty, string? enemyBook = null)
    {
        if (!c.CanStart(index)) return "That chapter can't be started now.";
        if (enemyBook != null) SetEnemyBook(c.Chapters[index], enemyBook);
        c.StartChapter(index, Math.Clamp(difficulty, 1, 4));
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null) continue;
            hero.CampaignName = c.Name;
            hero.CampaignDifficulty = c.Chapters[index].Difficulty;
            hero.WoundsTaken = 0;
            hero.PowerLeft = -1;
            hero.LastOutcome = "";
            CampaignPaths.SaveHero(file, hero);
        }
        bool ok = CampaignPaths.Save(path, c);
        Reload();
        return ok ? null : "Couldn't save the campaign file.";
    }

    /// <summary>Gives a chapter a whole army book as its enemy.</summary>
    public void SetEnemyBook(CampaignChapter ch, string bookFileName)
    {
        QuestEnemyCollections.BookChoice? b = QuestEnemyCollections.BooksFor(System).FirstOrDefault(x => x.FileName == bookFileName);
        if (b == null) return;
        ch.EnemyBook = b.FileName;
        ch.EnemyArmy = "";
        ch.EnemyName = $"{b.Faction} (whole book)";
    }

    /// <summary>Gives a chapter one of the player's saved armies as its enemy.</summary>
    public static void SetEnemyArmy(CampaignChapter ch, string path, string name)
    {
        ch.EnemyArmy = path;
        ch.EnemyBook = "";
        ch.EnemyName = name;
    }

    /// <summary>Leaves the chapter's enemy to be picked when it starts.</summary>
    public static void ClearEnemy(CampaignChapter ch) { ch.EnemyArmy = ""; ch.EnemyBook = ""; ch.EnemyName = ""; }

    /// <summary>
    /// PLAY NEXT MISSION: readies the heroes (the chapter's difficulty, the mission's planned objectives - the engine's
    /// Mission mode reads them off the sheets) and returns what the lobby needs. Null when the mission can't be played
    /// (see <see cref="CantPlay"/>). Also sets <see cref="Pending"/>.
    /// </summary>
    public QuestCampaignLaunch? PrepareNextMission(string path, QuestCampaign c)
    {
        if (CantPlay(c) != null || c.Active is not { } ch) return null;
        CampaignMission? mission = c.NextMission;
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null) continue;
            hero.CampaignName = c.Name;
            hero.CampaignDifficulty = ch.Difficulty;
            hero.CampaignPrimary = mission?.Primary ?? 0;
            hero.CampaignSecondary = mission?.Secondary ?? 0;
            CampaignPaths.SaveHero(file, hero);
        }
        var launch = new QuestCampaignLaunch(System, path, c.Name, ch.Number, ch.MissionsWon + 1, ch.Missions, ch.Difficulty,
            mission?.Primary ?? 0, mission?.Secondary ?? 0, EnemyLabel(ch), ChapterEnemyArmy(c, ch), c.HeroFiles.ToList());
        Pending = launch;
        return launch;
    }

    /// <summary>
    /// The army file the chapter's enemy seat gets: a saved army it names, or - for a chapter set to an army book - the
    /// whole book built fresh now, so it always matches the current book. Null when the file or book has gone missing.
    /// </summary>
    public string? ChapterEnemyArmy(QuestCampaign c, CampaignChapter ch)
    {
        if (ch.EnemyBook.Length > 0)
        {
            string outDir = IOPath.Combine(CampaignPaths.FolderFor(GameType), "enemy forces",
                IOPath.GetFileNameWithoutExtension(HeroPaths.FileNameFor(c.Name)));
            return QuestEnemyCollections.WriteFromBook(System, ch.EnemyBook, outDir);
        }
        return ch.EnemyArmy.Length > 0 && File.Exists(ch.EnemyArmy) ? ch.EnemyArmy : null;
    }

    /// <summary>Deletes a campaign; its heroes are kept, with everything they carry, and are free again.</summary>
    public string? Delete(string path, QuestCampaign c)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Couldn't delete the campaign file."; }
        foreach (var (file, hero) in Party(c))
        {
            if (hero == null || hero.CampaignName != c.Name) continue;
            hero.CampaignName = "";
            hero.CampaignDifficulty = 0;
            hero.CampaignPrimary = 0;
            hero.CampaignSecondary = 0;
            hero.WoundsTaken = 0;
            hero.PowerLeft = -1;
            CampaignPaths.SaveHero(file, hero);
        }
        Reload();
        return null;
    }

    public static bool Save(string path, QuestCampaign c) => CampaignPaths.Save(path, c);

    // ------------------------------------------------------------------ a new campaign

    public enum EBuild { Generate, Craft, Saved }

    /// <summary>Every saved hero of this Quest game, with the unfinished campaign it already belongs to (a hero
    /// belongs to one campaign - Kai, 2026-11-07).</summary>
    public List<(string Path, QuestHero Hero, QuestCampaign? TakenBy)> HeroesForNew()
    {
        var campaigns = Campaigns.Select(e => e.Campaign).ToList();
        return HeroPaths.LoadAll(GameType).Select(h => (h.Path, h.Hero, CampaignPaths.CampaignOf(GameType, h.Path, campaigns))).ToList();
    }

    /// <summary>Why the New Campaign page can't move on yet, or null.</summary>
    public string? NewProblem(string name, IReadOnlyCollection<string> party)
    {
        if (name.Trim().Length == 0) return "Give the campaign a name.";
        if (Campaigns.Any(c => string.Equals(c.Campaign.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))) return "That name is taken.";
        if (party.Count < 2) return "Pick at least 2 heroes.";
        if (party.Count > 4) return "A party is 2 to 4 heroes.";
        return null;
    }

    /// <summary>The structure a new campaign starts from: the book's roll, one empty chapter, or a saved plan.</summary>
    public QuestCampaign Draft(string name, IEnumerable<string> party, EBuild how, QuestCampaign? plan = null)
    {
        QuestCampaign draft = how == EBuild.Craft ? QuestCampaign.CreateEmpty(name.Trim(), party) : QuestCampaign.Create(name.Trim(), party, _rng);
        draft.GameType = GameType;
        draft.History.Clear();
        if (how == EBuild.Saved && plan != null) draft.ApplyPlan(plan);
        else LeaveSecondariesToRoll(draft);
        return draft;
    }

    /// <summary>Re-rolls a draft the book's way (only before anything is played).</summary>
    public bool Reroll(QuestCampaign draft)
    {
        if (draft.HasStarted || !draft.Generate(_rng)) return false;
        LeaveSecondariesToRoll(draft);
        return true;
    }

    /// <summary>
    /// Book (Campaign Builder v2.1.0): "After the primary objective is completed, you must set up a random secondary
    /// objective". A generated plan rolls only the primaries; the secondary is rolled in play unless the player picks one
    /// on the Structure screen (house rule B20).
    /// </summary>
    private static void LeaveSecondariesToRoll(QuestCampaign c)
    {
        foreach (CampaignChapter ch in c.Chapters)
            foreach (CampaignMission m in ch.MissionPlan) m.Secondary = 0;
    }

    /// <summary>Rolls one mission's primary objective, a D6 on the book's table (the secondary is rolled in play).</summary>
    public void RollMission(CampaignMission m) => m.Primary = _rng.Next(1, 7);

    /// <summary>Saves a new campaign. Returns its path, or null with the reason.</summary>
    public string? Create(QuestCampaign draft, string name, IEnumerable<string> party, out string? error)
    {
        error = draft.StructureProblem();
        if (error != null) return null;
        draft.Name = name.Trim();
        draft.HeroFiles = party.ToList();
        draft.History.Clear();
        draft.History.Add($"Campaign started: {Plural(draft.Chapters.Count, "chapter")}, {Plural(draft.Chapters.Sum(x => x.Missions), "mission")}.");
        string path = CampaignPaths.FileFor(GameType, draft.Name);
        if (!CampaignPaths.Save(path, draft)) { error = "Couldn't save the campaign file."; return null; }
        Reload();
        return path;
    }

    public List<(string Path, QuestCampaign Plan)> Plans() => CampaignPaths.LoadPlans(GameType);

    public string? SavePlan(QuestCampaign c) => CampaignPaths.SavePlan(GameType, c);

    /// <summary>Writes the whole journal to "&lt;campaign&gt; - Journal.txt" beside the campaign's file. The file, or null.</summary>
    public string? ExportJournal(string path, QuestCampaign c, out string? error)
    {
        error = null;
        CampaignPaths.Save(path, c);
        var names = Party(c).Select(p => p.Hero?.Name ?? IOPath.GetFileNameWithoutExtension(p.File));
        string text = QuestJournal.Export(c, System, names);
        string file = IOPath.Combine(IOPath.GetDirectoryName(path) ?? "", IOPath.GetFileNameWithoutExtension(path) + " - Journal.txt");
        try { File.WriteAllText(file, text.Replace("\n", Environment.NewLine)); return file; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = e.Message; return null; }
    }

    // ------------------------------------------------------------------ the party as an army

    /// <summary>
    /// The party as the army its lobby seat plays: each hero a unit, its faction's book for its rules (the Quest
    /// system's own edition first, #414). Null when no hero file could be read.
    /// </summary>
    public static BuiltArmyFile? PartyArmy(EGameSystem system, IEnumerable<string> heroFiles)
    {
        // Each sheet remembers its file, so the game can write what the hero earned back to it (QuestHeroSync).
        var heroes = new List<QuestHero>();
        foreach (string file in heroFiles)
            if (CampaignPaths.LoadHero(file) is { } h) { h.SourceFile = IOPath.GetFullPath(file); heroes.Add(h); }
        if (heroes.Count == 0) return null;
        var books = BookLibrary.Load();
        return QuestArmy.Build(heroes, faction => QuestArmy.BookForHeroFaction(books, faction, system), system);
    }

    /// <summary>The newest record of the campaign's last played mission (for the end-of-mission card).</summary>
    public static (CampaignChapter Chapter, int Mission, CampaignMissionRecord Record)? LastRecord(QuestCampaign c)
    {
        (CampaignChapter, int, CampaignMissionRecord)? best = null;
        DateTime bestAt = DateTime.MinValue;
        foreach (CampaignChapter ch in c.Chapters)
            for (int m = 0; m < ch.MissionPlan.Count; m++)
                foreach (CampaignMissionRecord r in ch.MissionPlan[m].Records)
                {
                    DateTime at = DateTime.TryParse(r.PlayedAt, out DateTime d) ? d : DateTime.MinValue;
                    if (best == null || at >= bestAt) { best = (ch, m, r); bestAt = at; }
                }
        return best;
    }
}
