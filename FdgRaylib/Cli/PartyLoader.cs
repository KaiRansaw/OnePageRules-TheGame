using FDG.ArmyBuilding;
using FDG.Quest;
using FDG.SaveLoad;
using FdgRaylib.Quest;
using FdgRaylib.Rendering;

namespace FdgRaylib.Cli;

/// <summary>
/// #413 - the Star Quest half of the headless switches: the party that plays slot 0.
///
/// <para>
/// Star Quest is the one game type whose local side is NOT an army list. It is 2-4
/// <see cref="QuestHero"/>es, each a one-model unit carrying its own hero sheet, and the enemy waves are
/// sized off the party's points. Hand it an army list instead and
/// <c>StarQuestHordeRules.Heroes</c> finds nothing with <c>IsQuestHero</c> set, so the very first
/// end-of-round check reports "every hero is down" and the mission ends in round 1 having exercised
/// nothing. That is exactly what <c>--game-system gfsq</c> did on its own before this existed.
/// </para>
///
/// <para>
/// Three ways to name a party, in the order a caller is likely to want them:
/// <list type="bullet">
/// <item><b>Generated</b> (<c>--party</c> with nothing after it, or <c>auto</c>): a party built here out
/// of the shipped Army Forge data - real base heroes, real classes, real starting skills. It needs no
/// files, so an automated run is reproducible on any checkout.</item>
/// <item><b>Named hero files</b>: one or more <c>.fdghero</c> paths.</item>
/// <item><b>A folder</b>: every <c>.fdghero</c> in it, in name order - so
/// <c>--party "heroes/GF Star Quest"</c> plays the heroes actually saved on this machine.</item>
/// </list>
/// </para>
/// </summary>
public static class PartyLoader
{
    /// <summary>GFSQ Core Rules v2.1.0: a party is 2 to 4 heroes.</summary>
    public const int MinHeroes = 2;
    public const int MaxHeroes = 4;

    /// <summary>The <c>--party</c> value that asks for the built-in generated party.</summary>
    public const string GeneratedKeyword = "auto";

    /// <summary>How many heroes the generated party has when nothing says otherwise.</summary>
    public const int DefaultGeneratedHeroes = 3;

    /// <summary>
    /// The faction the generated party is drawn from, first match wins. Any of these gives a party with
    /// a bundled army book behind it (so faction rules and spells attach) and a caster-worthy spell list.
    /// If none of them is in the shipped data, the faction with the most heroes is used instead - the
    /// point is a party that exists, not a particular flavour of one.
    /// </summary>
    private static readonly string[] PreferredFactions =
    {
        "Human Defense Force", "Battle Brothers", "Prime Brothers", "Alien Hives",
    };

    /// <summary>
    /// The classes a generated party takes, in order: a shooter, a melee specialist, a caster, then a
    /// scout. Two heroes get Soldier + Sentinel; three add the Psychic, which is what puts casting,
    /// spell tokens and a Wil-test skill into the run.
    /// </summary>
    private static readonly string[] GeneratedClasses = { "Soldier", "Sentinel", "Psychic", "Pathfinder" };

    /// <summary>
    /// The army file slot 0 plays: the party as hero units, with the faction's rule definitions and (for a
    /// caster) its spells folded in, exactly as the lobby's party picker builds it.
    /// </summary>
    /// <param name="paths">What <c>--party</c> named. Empty, or the single word "auto", generates one.</param>
    public static ArmyListFile Build(IReadOnlyList<string> paths)
    {
        List<QuestHero> heroes = Resolve(paths);
        List<BookFile> books = BookLibrary.Load();
        BuiltArmyFile party = QuestArmy.Build(heroes, faction =>
            books.FirstOrDefault(b => string.Equals(b.Faction, faction, StringComparison.OrdinalIgnoreCase)));

        Console.WriteLine($"Party: {heroes.Count} heroes, {party.TotalPoints} pts - "
            + string.Join(", ", heroes.Select(h => $"{h.Name} (Lvl {h.Level} {h.Class}, {h.Faction}, {h.Points} pts)")));
        return party;
    }

