using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Models3D;
using FdgRaylib.Rendering;
using FdgRaylib.Import;
using FDG.Rules.Dispatch;

namespace FdgRaylib.Forge;

/// <summary>
/// One Army Forge session with no drawing in it: the bundled books by game, the book being built from, the list,
/// which unit is clicked, and every edit the screen can make - add, remove, upgrades, joins, combined units,
/// per-model looks, save and load. The Godot Forge is a view over this; the rules underneath are the engine's
/// (<see cref="BuilderListEditing"/>, <see cref="ListCompiler"/>, <see cref="ListValidator"/>) and
/// <see cref="ForgeRules"/>'s availability maths, shared with the Raylib Forge.
/// </summary>
internal sealed class ForgeSession
{
    private readonly List<BookFile> _library;
    private string _cleanFingerprint = "";

    public ForgeSession(List<BookFile> library)
    {
        _library = library;
        Systems = ArmyForgeBookService.AllSystems
            .Where(slug => _library.Any(b => GameSystems.SameSystem(b.GameSystem, slug)))
            .ToList();
        if (Systems.Count == 0) Systems = new List<string> { GameSystems.Normalize(null) };
        System = Systems[0];
        UseBook(BooksInSystem[0]);
        List = new BuilderList { PointsLimit = ForgeRules.DefaultPointsLimit, BookName = Book.Name };
        MarkClean();
    }

    /// <summary>The bundled library (books load on a worker the first time).</summary>
    public static Task<List<BookFile>> LoadLibraryAsync() => BookLibrary.LoadAsync();

    // ---------------------------------------------------------------- game and faction

    /// <summary>The game systems that have books, Army Forge's order.</summary>
    public List<string> Systems { get; }

    public string System { get; private set; }

    public static string SystemName(string slug) => GameSystems.DisplayName(slug);

    public List<BookFile> BooksInSystem
    {
        get
        {
            var books = _library.Where(b => GameSystems.SameSystem(b.GameSystem, System))
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return books.Count > 0 ? books : _library.ToList();
        }
    }

    public BookFile Book { get; private set; } = null!;
    public RuleGlossary Glossary { get; private set; } = RuleGlossary.Empty;
    public IReadOnlyList<ForgeRosterCategories.Group> RosterGroups { get; private set; } = Array.Empty<ForgeRosterCategories.Group>();
    public Dictionary<string, (UnitFileEntry Unit, List<ItemEntry> Items)> RosterCards { get; private set; } = new();

    public BuilderList List { get; private set; }

    /// <summary>A roster unit being looked at (not in the army), or null.</summary>
    public string? SelectedRosterId { get; private set; }

    /// <summary>The army row being looked at, or null.</summary>
    public int? SelectedIndex { get; private set; }

    /// <summary>Switching game or faction starts a new list (its units belong to the old book).</summary>
    public void ChooseSystem(string slug)
    {
        if (GameSystems.SameSystem(slug, System)) return;
        System = slug;
        ChooseBook(BooksInSystem[0]);
    }

    public void ChooseBook(BookFile book)
    {
        UseBook(book);
        List = new BuilderList { PointsLimit = List.PointsLimit, BookName = Book.Name, Name = "" };
        SelectedIndex = null;
        SelectedRosterId = null;
        MarkClean();
    }

    private void UseBook(BookFile book)
    {
        Book = book;
        Glossary = RuleGlossary.Build(book);
        RosterGroups = ForgeRosterCategories.GroupBook(book);
        RosterCards = BuildRosterCards(book);
    }

    /// <summary>Every roster unit compiled at its book size, no upgrades - what its card shows. A unit the
    /// compiler cannot build still gets a card from the book's own numbers, so none silently vanish.</summary>
    public static Dictionary<string, (UnitFileEntry Unit, List<ItemEntry> Items)> BuildRosterCards(BookFile book)
    {
        var cards = new Dictionary<string, (UnitFileEntry, List<ItemEntry>)>();
        foreach (RosterUnit unit in book.Units)
        {
            try
            {
                cards[unit.Id] = ListCompiler.CompileUnitDetailed(book, new BuilderUnit { RosterUnitId = unit.Id, ModelCount = unit.BaseModelCount });
            }
            catch (Exception)
            {
                cards[unit.Id] = (new UnitFileEntry
                {
                    Name = unit.Name, ModelCount = unit.BaseModelCount, PointCost = unit.BasePointCost,
                    Quality = unit.Quality, Defense = unit.Defense,
                    Weapons = unit.Weapons.ToList(), SpecialRules = unit.Rules.ToList(),
                }, unit.Items.ToList());
            }
        }
        return cards;
    }

