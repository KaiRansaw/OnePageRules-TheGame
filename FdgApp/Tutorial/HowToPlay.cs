using FDG;
using FDG.ArmyBuilding;
using FdgRaylib.Rendering;

namespace FdgRaylib.Tutorial;

/// <summary>
/// How to Play (Kai, 2026-11-24, mockup "How to Play Mockups" - every screen kept): a practice battle with set armies
/// that teaches the core rules lesson by lesson, and the same lessons as a picture walkthrough (the Rules Guide).
/// This file is the content, with no drawing: the lessons, their steps, the armies and the table. The Godot screens
/// read it; any later front end reads it too.
///
/// <para>Kai's calls: both Grimdark Future and Age of Fantasy, each with its own armies (GF: Battle Brothers vs
/// Alien Hives; AoF: Human Empire vs Goblins); set dice (a step's rolls go the lesson's way); locked steps (only the
/// step's action works); drawn diagrams in the guide; a 12th lesson on common special rules. Only the free Core
/// Rules are taught, so it works in the free release too.</para>
///
/// <para>Rule text is never written here: a lesson names the book sections it teaches (<see cref="RuleText"/>), and
/// those are the book's own words. What is written here is the coach - plain words on what to click and why.</para>
/// </summary>
public static class HowToPlay
{
    /// <summary>The games How to Play teaches.</summary>
    public static readonly IReadOnlyList<EGameSystem> Systems = new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy };

    public static bool Teaches(EGameSystem system) => Systems.Contains(system);

    /// <summary>How many rounds the practice battle lasts - the book's game length.</summary>
    public const int Rounds = 4;

    /// <summary>The practice table (a map file shipped with the game).</summary>
    public static string MapPath => Path.Combine(AppAssets.AssetsFolder, "Tutorial", "practice-table.fdgterrain");

    /// <summary>Seed for every roll a step does not set, so the practice battle plays the same way every time.</summary>
    public const int DiceSeed = 2411;

    // ------------------------------------------------------------------ the armies

    /// <summary>The five parts of the two armies a step can point at.</summary>
    public enum Role { MyShooters, MyMelee, MyHero, FoeShooters, FoeMelee }

    /// <summary>One game's armies: the book and the unit for each role (the hero joins the melee unit).</summary>
    public sealed record Armies(string MyBook, string FoeBook, IReadOnlyDictionary<Role, string> Units)
    {
        public string Name(Role role) => Units[role];
    }

    public static Armies ArmiesFor(EGameSystem system) => system switch
    {
        EGameSystem.AgeOfFantasy => new Armies("Human Empire", "Goblins", new Dictionary<Role, string>
        {
            [Role.MyShooters] = "Marksmen", [Role.MyMelee] = "Infantrymen", [Role.MyHero] = "Battle Master",
            [Role.FoeShooters] = "Shooters", [Role.FoeMelee] = "Warriors",
        }),
        _ => new Armies("Battle Brothers", "Alien Hives", new Dictionary<Role, string>
        {
            [Role.MyShooters] = "Battle Brothers", [Role.MyMelee] = "Assault Brothers", [Role.MyHero] = "Master Brother",
            [Role.FoeShooters] = "Shooter Grunts", [Role.FoeMelee] = "Assault Grunts",
        }),
    };

    /// <summary>This game's own army book of that name (each game has its own books - never another game's).</summary>
    public static BookFile? Book(EGameSystem system, string name)
    {
        string? slug = GameSystems.CampaignBookSlug(system);
        return BookLibrary.Load().FirstOrDefault(b => GameSystems.SameSystem(b.GameSystem, slug)
            && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Your army and the enemy's, built from the current Army Forge books with no upgrades - the units exactly as the
    /// book lists them. Null with why when a book or unit is missing (a book refresh renamed something).
    /// </summary>
    public static (BuiltArmyFile Mine, BuiltArmyFile Foe)? BuildArmies(EGameSystem system, out string? error)
    {
        error = null;
        Armies a = ArmiesFor(system);
        BuiltArmyFile? mine = Build(system, a.MyBook, new[] { (a.Name(Role.MyShooters), (string?)null), (a.Name(Role.MyMelee), null), (a.Name(Role.MyHero), a.Name(Role.MyMelee)) }, ref error);
        BuiltArmyFile? foe = Build(system, a.FoeBook, new[] { (a.Name(Role.FoeShooters), (string?)null), (a.Name(Role.FoeMelee), null) }, ref error);
        return mine != null && foe != null ? (mine, foe) : null;
    }

    private static BuiltArmyFile? Build(EGameSystem system, string bookName, (string Unit, string? Joins)[] units, ref string? error)
    {
        if (Book(system, bookName) is not { } book) { error = $"The {bookName} army book is missing."; return null; }
        var list = new BuilderList { Name = $"How to Play - {book.Name}", BookName = book.Name };
        var ids = new Dictionary<string, string>();
        foreach ((string unit, string? _) in units)
        {
            if (book.Units.FirstOrDefault(u => string.Equals(u.Name, unit, StringComparison.OrdinalIgnoreCase)) is not { } entry)
            { error = $"{book.Name} has no unit called {unit}."; return null; }
            string id = "htp" + ids.Count;
            ids[unit] = id;
            list.Units.Add(new BuilderUnit { RosterUnitId = entry.Id, ModelCount = entry.BaseModelCount, Id = id });
        }
        foreach ((string unit, string? joins) in units)
            if (joins != null) list.Units.First(u => u.Id == ids[unit]).JoinsUnitId = ids[joins];
        BuiltArmyFile army = ListCompiler.Compile(book, list);
        list.PointsLimit = army.TotalPoints;
        army.PointsLimit = army.TotalPoints;
        return army;
    }

    // ------------------------------------------------------------------ lessons

    /// <summary>What a step asks for. <see cref="Info"/> is read-only: the coach talks, Next goes on.</summary>
    public enum Ask { Info, PickEdge, Deploy, Hold, Advance, Rush, Charge, Shoot, ToObjective, FreePlay }

    /// <summary>Whose dice go which way during a step (set dice - Kai, 2026-11-24).</summary>
    public enum Luck { None, Shooting, Melee }

    /// <summary>A spot on the table the step wants the unit's middle in.</summary>
    public sealed record Spot(float X, float Z, float Radius);

    /// <param name="Coach">What the coach says, in plain words. {tokens} are filled per game (see <see cref="Fill"/>).</param>
    /// <param name="Do">What the player must do.</param>
    /// <param name="Unit">Which unit does it (null = any of yours).</param>
    /// <param name="Target">Which enemy it should be aimed at, when there is a choice (any enemy is allowed if that one can't be).</param>
    /// <param name="Zone">Where the unit must end up.</param>
    /// <param name="OrElse">Actions allowed instead when <paramref name="Do"/> can't be done right now (out of reach, nothing to shoot).</param>
    /// <param name="OrElseCoach">What the coach says then.</param>
    public sealed record Step(string Coach, Ask Do = Ask.Info, Role? Unit = null, Role? Target = null, Spot? Zone = null,
        Luck Luck = Luck.None, IReadOnlyList<Ask>? OrElse = null, string? OrElseCoach = null)
    {
        public bool IsInfo => Do == Ask.Info;
    }

    /// <param name="RuleIds">The book sections taught (ids in <see cref="RuleText"/>), shown word for word.</param>
    /// <param name="Picture">The Rules Guide diagram for this lesson.</param>
    /// <param name="InThisGame">The Rules Guide's "how to click it here" line - not a rule.</param>
    public sealed record Lesson(int Number, string Title, string Summary, IReadOnlyList<string> RuleIds, IReadOnlyList<Step> Steps,
        string Picture, string InThisGame);

    /// <summary>
    /// The twelve lessons, in the order the practice battle reaches them (and the Rules Guide's chapters). Lesson 12
    /// lists the special rules your own units carry, which differ per game (<see cref="SpecialRuleIds"/>).
    /// </summary>
    public static IReadOnlyList<Lesson> Lessons(EGameSystem system) => new List<Lesson>
    {
        new(1, "Dice and Quality tests", "How a roll works: a 6 always works, a 1 always fails.",
            new[] { "quality-tests", "modifiers", "most-important-rule" },
            new Step[]
            {
                new("Every roll in this game uses six-sided dice. Most of them are Quality tests: roll one die per attack, and every die that hits the unit's Quality or higher is a success. Your {shooters} are Quality {shootersQ}+, so a {shootersQ} or better is a success."),
                new("Two rules beat everything else: a 6 is always a success, and a 1 is always a failure - no matter what bonuses or penalties are on the roll. You'll see every die you roll in the dice popup and the battle report."),
            },
            "dice", "Every roll pops up on the table and goes into the battle report. Hover a report line to see each die."),

        new(2, "The table and objectives", "Where you fight, and what wins the game.",
            new[] { "battlefield", "mission" },
            new Step[]
            {
                new("You're playing on a 4' x 4' table today. The book's table is 6' x 4', but a smaller one is fine as long as the two armies start at least 24\" apart."),
                new("See the three gold markers? Those are objectives - they're how you win. Normally both players take turns placing them; here they're already set up so you can get straight into it."),
                new("At the end of every round, if one of your units is within 3\" of a marker and no enemy is, you take it. After round 4, whoever holds the most markers wins - even if they've lost more units."),
            },
            "objectives", "Markers turn the colour of whoever holds them. The Mission panel (bottom right) keeps count."),

        new(3, "Deployment", "Putting your army on the table, one unit at a time.",
            new[] { "deployment" },
            new Step[]
            {
                new("You won the roll-off, so you choose which side of the table is yours. Take the near edge - the zone closest to you.", Ask.PickEdge),
                new("Now both sides take turns placing one unit at a time, within 12\" of their own edge. You go first: put your {shooters} in the green ring, behind the forest.", Ask.Deploy, Role.MyShooters, Zone: new(14, 10, 2f)),
                new("Next, your {melee}. Your {hero} is joining them - a Hero can deploy as part of a unit and fight alongside it. Put them in the green ring.", Ask.Deploy, Role.MyMelee, Zone: new(30, 8, 3f)),
            },
            "deployment", "Click inside your zone and the unit sets up there in formation. Drag single models to adjust, then press Space."),

        new(4, "Taking turns and actions", "Pick a unit, then pick what it does.",
            new[] { "playing-the-game", "activation" },
            new Step[]
            {
                new("The battle is played in rounds. In each round, you and the enemy take turns activating one unit at a time, until every unit has had a go. You won the roll-off, so you start."),
                new("When a unit activates, it picks one action: Hold (stay put and shoot), Advance (move 6\", then shoot), Rush (move 12\", no shooting) or Charge (move 12\" into a fight). Pick your {melee} and Rush them into the green ring.", Ask.Rush, Role.MyMelee, Zone: new(28, 19, 3f)),
            },
            "actions", "Pick a unit from your army list or on the table, then an action (keys 1-4, or the d-pad). Point where it goes, then Space."),

        new(5, "Moving and cover", "Keeping units together, and why the forest matters.",
            new[] { "movement", "cover", "difficult" },
            new Step[]
            {
                new("A unit moves as a group: every model has to stay within 1\" of another one, and within 9\" of all of them. The game keeps the formation together for you."),
                new("Your {shooters}' turn. Advance them into the forest (the green ring). If there's nothing to shoot yet, just End after the move.", Ask.Advance, Role.MyShooters, Zone: new(14, 15.5f, 2.5f)),
                new("Good spot. The forest is Cover: with most of the unit inside, your {shooters} get +1 to their Defense rolls when they're shot. It's also Difficult ground, so a move through it can't go further than 6\" - that's why the reach looks shorter there."),
            },
            "cover", "Hover any terrain to see its rules. The rings on the table show how far a unit can move."),

        new(6, "Shooting", "You roll to hit, they roll to block.",
            new[] { "shooting", "ap" },
            new Step[]
            {
                new("Round 2, and the enemy is getting close. Pick your {shooters}, choose Hold so they stay in cover, then shoot the {foeShooters} if you can see them.", Ask.Shoot, Role.MyShooters, Role.FoeShooters,
                    Luck: Luck.Shooting, OrElse: new[] { Ask.Advance }, OrElseCoach: "Nothing is in range from here. Advance your {shooters} up to 6\" toward the enemy, then shoot."),
                new("Here's what just happened. You rolled one die per attack to see what hit. The enemy rolled one die per hit to block it, needing their Defense value. Every hit they didn't block was a wound, and each wound took out a model. AP makes blocking harder: AP(1) means -1 to their block rolls."),
            },
            "shooting", "Pick a unit, choose Hold (or Advance), then pick an enemy. Hover an enemy first to see your odds."),

        new(7, "Charging and melee", "Into the fight: strike, strike back, see who won.",
            new[] { "melee" },
            new Step[]
            {
                new("Time to charge! Pick your {melee}, choose Charge, and pick the {foeMelee}. Your models run into contact and both sides fight with their melee weapons.", Ask.Charge, Role.MyMelee, Role.FoeMelee,
                    Luck: Luck.Melee, OrElse: new[] { Ask.Rush, Ask.Advance }, OrElseCoach: "Your {melee} can't reach an enemy from here (it has to be within 12\"). Rush them closer - they can charge next turn."),
                new("Melee works just like shooting: roll to hit, they roll to block. Then the unit you charged may strike back. Whoever took more wounds lost the fight."),
            },
            "melee", "Choose Charge, then pick the enemy: your unit moves into contact. The battle report shows the wounds and who won."),

        new(8, "Fatigue", "After a fight, units only hit on 6s for the rest of the round.",
            new[] { "fatigue" },
            new Step[]
            {
                new("Both units in that fight are now Fatigued until the round ends. If either fights again this round, it only hits on a 6 - and those 6s don't trigger special rules. So a second fight in the same round is a gamble."),
            },
            "fatigue", "Fatigued units show a Fatigued mark in the army list until the round ends."),

        new(9, "Morale and Shaken", "Losing a fight, running low, and having to sit still.",
            new[] { "morale-tests", "melee-results", "shaken" },
            new Step[]
            {
                new("The {foeMelee} lost that fight, so they had to take a Morale Test (a Quality test) - and they failed. A unit that fails becomes Shaken. If it's already at half strength or less, it Routs instead and is removed. Units also test at the end of any activation where wounds leave them at half strength or less."),
                new("A Shaken unit has to sit still: it can't move, shoot or take objectives, and if it's attacked it only fights back as if Fatigued. To recover, it spends one whole activation doing nothing."),
            },
            "morale", "Shaken units get a red frame and mark in the army list. Hover the mark for the rule."),

        new(10, "Heroes and Tough", "Leaders inside units, and models that take more than one wound.",
            new[] { "hero", "tough" },
            new Step[]
            {
                new("Your {hero} is a Hero. It joined the {melee} when you deployed, and it uses their Defense until the rest of the unit is gone. The unit can also use the Hero's Quality for Morale Tests."),
                new("Your {hero} is Tough(3), so it takes 3 wounds to kill, and wounds go on it last. The pips on its card show how many wounds it has left."),
            },
            "hero", "A joined Hero shows inside its unit's card. Pips show how many wounds each Tough model has left."),

        new(11, "Taking objectives and winning", "What you hold at the end of the round counts.",
            new[] { "mission" },
            new Step[]
            {
                new("Remember, you win on objectives, not kills. Move one of your units onto the nearest marker (the green ring). If no enemy is within 3\" of it when the round ends, it's yours.", Ask.ToObjective,
                    OrElse: new[] { Ask.Hold, Ask.Advance, Ask.Rush, Ask.Charge }, OrElseCoach: "This unit can't reach a marker this turn - do whatever you like with it, then try with another one."),
                new("Once you take a marker, it stays yours even after you walk away. You only lose it when an enemy unit is near it and none of yours are. If both sides are near it, nobody holds it."),
            },
            "seize", "The Mission panel shows how many markers each side holds. They're checked at the end of every round."),

        new(12, "Special rules", "The extra rules printed on your units.",
            new[] { "special-rules" },
            new Step[]
            {
                new("Units and weapons also have special rules - the words in brackets on their cards. Your army's rules are below, in the book's own words. In any game, hover a rule on a card to read it."),
                new("That's all the core rules - you're ready. Play the rest of the battle however you like; every action is open now. It ends after round 4, and whoever holds the most markers wins.", Ask.FreePlay),
            },
            "rules", "Hover a rule on a unit card or a weapon to read it. The Rules Guide has every lesson in this list."),
    };

    /// <summary>Lesson 12's rules: the core special rules your army and the enemy's carry in this game (book text).</summary>
    public static IReadOnlyList<string> SpecialRuleIds(EGameSystem system) => system switch
    {
        EGameSystem.AgeOfFantasy => new[] { "ap", "parry", "hero", "tough" },
        _ => new[] { "ap", "assault", "fearless", "bane", "strider", "reliable", "hero", "tough" },
    };

    /// <summary>The army rules (Army Forge's wording) of both books - Battleborn, Hive Bond, Hold the Line, Mischievous.</summary>
    public static IReadOnlyList<(string Name, string Text)> ArmyRules(EGameSystem system)
    {
        Armies a = ArmiesFor(system);
        var named = system == EGameSystem.AgeOfFantasy
            ? new[] { (a.MyBook, "Hold the Line"), (a.FoeBook, "Mischievous") }
            : new[] { (a.MyBook, "Battleborn"), (a.FoeBook, "Hive Bond") };
        var list = new List<(string, string)>();
        foreach ((string bookName, string rule) in named)
            if (Book(system, bookName)?.RuleDefinitions.FirstOrDefault(r => r.Name == rule) is { } def && !string.IsNullOrWhiteSpace(def.Description))
                list.Add(($"{rule} ({bookName})", def.Description));
        return list;
    }

    /// <summary>Fills a coach line's {tokens} with this game's unit names.</summary>
    public static string Fill(string text, EGameSystem system, int shootersQuality = 0)
    {
        Armies a = ArmiesFor(system);
        return text.Replace("{shooters}", a.Name(Role.MyShooters)).Replace("{melee}", a.Name(Role.MyMelee))
            .Replace("{hero}", a.Name(Role.MyHero)).Replace("{foeShooters}", a.Name(Role.FoeShooters))
            .Replace("{foeMelee}", a.Name(Role.FoeMelee)).Replace("{army}", a.MyBook).Replace("{foe}", a.FoeBook)
            .Replace("{shootersQ}", shootersQuality > 0 ? shootersQuality.ToString() : QualityOf(system, Role.MyShooters).ToString());
    }

    private static int QualityOf(EGameSystem system, Role role)
    {
        Armies a = ArmiesFor(system);
        string book = role is Role.FoeMelee or Role.FoeShooters ? a.FoeBook : a.MyBook;
        return Book(system, book)?.Units.FirstOrDefault(u => u.Name == a.Name(role))?.Quality ?? 4;
    }

    // ------------------------------------------------------------------ set dice

    /// <summary>
    /// The faces a step's rolls come up on: who rolls well and who rolls badly. Null = ordinary dice. A pattern is
    /// cycled over however many dice are rolled (<see cref="ScriptedDiceRoller"/>), most important face first.
    /// </summary>
    public static int[]? Faces(Luck luck, FDG.Rules.Definitions.ERollKind kind, bool mine) => (luck, kind, mine) switch
    {
        // Shooting: most of your shots land (a couple miss, so the roll reads like a roll), and theirs mostly fail to block.
        (Luck.Shooting, FDG.Rules.Definitions.ERollKind.Hit, true) => new[] { 6, 5, 6, 2, 5, 1 },
        (Luck.Shooting, FDG.Rules.Definitions.ERollKind.Save, false) => new[] { 2, 1, 6, 3 },
        // Melee: you win it clearly and they fail the Morale Test - lessons 8 and 9 build on that.
        // A few wounds, not a massacre: a unit that loses a melee at half strength or less Routs instead of being
        // Shaken, and lesson 9 is about Shaken.
        (Luck.Melee, FDG.Rules.Definitions.ERollKind.Hit, true) => new[] { 6, 2, 1, 5, 1, 2, 1 },
        (Luck.Melee, FDG.Rules.Definitions.ERollKind.Save, true) => new[] { 6, 5 },
        (Luck.Melee, FDG.Rules.Definitions.ERollKind.Hit, false) => new[] { 1, 2, 3 },
        (Luck.Melee, FDG.Rules.Definitions.ERollKind.Save, false) => new[] { 1, 6, 5, 2 },
        (Luck.Melee, FDG.Rules.Definitions.ERollKind.Morale, false) => new[] { 1 },
        _ => null,
    };

    /// <summary>
    /// The set-up roll-offs, in the order the game rolls them (yours first): you win the map-side roll-off and the
    /// deployment roll-off, so lesson 3 can have you pick your edge and deploy first.
    /// </summary>
    public static readonly int[][] RollOffs = { new[] { 6 }, new[] { 1 }, new[] { 6 }, new[] { 1 } };

    /// <summary>The steering function for a step, given which player is you.</summary>
    public static Func<FDG.Rules.Definitions.ERollKind, PlayerID, int[]?>? SteerFor(Luck luck, PlayerID me) =>
        luck == Luck.None ? null : (kind, roller) => Faces(luck, kind, roller.Equals(me));
}
