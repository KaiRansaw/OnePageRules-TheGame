using System.Text.Json;
using FdgRaylib.Cli;
using FdgRaylib.Config;
using FdgRaylib.Import;
using FdgRaylib.ListServer;
using FdgRaylib.Quest;
using FdgRaylib.Rendering;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Presentation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using TinyDialogsNet;

string crashLogPath = Path.Combine(AppContext.BaseDirectory, "crash.log");

void WriteCrash(string source, object? exObj)
{
    string text = $"=== {DateTime.Now:O}  {source} ===\n{exObj}\n\n";
    Console.Error.WriteLine(text);
    try { File.AppendAllText(crashLogPath, text); } catch { /* best-effort */ }
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    WriteCrash("AppDomain.UnhandledException", e.ExceptionObject);

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    WriteCrash("TaskScheduler.UnobservedTaskException", e.Exception);
    e.SetObserved();
};

// --field-harness (#162): offscreen GPU-vs-CPU field pixel diff. Hidden GL window, no game, exits with
// 0 iff every synthetic scene matches the CPU reference renderer within tolerance.
if (args.Contains("--field-harness"))
{
    Environment.Exit(FdgRaylib.Rendering.TacticalOverlay.FieldHarness.Run());
}

// #310: every install gets a config file (player name + the last hosted game's settings). Created with
// defaults on first run; best-effort, so a read-only home directory just means nothing is remembered.
UserConfig.EnsureExists();

bool headless = args.Contains("--headless");

// #354: let army load see the CURRENT rulebook, not just the frozen copy a saved .fdgarmy embeds, so a
// list saved before a rule was implemented picks that rule up instead of silently fielding nothing.
// Installed in every mode - this is army data, not a UI concern.
FdgRaylib.Import.BundledBookRulebook.Install();

// #168: in any windowed mode, collect the engine's rule-load warnings for the in-game log (they fire
// before the log exists and would otherwise only reach stdout, invisible in a GUI session). Headless
// keeps the channel's plain stdout fallback that automated runs grep.
if (!headless)
{
    FdgRaylib.Rendering.RuleLoadWarnings.Install();
}

// --trace-rules (#163): narrate every live rule hook evaluation (fired / condition failed / suppressed /
// ability offered) through the Debug log channel — printed as [LOG] lines headless, shown in the GUI
// console's Debug view. The GUI Debug toggle flips the same switch at runtime.
if (args.Contains("--trace-rules"))
{
    FDG.Rules.Dispatch.RuleTrace.Enabled = true;
}

// --slow [ms]  — pause N milliseconds before each resolver call (default 1500ms)
int slowDelayMs = 0;
int slowIdx = Array.IndexOf(args, "--slow");
if (slowIdx >= 0)
    slowDelayMs = slowIdx + 1 < args.Length && int.TryParse(args[slowIdx + 1], out int ms) ? ms : 1500;

// --seed <int> (#193): seeds the whole game — dice, decisive rolls, objective auto-placement, and each
// AI player's own stream. Same seed + same build => identical game. Overrides a seed saved in a scenario.
int? diceSeed = null;
int seedIdx = Array.IndexOf(args, "--seed");
if (seedIdx >= 0 && seedIdx + 1 < args.Length && int.TryParse(args[seedIdx + 1], out int parsedSeed))
    diceSeed = parsedSeed;

// --ai-profile <solorules|tactician> (#191): which AI drives the computer slots on the headless and
// scenario paths (lobby AI selection is a later slice, plan A6). Default: solo-rules.
var aiProfile = FDG.Ai.EAiProfile.SoloRules;
int profileIdx = Array.IndexOf(args, "--ai-profile");
if (profileIdx >= 0 && profileIdx + 1 < args.Length)
{
    if (!Enum.TryParse(args[profileIdx + 1], ignoreCase: true, out aiProfile))
    {
        Console.Error.WriteLine($"Unknown --ai-profile '{args[profileIdx + 1]}'. Known: " +
            string.Join(", ", Enum.GetNames<FDG.Ai.EAiProfile>()).ToLowerInvariant());
        Environment.Exit(2);
    }
}

// The flag's file arguments from startIdx up to the next --flag (#375: the supplement CLI flags take
// one or more supplement files).
static List<string> TrailingPaths(string[] args, int startIdx)
{
    List<string> paths = new();
    for (int i = startIdx; i < args.Length && !args[i].StartsWith("--"); i++)
        paths.Add(args[i]);
    return paths;
}

// --book-cache <dir> [--book-cache-only]  (#411): every Army Forge fetch reads <dir> first and saves
// what it does fetch back into it, so the whole importer family works from saved responses with no
// network. With --book-cache-only it never goes out at all, and a response that isn't saved yet is an
// error naming the URL and the exact file name to save it as. This is how the books get re-imported
// from a machine that CAN reach onepagerules.com when Claude's workspace cannot.
int bookCacheIdx = Array.IndexOf(args, "--book-cache");
if (bookCacheIdx >= 0 && bookCacheIdx + 1 < args.Length)
{
    FdgRaylib.Import.ArmyForgeBookService.CacheFolder = args[bookCacheIdx + 1];
    FdgRaylib.Import.ArmyForgeBookService.CacheOnly = args.Contains("--book-cache-only");
    Directory.CreateDirectory(args[bookCacheIdx + 1]);
}

// --book-urls <books-dir>  (#411): print every Army Forge URL the importers need, each with the file
// name its response must be saved as. Run it once to get the two index URLs; save those into the cache
// folder; run it again and it lists a URL per book, because the book uids come out of the indexes.
// --book-system-id <slug>=<id>  (#412): override an Army Forge game-system id. Repeatable. The
// skirmish systems' ids are OPR's documented ordering rather than something verified against the live
// API, so this is the one-flag fix if one turns out wrong, instead of a code change.
foreach (string arg in args)
{
    int eq = arg.IndexOf('=');
    int flag = Array.IndexOf(args, "--book-system-id");
    if (flag < 0 || eq <= 0 || Array.IndexOf(args, arg) != flag + 1) continue;
    if (int.TryParse(arg[(eq + 1)..], out int sysId))
    {
        FdgRaylib.Import.ArmyForgeBookService.SetGameSystemId(arg[..eq], sysId);
        Console.WriteLine($"Army Forge id for '{arg[..eq]}' set to {sysId}.");
    }
}

int bookUrlsIdx = Array.IndexOf(args, "--book-urls");
if (bookUrlsIdx >= 0 && bookUrlsIdx + 1 < args.Length)
{
    string booksDir = args[bookUrlsIdx + 1];
    var service = typeof(FdgRaylib.Import.ArmyForgeBookService);
    Console.WriteLine("# Army Forge responses the importers need.");
    Console.WriteLine("# Save each one's raw JSON into the --book-cache folder, under the name given.");
    Console.WriteLine();

    int listed = 0, pending = 0;
    foreach (string url in FdgRaylib.Import.ArmyForgeBookService.IndexUrls())
    {
        Console.WriteLine($"{FdgRaylib.Import.ArmyForgeBookService.CacheFileName(url)}\t{url}");
        listed++;
    }
    Console.WriteLine();

    string[] books = Directory.Exists(booksDir)
        ? Directory.EnumerateFiles(booksDir, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories).OrderBy(p => p).ToArray()
        : Array.Empty<string>();
    if (books.Length == 0) Console.WriteLine($"# No {BookFile.EXTENSION_WITH_PERIOD} files in '{booksDir}'.");
    var indexes = new Dictionary<string, IReadOnlyDictionary<string, string>?>(StringComparer.OrdinalIgnoreCase);
    foreach (string path in books)
    {
        BookFile book;
        try { book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!; }
        catch (Exception e) when (e is IOException or JsonException) { continue; }
        string system = FDG.ArmyBuilding.GameSystems.Normalize(book.GameSystem);
        if (!indexes.TryGetValue(system, out var index))
            indexes[system] = index = FdgRaylib.Import.ArmyForgeBookService.IndexFromCache(system);
        if (index == null) { pending++; continue; }
        if (!index.TryGetValue(book.Name, out string? uid))
        {
            Console.WriteLine($"# {book.Name}: no OPR official {system} book by that name.");
            continue;
        }
        string url = FdgRaylib.Import.ArmyForgeBookService.BookUrl(uid, book.GameSystem);
        string warn = FdgRaylib.Import.ArmyForgeBookService.IdIsVerified(book.GameSystem) ? ""
            : "  (game-system id unverified - see --book-system-id)";
        Console.WriteLine($"{FdgRaylib.Import.ArmyForgeBookService.CacheFileName(url)}\t{url}\t# {book.Name}{warn}");
        listed++;
    }
    if (pending > 0)
        Console.WriteLine($"{Environment.NewLine}# {pending} book(s) are waiting on an index above - save those two first, then run this again.");
    Console.WriteLine($"{Environment.NewLine}# {listed} URL(s) listed.");
    return;
}