    /// <summary>
    /// The heroes <c>--party</c> names, validated. Anything wrong throws rather than playing a party the
    /// caller did not ask for: #413 settled that a scripted run which silently plays something else is
    /// worse than one that stops, because every result it prints afterwards is meaningless.
    /// </summary>
    public static List<QuestHero> Resolve(IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> tokens = Split(paths);
        if (tokens.Count == 0) return Generate(DefaultGeneratedHeroes);
        if (tokens.Count == 1 && IsGeneratedKeyword(tokens[0], out int count)) return Generate(count);

        var party = new List<QuestHero>();
        foreach (string token in tokens)
        {
            if (IsGeneratedKeyword(token, out _))
                throw new ArgumentException(
                    $"--party: '{token}' asks for a generated party, so it cannot be mixed with hero files.");

            if (Directory.Exists(token))
            {
                var inFolder = HeroPaths.LoadAllIn(token);
                if (inFolder.Count == 0)
                    throw new ArgumentException($"--party: no {QuestHero.FileExtension} files in '{token}'.");
                party.AddRange(inFolder.Select(e => e.Hero));
                continue;
            }

            if (!File.Exists(token))
                throw new ArgumentException($"--party: hero file not found: '{token}'.");

            string json;
            try { json = File.ReadAllText(token); }
            catch (IOException ex) { throw new ArgumentException($"--party: could not read '{token}': {ex.Message}"); }

            QuestHero hero = QuestHero.FromJson(json)
                ?? throw new ArgumentException($"--party: '{token}' is not a readable hero file.");
            hero.SourceFile = token;
            party.Add(hero);
        }

        // A folder may hold more heroes than a party may field. Truncating would quietly play a different
        // party than the one named, so say which heroes to pick instead of choosing for the caller.
        if (party.Count > MaxHeroes)
            throw new ArgumentException($"--party: {party.Count} heroes named, but a party is at most "
                + $"{MaxHeroes}. Name the ones you want: "
                + string.Join(", ", party.Select(h => h.Name)));
        if (party.Count < MinHeroes)
            throw new ArgumentException($"--party: {party.Count} hero(es) named, but a party is at least {MinHeroes}.");

        // A headless run must never write a hero's earned XP, injuries or level back over a saved file -
        // these runs exist to crash on purpose. QuestHeroSync is the renderer's, and it keys off
        // SourceFile, so clearing it here means a scripted party can never scribble on the real heroes
        // even if some later path starts calling it.
        foreach (QuestHero hero in party) hero.SourceFile = null;
        return party;
    }

    /// <summary>"auto" or "auto:3" - the generated party, optionally with a hero count.</summary>
    private static bool IsGeneratedKeyword(string token, out int count)
    {
        count = DefaultGeneratedHeroes;
        if (token.Equals(GeneratedKeyword, StringComparison.OrdinalIgnoreCase)) return true;
        if (!token.StartsWith(GeneratedKeyword + ":", StringComparison.OrdinalIgnoreCase)) return false;

        string tail = token[(GeneratedKeyword.Length + 1)..];
        if (!int.TryParse(tail, out count) || count < MinHeroes || count > MaxHeroes)
            throw new ArgumentException(
                $"--party: '{token}' - the hero count must be {MinHeroes} to {MaxHeroes}.");
        return true;
    }

