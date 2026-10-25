using System.Numerics;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Horde Mode's shop window (end of a wave). Recruit tab: new units from your army's book, configured with
/// the same editor the Army Forge uses. Upgrade tab: your units within 9" of a seized objective, re-equipped
/// for the difference between their new and current cost (a cheaper loadout pays points back). The engine
/// then takes the points, re-equips the upgraded units and asks you to place each recruit.
/// </summary>
public sealed class GuiHordeShopResolver : IStageResolver<HordeShopRequest, HordeShopReply>, IGuiResolver
{
    private readonly object _lock = new();
    private HordeShopRequest? _request;
    private TaskCompletionSource<HordeShopReply>? _tcs;

    // Per-shop UI state (main thread only).
    private List<BookFile>? _books;          // books the lobby's Army Source allows, once loaded
    private BookFile? _book;
    private RuleGlossary _glossary = RuleGlossary.Empty;
    private bool _bookMatched;
    private BuilderList _cart = new();
    private readonly List<int> _cartObjectives = new();
    private readonly Dictionary<Guid, UpgradeEdit> _upgrades = new();
    private int _selectedRecruit = -1;
    private Guid? _selectedUpgrade;
    private string _filter = "";

    /// <summary>A unit on the table being re-equipped in this shop visit.</summary>
    private sealed class UpgradeEdit
    {
        public required HordeShopUnit Info;
        public BuilderUnit? Unit;           // null when it can't be upgraded (see Problem)
        public string Seed = "";            // its choices as the shop opened, to tell a change from none
        public bool FromLoadout;            // true: its real choices were remembered
        public string? Problem;

