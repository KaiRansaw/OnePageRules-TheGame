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
        new(1, "Dice and Quality tests", "Reading a roll: a 6 always works, a 1 always fails.",
            new[] { "quality-tests", "modifiers", "most-important-rule" },
            new Step[]
            {
                new("Everything in this game is settled with six-sided dice. Most rolls are Quality tests: one die per attack, and each die that scores the unit's Quality or higher is a success. Your {shooters} are Quality {shootersQ}+: any die showing {shootersQ} or more is a success."),
                new("A 6 always succeeds and a 1 always fails, whatever adds to or takes from the roll. Every roll in this battle is shown in the dice popup and the battle report, so you can see exactly what happened."),
            },
            "dice", "Every roll pops up on the table and goes into the battle report. Hover a report line to see each die."),

        new(2, "The table and objectives", "Where you fight and what you're fighting for.",
            new[] { "battlefield", "mission" },
            new Step[]
            {
                new("This practice table is 4' x 4' - smaller than the book's 6' x 4'. The book allows that, as long as the armies deploy at least 24\" apart, and here they do."),
                new("The three gold markers are objectives. In a normal game the players roll off and take turns placing them; in this practice battle they're already placed so you can get straight to fighting."),
                new("At the end of every round, a unit within 3\" of a marker with no enemy within 3\" seizes it. After round 4, whoever holds the most markers wins - even with fewer units left on the table."),
            },
            "objectives", "Markers show who holds them in that player's colour. The Mission panel (bottom right) counts them."),

        new(3, "Deployment", "Setting up your army, one unit at a time.",
            new[] { "deployment" },
            new Step[]
            {
                new("You won the deployment roll-off, so you pick your table edge. Pick the near edge - the zone closest to you.", Ask.PickEdge),
                new("Now you and the enemy take turns placing units within 12\" of your own edge, you first. Place your {shooters} inside the green ring, behind the forest.", Ask.Deploy, Role.MyShooters, Zone: new(14, 10, 2f)),
                new("Your {melee} come next, with your {hero} leading them - a Hero can deploy as part of one unit. Place them inside the green ring.", Ask.Deploy, Role.MyMelee, Zone: new(30, 8, 3f)),
            },
            "deployment", "Click inside the outlined zone and the unit is set up there in formation. Drag single models to adjust, then Space."),

        new(4, "Taking turns and actions", "Activate a unit, pick Hold, Advance, Rush or Charge.",
            new[] { "playing-the-game", "activation" },
            new Step[]
            {
                new("The battle runs in rounds. Each round you and the enemy take turns activating one unit each until every unit has gone. You won the roll-off, so you go first."),
                new("A unit that activates must take one action: Hold (stay and shoot), Advance (move 6\" and then shoot), Rush (move 12\", no shooting) or Charge (move 12\" into melee). Pick your {melee} and Rush them into the green ring.", Ask.Rush, Role.MyMelee, Zone: new(28, 19, 3f)),
            },
            "actions", "Pick a unit in your army list or on the table, then press an action (keys 1-4). Click where it should go, then Space."),

        new(5, "Moving and cover", "Staying together, and why forests matter.",
            new[] { "movement", "cover", "difficult" },
            new Step[]
            {
                new("Units move as a group: each model stays within 1\" of another and within 9\" of all of them. The game keeps your formation for you."),
                new("Now your {shooters}: Advance them into the forest (the green ring). Advance lets a unit shoot after moving; if nothing is in range yet, End after the move.", Ask.Advance, Role.MyShooters, Zone: new(14, 15.5f, 2.5f)),
                new("The forest is Cover: with most of the unit inside it, your {shooters} get +1 to Defense rolls against shooting. It's also Difficult terrain: moving through it caps a move at 6\" - that's why the game shows a shorter reach there."),
            },
            "cover", "Terrain shows its rules when you hover it. Rings on the table show how far the unit can go."),

        new(6, "Shooting", "Rolling to hit, then the enemy rolls to block.",
            new[] { "shooting", "ap" },
            new Step[]
            {
                new("Round 2. Your {shooters} are in the forest with the enemy closing in. Choose Hold - they stay in cover and shoot - then click an enemy unit, the {foeShooters} if you can see them.", Ask.Shoot, Role.MyShooters, Role.FoeShooters,
                    Luck: Luck.Shooting, OrElse: new[] { Ask.Advance }, OrElseCoach: "Nothing is in range from here. Advance your {shooters} up to 6\" toward the enemy, then shoot."),
                new("That's a full shot: one Quality test per attack makes the hits, the enemy rolls one die per hit to block on its Defense, and every hit not blocked is a wound - one model removed. AP makes blocking harder: AP(1) is -1 to their Defense rolls."),
            },
            "shooting", "Pick a unit, Hold (or Advance), then click an enemy. Hover an enemy first to see your odds."),

        new(7, "Charging and melee", "Into combat: strike, strike back, see who won.",
            new[] { "melee" },
            new Step[]
            {
                new("Your {melee} charge! Choose Charge and click the {foeMelee}. Charging models move into base contact, then both sides strike with their melee weapons.", Ask.Charge, Role.MyMelee, Role.FoeMelee,
                    Luck: Luck.Melee, OrElse: new[] { Ask.Rush, Ask.Advance }, OrElseCoach: "No enemy is within 12\" of your {melee} yet. Rush them closer - they can charge next time."),
                new("Melee works like shooting: one Quality test per attack, the defender rolls to block. Then the defender may strike back. The side that took more wounds lost the melee."),
            },
            "melee", "Charge, then click the enemy unit: the move ends in contact. Wounds and who won show in the battle report."),

        new(8, "Fatigue", "After fighting, units hit only on 6s for the rest of the round.",
            new[] { "fatigue" },
            new Step[]
            {
                new("Both units that fought are now Fatigued until the end of this round: in another melee this round they only hit on a 6, and those 6s don't trigger special rules. That's why a second charge in the same round is a gamble."),
            },
            "fatigue", "A Fatigued unit shows the Fatigued mark in the army list until the round ends."),

        new(9, "Morale and Shaken", "Losing a fight, half strength, and staying idle.",
            new[] { "morale-tests", "melee-results", "shaken" },
            new Step[]
            {
                new("The {foeMelee} lost the melee, so they took a Morale Test - a Quality test - and failed it. A unit that fails is Shaken; one that fails while down to half strength or less Routs instead and is removed. The battle report says which. Units also test at the end of an activation where wounds leave them at half strength or less."),
                new("A Shaken unit must stay idle: it can't move, shoot or seize objectives, and it fights back only as if Fatigued. It stops being Shaken by spending one whole activation idle."),
            },
            "morale", "Shaken units get a red frame and mark in the army list. Hover the mark for the rule."),

        new(10, "Heroes and Tough", "Leaders inside units, and models that take several wounds.",
            new[] { "hero", "tough" },
            new Step[]
            {
                new("Your {hero} is a Hero: it joined the {melee} at deployment and uses their Defense until the rest of the unit is gone. Its unit may use the Hero's Quality for Morale Tests."),
                new("Your {hero} is also Tough(3): it takes 3 wounds before it's killed, and wounds go on it last. The wound pips on its card show how many it has left."),
            },
            "hero", "Joined Heroes show inside their unit's card. Wound pips show what each Tough model has left."),

        new(11, "Seizing objectives and winning", "End of round: who holds what.",
            new[] { "mission" },
            new Step[]
            {
                new("The game is won on objectives, not kills. Move one of your units onto the nearest marker - inside the green ring. If no enemy is within 3\" of it at the end of the round, it's yours.", Ask.ToObjective,
                    OrElse: new[] { Ask.Hold, Ask.Advance, Ask.Rush, Ask.Charge }, OrElseCoach: "That unit can't reach a marker this time - use it as you like, then try with another."),
                new("A seized marker stays yours even after you leave it - until an enemy unit is within 3\" of it with none of yours, or both sides are, which makes it neutral again."),
            },
            "seize", "The Mission panel counts the markers each side holds. They're checked at the end of every round."),

        new(12, "Special rules", "The extra rules on your units' cards.",
            new[] { "special-rules" },
            new Step[]
            {
                new("Units and weapons carry special rules - the words in brackets on their cards. Your army's are below in the book's own words. Hover any rule on a card in a real game to read it."),
                new("That's everything in the core rules. Play the battle out however you like - every action is open now. It ends after round 4, and the side with the most markers wins.", Ask.FreePlay),
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