    public RosterUnit? Roster(string id) => Book.Units.FirstOrDefault(u => u.Id == id);

    public string CategoryOf(RosterUnit u) => ForgeRosterCategories.TitleFor(u, Book.GameSystem);

    // ---------------------------------------------------------------- the list

    public void SelectRoster(string rosterId) { SelectedRosterId = rosterId; SelectedIndex = null; }

    public void SelectRow(int index)
    {
        if (index < 0 || index >= List.Units.Count) return;
        SelectedIndex = index;
        SelectedRosterId = null;
    }

    public int Add(string rosterId)
    {
        int added = BuilderListEditing.AddUnit(Book, List, rosterId);
        if (added >= 0) SelectRow(added);
        return added;
    }

    public void Remove(int index)
    {
        if (!BuilderListEditing.RemoveUnit(List, index)) return;
        SelectedIndex = List.Units.Count == 0 ? null : Math.Min(SelectedIndex ?? 0, List.Units.Count - 1);
    }

    public BuiltArmyFile Compile() => ListCompiler.Compile(Book, List);

    /// <summary>One compiled unit per list row (combined copies stay two rows).</summary>
    public List<UnitFileEntry> Rows() => ListCompiler.CompileRows(Book, List);

    public IReadOnlyList<ListIssue> Issues(BuiltArmyFile compiled) => ListValidator.Validate(Book, List, compiled);

    public (UnitFileEntry Unit, List<ItemEntry> Items) Detail(int index) => ListCompiler.CompileUnitDetailed(Book, List.Units[index]);

    public int PartnerOf(int index) => BuilderListEditing.CombinePartnerIndex(List, index);

    public bool CanCombine(int index) => BuilderListEditing.CanCombine(Book, List, index);

    public void SetCombined(int index, bool on)
    {
        int selected = BuilderListEditing.SetCombined(Book, List, index, on);
        if (selected >= 0) SelectRow(selected);
    }

    /// <summary>"Retributors [5]", "Retributors [5] #2" ... for pickers.</summary>
    public static string RowLabel(IReadOnlyList<UnitFileEntry> rows, int idx)
    {
        UnitFileEntry unit = rows[idx];
        int nth = 0;
        for (int i = 0; i <= idx; i++) if (rows[i].Name == unit.Name) nth++;
        return nth > 1 ? $"{unit.Name} [{unit.ModelCount}] #{nth}" : $"{unit.Name} [{unit.ModelCount}]";
    }

    public bool HeroesMayJoin => HeroJoinResolver.JoinCapFor(Book.GameSystem) > 0;

    public List<int> HostsFor(int heroIndex, IReadOnlyList<UnitFileEntry> rows) => BuilderListEditing.HostCandidates(rows, heroIndex);

    public void Join(int heroIndex, int? hostIndex) =>
        List.Units[heroIndex].JoinsUnitId = hostIndex is int h ? BuilderListEditing.EnsureId(List.Units[h]) : null;

    // ---------------------------------------------------------------- upgrades

    public List<ForgeRules.SectionView> Sections(int index)
    {
        BuilderUnit bu = List.Units[index];
        if (Roster(bu.RosterUnitId) is not { } roster) return new();
        var (unit, items) = Detail(index);
        return ForgeRules.Sections(Book, bu, roster, unit, items, PartnerOf(index) >= 0);
    }

    /// <summary>Sets an option (count 0 clears it). A pick-one group swaps its pick; a whole-unit group of a
    /// combined pair is copied to the other half.</summary>
    public void Choose(int index, UpgradeSection section, string optionId, int count)
    {
        int partner = PartnerOf(index);
        BuilderUnit? mirror = partner >= 0 ? List.Units[partner] : null;
        BuilderListEditing.ApplyChoice(List.Units[index], mirror, section, optionId, count);
        TrimLooks(index);
    }