    /// <summary>Accepts both "--party a.fdghero b.fdghero" and "--party a.fdghero,b.fdghero".</summary>
    private static IReadOnlyList<string> Split(IReadOnlyList<string> paths)
    {
        var tokens = new List<string>();
        foreach (string path in paths)
        foreach (string part in path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            tokens.Add(part.Trim('"'));
        return tokens;
    }

    /// <summary>
    /// A party built from the shipped Star Quest data: the preferred faction's base heroes, one per hero,
    /// each given one of <see cref="GeneratedClasses"/> with its class feature and its level-1 skills.
    /// Deterministic - the same checkout always produces the same party, which is what makes a scripted
    /// run worth comparing against the last one.
    /// </summary>
    public static List<QuestHero> Generate(int count)
    {
        count = Math.Clamp(count, MinHeroes, MaxHeroes);

        SqFaction? faction = PickFaction();
        List<SqClass> classes = PickClasses(count);
        if (faction == null || classes.Count < count)
        {
            Console.WriteLine("Star Quest data unavailable ("
                + (StarQuestData.LoadError ?? "no factions or classes in Assets/StarQuest")
                + ") - generating a plain stat-only party instead.");
            return Fallback(count);
        }

        var party = new List<QuestHero>();
        for (int i = 0; i < count; i++)
        {
            SqHero baseHero = faction.Heroes[i % faction.Heroes.Count];
            SqClass cls = classes[i];

            QuestHero hero = StarQuestData.NewHero(faction, baseHero);
            StarQuestData.ApplyClass(hero, cls);
            hero.Name = $"Test {cls.Name}";
            hero.GameType = HeroBuilderScreen.GameType;

            // Level 1 unlocks tier 0, so these are the skills the Hero Builder would offer on creation.
            foreach (SqSkill skill in StarQuestData.ClassSkills(cls, tier: 0).Take(Math.Max(0, hero.SkillSlots)))
                hero.Skills.Add(skill.Name);

            QuestProgression.SetToughRule(hero);
            party.Add(hero);
        }
        return party;
    }

    private static SqFaction? PickFaction()
    {
        IReadOnlyList<SqFaction> factions = StarQuestData.Factions;
        foreach (string name in PreferredFactions)
            if (factions.FirstOrDefault(f => string.Equals(f.Faction, name, StringComparison.OrdinalIgnoreCase)
                    && f.Heroes.Count > 0) is { } preferred)
                return preferred;
        return factions.Where(f => f.Heroes.Count > 0)
            .OrderByDescending(f => f.Heroes.Count).ThenBy(f => f.Faction, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static List<SqClass> PickClasses(int count)
    {
        var classes = new List<SqClass>();
        foreach (string name in GeneratedClasses)
        {
            if (classes.Count == count) break;
            if (StarQuestData.Classes.FirstOrDefault(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } cls)
                classes.Add(cls);
        }
        // A renamed or missing class must not shrink the party below what was asked for.
        foreach (SqClass cls in StarQuestData.Classes)
        {
            if (classes.Count == count) break;
            if (!classes.Contains(cls)) classes.Add(cls);
        }
        return classes;
    }

    /// <summary>
    /// The last resort, for a build whose Assets/StarQuest folder is missing or broken: heroes with
    /// hand-written stats and no class, so Star Quest is still playable headlessly. Mirrors the engine's
    /// own Star Quest fixtures rather than inventing a third shape.
    /// </summary>
    private static List<QuestHero> Fallback(int count)
    {
        var party = new List<QuestHero>();
        for (int i = 0; i < count; i++)
        {
            var hero = new QuestHero
            {
                Name = $"Test Hero {i + 1}",
                GameType = HeroBuilderScreen.GameType,
                Faction = "Test",
                BaseHeroName = "Leader",
                BaseCost = 60,
                Class = "Soldier",
                Quality = 4, Defense = 4, Tough = 9, Power = 4,
                Strength = 4, Dexterity = 4, Willpower = 5,
                Weapons =
                {
                    new QuestWeapon { Name = "Master Rifle", Range = 24, Attacks = 3, Rules = { "AP(1)" } },
                    new QuestWeapon { Name = "CCW", Range = 0, Attacks = 2 },
                },
            };
            QuestProgression.SetToughRule(hero);
            party.Add(hero);
        }
        return party;
    }
}