        public bool Changed => Unit != null && ChoicesJson(Unit) != Seed;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<HordeShopReply> Resolve(HordeShopRequest request)
    {
        var tcs = new TaskCompletionSource<HordeShopReply>();
        lock (_lock)
        {
            _request = request;
            _tcs = tcs;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        HordeShopRequest? request;
        TaskCompletionSource<HordeShopReply>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        if (_books == null)
        {
            Task<List<BookFile>> loading = BookLibrary.LoadAsync();
            if (loading.IsCompleted)
            {
                // Only the books the lobby's Army Source allows: Grimdark Future, Age of Fantasy, or All.
                _books = loading.Result.Where(b => GameSystems.IsAllowed(request.AllowedGameSystems, b.GameSystem))
                    .OrderBy(b => BookLabel(b, request.AllowedGameSystems)).ToList();
                BookFile? match = _books.FirstOrDefault(b =>
                    string.Equals(b.Faction, request.Faction, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(b.Name, request.Faction, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(b.Name, request.ArmyName, StringComparison.OrdinalIgnoreCase));
                _bookMatched = match != null;
                UseBook(match ?? _books.FirstOrDefault());
            }
        }

        float margin = MathF.Max(24f, screenW * 0.04f);
        ImGui.SetNextWindowPos(new Vector2(margin, margin), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW - margin * 2f, screenH - margin * 2f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.97f);
        ImGui.Begin($"Horde Shop - Wave {request.Wave} complete##hordeShop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings);

        HordeShopReply? answer = null;

        if (_books == null || _book == null)
        {
            UiText.Disabled("Loading army books...");
            if (ImGui.Button("Skip the shop")) answer = HordeShopReply.Nothing;
        }
        else
        {
            answer = DrawShop(request);
        }

        ImGui.End();

        if (answer != null) Complete(tcs, answer);
    }

    private HordeShopReply? DrawShop(HordeShopRequest request)
    {
        List<UnitFileEntry> rows = ListCompiler.CompileRows(_book!, _cart);
        int recruitCost = rows.Sum(r => r.PointCost);
        List<UpgradeEdit> pending = _upgrades.Values.Where(u => u.Changed).ToList();
        int upgradeCost = pending.Sum(PriceOf);
        int total = recruitCost + upgradeCost;
        int left = request.PointsAvailable - total;

        // ---- header: points + book
        UiText.Colored(ImGuiTheme.HeroGold, $"Points to spend: {request.PointsAvailable}");
        ImGui.SameLine(0, 30f);
        UiText.Colored(left < 0 ? ImGuiTheme.WoundsRed : ImGuiTheme.PointsBlue,
            $"In cart: {Signed(total)}   Left after buying: {left}");
        ImGui.SameLine(0, 30f);
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("Army book", BookLabel(_book!, request.AllowedGameSystems)))
        {
            foreach (BookFile book in _books!)
            {
                if (ImGui.Selectable(BookLabel(book, request.AllowedGameSystems), ReferenceEquals(book, _book))
                    && !ReferenceEquals(book, _book))
                    UseBook(book);
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Books shown follow the lobby's Army Source: {GameSystems.DisplayName(request.AllowedGameSystems)}.\n" +
                "Changing the book empties the cart.");
        if (!_bookMatched)
        {
            ImGui.SameLine();
            UiText.Disabled("(couldn't find your army's book - pick one)");
        }
        UiText.Disabled("Objectives your side seized this wave: " +
            string.Join(", ", request.SeizedObjectives.Select(o => o.IsPriority ? $"{o.Number} (priority)" : o.Number.ToString())) +
            $". Recruits deploy within {FDG.StateMachine.HordeRules.ShopRangeInches:0}\" of one; units within {FDG.StateMachine.HordeRules.ShopRangeInches:0}\" of one can buy upgrades.");
        ImGui.Separator();

        float footer = ImGui.GetFrameHeightWithSpacing() * 1.8f;
        float paneH = ImGui.GetContentRegionAvail().Y - footer;
        float fullW = ImGui.GetContentRegionAvail().X;
        float gap = ImGui.GetStyle().ItemSpacing.X;
        float leftW = fullW * 0.30f, cartW = fullW * 0.25f, detailW = fullW - leftW - cartW - gap * 2f;

        // ---- left: Recruit / Upgrade tabs
        ImGui.BeginChild("##shopLeft", new Vector2(leftW, paneH), ImGuiChildFlags.Borders);
        if (ImGui.BeginTabBar("##shopTabs"))
        {
            if (ImGui.BeginTabItem("Recruit"))
            {
                DrawRecruitList(request, left);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Upgrade ({request.Upgradable.Count(u => u.NotEligibleReason == null)})"))
            {
                DrawUpgradeList(request);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.EndChild();

        // ---- middle: the selected recruit or unit
        ImGui.SameLine();
        ImGui.BeginChild("##shopDetail", new Vector2(detailW, paneH), ImGuiChildFlags.Borders);
        if (_selectedUpgrade is Guid id && _upgrades.TryGetValue(id, out UpgradeEdit? edit))
            DrawUpgradeDetail(edit);
        else if (_selectedRecruit >= 0 && _selectedRecruit < _cart.Units.Count)
            DrawRecruitDetail(request);
        else
            UiText.Disabled("Recruit: pick a unit from your book to add it to the cart.\n" +
                            "Upgrade: pick one of your units near a seized objective to change its gear.");
        ImGui.EndChild();

        // ---- right: cart
        ImGui.SameLine();
        ImGui.BeginChild("##shopCart", new Vector2(cartW, paneH), ImGuiChildFlags.Borders);
        DrawCart(rows, pending);
        ImGui.EndChild();

        // ---- footer
        ImGui.Spacing();
        int items = _cart.Units.Count + pending.Count;
        float btnW = 240f, btnH = ImGui.GetFrameHeight() * 1.4f;
        bool canBuy = items > 0 && left >= 0;
        bool buy = ResolverButtons.Primary(items == 0 ? "Buy" : $"Buy ({Signed(total)} pts)",
            new Vector2(btnW, btnH), enabled: canBuy, bindConfirm: false);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(left < 0 ? "Not enough points - take something out of the cart."
                : items == 0 ? "The cart is empty." : "Buy the cart: upgrades apply now, then you place each recruit.");
        ImGui.SameLine();
        bool skip = ResolverButtons.Deemphasized("Done - buy nothing", new Vector2(btnW, btnH));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Close the shop and keep your points for later waves.");

        if (buy && canBuy) return BuildReply(pending);
        if (skip) return HordeShopReply.Nothing;
        return null;
    }

    // ------------------------------------------------------------------ recruit

    private void DrawRecruitList(HordeShopRequest request, int left)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##shopFilter", "search units", ref _filter, 64);
        foreach (RosterUnit unit in _book!.Units)
        {
            if (_filter.Length > 0 && !unit.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.PushID(unit.Id);
            bool affordable = unit.BasePointCost <= left;
            if (!affordable) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            if (ImGui.Selectable($"{unit.Name} [{unit.BaseModelCount}]  -  {unit.BasePointCost} pts"))
            {
                int index = BuilderListEditing.AddUnit(_book, _cart, unit.Id);
                _cartObjectives.Add(request.SeizedObjectives[0].Number);
                _selectedRecruit = index;
                _selectedUpgrade = null;
            }
            if (!affordable) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to add to the cart.");
            ImGui.PopID();
        }
    }

    private void DrawRecruitDetail(HordeShopRequest request)
    {
        BuilderUnit bu = _cart.Units[_selectedRecruit];
        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(_book!, bu);
        UiText.Colored(ImGuiTheme.HeaderAccent, "NEW RECRUIT");
        ForgeUnitDetail.DrawHeader(unit);
        ForgeUnitDetail.DrawGear(unit, items, _glossary);

        ImGui.Spacing();
        ImGui.SetNextItemWidth(200f);
        int current = _cartObjectives[_selectedRecruit];
        if (ImGui.BeginCombo("Deploy at objective", current.ToString()))
        {
            foreach (HordeShopObjective objective in request.SeizedObjectives)
            {
                string label = objective.IsPriority ? $"{objective.Number} (priority)" : objective.Number.ToString();
                if (ImGui.Selectable(label, objective.Number == current))
                    _cartObjectives[_selectedRecruit] = objective.Number;
            }
            ImGui.EndCombo();
        }

        RosterUnit? roster = _book!.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
        if (roster != null) ForgeUnitDetail.DrawUpgrades(_book, _glossary, bu, roster, unit, items);
    }

    // ------------------------------------------------------------------ upgrade

    private void DrawUpgradeList(HordeShopRequest request)
    {
        if (request.Upgradable.Count == 0)
        {
            UiText.Disabled("You have no units in play.");
            return;
        }
        if (!request.Upgradable.Any(u => u.NotEligibleReason == null))
            UiText.Disabled($"None of your units is within {FDG.StateMachine.HordeRules.ShopRangeInches:0}\" of a seized objective.");
        // Units that can buy upgrades first; the rest stay listed (greyed) so the reason is one hover away.
        foreach (HordeShopUnit info in request.Upgradable.OrderBy(u => u.NotEligibleReason != null))
        {
            UpgradeEdit edit = EditFor(info);
            ImGui.PushID(info.UnitId.ToString());
            string mark = edit.Changed ? $"  ({Signed(PriceOf(edit))} pts)" : "";
            bool blocked = edit.Problem != null;
            if (blocked) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            string hero = info.JoinedHeroName != null ? $" + {info.JoinedHeroName}" : "";
            if (ImGui.Selectable($"{info.Name} [{info.ModelCount}]{hero} - {info.PointCost} pts{mark}", _selectedUpgrade == info.UnitId))
            {
                _selectedUpgrade = info.UnitId;
                _selectedRecruit = -1;
            }
            if (blocked) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(blocked ? edit.Problem! : "Click to change this unit's gear.");
            ImGui.PopID();
        }
    }

    private void DrawUpgradeDetail(UpgradeEdit edit)
    {
        UiText.Colored(ImGuiTheme.HeaderAccent, "UPGRADE");
        ImGui.TextUnformatted($"{edit.Info.Name} [{edit.Info.ModelCount}] - now {edit.Info.PointCost} pts");
        if (edit.Info.JoinedHeroName != null)
            UiText.Disabled($"Joined by {edit.Info.JoinedHeroName}: only the unit's own models are re-equipped; the hero and its points stay as they are.");
        ImGui.PushTextWrapPos(0f);
        UiText.Disabled("Has now: " + edit.Info.CurrentGear);
        ImGui.PopTextWrapPos();
        ImGui.Separator();

        if (edit.Problem != null || edit.Unit == null)
        {
            ImGui.PushTextWrapPos(0f);
            UiText.Colored(ImGuiTheme.WoundsRed, edit.Problem ?? "This unit can't be upgraded.");
            ImGui.PopTextWrapPos();
            return;
        }

        if (!edit.FromLoadout)
        {
            ImGui.PushTextWrapPos(0f);
            UiText.Colored(ImGuiTheme.HitsYellow,
                "The shop doesn't know which upgrades this unit started with, so it opens at the book's basic loadout. " +
                "Pick everything you want it to have (including upgrades it already had) - you only pay the difference.");
            ImGui.PopTextWrapPos();
        }

        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(_book!, edit.Unit);
        int price = PriceOf(edit);
        UiText.Colored(edit.Changed ? (price > 0 ? ImGuiTheme.PointsBlue : ImGuiTheme.HeroGold) : ImGuiTheme.RuleBlueDim,
            edit.Changed
                ? $"New cost {unit.PointCost} - current {edit.Info.PointCost} = {Signed(price)} pts" + (price < 0 ? " (you get points back)" : "")
                : "No changes yet.");
        if (edit.Changed)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset")) ResetEdit(edit);
        }

        ForgeUnitDetail.DrawHeader(unit);
        ForgeUnitDetail.DrawGear(unit, items, _glossary);
        RosterUnit? roster = _book!.Units.FirstOrDefault(u => u.Id == edit.Unit.RosterUnitId);
        if (roster != null) ForgeUnitDetail.DrawUpgrades(_book, _glossary, edit.Unit, roster, unit, items);
    }

    /// <summary>The upgrade editor for a unit, seeded on first use: its remembered choices if this book
    /// knows them, else the book's basic loadout for a unit of the same name and size.</summary>
    private UpgradeEdit EditFor(HordeShopUnit info)
    {
        if (_upgrades.TryGetValue(info.UnitId, out UpgradeEdit? existing)) return existing;

        var edit = new UpgradeEdit { Info = info };
        if (info.NotEligibleReason != null)
        {
            edit.Problem = info.NotEligibleReason;
            _upgrades[info.UnitId] = edit;
            return edit;
        }
        HordeLoadout? loadout = HordeLoadout.FromJson(info.LoadoutJson);
        if (loadout != null && loadout.BookName == _book!.Name
            && _book.Units.Any(u => u.Id == loadout.Unit.RosterUnitId))
        {
            edit.Unit = Clone(loadout.Unit);
            edit.FromLoadout = true;
        }
        else
        {
            RosterUnit? roster = _book!.Units.FirstOrDefault(u =>
                string.Equals(u.Name, info.Name, StringComparison.OrdinalIgnoreCase));
            if (roster == null)
                edit.Problem = $"There is no \"{info.Name}\" in the {_book.Name} book, so the shop can't offer its upgrades.";
            else if (roster.BaseModelCount != info.ModelCount)
                edit.Problem = $"This unit has {info.ModelCount} models but the book's {roster.Name} has {roster.BaseModelCount} " +
                               "(a combined unit) - it can't be upgraded in the shop.";
            else
                edit.Unit = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
        }
        edit.Seed = edit.Unit != null ? ChoicesJson(edit.Unit) : "";
        _upgrades[info.UnitId] = edit;
        return edit;
    }

    private void ResetEdit(UpgradeEdit edit)
    {
        _upgrades.Remove(edit.Info.UnitId);
        EditFor(edit.Info);
    }

    private int PriceOf(UpgradeEdit edit) => edit.Unit == null
        ? 0
        : ListCompiler.CompileUnitDetailed(_book!, edit.Unit).Unit.PointCost - edit.Info.PointCost;

    // ------------------------------------------------------------------ cart + reply

    private void DrawCart(List<UnitFileEntry> rows, List<UpgradeEdit> pending)
    {
        UiText.Colored(ImGuiTheme.HeaderAccent, "CART");
        int remove = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            ImGui.PushID(i);
            if (ImGui.SmallButton("X")) remove = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove from the cart");
            ImGui.SameLine();
            if (ImGui.Selectable($"Recruit {rows[i].Name} [{rows[i].ModelCount}] - {rows[i].PointCost} pts  @ obj {_cartObjectives[i]}",
                    _selectedUpgrade == null && i == _selectedRecruit))
            {
                _selectedRecruit = i;
                _selectedUpgrade = null;
            }
            ImGui.PopID();
        }

        UpgradeEdit? undo = null;
        foreach (UpgradeEdit edit in pending)
        {
            ImGui.PushID("up" + edit.Info.UnitId);
            if (ImGui.SmallButton("X")) undo = edit;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Undo this upgrade");
            ImGui.SameLine();
            if (ImGui.Selectable($"Upgrade {edit.Info.Name} - {Signed(PriceOf(edit))} pts", _selectedUpgrade == edit.Info.UnitId))
            {
                _selectedUpgrade = edit.Info.UnitId;
                _selectedRecruit = -1;
            }
            ImGui.PopID();
        }

        if (rows.Count == 0 && pending.Count == 0) UiText.Disabled("Empty.");

        if (remove >= 0)
        {
            BuilderListEditing.RemoveUnit(_cart, remove);
            _cartObjectives.RemoveAt(remove);
            if (_selectedRecruit >= _cart.Units.Count) _selectedRecruit = _cart.Units.Count - 1;
        }
        if (undo != null) ResetEdit(undo);
    }

    private HordeShopReply BuildReply(List<UpgradeEdit> pending)
    {
        string? recruitsJson = null;
        if (_cart.Units.Count > 0)
        {
            BuiltArmyFile compiled = ListCompiler.Compile(_book!, _cart);
            recruitsJson = JsonSerializer.Serialize<ArmyListFile>(compiled, RuleJson.Options);
        }

        var reply = new HordeShopReply(recruitsJson, new List<int>(_cartObjectives))
        {
            RecruitLoadouts = _cart.Units.Select(u => (string?)new HordeLoadout { BookName = _book!.Name, Unit = u }.ToJson()).ToList(),
        };

        if (pending.Count > 0)
        {
            var list = new BuilderList { Name = "Horde Upgrades", BookName = _book!.Name };
            foreach (UpgradeEdit edit in pending) list.Units.Add(edit.Unit!);
            reply.UpgradesJson = JsonSerializer.Serialize<ArmyListFile>(ListCompiler.Compile(_book, list), RuleJson.Options);
            reply.UpgradeUnitIds = pending.Select(e => e.Info.UnitId).ToList();
            reply.UpgradeLoadouts = pending.Select(e => (string?)new HordeLoadout { BookName = _book.Name, Unit = e.Unit! }.ToJson()).ToList();
        }
        return reply;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The book's name, tagged with its game system when the shop mixes systems (Army Source: All).</summary>
    private static string BookLabel(BookFile book, EAllowedGameSystems allowed) =>
        allowed == EAllowedGameSystems.All
            ? $"{book.Name} ({(GameSystems.SameSystem(book.GameSystem, GameSystems.AgeOfFantasy) ? "AoF" : GameSystems.SameSystem(book.GameSystem, GameSystems.GrimdarkFuture) ? "GF" : GameSystems.DisplayName(book.GameSystem))})"
            : book.Name;

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static string ChoicesJson(BuilderUnit unit) =>
        JsonSerializer.Serialize(new { unit.ModelCount, unit.Choices });

    private static BuilderUnit Clone(BuilderUnit unit) =>
        JsonSerializer.Deserialize<BuilderUnit>(JsonSerializer.Serialize(unit))!;

    private void UseBook(BookFile? book)
    {
        _book = book;
        _glossary = book != null ? RuleGlossary.Build(book) : RuleGlossary.Empty;
        _cart = new BuilderList { Name = "Horde Recruits", BookName = book?.Name ?? "" };
        _cartObjectives.Clear();
        _upgrades.Clear();
        _selectedRecruit = -1;
        _selectedUpgrade = null;
        _filter = "";
    }

    private void Complete(TaskCompletionSource<HordeShopReply> tcs, HordeShopReply reply)
    {
        lock (_lock) { _request = null; _tcs = null; }
        _books = null; // re-match the book next time (cheap: the library is cached)
        _book = null;
        _cart = new BuilderList();
        _cartObjectives.Clear();
        _upgrades.Clear();
        _selectedRecruit = -1;
        _selectedUpgrade = null;
        tcs.SetResult(reply);
    }
}
