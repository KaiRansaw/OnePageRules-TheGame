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
/// #412: the books now live in a folder per game system ("Grimdark Future", "Age of Fantasy", and
/// the still-empty "GF Firefight" and "AoF Skirmish"), because OPR publishes a separate book per
/// system and roughly another 87 are about to arrive. Every lookup here is therefore recursive, and
/// <see cref="Path"/> finds a book by file name wherever it sits - so a fixture never has to know
/// which system folder a book is in.
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
