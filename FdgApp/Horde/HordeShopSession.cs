using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FdgRaylib.Forge;

namespace FdgRaylib.Horde;

/// <summary>
/// One visit to Horde Mode's shop with no drawing in it (ported from the Raylib <c>GuiHordeShopResolver</c>; the
/// Godot shop is a view over this). Recruits are a cart list edited with the Army Forge's own session (upgrades,
/// looks); on-table units being re-equipped are a second list; reinforcements are ticks. <see cref="Reply"/> is what
/// goes back to the engine, which re-checks every price and range itself.
/// </summary>
internal sealed class HordeShopSession
{
    public HordeShopRequest Request { get; }
    public List<BookFile> Books { get; }
    public BookFile? Book { get; private set; }
    public bool BookMatched { get; }

    /// <summary>The recruits, as a Forge list (upgrades and looks edit it directly).</summary>
    public ForgeSession Cart { get; private set; } = null!;
    /// <summary>Which seized objective each recruit deploys at, by cart row.</summary>
    public List<int> CartObjectives { get; } = new();

    /// <summary>On-table units being re-equipped: one row per unit, in <see cref="Request"/>'s order.</summary>
    public ForgeSession Upgrades { get; private set; } = null!;
    private readonly Dictionary<Guid, UpgradeEdit> _edits = new();

    /// <summary>Units ticked for reinforcing (#409).</summary>
    public HashSet<Guid> Reinforce { get; } = new();

    public sealed class UpgradeEdit
    {
        public required HordeShopUnit Info { get; init; }
        /// <summary>Its row in <see cref="Upgrades"/>, or -1 when it can't be upgraded (<see cref="Problem"/>).</summary>
        public int Row { get; set; } = -1;
        public string Seed { get; set; } = "";
        public bool FromLoadout { get; set; }
        public string? Problem { get; set; }
        /// <summary>The player pressed "add to cart" (Kai's mockup); changes after that stay in the cart.</summary>
        public bool InCart { get; set; }
    }

    public HordeShopSession(HordeShopRequest request, IEnumerable<BookFile> library)
    {
        Request = request;
        Books = library.Where(b => GameSystems.IsAllowed(request.AllowedGameSystems, b.GameSystem))
            .OrderBy(b => BookLabel(b, request.AllowedGameSystems)).ToList();
        // #412: MatchesFactionName covers the names OPR has renamed away from.
        // The same faction is published once per game (GF, Firefight, Star Quest...): prefer the game being played.
        var matches = Books.Where(b => b.MatchesFactionName(request.Faction) || b.MatchesFactionName(request.ArmyName)).ToList();
        BookFile? match = matches.FirstOrDefault(b => request.GameSystemSlug != null && GameSystems.SameSystem(b.GameSystem, request.GameSystemSlug))
                          ?? matches.FirstOrDefault();
        BookMatched = match != null;
        UseBook(match ?? Books.FirstOrDefault());
    }

    /// <summary>Changing the book empties the cart and the upgrade edits.</summary>
    public void UseBook(BookFile? book)
    {
        Book = book;
        CartObjectives.Clear();
        _edits.Clear();
        if (book == null) return;
        Cart = new ForgeSession(book, new BuilderList { Name = "Horde Recruits", BookName = book.Name });
        Upgrades = new ForgeSession(book, new BuilderList { Name = "Horde Upgrades", BookName = book.Name });
        foreach (HordeShopUnit info in Request.Upgradable) EditFor(info);
    }

    public static string BookLabel(BookFile book, EAllowedGameSystems allowed) =>
        allowed == EAllowedGameSystems.All
            ? $"{book.Name} ({(GameSystems.SameSystem(book.GameSystem, GameSystems.AgeOfFantasy) ? "AoF" : GameSystems.SameSystem(book.GameSystem, GameSystems.GrimdarkFuture) ? "GF" : GameSystems.DisplayName(book.GameSystem))})"
            : book.Name;

    // ---------------------------------------------------------------- recruits

    /// <summary>Puts a copy of <paramref name="draft"/> (a unit picked and set up in the shop) in the cart.</summary>
    public int AddRecruit(BuilderUnit draft, int objective)
    {
        Cart.List.Units.Add(Clone(draft));
        CartObjectives.Add(objective);
        return Cart.List.Units.Count - 1;
    }

    public void RemoveRecruit(int row)
    {
        if (row < 0 || row >= Cart.List.Units.Count) return;
        BuilderListEditing.RemoveUnit(Cart.List, row);
        CartObjectives.RemoveAt(row);
    }

    /// <summary>A fresh one-unit list for a roster unit picked in the Recruit tab, before it goes in the cart.</summary>
    public ForgeSession Draft(string rosterId)
    {
        var s = new ForgeSession(Book!, new BuilderList { BookName = Book!.Name });
        s.Add(rosterId);
        return s;
    }

    // ---------------------------------------------------------------- upgrades

    public IReadOnlyCollection<UpgradeEdit> Edits => _edits.Values;

