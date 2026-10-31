using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;

namespace FdgRaylib.Tests;

/// <summary>
/// #378: which bundled books a fixture should walk. The #196/#197-era ShippedData census fixtures pin
/// counts and populations that were closed against the 47-book GDF corpus, so they enumerate
/// <see cref="GdfPaths"/>; the AoF corpus carries its own census (#375) and is pinned by the
/// all-books fixtures (BookRuleCensusTests, BookSpellCoverageTests, BookRuleScopeTests). AoF bundles
/// are filename-prefixed "AoF-" by scripts/bake-aof-books.sh - the filename convention IS the
/// contract here, and the census fixtures verify the books' GameSystem field independently.
/// <para>
/// #412: the books now live in a folder per game system - "Grimdark Future", "Age of Fantasy",
/// "GF Firefight", "AoF Skirmish" and "GF Star Quest" - because OPR publishes a separate book per
/// system. Every lookup here is therefore recursive, and <see cref="Path"/> finds a book by file name
/// wherever it sits, so a fixture never has to know which system folder a book is in.
/// </para>
/// <para>
/// <b>A warning for whoever hits this next.</b> <see cref="GdfPaths"/> means "every bundled book
/// except the AoF ones", which today is four game systems: Grimdark Future, GF Firefight, AoF
/// Skirmish and GF Star Quest. So every hand-typed total in the ShippedData census fixtures counts
/// across all four, and <b>importing a new game system moves every single one of them.</b> That
/// happened twice on 2026-10-31 alone - the corpus went 88 books to 188 (Firefight and Skirmish) and
/// then to 242 (Star Quest), and each expansion invalidated the same ~20 totals and cost a full pass
/// of re-taking them. Neither pass caught a defect; the counts only ever reported the growth we had
/// just caused.
/// </para>
/// <para>
/// The counts are still worth keeping - a census that moves when nobody imported anything is a real
/// signal - but they are the weakest assertion in each of those fixtures, and the cheap way to
/// survive the next OPR expansion is to lead with the STRUCTURAL invariant (every carrier is a
/// single-model unit; every referencing book embeds the definition; the variant routing is
/// unchanged) and let the total be informational beside it. Several fixtures already do exactly
/// that. Converting the rest is a tidy-up nobody has needed badly enough yet; if you are reading
/// this because you are re-taking twenty numbers again, that has changed.
/// </para>
/// </summary>
internal static class ShippedBooks
{
    public static string Directory => System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    /// <summary>Every bundled book, in every game-system folder.</summary>
    public static IEnumerable<string> AllPaths() =>
        !System.IO.Directory.Exists(Directory)
            ? Enumerable.Empty<string>()
            : System.IO.Directory
                .EnumerateFiles(Directory, "*" + BookFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> GdfPaths() =>
        AllPaths().Where(p => !System.IO.Path.GetFileName(p)
            .StartsWith("AoF-", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The ARMY a book file belongs to, with its game-system prefix removed: both
    /// "GFSQ-LustDisciples" and "LustDisciples" are "LustDisciples".
    /// <para>
    /// #412: OPR publishes the same faction once per system, so a fixture whose claim is about an
    /// ARMY ("only Lust Disciples and War Disciples print the immediate variant") must compare armies
    /// and not file names, or it reads as a new carrier appearing every time a system is imported.
    /// The prefixes are the filename convention the bake scripts apply, which is the same contract
    /// <see cref="GdfPaths"/> already leans on.
    /// </para>
    /// </summary>
    public static string ArmyName(string bookPathOrFileName)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(bookPathOrFileName);
        foreach (string prefix in SystemPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return name[prefix.Length..];
        }

        return name;
    }

    /// <summary>Longest first, so "AoFS-" is never mistaken for "AoF-".</summary>
    private static readonly string[] SystemPrefixes = { "AoFS-", "GFSQ-", "GFF-", "AoF-" };

    /// <summary>
    /// A bundled book by file name, wherever its game-system folder puts it. Throws rather than
    /// returning a missing path, so a renamed or dropped book fails where it is used with a message
    /// that says which book, instead of as a bare FileNotFoundException deep in a reader.
    /// </summary>
    public static string Path(string fileName)
    {
        string? found = AllPaths().FirstOrDefault(
            p => string.Equals(System.IO.Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return found ?? throw new FileNotFoundException(
            $"No bundled book named '{fileName}' under {Directory} or any game-system folder in it.", fileName);
    }
}