    /// <summary>The unit's choices as they were, to put back when the upgrade editor is cancelled.</summary>
    public List<List<UpgradeChoice>> Snapshot() =>
        List.Units.Select(u => u.Choices.Select(c => new UpgradeChoice { SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count }).ToList()).ToList();

    public void Restore(List<List<UpgradeChoice>> snapshot)
    {
        for (int i = 0; i < List.Units.Count && i < snapshot.Count; i++) List.Units[i].Choices = snapshot[i];
    }

    // ---------------------------------------------------------------- looks (Kai, 2026-11-12)

    /// <summary>One model of a unit as the look picker shows it: which list row owns it and where in that row.</summary>
    public readonly record struct ModelSlot(int Row, int Index, string? Look, IReadOnlyList<string> Special);

    /// <summary>Every model of the unit at <paramref name="index"/> in table order - a combined pair's both
    /// halves, the first copy first - with its look and any special kit it carries.</summary>
    public List<ModelSlot> Slots(int index)
    {
        int partner = PartnerOf(index);
        var rows = new List<int> { index };
        if (partner >= 0) rows = List.Units[index].CombinedWithId != null && List.Units[index].CombinedWithId != List.Units[index].Id
            ? new List<int> { partner, index } : new List<int> { index, partner };
        var slots = new List<ModelSlot>();
        foreach (int r in rows)
        {
            var (unit, _) = Detail(r);
            var special = ForgeRules.SpecialKit(unit);
            var looks = List.Units[r].ModelLooks;
            for (int i = 0; i < unit.ModelCount; i++)
                slots.Add(new ModelSlot(r, i, looks != null && i < looks.Count ? looks[i] : null, special[i]));
        }
        return slots;
    }

    /// <summary>Gives these models a look (a model file's name), or null for the first version.</summary>
    public void SetLook(IEnumerable<ModelSlot> slots, string? look)
    {
        foreach (ModelSlot s in slots)
        {
            BuilderUnit bu = List.Units[s.Row];
            bu.ModelLooks ??= new List<string?>();
            while (bu.ModelLooks.Count <= s.Index) bu.ModelLooks.Add(null);
            bu.ModelLooks[s.Index] = string.IsNullOrEmpty(look) ? null : look;
            if (bu.ModelLooks.All(l => l == null)) bu.ModelLooks = null;
        }
    }

    /// <summary>"5 models - 2 looks picked, 3 use the first version".</summary>
    public string LooksSummary(int index)
    {
        var slots = Slots(index);
        int picked = slots.Count(s => s.Look != null);
        string models = $"{slots.Count} model{(slots.Count == 1 ? "" : "s")}";
        if (picked == 0) return $"{models} - all use the first version";
        if (picked == slots.Count) return $"{models} - every model has a look";
        return $"{models} - {picked} picked, {slots.Count - picked} use the first version";
    }

    // A unit that lost models (an upgrade taken back) keeps no looks for models it no longer has.
    private void TrimLooks(int index)
    {
        foreach (int r in new[] { index, PartnerOf(index) })
        {
            if (r < 0 || List.Units[r].ModelLooks is not { } looks) continue;
            int n = Detail(r).Unit.ModelCount;
            if (looks.Count > n) looks.RemoveRange(n, looks.Count - n);
            if (looks.All(l => l == null)) List.Units[r].ModelLooks = null;
        }
    }

    /// <summary>The downloaded versions of a unit for this book, A-Z (the order "first version" means).</summary>
    public IReadOnlyList<string> Versions(UnitModelLibrary? library, string unitName) =>
        library?.FilesFor(Book.Faction.Length > 0 ? Book.Faction : Book.Name, unitName) ?? Array.Empty<string>();

    // ---------------------------------------------------------------- save / load

    public bool Dirty => Fingerprint() != _cleanFingerprint;

    private void MarkClean() => _cleanFingerprint = Fingerprint();

    private string Fingerprint() => JsonSerializer.Serialize(List, RuleJson.Options);

    public string SuggestedName()
    {
        if (!string.IsNullOrWhiteSpace(List.Name)) return List.Name;
        return string.IsNullOrWhiteSpace(Book.Name) ? "New Army" : Book.Name;
    }

