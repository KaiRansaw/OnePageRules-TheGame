using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;

namespace FdgRaylib.Quest;

/// <summary>
/// A Quest game's enemy force built straight from an army book: the WHOLE roster at base model count.
///
/// <para>
/// A Quest enemy side is not an army in the ordinary sense. <c>StarQuestHorde.DeployWave</c> treats it as a
/// POOL - each wave draws from the units still in reserve or already destroyed until the wave's points
/// budget is spent - so the collection's job is to be a varied pool, not a legal list. The whole roster is
/// both the richest pool and the one a book defines on its own, with no picks for anyone to make.
/// </para>
///
/// <para>
/// Shared by the <c>--make-collections</c> dev tool (which writes the checked-in collections under
/// <c>armies/</c>) and the Quest campaign's "build from an army book" chapter enemy, which builds a fresh one
/// from the current book each time a mission is played so it can never drift from the book.
/// </para>
/// </summary>
public static class QuestEnemyCollections
{
    /// <summary>The folder a Quest system's enemy books ship in: <c>Assets/Books/&lt;game type&gt;</c>.</summary>
    public static string BooksFolderFor(EGameSystem system) =>
        Path.Combine(AppAssets.AssetsFolder, "Books", QuestGameTypes.For(system));

    /// <summary>One enemy book on offer: its file name and the faction it is for.</summary>
    public sealed record BookChoice(string FileName, string Faction);

    private static readonly Dictionary<EGameSystem, IReadOnlyList<BookChoice>> Cache = new();

    /// <summary>The enemy books a Quest system ships, by faction name. Read once per system and cached.</summary>
    public static IReadOnlyList<BookChoice> BooksFor(EGameSystem system)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(system, out IReadOnlyList<BookChoice>? cached)) return cached;
            var found = new List<BookChoice>();
            string folder = BooksFolderFor(system);
            if (Directory.Exists(folder))
            {
                foreach (string path in Directory.GetFiles(folder, "*.fdgbook").OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (TryLoad(path) is { } book)
                        found.Add(new BookChoice(Path.GetFileName(path), book.Faction ?? book.Name));
                }
            }
            IReadOnlyList<BookChoice> sorted = found.OrderBy(b => b.Faction, StringComparer.OrdinalIgnoreCase).ToList();
            Cache[system] = sorted;
            return sorted;
        }
    }

    /// <summary>The enemy collection for <paramref name="book"/>: every unit at base model count.</summary>
    public static BuiltArmyFile Build(BookFile book)
    {
        var list = new BuilderList { Name = book.Name, BookName = book.Name, PointsLimit = 100000 };
        foreach (RosterUnit unit in book.Units)
            list.Units.Add(new BuilderUnit { RosterUnitId = unit.Id, ModelCount = unit.BaseModelCount });

        BuiltArmyFile collection = ListCompiler.Compile(book, list);
        collection.Name = $"{book.Faction ?? book.Name} - enemy collection";
        // Drop the editable session. ListCompiler attaches the picks plus a FULL SNAPSHOT of the source book,
        // and that snapshot is most of the file. BuiltArmyFile documents both fields as null on an army the
        // Forge did not author, and such a file still plays: the engine reads Units, RuleDefinitions and Spells.
        collection.Selections = null;
        collection.Book = null;
        return collection;
    }

    /// <summary>
    /// Builds the collection for the book <paramref name="bookFileName"/> of <paramref name="system"/> and
    /// writes it to <paramref name="outDir"/>. Returns the file's path, or null when the book is missing or
    /// unreadable or the file cannot be written.
    /// </summary>
    public static string? WriteFromBook(EGameSystem system, string bookFileName, string outDir)
    {
        string bookPath = Path.Combine(BooksFolderFor(system), Path.GetFileName(bookFileName));
        if (TryLoad(bookPath) is not { } book) return null;
        try
        {
            Directory.CreateDirectory(outDir);
            BuiltArmyFile collection = Build(book);
            string outPath = Path.Combine(outDir, $"{FileSafe(book.Faction ?? book.Name)} - enemy collection.fdgarmy");
            File.WriteAllText(outPath, JsonSerializer.Serialize(collection, RuleJson.Options));
            return outPath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static BookFile? TryLoad(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options) : null; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static string FileSafe(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