    public UpgradeEdit EditFor(HordeShopUnit info)
    {
        if (_edits.TryGetValue(info.UnitId, out UpgradeEdit? existing)) return existing;
        var edit = new UpgradeEdit { Info = info };
        _edits[info.UnitId] = edit;
        if (info.NotEligibleReason != null) { edit.Problem = info.NotEligibleReason; return edit; }
        BuilderUnit? unit = null;
        HordeLoadout? loadout = HordeLoadout.FromJson(info.LoadoutJson);
        if (loadout != null && loadout.BookName == Book!.Name && Book.Units.Any(u => u.Id == loadout.Unit.RosterUnitId))
        {
            unit = Clone(loadout.Unit);
            edit.FromLoadout = true;
        }
        else
        {
            RosterUnit? roster = Book!.Units.FirstOrDefault(u => string.Equals(u.Name, info.Name, StringComparison.OrdinalIgnoreCase));
            if (roster == null)
                edit.Problem = $"There is no \"{info.Name}\" in the {Book.Name} book, so the shop can't offer its upgrades.";
            else if (roster.BaseModelCount != info.ModelCount)
                edit.Problem = $"This unit has {info.ModelCount} models but the book's {roster.Name} has {roster.BaseModelCount} (a combined unit) - it can't be upgraded in the shop.";
            else
                unit = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
        }
        if (unit != null)
        {
            Upgrades.List.Units.Add(unit);
            edit.Row = Upgrades.List.Units.Count - 1;
            edit.Seed = ChoicesJson(unit);
        }
        return edit;
    }

    public bool Changed(UpgradeEdit e) => e.Row >= 0 && ChoicesJson(Upgrades.List.Units[e.Row]) != e.Seed;

    /// <summary>New cost minus what it costs now (negative: points back).</summary>
    public int PriceOf(UpgradeEdit e) => e.Row < 0 ? 0 : Upgrades.Detail(e.Row).Unit.PointCost - e.Info.PointCost;

    public void Reset(UpgradeEdit e)
    {
        e.InCart = false;
        if (e.Row < 0) return;
        var seed = JsonSerializer.Deserialize<SeedShape>(e.Seed)!;
        BuilderUnit u = Upgrades.List.Units[e.Row];
        u.ModelCount = seed.ModelCount;
        u.Choices = seed.Choices;
    }

    private sealed class SeedShape
    {
        public int ModelCount { get; set; }
        public List<UpgradeChoice> Choices { get; set; } = new();
    }

    /// <summary>Upgrades in the cart: added with "add to cart" and still different from what the unit has.</summary>
    public List<UpgradeEdit> PendingUpgrades => _edits.Values.Where(e => e.InCart && Changed(e)).ToList();

    // ---------------------------------------------------------------- reinforce

    public List<HordeShopUnit> Reinforcing => Request.Upgradable
        .Where(u => u.NotReinforceableReason == null && Reinforce.Contains(u.UnitId)).ToList();

    // ---------------------------------------------------------------- totals

    public int RecruitCost => Book == null ? 0 : Cart.Rows().Sum(r => r.PointCost);
    public int UpgradeCost => PendingUpgrades.Sum(PriceOf);
    public int ReinforceCost => Reinforcing.Sum(u => u.ReinforceCost);
    public int Total => RecruitCost + UpgradeCost + ReinforceCost;
    public int Left => Request.PointsAvailable - Total;
    public int Items => (Book == null ? 0 : Cart.List.Units.Count) + PendingUpgrades.Count + Reinforcing.Count;
    public bool CanBuy => Items > 0 && Left >= 0;

    /// <summary>Points kept from earlier shops (what's available beyond this wave's reward).</summary>
    public int Kept => Math.Max(0, Request.PointsAvailable - Request.PointsEarned);

    public string EarnedLine
    {
        get
        {
            int seized = Request.SeizedObjectives.Count;
            bool priority = Request.SeizedObjectives.Any(o => o.IsPriority);
            string line = $"{seized} seized x {Request.PointsPerObjective}";
            if (priority && Request.PriorityBonus > 0) line += $" + priority {Request.PriorityBonus}";
            return line;
        }
    }

    public HordeShopReply Reply()
    {
        string? recruitsJson = null;
        if (Book != null && Cart.List.Units.Count > 0)
            recruitsJson = JsonSerializer.Serialize<ArmyListFile>(ListCompiler.Compile(Book, Cart.List), RuleJson.Options);
        var reply = new HordeShopReply(recruitsJson, new List<int>(CartObjectives))
        {
            RecruitLoadouts = Book == null ? new() : Cart.List.Units.Select(u => (string?)new HordeLoadout { BookName = Book.Name, Unit = u }.ToJson()).ToList(),
        };
        var pending = PendingUpgrades;
        if (pending.Count > 0 && Book != null)
        {
            var list = new BuilderList { Name = "Horde Upgrades", BookName = Book.Name };
            foreach (UpgradeEdit e in pending) list.Units.Add(Upgrades.List.Units[e.Row]);
            reply.UpgradesJson = JsonSerializer.Serialize<ArmyListFile>(ListCompiler.Compile(Book, list), RuleJson.Options);
            reply.UpgradeUnitIds = pending.Select(e => e.Info.UnitId).ToList();
            reply.UpgradeLoadouts = pending.Select(e => (string?)new HordeLoadout { BookName = Book.Name, Unit = Upgrades.List.Units[e.Row] }.ToJson()).ToList();
        }
        // #409: a reinforcement restores the unit already on the table; the engine re-checks the price.
        reply.ReinforceUnitIds = Reinforcing.Select(u => u.UnitId).ToList();
        return reply;
    }

    public static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString();

    public static string ObjectiveLabel(HordeShopObjective o) => o.IsPriority ? $"Objective {o.Number} (priority)" : $"Objective {o.Number}";

    private static string ChoicesJson(BuilderUnit unit) => JsonSerializer.Serialize(new { unit.ModelCount, unit.Choices });

    private static BuilderUnit Clone(BuilderUnit unit) => JsonSerializer.Deserialize<BuilderUnit>(JsonSerializer.Serialize(unit))!;
}