    public void NewList()
    {
        List = new BuilderList { PointsLimit = List.PointsLimit, BookName = Book.Name };
        SelectedIndex = null;
        SelectedRosterId = null;
        MarkClean();
    }

    public EGameSystem GameSystemEnum => GameSystemInfo.FromSlug(Book.GameSystem) ?? EGameSystem.GrimdarkFuture;

    /// <summary>Where a list with this name would be written (its own game's armies folder), or null when there is
    /// no armies folder.</summary>
    public string? PathFor(string name)
    {
        if (ArmyPaths.FolderPath is not { } root) return null;
        string folder = Path.Combine(root, ArmyPaths.FolderNameFor(GameSystemEnum));
        return Path.Combine(folder, ForgeRules.SanitizeFileName(name) + ArmyListFile.EXTENSION_WITH_PERIOD);
    }

    /// <summary>Writes the list under <paramref name="name"/> into its game's folder. Returns the path, or throws
    /// with the reason (never reports a write that did not happen).</summary>
    public string Save(string name)
    {
        string path = PathFor(name) ?? throw new IOException("No armies folder was found to save into.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        List.Name = name;
        BuiltArmyFile compiled = Compile();
        compiled.Name = name;
        File.WriteAllText(path, JsonSerializer.Serialize(compiled, compiled.GetType(), RuleJson.Options));
        ArmyCatalogCache.Invalidate();
        MarkClean();
        return path;
    }

    /// <summary>The saved armies of the game being built, newest first.</summary>
    public List<ArmyCatalogEntry> SavedArmies() =>
        ArmyPaths.EntriesFor(GameSystemEnum, ArmyCatalogCache.Current.Entries)
            .OrderByDescending(e => File.Exists(e.Path) ? File.GetLastWriteTimeUtc(e.Path) : DateTime.MinValue).ToList();

    public sealed record LoadResult(BuiltArmyFile? File, string? Problem, string? DriftNote);

    /// <summary>Reads a saved army. <see cref="LoadResult.Problem"/> says why it can't be edited here;
    /// <see cref="LoadResult.DriftNote"/> (an Army Forge import whose rebuild differs) needs a yes before
    /// <see cref="Adopt"/>.</summary>
    public static LoadResult Read(string path)
    {
        BuiltArmyFile? loaded;
        try { loaded = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options); }
        catch (Exception e) { return new LoadResult(null, "That file could not be read: " + e.Message, null); }
        if (loaded == null) return new LoadResult(null, "That file is empty, or is not an army list.", null);
        if (loaded.Selections is null || loaded.Book is null)
            return new LoadResult(null, "That army has no book saved in it, so it can't be opened here for editing. It still plays normally.", null);
        EditableSessionDrift? drift = EditableSession.Measure(loaded);
        string? note = null;
        if (drift is { Differs: true })
        {
            var lines = new List<string>();
            if (drift.SavedPoints != drift.RebuiltPoints) lines.Add($"Points: {drift.SavedPoints} -> {drift.RebuiltPoints}");
            if (drift.SavedUnitCount != drift.RebuiltUnitCount) lines.Add($"Units: {drift.SavedUnitCount} -> {drift.RebuiltUnitCount}");
            if (drift.DroppedUnits.Count > 0) lines.Add("Dropped: " + string.Join(", ", drift.DroppedUnits));
            note = "This list came from the Army Forge website. Rebuilding it from our copy of the book changes it:\n"
                + string.Join("\n", lines) + "\n\nThe file is not changed until you save.";
        }
        return new LoadResult(loaded, null, note);
    }

    /// <summary>Makes a read army the list being edited, switching game and book to its own.</summary>
    public void Adopt(BuiltArmyFile loaded)
    {
        // The book saved inside the army (never silently re-pointed at a newer copy - #153 decision 6).
        BookFile book = loaded.Book!;
        if (Systems.FirstOrDefault(s => GameSystems.SameSystem(s, book.GameSystem)) is { } sys) System = sys;
        UseBook(book);
        List = loaded.Selections!;
        SelectedRosterId = null;
        SelectedIndex = List.Units.Count == 0 ? null : 0;
        MarkClean();
    }
}