// --book-diff <A> <B> [report.md]  (#412): compare two sets of .fdgbook files, unit by unit, and
// write a Markdown report of every difference - points, unit size, quality, defense, weapons, rules,
// upgrade options and their prices. Two uses, one mechanism:
//   * old books vs refreshed books - did the 3.6 refresh do what it should, and what moved?
//   * one game system's books vs another's - are Firefight and Grimdark Future really carrying their
//     own data (#406), or did one silently fall back to the other?
// A and B may each be a folder or a single .fdgbook. Books are paired by file name; one present in
// only one side is reported rather than skipped.
int bookDiffIdx = Array.IndexOf(args, "--book-diff");
if (bookDiffIdx >= 0 && bookDiffIdx + 2 < args.Length)
{
    string leftPath = args[bookDiffIdx + 1], rightPath = args[bookDiffIdx + 2];
    string? outPath = bookDiffIdx + 3 < args.Length && !args[bookDiffIdx + 3].StartsWith("--", StringComparison.Ordinal)
        ? args[bookDiffIdx + 3] : null;
    try
    {
        var left = LoadBookSet(leftPath);
        var right = LoadBookSet(rightPath);
        if (left.Count == 0 && right.Count == 0) { Console.WriteLine("No books found on either side."); Environment.Exit(1); }

        string leftLabel = SetLabel(left, leftPath), rightLabel = SetLabel(right, rightPath);
        var comparisons = BookDiff.CompareSets(left, right, leftLabel, rightLabel);
        string report = BookDiff.Report(comparisons, $"Army book comparison: {leftLabel} vs {rightLabel}");

        int touched = comparisons.Count(c => c.Any);
        Console.WriteLine($"{comparisons.Count} book(s) compared, {touched} differ.");
        Console.WriteLine($"  changed {comparisons.Sum(c => c.Changed)}, added {comparisons.Sum(c => c.Added)}, "
            + $"only-on-the-left {comparisons.Sum(c => c.Removed)}");
        if (outPath != null) { File.WriteAllText(outPath, report); Console.WriteLine($"Report written to {outPath}"); }
        else Console.WriteLine(report);
    }
    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
    {
        Console.WriteLine($"Book comparison failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;

    static Dictionary<string, BookFile> LoadBookSet(string target)
    {
        var set = new Dictionary<string, BookFile>(StringComparer.OrdinalIgnoreCase);
        string[] paths = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
                .OrderBy(p => p).ToArray()
            : File.Exists(target) ? new[] { target } : Array.Empty<string>();
        foreach (string path in paths)
        {
            try
            {
                if (JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options) is { } book)
                {
                    // Keyed by the path relative to the root, not the bare file name: the same book
                    // exists under several game-system folders and the two must not collide.
                    string key = Directory.Exists(target)
                        ? Path.GetRelativePath(target, path).Replace('\\', '/')
                        : Path.GetFileName(path);
                    set[key] = book;
                }
            }
            catch (JsonException) { Console.WriteLine($"  (skipped unreadable book {Path.GetFileName(path)})"); }
        }
        return set;
    }

    // The label a side gets in the report: its books' own version stamp when they agree, else the path.
    static string SetLabel(Dictionary<string, BookFile> set, string path)
    {
        var versions = set.Values.Select(b => b.Version).Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var systems = set.Values.Select(b => FDG.ArmyBuilding.GameSystems.DisplayName(b.GameSystem))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (systems.Count == 1 && versions.Count == 1) return $"{systems[0]} {versions[0]}";
        if (systems.Count == 1)
            // Mixed versions within one system (the bundled corpus has both 3.5.2 and 3.5.3): name the
            // system, and don't repeat it when the folder is already called that.
            return string.Equals(systems[0], folder, StringComparison.OrdinalIgnoreCase)
                ? systems[0] : $"{systems[0]} ({folder})";
        return folder;
    }
}

// --rebuild-books <cache-dir> <books-root> [--dry-run]  (#412): rebuild EVERY bundled book, for every
// game system, from Army Forge responses saved in the cache folder. This is the full rebuild, not the
// cost refresh: --import-book only transfers upgrade prices, so it leaves a unit's own points, quality,
// defense, size, weapons, rules and any unit added or removed in a new OPR version untouched. Running
// that alone makes the data LOOK current when it is not. --import-opr regenerates the whole book, and
// the curated rule behaviour survives because it lives in the supplement files, which are re-embedded
// here exactly as scripts/bake-aof-books.sh did it per book.
//
// Nothing is fetched: save the responses first with --book-urls and --book-cache (see
// claude/army-forge-refresh.md). A book whose response is missing is REPORTED, never silently skipped.
// An existing book of the same name keeps its file name, so --book-diff pairs old against new.
int rebuildIdx = Array.IndexOf(args, "--rebuild-books");
if (rebuildIdx >= 0 && rebuildIdx + 2 < args.Length)
{
    string cacheDir = args[rebuildIdx + 1];
    string booksRoot = args[rebuildIdx + 2];
    bool dryRun = args.Contains("--dry-run");

    // #412: curation inputs (the rule supplements, the per-book AoF overrides, BookRenames.json) are
    // SOURCE, and the books root is OUTPUT. Reading them out of the output folder - which is what this
    // used to do - means a rebuild into a scratch directory silently bakes whatever stale copy of the
    // supplement happens to be sitting there, and the rebuilt books then disagree with the repo's
    // curation with nothing to show it went wrong. So they come from the shipped Assets/Books by
    // default; --curation <dir> overrides, and the books root is the last resort for a standalone
    // bundle that carries its own copies.
    int curationIdx = Array.IndexOf(args, "--curation");
    string shippedBooks = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
    string curationRoot =
        curationIdx >= 0 && curationIdx + 1 < args.Length ? args[curationIdx + 1]
        : File.Exists(Path.Combine(shippedBooks, "GdfRuleSupplement.json")) ? shippedBooks
        : booksRoot;
    Console.WriteLine($"Curation from: {curationRoot}");
    FdgRaylib.Import.ArmyForgeBookService.CacheFolder = cacheDir;
    FdgRaylib.Import.ArmyForgeBookService.CacheOnly = true;

    // slug -> the folder its books live in. Deliberately not DisplayName: "GF: Firefight" has a colon.
    var systemFolders = new (string Slug, string Folder, string Prefix)[]
    {
        (FDG.ArmyBuilding.GameSystems.GrimdarkFuture, "Grimdark Future", ""),
        (FDG.ArmyBuilding.GameSystems.GFFirefight, "GF Firefight", "GFF-"),
        (FDG.ArmyBuilding.GameSystems.AgeOfFantasy, "Age of Fantasy", "AoF-"),
        (FDG.ArmyBuilding.GameSystems.AoFSkirmish, "AoF Skirmish", "AoFS-"),
    };

    int built = 0, pending = 0, failed = 0, noIndex = 0, skipped = 0;
    var missing = new List<string>();

    // #412: OPR renames factions between ruleset versions, and the index is keyed by name - so a
    // renamed book stops matching the file we already have, gets written out a second time under its
    // new name, and leaves the old file behind as a stale duplicate of the same faction. The map says
    // which of our names OPR now publishes under; the rebuild keeps the original file and records the
    // old name in previousNames so saved armies and share links still resolve.
    var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    string renamePath = Path.Combine(curationRoot, "BookRenames.json");
    if (File.Exists(renamePath))
        try
        {
            using JsonDocument renameDoc = JsonDocument.Parse(File.ReadAllText(renamePath));
            if (renameDoc.RootElement.TryGetProperty("renames", out JsonElement renameList))
                foreach (JsonElement e in renameList.EnumerateArray())
                    if (e.TryGetProperty("from", out JsonElement f) && e.TryGetProperty("to", out JsonElement t)
                        && f.GetString() is { Length: > 0 } fromName && t.GetString() is { Length: > 0 } toName)
                        renames[fromName] = toName;
            if (renameDoc.RootElement.TryGetProperty("skip", out JsonElement skipList))
                foreach (JsonElement e in skipList.EnumerateArray())
                    if (e.TryGetProperty("name", out JsonElement n) && n.GetString() is { Length: > 0 } skipName)
                        skipNames.Add(skipName);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"BookRenames.json is malformed, continuing without it: {ex.Message}");
        }

    foreach ((string slug, string folder, string prefix) in systemFolders)
    {
        var index = FdgRaylib.Import.ArmyForgeBookService.IndexFromCache(slug);
        string systemDir = Path.Combine(booksRoot, folder);
        Console.WriteLine($"== {FDG.ArmyBuilding.GameSystems.DisplayName(slug)}  ({folder})");
        if (!FdgRaylib.Import.ArmyForgeBookService.IdIsVerified(slug))
            Console.WriteLine($"   note: Army Forge id {FdgRaylib.Import.ArmyForgeBookService.IdFor(slug)} is unverified "
                + "- check one unit against the site, or override with --book-system-id.");
        if (index == null)
        {
            string url = FdgRaylib.Import.ArmyForgeBookService.IndexUrl(slug);
            Console.WriteLine($"   no index saved yet - save {FdgRaylib.Import.ArmyForgeBookService.CacheFileName(url)}");
            missing.Add($"{FdgRaylib.Import.ArmyForgeBookService.CacheFileName(url)}\t{url}");
            noIndex++;
            continue;
        }

        // Existing books by their own Name, so a rebuild keeps each one's file name.
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(systemDir))
            foreach (string path in Directory.EnumerateFiles(systemDir, "*" + BookFile.EXTENSION_WITH_PERIOD,
                         SearchOption.AllDirectories))
                try
                {
                    if (JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options) is { } b)
                        existing[b.Name] = path;
                }
                catch (JsonException) { /* a malformed existing book just means no name to reuse */ }

        // #412: our old names for a book OPR now publishes under a different one, so the rebuild can
        // find the file we already have. The same new name can come from two old ones (Grimdark
        // Future's "Wormhole Daemons of Change" and Age of Fantasy's "Rift Daemons of Change" are both
        // "Daemons of Change" now), but only one of them exists in any one system's folder.
        List<string> OldNamesFor(string currentName) =>
            renames.Where(kv => string.Equals(kv.Value, currentName, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList();

        foreach ((string bookName, string uid) in index.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (skipNames.Contains(bookName))
            {
                Console.WriteLine($"   {bookName}: skipped - listed in {Path.GetFileName(renamePath)}");
                skipped++;
                continue;
            }
            string url = FdgRaylib.Import.ArmyForgeBookService.BookUrl(uid, slug);
            string cached = Path.Combine(cacheDir, FdgRaylib.Import.ArmyForgeBookService.CacheFileName(url));
            if (!File.Exists(cached))
            {
                Console.WriteLine($"   {bookName}: not saved yet");
                missing.Add($"{Path.GetFileName(cached)}\t{url}\t# {bookName}");
                pending++;
                continue;
            }

            try
            {
                BookFile book = OprBookImporter.Import(File.ReadAllText(cached),
                    source: "OnePageRules - Army Forge (army-forge.onepagerules.com)",
                    license: "CC-BY-SA 4.0",
                    warn: msg => Console.WriteLine($"      {msg}"));
                book.GameSystem = slug == FDG.ArmyBuilding.GameSystems.GrimdarkFuture ? null : slug;

                // #412: an index entry with no units is not a book - OPR 3.6.0 split the daemon books
                // into four per-god books each and left the parent entry behind with an empty roster.
                // Writing it out would put an unplayable faction in the Army Forge screen.
                if (book.Units.Count == 0)
                {
                    Console.WriteLine($"   {bookName}: skipped - the live book has no units");
                    skipped++;
                    continue;
                }

                // #412: the file we already have for this faction, under whatever name it went by then.
                string? priorName = existing.ContainsKey(book.Name)
                    ? book.Name
                    : OldNamesFor(book.Name).FirstOrDefault(existing.ContainsKey);
                if (priorName != null && !string.Equals(priorName, book.Name, StringComparison.OrdinalIgnoreCase))
                    book.PreviousNames = new List<string> { priorName };

                // The supplements, in the order scripts/bake-aof-books.sh established: the GDF set
                // always, then AoF's for the fantasy systems, then the per-book override LAST so its
                // redefinitions win (#375 C9).
                var supplements = new List<string> { Path.Combine(curationRoot, "GdfRuleSupplement.json") };
                bool fantasy = slug is FDG.ArmyBuilding.GameSystems.AgeOfFantasy or FDG.ArmyBuilding.GameSystems.AoFSkirmish;
                if (fantasy) supplements.Add(Path.Combine(curationRoot, "AofRuleSupplement.json"));
                // #412: the override files are named for the book as it was called when they were
                // written, so fall back through the names this faction has gone by.
                foreach (string candidate in new[] { book.Name }.Concat(OldNamesFor(book.Name)))
                {
                    string overridePath = Path.Combine(curationRoot, "AofBookOverrides", candidate + ".json");
                    if (fantasy && File.Exists(overridePath)) { supplements.Add(overridePath); break; }
                }
                supplements.RemoveAll(sp => !File.Exists(sp));
                if (supplements.Count > 0)
                {
                    var merged = RuleSupplementSet.LoadMerged(supplements);
                    BookRuleSupplement.Apply(book, merged, msg => Console.WriteLine($"      {msg}"));
                }

                // #412: the other two curation passes the original bake applied. Both are deterministic
                // functions of the book, which is why a wholesale rebuild is safe - but they are not
                // optional. Without the first, every rebuilt book loses the faction default effect sets
                // #239 stamped; without the second, every rectangular base goes back to OPR's
                // length-first spec and faces across its short axis (#225).
                WeaponEffectAssigner.ApplyToBook(book);
                var baseEstimates = new List<string>();
                BaseOrientationRetrofit.ApplyToBook(book, baseEstimates);

                string outPath = priorName != null && existing.TryGetValue(priorName, out string? kept)
                    ? kept
                    : Path.Combine(systemDir, prefix
                        + new string(book.Name.Where(char.IsLetterOrDigit).ToArray())
                        + BookFile.EXTENSION_WITH_PERIOD);

                if (!dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                    File.WriteAllText(outPath, JsonSerializer.Serialize(book, RuleJson.Options));
                }
                Console.WriteLine($"   {book.Name} {book.Version}: {book.Units.Count} units, {book.Spells.Count} spells"
                    + $" -> {Path.GetFileName(outPath)}{(dryRun ? "  (dry run)" : "")}"
                    + (book.PreviousNames is { Count: > 0 } pn ? $"   [OPR renamed '{pn[0]}']" : string.Empty)
                    + (baseEstimates.Count > 0 ? $"   ({baseEstimates.Count} base(s) estimated)" : string.Empty));
                built++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                Console.WriteLine($"   {bookName}: FAILED - {ex.Message}");
                failed++;
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{built} book(s) {(dryRun ? "would be rebuilt" : "rebuilt")}, {pending} waiting on a saved "
        + $"response, {skipped} skipped, {failed} failed, {noIndex} system(s) with no index saved.");
    if (missing.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("# Save these, then run this again:");
        foreach (string line in missing) Console.WriteLine(line);
    }
    if (!dryRun && built > 0)
        Console.WriteLine($"{Environment.NewLine}Now compare: --book-diff <the old books> {booksRoot} report.md");
    Environment.Exit(failed > 0 ? 1 : 0);
}

// --import-opr <in.json> <out.fdgbook> [supplement.json ...]  (#153 P0b): one-time OnePageRules Army
// Forge JSON → .fdgbook snapshot, via the engine importer. Data is OPR's, used under CC-BY-SA (stamped
// on the book). Optional supplements embed curated rule definitions the book references (see
// --apply-rules); multiple files merge later-wins by name (#375).
int importIdx = Array.IndexOf(args, "--import-opr");
if (importIdx >= 0 && importIdx + 2 < args.Length)
{
    string inJson = args[importIdx + 1];
    string outPath = args[importIdx + 2];
    BookFile book = OprBookImporter.Import(File.ReadAllText(inJson),
        source: "OnePageRules - Army Forge (army-forge.onepagerules.com)",
        license: "CC-BY-SA 4.0",
        warn: msg => Console.WriteLine($"  {msg}"));
    var importSupplementPaths = TrailingPaths(args, importIdx + 3);
    if (importSupplementPaths.Count > 0)
    {
        var supplement = RuleSupplementSet.LoadMerged(importSupplementPaths);
        var embedded = BookRuleSupplement.Apply(book, supplement, msg => Console.WriteLine($"  {msg}"));
        Console.WriteLine($"  supplement: embedded {embedded.Count} rule definitions ({string.Join(", ", embedded)})");
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, JsonSerializer.Serialize(book, RuleJson.Options));
    Console.WriteLine($"Imported '{book.Name}' {book.Version}: {book.Units.Count} units, {book.Spells.Count} spells -> {outPath}");
    return;
}

// --import-army <share-link-or-id> <out.fdgarmy>  (#241): fetch an Army Forge share list and write a
// playable army file (same pipeline as the Forge screen's Import Link button). Refuses on an OPR
// version / game-system mismatch. Exit 0 on success, 1 on any failure.
int importArmyIdx = Array.IndexOf(args, "--import-army");
if (importArmyIdx >= 0 && importArmyIdx + 2 < args.Length)
{
    try
    {
        var outcome = FdgRaylib.Import.ArmyForgeShareService
            .FetchAndImportAsync(args[importArmyIdx + 1]).GetAwaiter().GetResult();
        foreach (string w in outcome.Result.Warnings) Console.WriteLine($"  warning: {w}");
        foreach (string e in outcome.Result.ListErrors) Console.WriteLine($"  Army Forge list error: {e}");
        if (outcome.InertRules.Count > 0)
            Console.WriteLine($"  not enforced by engine: {string.Join(", ", outcome.InertRules)}");

        // #241 v2: pricing reconciliation - our ListCompiler vs Army Forge (a #218/#219 detector).
        if (outcome.ForgeSession is { } session)
        {
            foreach ((string name, int pts) in session.ExcludedUnits)
                Console.WriteLine($"  excluded (not in bundled book): {name} ({pts} pts)");
            foreach ((string name, int ours, int theirs) in session.UnitPointsDeltas)
                Console.WriteLine($"  base cost drift: {name} - bundled book {ours} pts, Army Forge {theirs} pts");
            if (session.UnpricedUpgradeCount > 0)
                Console.WriteLine($"  {session.UnpricedUpgradeCount} selected upgrade(s) have no published " +
                    "Army Forge price - our total counts them as free (#219)");
            Console.WriteLine(session.OurTotalPoints == session.TheirTotalPoints
                ? $"  points check: OK ({session.OurTotalPoints} pts both ways)"
                : $"  points check: our Forge {session.OurTotalPoints} pts vs Army Forge " +
                  $"{session.TheirTotalPoints} pts" +
                  (session.UnpricedUpgradeCount > 0 ? " (expected - unpriced upgrades above)" : ""));
        }

        string outArmyPath = args[importArmyIdx + 2];
        if (Path.GetExtension(outArmyPath) != ArmyListFile.EXTENSION_WITH_PERIOD)
            outArmyPath = Path.ChangeExtension(outArmyPath, ArmyListFile.EXTENSION_WITH_PERIOD);
        string? outArmyDir = Path.GetDirectoryName(Path.GetFullPath(outArmyPath));
        if (!string.IsNullOrEmpty(outArmyDir)) Directory.CreateDirectory(outArmyDir);
        File.WriteAllText(outArmyPath, JsonSerializer.Serialize(outcome.Result.Army, RuleJson.Options));
        Console.WriteLine($"Imported '{outcome.Result.Army.Name}' ({outcome.Result.Army.Faction}): " +
            $"{outcome.Result.Army.Units.Count} units, {outcome.Result.Army.TotalPoints} pts -> {outArmyPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Import failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --import-book <book.fdgbook | dir>  (#219): refresh the "unpriced upgrade" flags on a bundled book (or
// every .fdgbook in a directory) from OPR's live army-book endpoint. Re-imports to a throwaway book and
// copies only costUnpriced onto the existing snapshot by option Id - all other curation (effect sets,
// embedded rules, base retrofits) is preserved. Writes in place. Exit 0 on success, 1 on any failure.
int importBookIdx = Array.IndexOf(args, "--import-book");
if (importBookIdx >= 0 && importBookIdx + 1 < args.Length)
{
    try
    {
        string target = args[importBookIdx + 1];
        string[] bookPaths = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories).OrderBy(p => p).ToArray()
            : new[] { target };
        if (bookPaths.Length == 0) { Console.WriteLine($"No {BookFile.EXTENSION_WITH_PERIOD} files in '{target}'."); return; }

        // #378: the official-book index is per game system - fetched lazily so a GDF-only run never
        // touches the AoF endpoint (and vice versa). A book with no GameSystem field is GDF.
        var indexBySystem = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        int totalPriced = 0, missing = 0;
        foreach (string path in bookPaths)
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string system = FDG.ArmyBuilding.GameSystems.Normalize(book.GameSystem);
            if (!indexBySystem.TryGetValue(system, out var index))
                indexBySystem[system] = index =
                    FdgRaylib.Import.ArmyForgeBookService.FetchBookIndexAsync(system).GetAwaiter().GetResult();
            if (!index.TryGetValue(book.Name, out string? uid))
            {
                Console.WriteLine($"  {book.Name}: no OPR official {system} book by that name - skipped.");
                missing++;
                continue;
            }
            string raw = FdgRaylib.Import.ArmyForgeBookService.FetchBookJsonAsync(uid, system).GetAwaiter().GetResult();
            var report = FdgRaylib.Import.ArmyForgeBookService.RefreshCosts(book, raw);
            File.WriteAllText(path, JsonSerializer.Serialize(book, RuleJson.Options));
            totalPriced += report.Priced;
            Console.WriteLine($"  {report.BookName}: recovered {report.Priced} previously-unpriced cost(s), "
                + $"repriced {report.Repriced}, {report.Unmatched} option(s) not in the live book.");
            foreach (string s in report.Samples) Console.WriteLine($"      {s}");
        }
        Console.WriteLine($"Done: {bookPaths.Length - missing}/{bookPaths.Length} book(s) refreshed, "
            + $"{totalPriced} option(s) got a recovered price.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Book refresh failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --import-section-shapes <book.fdgbook | dir>  (#383): re-stamp the per-model "Any model may ..."
// upgrade sections (Affects=Any + perModelBudget) on a bundled book (or every .fdgbook in a directory)
// from OPR's live army-book endpoint - the classification needs the raw `select`/`model` fields the
// snapshot never stored. Same transfer discipline as --import-book: matched by (unit, section) Id,
// everything else on the snapshot preserved. Writes in place. Exit 0 on success, 1 on any failure.
int shapesIdx = Array.IndexOf(args, "--import-section-shapes");
if (shapesIdx >= 0 && shapesIdx + 1 < args.Length)
{
    try
    {
        string target = args[shapesIdx + 1];
        string[] bookPaths = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories).OrderBy(p => p).ToArray()
            : new[] { target };
        if (bookPaths.Length == 0) { Console.WriteLine($"No {BookFile.EXTENSION_WITH_PERIOD} files in '{target}'."); return; }

        // #378: per-system index, lazily fetched - same discipline as --import-book above.
        var indexBySystem = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        int totalStamped = 0, missing = 0;
        foreach (string path in bookPaths)
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string system = FDG.ArmyBuilding.GameSystems.Normalize(book.GameSystem);
            if (!indexBySystem.TryGetValue(system, out var index))
                indexBySystem[system] = index =
                    FdgRaylib.Import.ArmyForgeBookService.FetchBookIndexAsync(system).GetAwaiter().GetResult();
            if (!index.TryGetValue(book.Name, out string? uid))
            {
                Console.WriteLine($"  {book.Name}: no OPR official {system} book by that name - skipped.");
                missing++;
                continue;
            }
            string raw = FdgRaylib.Import.ArmyForgeBookService.FetchBookJsonAsync(uid, system).GetAwaiter().GetResult();
            var report = FdgRaylib.Import.ArmyForgeBookService.RefreshPerModelSections(book, raw);
            if (report.Stamped > 0)
                File.WriteAllText(path, JsonSerializer.Serialize(book, RuleJson.Options));
            totalStamped += report.Stamped;
            Console.WriteLine($"  {report.BookName}: stamped {report.Stamped} per-model section(s), "
                + $"{report.Unmatched} section(s) not in the live book.");
            foreach (string s in report.Samples) Console.WriteLine($"      {s}");
        }
        Console.WriteLine($"Done: {bookPaths.Length - missing}/{bookPaths.Length} book(s) checked, "
            + $"{totalStamped} section(s) stamped per-model.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Section-shape refresh failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --import-spells <snapshotDir> <book.fdgbook | dir>  (#377): re-stamp ONLY the spells (and their
// synthesized "<Spell> Effect" definitions) on a bundled book (or every .fdgbook in a directory) from
// the off-repo OPR JSON snapshots, matched by book name -> "<Name>.json". Same targeted-stamp
// discipline as --import-section-shapes: the bundled books carry post-bake live-API passes (#219
// prices, #383 shapes) that a full re-import would wipe. Writes in place only when spells changed.
int spellsIdx = Array.IndexOf(args, "--import-spells");
if (spellsIdx >= 0 && spellsIdx + 2 < args.Length)
{
    try
    {
        string snapshotDir = args[spellsIdx + 1];
        string target = args[spellsIdx + 2];
        string[] bookPaths = Directory.Exists(target)
            ? Directory.EnumerateFiles(target, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories).OrderBy(p => p).ToArray()
            : new[] { target };
        if (bookPaths.Length == 0) { Console.WriteLine($"No {BookFile.EXTENSION_WITH_PERIOD} files in '{target}'."); return; }

        int totalStamped = 0, missing = 0;
        foreach (string path in bookPaths)
        {
            string before = File.ReadAllText(path);
            BookFile book = JsonSerializer.Deserialize<BookFile>(before, RuleJson.Options)!;
            string snapshotPath = Path.Combine(snapshotDir, book.Name + ".json");
            if (!File.Exists(snapshotPath))
            {
                Console.WriteLine($"  {book.Name}: no snapshot '{snapshotPath}' - skipped.");
                missing++;
                continue;
            }
            int stamped = OprBookImporter.RestampSpells(book, File.ReadAllText(snapshotPath),
                msg => Console.WriteLine($"  {msg}"));
            string after = JsonSerializer.Serialize(book, RuleJson.Options);
            if (after != before) File.WriteAllText(path, after);
            totalStamped += stamped;
            Console.WriteLine($"  {book.Name}: {stamped} spell(s) stamped" +
                (after == before ? " (unchanged)" : ""));
        }
        Console.WriteLine($"Done: {bookPaths.Length - missing}/{bookPaths.Length} book(s) stamped, " +
            $"{totalStamped} spell(s).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Spell re-stamp failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --import-starquest <out.json> [--cache <dir>] [--diff <report.md>]  (#415): rebuild the Hero Builder's
// Star Quest roster from Army Forge's game-system-9 endpoint - every official faction's hero units, their
// Star Quest points, weapons, gear and upgrade packages. Writes the file whole (there is nothing
// hand-curated in it), then reports what moved against the copy that was there before.
int sqHeroesIdx = Array.IndexOf(args, "--import-starquest");
if (sqHeroesIdx >= 0 && sqHeroesIdx + 1 < args.Length)
{
    try
    {
        string outPath = args[sqHeroesIdx + 1];
        int cacheIdx = Array.IndexOf(args, "--cache");
        string? cacheDir = cacheIdx >= 0 && cacheIdx + 1 < args.Length ? args[cacheIdx + 1] : null;
        int diffIdx = Array.IndexOf(args, "--diff");
        string? diffPath = diffIdx >= 0 && diffIdx + 1 < args.Length ? args[diffIdx + 1] : null;

        SqHeroFile? before = StarQuestHeroImporter.ReadFile(outPath);
        StarQuestHeroImporter.RunReport report =
            StarQuestHeroImporter.RunAsync(outPath, cacheDir, Console.WriteLine).GetAwaiter().GetResult();

        Console.WriteLine($"Wrote {outPath}: {report.Factions}/{report.Books} faction(s), " +
            $"{report.Heroes} hero(es), {report.Packages} package(s), {report.Options} upgrade option(s).");
        if (report.Skipped.Count > 0)
            Console.WriteLine($"  No heroes (skipped): {string.Join(", ", report.Skipped)}");

        if (diffPath is not null)
        {
            SqHeroFile? after = StarQuestHeroImporter.ReadFile(outPath);
            if (after is not null)
            {
                File.WriteAllText(diffPath, StarQuestHeroDiff.Report(before, after));
                Console.WriteLine($"Comparison written to {diffPath}.");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Star Quest hero import failed: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --apply-rules <book.fdgbook> <supplement.json ...>  (#153): merge curated rule definitions into an
// existing book snapshot in place — the definitions the book references (plus what those grant) embed
// into the book's ruleDefinitions, replace-by-name, so re-applying after editing the supplement is
// idempotent. Multiple supplement files merge later-wins by name before the bake (#375: AoF books bake
// against GDF + AoF, so an AoF redefinition of a shared name wins; GDF books keep GDF alone).
// Validation is hard-fail; an invalid supplement leaves the book untouched.
int applyIdx = Array.IndexOf(args, "--apply-rules");
if (applyIdx >= 0 && applyIdx + 2 < args.Length)
{
    string bookPath = args[applyIdx + 1];
    BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(bookPath), RuleJson.Options)!;
    var supplement = RuleSupplementSet.LoadMerged(TrailingPaths(args, applyIdx + 2));
    var embedded = BookRuleSupplement.Apply(book, supplement, msg => Console.WriteLine($"  {msg}"));
    File.WriteAllText(bookPath, JsonSerializer.Serialize(book, RuleJson.Options));
    Console.WriteLine($"'{book.Name}': embedded {embedded.Count} rule definitions " +
        $"({string.Join(", ", embedded)}) -> {bookPath}");
    return;
}

// --validate-rules <supplement.json ...>  (#153): authoring aid — strict-parse the supplement(s) and
// validate every definition (hook/capability fit, granted names resolve, no duplicates) without touching
// a book. Multiple files validate as one later-wins merged universe (#375), so an AoF definition may
// grant a GDF-supplement name; validate a file alone to check it is self-contained.
int validateIdx = Array.IndexOf(args, "--validate-rules");
if (validateIdx >= 0 && validateIdx + 1 < args.Length)
{
    var supplement = RuleSupplementSet.LoadMerged(TrailingPaths(args, validateIdx + 1));
    var problems = BookRuleSupplement.ValidateAll(supplement);
    foreach (string problem in problems)
        Console.WriteLine($"  {problem}");
    Console.WriteLine(problems.Count == 0
        ? $"OK: {supplement.Count} definitions, no problems."
        : $"{problems.Count} problem(s) in {supplement.Count} definitions.");
    return;
}

// --rule-coverage <booksDir>  (#196 slice 1 / SYS-5): the import reconciliation report the audit asked
// for. Mirrors what army load actually does — CoreRuleCatalog + each book's own embedded rule
// definitions, walked over every reference at its real attachment scope (unit rules/items, weapon
// profiles, and the same three inside every upgrade option) — so a name with no definition anywhere and
// a name whose definition disagrees with its attachment scope are reported separately, and a name that
// resolves cleanly (including a weapon-scoped rule reached via a unit-level wargear bundle, which #197
// slice 0 made a legal attach, not a mismatch) is not reported at all.
int coverageIdx = Array.IndexOf(args, "--rule-coverage");
if (coverageIdx >= 0 && coverageIdx + 1 < args.Length)
{
    RuleCoverageReport.Run(args[coverageIdx + 1]);
    return;
}

// --book-to-army <book.fdgbook> <out.fdgarmy>  (#153): dev/verify — compile every unit of a book at base size,
// proving the whole book compiles; writes a small (first-few-units) playable army for a headless smoke.
int b2aIdx = Array.IndexOf(args, "--book-to-army");
if (b2aIdx >= 0 && b2aIdx + 2 < args.Length)
{
    BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(args[b2aIdx + 1]), RuleJson.Options)!;

    BuilderList Base(IEnumerable<FDG.SaveLoad.UnitFileEntry>? _ = null, int take = int.MaxValue)
    {
        var list = new BuilderList { Name = book.Name, BookName = book.Name, PointsLimit = 100000 };
        foreach (RosterUnit u in book.Units.Take(take))
            list.Units.Add(new BuilderUnit { RosterUnitId = u.Id, ModelCount = u.BaseModelCount });
        return list;
    }

    BuiltArmyFile all = ListCompiler.Compile(book, Base());               // proves every unit compiles
    BuiltArmyFile small = ListCompiler.Compile(book, Base(take: 4));      // small, playable for a smoke
    File.WriteAllText(args[b2aIdx + 2], JsonSerializer.Serialize(small, RuleJson.Options));
    Console.WriteLine($"'{book.Name}': all {all.Units.Count} units compiled ({all.TotalPoints} pts); wrote {small.Units.Count}-unit army -> {args[b2aIdx + 2]}");
    return;
}

// --retrofit-editable <fileOrDir> [more...]  (#357): make already-saved armies re-editable in the Forge.
// A plain .fdgarmy stores the RESULT of its upgrade picks, never the picks, so the Forge cannot reopen it
// (#307). This solves the picks back out against the bundled book (SelectionSolver) and attaches them as an
// editable session, leaving the playable half byte-for-byte alone. Idempotent - a file that already carries
// a session is skipped. Writes in place ONLY when every unit solved exactly; add --dry-run to report first.
int editableIdx = Array.IndexOf(args, "--retrofit-editable");
if (editableIdx >= 0 && editableIdx + 1 < args.Length)
{
    bool dryRun = args.Contains("--dry-run");
    List<string> targets = args.Skip(editableIdx + 1).TakeWhile(a => !a.StartsWith("--"))
        .SelectMany(t => Directory.Exists(t)
            ? Directory.GetFiles(t, "*" + ArmyListFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
                .OrderBy(p => p).ToArray()
            : new[] { t })
        .ToList();

    var books = new List<BookFile>();
    string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
    if (Directory.Exists(booksDir))
        foreach (string p in Directory.EnumerateFiles(booksDir, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories).OrderBy(p => p))
            try
            {
                BookFile? b = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(p), RuleJson.Options);
                if (b is not null) books.Add(b);
            }
            catch { /* a malformed book just means one fewer faction can be matched */ }

    int converted = 0, alreadyEditable = 0, failed = 0;
    foreach (string path in targets)
    {
        string name = Path.GetFileName(path);
        BuiltArmyFile? army;
        try { army = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options); }
        catch (Exception ex) { Console.WriteLine($"  UNREADABLE {name}: {ex.Message}"); failed++; continue; }

        if (army is null) { Console.WriteLine($"  UNREADABLE {name}: not an army list."); failed++; continue; }
        if (army.Selections is not null && army.Book is not null)
        {
            Console.WriteLine($"  already editable: {name}");
            alreadyEditable++;
            continue;
        }

        // #378: the faction-name match is gated on the army's game system (absent = GDF), because four
        // AoF faction names collide with GDF books and the wrong book solves to the wrong upgrades.
        BookFile? book = books.FirstOrDefault(b =>
            (string.Equals(b.Faction, army.Faction, StringComparison.OrdinalIgnoreCase)
             || string.Equals(b.Name, army.Faction, StringComparison.OrdinalIgnoreCase))
            && FDG.ArmyBuilding.GameSystems.SameSystem(b.GameSystem, army.GameSystem));
        if (book is null)
        {
            Console.WriteLine($"  NO BOOK    {name}: no bundled book matches faction '{army.Faction}'.");
            failed++;
            continue;
        }

        ArmySolve solve = SelectionSolver.Solve(book, army);
        if (!solve.Complete)
        {
            Console.WriteLine($"  UNSOLVED   {name}: {solve.SolvedCount}/{solve.Units.Count} units solved.");
            foreach (UnitSolve u in solve.Units.Where(u => !u.Solved))
                Console.WriteLine($"               - {u.UnitName}: {u.Failure}");
            failed++;
            continue;
        }

        BuiltArmyFile attached = EditableSession.Attach(army, solve.Selections!, book);

        // Hard guard: this tool ADDS an editable session, it never re-prices or re-shapes the army. Compare
        // the playable projection (the base-type view, which is all the engine ever reads) before and after,
        // and refuse to write if a single byte moved.
        string before = JsonSerializer.Serialize<ArmyListFile>(army, RuleJson.Options);
        string after = JsonSerializer.Serialize<ArmyListFile>(attached, RuleJson.Options);
        if (before != after)
        {
            Console.WriteLine($"  REFUSED    {name}: the playable half would change - not written.");
            failed++;
            continue;
        }

        if (!dryRun) File.WriteAllText(path, JsonSerializer.Serialize(attached, attached.GetType(), RuleJson.Options));
        Console.WriteLine($"  {(dryRun ? "would convert" : "converted")}: {name} ({solve.Units.Count} units)");

        // What reopening it in the Forge would change (#356 shows the same figures in its modal). Nonzero is
        // expected on an OPR import - their points are theirs - but the user should see it before playing.
        EditableSessionDrift? drift = EditableSession.Measure(attached);
        if (drift is { Differs: true })
            Console.WriteLine($"               reopening rebuilds it as {drift.RebuiltPoints} pts "
                + $"(saved: {drift.SavedPoints}), {drift.RebuiltUnitCount} units (saved: {drift.SavedUnitCount})"
                + (drift.DroppedUnits.Count > 0 ? $", dropping {string.Join(", ", drift.DroppedUnits)}" : ""));
        converted++;
    }

    Console.WriteLine($"Retrofit complete: {converted} converted, {alreadyEditable} already editable, " +
        $"{failed} left alone (of {targets.Count}).{(dryRun ? " [dry run - nothing written]" : "")}");
    return;
}

// --retrofit-effects <fileOrDir> [more...]  (#239): stamp weapon effect-set keys into existing data.
// Books get their faction's default sets; armies get army-level defaults plus per-weapon keyword keys
// (explicit keys already in a file are never touched). Idempotent — re-run after a keyword-table change.
int retrofitIdx = Array.IndexOf(args, "--retrofit-effects");
if (retrofitIdx >= 0 && retrofitIdx + 1 < args.Length)
{
    List<string> retrofitTargets = args.Skip(retrofitIdx + 1).TakeWhile(a => !a.StartsWith("--"))
        .SelectMany(t => Directory.Exists(t)
            ? Directory.GetFiles(t, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(t, "*" + ArmyListFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories))
            : new[] { t })
        .ToList();
    int retrofitPatched = 0;
    foreach (string path in retrofitTargets)
    {
        bool changed;
        if (path.EndsWith(BookFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            changed = WeaponEffectAssigner.ApplyToBook(book);
            if (changed) File.WriteAllText(path, JsonSerializer.Serialize(book, RuleJson.Options));
        }
        else
        {
            // Deserialize as BuiltArmyFile so a forge army's selections/book snapshot survives the
            // round-trip (#236); a hand-authored army has both null and re-saves as the base type.
            BuiltArmyFile army = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)!;
            changed = WeaponEffectAssigner.ApplyToArmy(army);
            if (changed)
                File.WriteAllText(path, army.Book != null
                    ? JsonSerializer.Serialize(army, RuleJson.Options)
                    : JsonSerializer.Serialize<ArmyListFile>(army, RuleJson.Options));
        }
        Console.WriteLine($"  {(changed ? "patched" : "unchanged")}: {path}");
        if (changed) retrofitPatched++;
    }
    Console.WriteLine($"Retrofit complete: {retrofitPatched}/{retrofitTargets.Count} file(s) patched.");
    return;
}

// --retrofit-bases <fileOrDir> [more...]  (#225): correct rectangular base orientation in existing data.
// OPR writes its base spec length-first and the engine runs HeightInches along the facing, so books
// imported before the fix have the two swapped (a 60x35 bike faced across its 35mm axis). Idempotent —
// only entries with Width > Height are touched, and a corrected base never matches again.
int retrofitBasesIdx = Array.IndexOf(args, "--retrofit-bases");
if (retrofitBasesIdx >= 0 && retrofitBasesIdx + 1 < args.Length)
{
    List<string> targets = args.Skip(retrofitBasesIdx + 1).TakeWhile(a => !a.StartsWith("--"))
        .SelectMany(t => Directory.Exists(t)
            ? Directory.GetFiles(t, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(t, "*" + ArmyListFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories))
            : new[] { t })
        .ToList();
    int patched = 0;
    int estimated = 0;
    bool verbose = args.Contains("--verbose");
    foreach (string path in targets)
    {
        bool changed;
        var estimates = new List<string>();
        if (path.EndsWith(BookFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            changed = BaseOrientationRetrofit.ApplyToBook(book, estimates);
            if (changed) File.WriteAllText(path, JsonSerializer.Serialize(book, RuleJson.Options));
        }
        else
        {
            // As #239: read as BuiltArmyFile so a forge army's selections/book snapshot survive the
            // round-trip (#236), and re-serialize as the base type when there is no forge block.
            BuiltArmyFile army = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)!;
            changed = BaseOrientationRetrofit.ApplyToArmy(army, estimates);
            if (changed)
                File.WriteAllText(path, army.Book != null
                    ? JsonSerializer.Serialize(army, RuleJson.Options)
                    : JsonSerializer.Serialize<ArmyListFile>(army, RuleJson.Options));
        }
        Console.WriteLine($"  {(changed ? "patched" : "unchanged")}: {path}"
            + (estimates.Count > 0 ? $"  ({estimates.Count} base(s) estimated)" : string.Empty));
        // #225 defect B: an estimated base is invented, not imported. Never let that pass silently -
        // --verbose names every one so the guesses can be reviewed.
        if (verbose)
            foreach (string e in estimates) Console.WriteLine($"      estimated: {e}");
        estimated += estimates.Count;
        if (changed) patched++;
    }
    Console.WriteLine($"Base retrofit complete: {patched}/{targets.Count} file(s) patched, "
        + $"{estimated} base(s) estimated from unit rules (pass --verbose to list them).");
    return;
}

// --make-scenario <scenario.json> <out.fdgsave>  (#167 T1): compile a compact scenario JSON (armies,
// placements, wounds/tokens, whose activation it is) into a resumable save positioned at the start of
// the active player's activation. Author a rule test in ~20 lines of JSON instead of playing to it.
int makeScenarioIdx = Array.IndexOf(args, "--make-scenario");
if (makeScenarioIdx >= 0 && makeScenarioIdx + 2 < args.Length)
{
    string outPath = args[makeScenarioIdx + 2];
    try
    {
        string saveJson = ScenarioCompiler.CompileFileToSaveJson(args[makeScenarioIdx + 1]);
        string? outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        File.WriteAllText(outPath, saveJson);
        Console.WriteLine($"Compiled scenario '{args[makeScenarioIdx + 1]}' -> {outPath}");
    }
    catch (ScenarioCompileException ex)
    {
        Console.WriteLine($"Scenario error: {ex.Message}");
        Environment.Exit(1);
    }
    return;
}

// --scenario <file.json|file.fdgsave>  (#167): launch straight into a scenario, skipping the main menu
// AND the lobby — slot 0 is the local player, every other slot is AI. A .json compiles in-memory first;
// a .fdgsave (from --make-scenario or an in-game save) loads directly. Works headless and in the GUI.
int scenarioIdx = Array.IndexOf(args, "--scenario");
string? scenarioPath = scenarioIdx >= 0 && scenarioIdx + 1 < args.Length ? args[scenarioIdx + 1] : null;

// --all-ai: slot 0 is AI too (bot-vs-bot observation). On the scenario path, GUI or headless; #413
// extended it to the ordinary headless lobby path, where it is what makes Star Quest actually testable -
// EOF defaults make each hero take the first option offered, so a human-slot party never leaves its
// deployment and no mission is ever completed.
// --log-decisions: interleave each planning AI's candidate-table narration into stdout (FdgLab's
// flag on the scenario path; the AI resolvers have no other sink in an ordinary game - #264 note).
bool scenarioAllAi = args.Contains("--all-ai");
bool scenarioLogDecisions = args.Contains("--log-decisions");

// --army <path> (#153): non-interactive headless smoke — both players load <path>, then EOF defaults take
// over (exactly what the old `printf "1\n<path>\n..." |` pipe idiom did, minus the pipe).
int armyIdx = Array.IndexOf(args, "--army");
if (headless && armyIdx >= 0 && armyIdx + 1 < args.Length)
{
    string armyPath = args[armyIdx + 1];
    Console.SetIn(new StringReader($"1\n{armyPath}\n1\n{armyPath}\n"));
}

// #413: what a scripted headless run plays. Before these, CliApp built every game from
// GameSettings.GetDefault(), so headless could only ever reach standard Grimdark Future - which left
// Firefight, Skirmish, Horde and the Game Master testable by hand only.
//   --game-system  gf | gff | aof | aofs      (also the full names)
//   --game-mode    standard | horde | star-quest-horde | star-quest-mission
//   --play-style   standard | game-master
//   --army-points  <n>          --army1 <file.fdgarmy>   --army2 <file.fdgarmy>
//   --party        auto | auto:<2-4> | <file.fdghero>... | <folder>   (Star Quest's local side)
//   --challenges   all | boss,injuries,alertness,time,weather,events  (Star Quest optional challenges)
static string? ArgValue(string[] a, string name)
{
    int i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

EGameSystem? cliSystem = ArgValue(args, "--game-system") is { } sysText
    ? sysText.Replace("-", "").Replace(":", "").Replace(" ", "").ToLowerInvariant() switch
    {
        "gf" or "grimdarkfuture" => EGameSystem.GrimdarkFuture,
        "gff" or "firefight" or "gffirefight" or "grimdarkfuturefirefight" => EGameSystem.GFFirefight,
        "aof" or "ageoffantasy" => EGameSystem.AgeOfFantasy,
        "aofs" or "skirmish" or "aofskirmish" or "ageoffantasyskirmish" => EGameSystem.AoFSkirmish,
        "gfsq" or "starquest" or "gfstarquest" => EGameSystem.GFStarQuest,
        "aofq" or "quest" or "aofquest" => EGameSystem.AoFQuest,
        _ => throw new ArgumentException($"Unknown --game-system '{sysText}'."),
    }
    : null;

EGameMode? cliMode = ArgValue(args, "--game-mode") is { } modeText
    ? modeText.Replace("-", "").Replace(" ", "").ToLowerInvariant() switch
    {
        "standard" => EGameMode.Standard,
        "horde" => EGameMode.Horde,
        "starquesthorde" or "sqhorde" => EGameMode.StarQuestHorde,
        "starquestmission" or "sqmission" => EGameMode.StarQuestMission,
        _ => throw new ArgumentException($"Unknown --game-mode '{modeText}'."),
    }
    : null;

EPlayStyle? cliStyle = ArgValue(args, "--play-style") is { } styleText
    ? styleText.Replace("-", "").Replace(" ", "").ToLowerInvariant() switch
    {
        "standard" => EPlayStyle.Standard,
        "gamemaster" or "gm" => EPlayStyle.GameMaster,
        _ => throw new ArgumentException($"Unknown --play-style '{styleText}'."),
    }
    : null;

int? cliPoints = int.TryParse(ArgValue(args, "--army-points"), out int cliPointsValue) ? cliPointsValue : null;

// #413 --party: the 2-4 heroes slot 0 plays in GF: Star Quest. Star Quest's local side is a party, not
// an army list, so without this a scripted Star Quest run put an ordinary army on the table, nothing on
// it counted as a quest hero, and the first end-of-round check ended the mission with "every hero is
// down" in round 1. Takes .fdghero paths, a folder of them, or "auto" (optionally "auto:4") for a party
// generated from the shipped Army Forge data. Bare "--party" with nothing after it also generates one.
//   --party auto            --party "heroes/GF Star Quest"      --party a.fdghero b.fdghero
int partyIdx = Array.IndexOf(args, "--party");
List<string>? cliParty = partyIdx >= 0 ? TrailingPaths(args, partyIdx + 1) : null;

// #413 --challenges: Star Quest's optional challenges (Boss Fights, Hero Injuries, Increased Alertness,
// Time of Day, Weather, Random Events). They are picked from a menu at mission set-up, and an automated
// answerer takes the first option - "Start" - so a scripted run always played with NONE of them on, and
// the boss fight in particular could never be reached. `all` switches every one on; otherwise name them
// comma-separated, matching loosely ("boss", "random-events").
//   --challenges all        --challenges boss,weather
if (ArgValue(args, "--challenges") is { } challengeText)
{
    var wanted = new List<string>();
    if (challengeText.Equals("all", StringComparison.OrdinalIgnoreCase))
        wanted.AddRange(FDG.Stages.QuestChallenges.AllChallenges);
    else
        foreach (string raw in challengeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string key = raw.Replace("-", "").Replace(" ", "").ToLowerInvariant();
            string? match = FDG.Stages.QuestChallenges.AllChallenges.FirstOrDefault(c =>
                c.Replace(" ", "").ToLowerInvariant().StartsWith(key, StringComparison.Ordinal));
            if (match == null)
                throw new ArgumentException($"Unknown --challenges entry '{raw}'. Known: "
                    + string.Join(", ", FDG.Stages.QuestChallenges.AllChallenges) + ", or 'all'.");
            if (!wanted.Contains(match)) wanted.Add(match);
        }
    FDG.Stages.QuestChallenges.ForceForTests = wanted;
    Console.WriteLine("Optional challenges: " + string.Join(", ", wanted));
}

if (cliParty != null && ArgValue(args, "--army1") != null)
    throw new ArgumentException("--party and --army1 both set slot 0: name one or the other. "
        + "A Star Quest party and an army list cannot both be the local side.");

// Star Quest's enemies follow the Star Quest AI flowchart, which is what the lobby gives them (it offers
// "Add Star Quest AI" and nothing else on a Quest game). A scripted run would otherwise get the
// solo-rules bot and quietly test the wrong opponent, so the Quest systems pick it themselves unless
// --ai-profile said otherwise.
if (profileIdx < 0 && (cliSystem is { } questCheck && GameSystemInfo.IsQuest(questCheck)
        || cliMode is { } questModeCheck && GameSystemInfo.IsQuestMode(questModeCheck)))
    aiProfile = FDG.Ai.EAiProfile.StarQuest;

if (scenarioAllAi && !headless && scenarioPath == null)
{
    Console.Error.WriteLine("--all-ai requires --headless (the GUI needs a local player on slot 0).");
    Environment.Exit(2);
}

var app = new CliApp(headless, slowDelayMs, diceSeed, aiProfile, scenarioAllAi && scenarioPath == null);
if (cliSystem != null || cliMode != null || cliStyle != null || cliPoints != null || cliParty != null
    || ArgValue(args, "--army1") != null || ArgValue(args, "--army2") != null)
{
    app.Scripted = new CliApp.Setup(cliSystem, cliMode, cliStyle, cliPoints,
        ArgValue(args, "--army1"), ArgValue(args, "--army2"), cliParty);
}

if (headless)
{
    if (scenarioPath != null)
    {
        try
        {
            await app.RunScenarioAsync(ScenarioLauncher.LoadStore(scenarioPath),
                scenarioAllAi, scenarioLogDecisions);
        }
        catch (ScenarioCompileException ex)
        {
            Console.WriteLine($"Scenario error: {ex.Message}");
            Environment.Exit(1);
        }
        return;
    }

    app.Prepare();
    await app.RunAsync();
}
else
{
    var renderer = new RaylibRenderer();

    // ── Main Menu ──────────────────────────────────────────────────────────────
    // The Game Type the player picked on the menu's tiles, applied to the lobby once the host dialog
    // has made one. Null means "whatever the lobby defaults to": the campaign screens reach the host
    // dialog on their own terms and must not inherit a tile clicked earlier in the session.
    EGameSystem? pendingGameSystem = null;

    renderer.MainMenu.OnGameTypeClicked = system =>
    {
        pendingGameSystem = system;
        renderer.NavigateTo(renderer.HostModal);
    };

    renderer.MainMenu.OnHostClicked = () =>
    {
        pendingGameSystem = null;
        renderer.NavigateTo(renderer.HostModal);
    };

    renderer.MainMenu.OnArmyBuilderClicked = () =>
        renderer.NavigateTo(renderer.ArmyBuilder);

    renderer.MainMenu.OnArmyForgeClicked = () =>
        renderer.NavigateTo(renderer.ArmyForge);

    renderer.MainMenu.OnCombatCalculatorClicked = () =>
        renderer.NavigateTo(renderer.CombatCalculator);

    renderer.MainMenu.OnHeroesClicked = () =>
        renderer.NavigateTo(renderer.HeroBuilder);
    renderer.HeroBuilder.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.MainMenu.OnCampaignsClicked = () =>
        renderer.NavigateTo(renderer.Campaigns);
    renderer.Campaigns.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);
    renderer.Campaigns.OnPlay = () =>
    {
        pendingGameSystem = null;
        renderer.NavigateTo(renderer.HostModal);
    };

    renderer.MainMenu.OnStrikeTeamsClicked = () =>
        renderer.NavigateTo(renderer.StrikeTeams);
    renderer.StrikeTeams.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);
    // #410: "Play next mission" on the campaign screen. The screen has already recorded which army and
    // which units (LobbyScreen.PendingCampaignArmy); here we only ask for the right game type, and the
    // lobby picks the army up once it exists.
    renderer.StrikeTeams.OnPlay = system =>
    {
        pendingGameSystem = system;
        renderer.NavigateTo(renderer.HostModal);
    };

    renderer.MainMenu.OnClientClicked = () =>
        renderer.NavigateTo(renderer.ClientModal);

    // The public-listing heartbeat (#271), when the host ticked "List publicly". Stopped on every
    // lobby/game exit path below; a missed path only means the entry lingers until the registry's
    // 90s TTL, so this is belt-and-braces rather than load-bearing.
    PublicListingService? activeListing = null;
    NatPortMapper? activeMapper = null;
    // The live lobby view model, host or client (#279). Unlike the listing, tearing this down IS
    // load-bearing: disposing it stops the host's listener (releasing the port for the next lobby)
    // or closes the client's connection (freeing its roster slot on the host). Before this, an
    // abandoned host lobby kept serving the port as a zombie while the next lobby's listener
    // silently failed to bind - joins and chat went to a lobby no UI was bound to.
    ILobbyViewModel? activeLobby = null;
    void TeardownLobby()
    {
        StopListing();
        activeLobby?.Dispose();
        activeLobby = null;
    }
    // `blocking` is for app exit only: it waits for the router to confirm the UPnP removal, which there
    // is no later moment to do. Every in-session call leaves it false - waiting on a slow router there
    // froze the UI for most of a second on the way back to the main menu.
    void StopListing(bool blocking = false)
    {
        activeListing?.Dispose();
        activeListing = null;
        // Removes the UPnP port mapping too (#271), so we don't leave a router port open past the
        // session. Best-effort; a missed path just leaves the mapping to the router's lease TTL.
        if (blocking) activeMapper?.DisposeBlocking();
        else activeMapper?.Dispose();
        activeMapper = null;
    }

    // ── Load Game (work item #052): pick a .fdgsave, resume it as host ───────────
    // Shared by the main menu's Load Game and the in-game menu's Load (#246); the latter tears the
    // current game down and returns to the menu before calling this. Opens the in-game Load Game
    // screen (tiles with a board preview) instead of a file dialog; picking a tile calls LoadSavedGame.
    void LoadGameFlow() => renderer.NavigateTo(renderer.LoadGame);

    renderer.LoadGame.OnBack = () => renderer.NavigateTo(renderer.MainMenu);
    renderer.LoadGame.OnLoad = path => LoadSavedGame(path);

    void LoadSavedGame(string path)
    {
        if (!File.Exists(path)) return;

        GameDataStore loadedStore;
        try
        {
            loadedStore = GameSaveSerializer.Load(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            WriteCrash("Load game failed", ex);
            renderer.LoadGame.ShowError("That save couldn't be read.");
            return;
        }

        // The game can only resume from round 1 onward: a save made during setup / deployment has no
        // round-tracking record yet, and resuming it used to fail silently on "Resume Game".
        if (GameProgressUtilities.TryGetProgress(loadedStore) == null)
        {
            renderer.LoadGame.ShowError("That game was saved before round 1 started, so it can't be resumed.");
            return;
        }

        // Release any previous lobby's listener/connection first (#279), so this host can bind.
        TeardownLobby();

        FDGHost host = new FDGHost();
        Task hostTask = host.StartAsync();

        // Same silent-bind-failure guard as HostModal.CreateServer (#279): a port conflict faults the
        // task before its first await, and discarding it would open a lobby around a dead server.
        if (hostTask.IsFaulted)
        {
            Console.WriteLine($"Host failed to start: {hostTask.Exception?.GetBaseException().Message}");
            return;
        }

        // #310: the saved player name, same as a fresh host lobby uses.
        var lobby = new LobbyViewModel_Host(UserConfig.Current.PlayerName, "Loaded Game", "", host, loadedStore);
        activeLobby = lobby;
        renderer.LobbyScreen.SetViewModel(lobby);
        renderer.NavigateTo(renderer.LobbyScreen);
    }

    renderer.MainMenu.OnLoadGameClicked = LoadGameFlow;
    renderer.OnLoadGameRequested        = LoadGameFlow;

    // ── Take Back (local games against bots) ─────────────────────────────────────
    // The engine records a save point at the start of every activation. Take Back relaunches the game
    // from the start of your most recent activation through the same resume path Load Game uses, with
    // every seat kept as it was; Redo goes back to where you were before the last Take Back (until you
    // play on). Offered only when every seat is on this computer.
    IReadOnlyList<PlayerID> LocalSeats(LobbyViewModel_Host lobby) => lobby.GetCrew()
        .Where(seat => seat.PlayerType == FDG.Players.EPlayerType.Local).Select(seat => seat.PlayerID).ToList();

    renderer.TakeBackStatusProvider = () =>
    {
        if (activeLobby is not LobbyViewModel_Host lobby || !lobby.AllSeatsLocal) return null;
        var history = FDG.TakeBackTools.TakeBackHistory.Shared;
        var target = history.PeekTakeBack(LocalSeats(lobby));
        var redoTarget = history.PeekRedo();
        return new RaylibRenderer.TakeBackStatus(
            target != null,
            target != null
                ? $"Take Back (Ctrl+Z): rewind to the start of your activation in round {target.Round}.\nEverything after that is undone, bot turns included. Dice will be rolled again."
                : "Take Back (Ctrl+Z): nothing to take back yet - it works from your first activation after deployment.",
            redoTarget != null,
            redoTarget != null
                ? $"Redo (Ctrl+X): return to where you were before Take Back\n(the start of the activation in round {redoTarget.Round}). Gone once you play on."
                : "Redo (Ctrl+X): nothing to redo.");
    };

    renderer.OnTakeBackRequested = redo =>
    {
        if (activeLobby is not LobbyViewModel_Host oldLobby || !oldLobby.AllSeatsLocal) return;
        var history = FDG.TakeBackTools.TakeBackHistory.Shared;
        var crew = oldLobby.GetCrew();
        var local = LocalSeats(oldLobby);
        if (redo ? history.PeekRedo() == null : history.PeekTakeBack(local) == null) return;

        // Stop the running game first, so nothing it does from here can land in the history.
        oldLobby.StopGame();
        string? json = redo ? history.Redo() : history.TakeBack(local);
        if (json == null) return;

        GameDataStore store;
        try
        {
            store = GameSaveSerializer.Load(json);
        }
        catch (Exception ex)
        {
            WriteCrash("Take Back failed to load its save point", ex);
            renderer.AbortTakeBack();
            return;
        }

        // The board stays on screen (dimmed) while the relaunch comes up; the new game swaps itself in.
        renderer.BeginTakeBack();

        // Release the old lobby's networking (and port) before the new one binds.
        TeardownLobby();

        FDGHost host = new FDGHost();
        Task hostTask = host.StartAsync();
        if (hostTask.IsFaulted)
        {
            Console.WriteLine($"Take Back: host failed to start: {hostTask.Exception?.GetBaseException().Message}");
            renderer.AbortTakeBack();
            return;
        }

        var lobby = new LobbyViewModel_Host(UserConfig.Current.PlayerName, "Loaded Game", "", host, store);
        lobby.ApplyCrew(crew);
        activeLobby = lobby;
        // The lobby screen is only the hand-off point here; it is never navigated to.
        renderer.LobbyScreen.SetViewModel(lobby);
        history.MarkRewindLaunch();
        if (!lobby.TryResumeGame(out string? failReason))
        {
            Console.WriteLine($"Take Back: could not relaunch: {failReason}");
            renderer.AbortTakeBack();
        }
    };

    renderer.MainMenu.OnQuitClicked = renderer.RequestClose;

    // ── Army Builder ───────────────────────────────────────────────────────────
    renderer.ArmyBuilder.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    // ── Army Forge (#153) ────────────────────────────────────────────────────────
    renderer.ArmyForge.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    // ── Combat Calculator (#397) ─────────────────────────────────────────────────
    renderer.CombatCalculator.OnBack = () =>
        renderer.NavigateTo(renderer.MainMenu);

    // ── Host Modal ─────────────────────────────────────────────────────────────
    renderer.HostModal.OnCancel = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.HostModal.OnCreated = (lobby, listing, mapper) =>
    {
        TeardownLobby();
        activeLobby = lobby;
        activeListing = listing;
        activeMapper = mapper;
        renderer.LobbyScreen.SetViewModel(lobby);
        // The menu's tile, if that is how we got here: the lobby opens already pointed at that game's
        // armies, points and modes rather than at the Grimdark Future default. Cleared either way, so a
        // later lobby reached another way keeps its own default.
        if (pendingGameSystem is { } picked)
            LobbyScreen.ApplyGameSystem(lobby, picked);
        pendingGameSystem = null;
        // #410: a campaign army waiting to be played takes the host's own slot, with the points limit
        // raised to fit it. Must follow ApplyGameSystem, which resets the points and the allowed
        // systems, and cannot live in SetViewModel, which clears the auto-army marks and then re-applies
        // the saved host settings over the top.
        renderer.LobbyScreen.ApplyPendingCampaignArmy();
        renderer.NavigateTo(renderer.LobbyScreen);
    };

    // ── Client Modal ───────────────────────────────────────────────────────────
    renderer.ClientModal.OnCancel = () =>
        renderer.NavigateTo(renderer.MainMenu);

    renderer.ClientModal.OnConnected = lobby =>
    {
        TeardownLobby();
        activeLobby = lobby;
        renderer.LobbyScreen.SetViewModel(lobby);
        renderer.NavigateTo(renderer.LobbyScreen);
    };

    // ── Choose Army ────────────────────────────────────────────────────────────
    // The lobby's Army cell is a button now, not a dropdown: it hands the choice to a screen of its
    // own, which has room to search dozens of lists and to show the one you are about to take. The
    // lobby keeps the state - which slot asked, and the loading - so this wiring is only the trip
    // there and back.
    renderer.LobbyScreen.OnChooseArmyRequested = () =>
    {
        renderer.ArmyPicker.Open(
            renderer.LobbyScreen.PickPlayerName,
            renderer.LobbyScreen.PickArmies,
            renderer.LobbyScreen.PickPointsLimit,
            renderer.LobbyScreen.PickCurrentKey);
        renderer.NavigateTo(renderer.ArmyPicker);
    };

    renderer.ArmyPicker.OnPicked = entry =>
    {
        renderer.LobbyScreen.ApplyPickedArmy(entry);
        renderer.NavigateTo(renderer.LobbyScreen);
    };

    renderer.ArmyPicker.OnCancel = () =>
        renderer.NavigateTo(renderer.LobbyScreen);

    // ── Lobby ──────────────────────────────────────────────────────────────────
    renderer.LobbyScreen.OnBack = () =>
    {
        // Backing out must actually shut the networking down (#279): stop the host's listener /
        // close the client's connection, not just the listing heartbeat.
        TeardownLobby();
        renderer.NavigateTo(renderer.MainMenu);
    };

    // Covers game-over, escape-menu quit-to-menu, and escape-menu load (#271, #279).
    renderer.OnGameExited = TeardownLobby;

    renderer.LobbyScreen.OnGameLaunched = (tableState, colorFunc, log, overlay, taskDisplay, presentationPlayer, saveGame, chatUI) =>
        renderer.TransitionToGame(tableState, colorFunc, log, overlay, taskDisplay, presentationPlayer, saveGame, chatUI);

    renderer.LobbyScreen.OnGameEnded = result => renderer.ShowGameOver(result);

    // #187: a game the engine ended because a player's connection dropped is not lost - the host writes a
    // recovery save automatically (over the internet a brief drop is common, and nobody should have to
    // have thought to save first). This runs on the engine thread, synchronously, before the game-over
    // card is shown: the state machine has unwound but nothing has torn the game down yet, so it is the
    // cleanest snapshot of the game there is. Host only - a client's SaveGameToJson returns null (#054).
    renderer.LobbyScreen.OnGameCompleted = result =>
    {
        // GF: Firefight Campaign: strike teams get their result (casualties, XP, level ups) written back.
        if (renderer.RecordStrikeTeams(result) is { } strikeNote) renderer.SetGameOverNote(strikeNote);
        if (result.Outcome != EGameOutcome.Disconnect) return;

        string? path = RecoverySave.TryWrite(activeLobby?.SaveGameToJson(), DateTime.UtcNow);
        renderer.SetGameOverNote(path != null
            ? $"Recovery save written to:\n{path}\nLoad Game from the main menu to resume it."
            : "Could not write a recovery save (see the console for why).");
    };

    // ── Local play (Host with no network players) also still works via CliApp ─
    // The old "Host" path now goes through the lobby. CliApp is only used
    // in headless mode above.

    // ── --scenario (#167): straight into the game, no menu, no lobby ──────────
    // Load/compile eagerly so a bad scenario fails fast with a message and no window. The game
    // wiring is deferred to OnWindowReady (first thing inside Run(), window + ImGui live):
    // TransitionToGame attaches the tactical overlay, which creates GL resources (#162) and
    // segfaults before InitWindow. Slot 0 = local player, other slots AI; the engine waits on
    // the resolver registry (assigned in GameGuiWiring.Launch) before requesting decisions.
    if (scenarioPath != null)
    {
        // Headless-only for now: the GUI wiring assigns its interfaces onto the human game, which
        // owns no player under --all-ai (spectator rendering is its own slice if ever wanted).
        if (scenarioAllAi)
        {
            Console.Error.WriteLine("--all-ai requires --headless (the GUI needs a local player on slot 0).");
            Environment.Exit(2);
        }
        try
        {
            GameDataStore scenarioStore = ScenarioLauncher.LoadStore(scenarioPath);
            var parts = ScenarioLauncher.BuildResume(scenarioStore, diceSeed, aiProfile,
                scenarioAllAi,
                scenarioLogDecisions ? slot => line => Console.WriteLine($"[ai {slot}] {line}") : null);

            var players = new List<(PlayerID ID, string Name)>();
            for (int i = 0; i < parts.SavedInfos.Count; i++)
                players.Add((parts.SavedInfos[i].PlayerID,
                    i == 0 && !scenarioAllAi ? "Player 1" : $"Player {i + 1} (AI)"));

            // #201: a compiled scenario carries its GameSettings in the store's progress record;
            // absent (or a pre-#201 save) means the default ON.
            GameSettings scenarioSettings = GameProgressUtilities.TryGetProgress(parts.Store)?.Settings
                ?? GameSettings.GetDefault();

            renderer.OnWindowReady = () =>
            {
                GameGuiWiring.Launch(parts.HumanGame, players,
                    saveGameToJson: () => GameSaveSerializer.Save(parts.Store),
                    onLaunched: renderer.TransitionToGame,
                    coverProximityExceptions: scenarioSettings.CoverProximityExceptionsEnabled,
                    tableBackground: scenarioSettings.TableBackground,
                    seeThroughFriendlyUnits: scenarioSettings.SeeThroughFriendlyUnits);

                var scenarioServer = new FDGServer(parts.Store, parts.Bus, parts.Slots,
                    new RealtimePresentationClock());
                // #168: this path builds the server (whose resume re-runs rule/spell resolution) after
                // Launch attached the log - push any drops it just produced into the visible summary.
                RuleLoadWarnings.FlushPending();
                scenarioServer.OnGameEnded += result => renderer.ShowGameOver(result);
            };
        }
        catch (ScenarioCompileException ex)
        {
            Console.WriteLine($"Scenario error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    try
    {
        renderer.Run();
    }
    catch (Exception ex)
    {
        WriteCrash("renderer.Run() threw", ex);
        Console.Error.WriteLine("Press Enter to exit.");
        Console.ReadLine();
        throw;
    }
    finally
    {
        // App exit: send the polite delist so the entry vanishes now, not at TTL (#271), and wait for
        // the router to drop the port mapping - the process is about to end, so a background removal
        // would die with it.
        StopListing(blocking: true);
    }
}
