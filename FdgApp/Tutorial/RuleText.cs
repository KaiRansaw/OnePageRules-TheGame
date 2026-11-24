using System.Text.Json;
using FDG;

namespace FdgRaylib.Tutorial;

/// <summary>
/// The core rules as the books print them, for How to Play and the Rules Guide: Grimdark Future and Age of Fantasy
/// Core Rules v3.6.0, section by section, word for word (Kai: rule text shown to the player is 1:1 with the book -
/// never summarised). Read from <c>Assets/Tutorial/core-rules-gf.json</c> and <c>core-rules-aof.json</c>.
/// </summary>
public static class RuleText
{
    public sealed record Section(string Id, string Chapter, string Title, string Text);

    public sealed record BookText(string Book, IReadOnlyList<Section> Sections)
    {
        public Section? this[string id] => Sections.FirstOrDefault(s => s.Id == id);
    }

    private static readonly Dictionary<EGameSystem, BookText> Cache = new();
    private static readonly object Gate = new();

    public static string FileFor(EGameSystem system) =>
        Path.Combine(AppAssets.AssetsFolder, "Tutorial", system == EGameSystem.AgeOfFantasy ? "core-rules-aof.json" : "core-rules-gf.json");

    /// <summary>The book's sections for a game; empty (never null) if the file is missing.</summary>
    public static BookText For(EGameSystem system)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(system, out BookText? cached)) return cached;
            BookText text = Load(FileFor(system));
            Cache[system] = text;
            return text;
        }
    }

    private sealed class FileShape
    {
        public string Book { get; set; } = "";
        public List<Section> Sections { get; set; } = new();
    }

    private static BookText Load(string path)
    {
        try
        {
            var f = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (f != null) return new BookText(f.Book, f.Sections);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new BookText("", Array.Empty<Section>());
    }

    /// <summary>
    /// A lesson's sections: its rule ids, with "special-rules" standing for the special rules this game's practice
    /// armies carry (<see cref="HowToPlay.SpecialRuleIds"/>).
    /// </summary>
    public static IReadOnlyList<Section> ForLesson(EGameSystem system, HowToPlay.Lesson lesson)
    {
        BookText book = For(system);
        var ids = lesson.RuleIds.SelectMany(id => id == "special-rules" ? HowToPlay.SpecialRuleIds(system) : new[] { id });
        return ids.Select(id => book[id]).OfType<Section>().ToList();
    }
}
