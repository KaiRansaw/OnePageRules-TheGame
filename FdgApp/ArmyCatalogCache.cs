namespace FdgRaylib;

/// <summary>
/// #421 - the process-wide <see cref="ArmyCatalog"/>, and the one thing the old version could not do:
/// notice that the armies folder changed.
///
/// <para>
/// <b>The bug this exists to fix.</b> The catalog was a <c>static readonly Lazy&lt;ArmyCatalog&gt;</c>
/// scanned once per launch, with a comment explaining that re-reading 12 MB of JSON per lobby would be
/// waste "because the armies folder doesn't change under us". That was true when the only way to add an
/// army was to drop a file in before starting the game. It stopped being true the moment the in-game
/// Army Forge could save one: build a list, save it, open the lobby, and it is not there. The owner hit
/// exactly that and found the only workaround - restart the game.
/// </para>
/// <para>
/// The scan is genuinely expensive enough to be worth caching (the bundled lists are Forge-built and
/// carry a full book snapshot each), so the answer is not "scan every frame" - it is to keep the cache
/// and give it an <see cref="Invalidate"/>. Three callers, covering every way the folder changes:
/// </para>
/// <list type="bullet">
/// <item>a successful army <b>write</b>, from the Forge or the Army Builder - the common case, and the
/// only one where the app itself is the thing that changed the folder;</item>
/// <item>the <b>lobby being shown</b>, which is where armies are chosen and is reached by navigating, so
/// walking Forge -&gt; Save -&gt; back to the lobby always rescans;</item>
/// <item>the <b>army picker opening</b>, the last moment before a player needs the list to be right -
/// and the one that covers a file dropped in from outside the app while it runs.</item>
/// </list>
/// <para>
/// Invalidation only drops the reference; the next reader starts a fresh background scan, exactly as the
/// first one did. Nothing blocks on the draw thread.
/// </para>
/// </summary>
public static class ArmyCatalogCache
{
    private static readonly object Gate = new();
    private static ArmyCatalog? _current;
    private static string? _signature;

    /// <summary>
    /// The current catalog, scanning in the background on first use after a start or an
    /// <see cref="Invalidate"/>. Readers that cannot block should check
    /// <see cref="ArmyCatalog.IsLoaded"/> first, as the lobby does.
    /// </summary>
    public static ArmyCatalog Current
    {
        get
        {
            lock (Gate)
            {
                if (_current != null) return _current;

                // Creating the Game Type folders here rather than at startup keeps the two together:
                // whatever is about to read the folder is also what guarantees it exists.
                ArmyPaths.EnsureGameTypeFolders();
                _current = new ArmyCatalog();
                return _current;
            }
        }
    }

    /// <summary>
    /// Drops the cached scan, so the next read re-reads the folder. Cheap and safe to call often - it
    /// does no IO itself, and a scan already in flight is simply abandoned rather than cancelled.
    /// </summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _current = null;
            _signature = null;
        }
    }

    /// <summary>
    /// Drops the cache only if the armies folder has actually changed since the last scan, and reports
    /// whether it did.
    ///
    /// <para>
    /// This is what lets the lobby check on every entry without paying for it. A full rescan parses
    /// every list (the Forge-built ones carry a book snapshot each, ~12 MB across the folder); this
    /// walks the tree for names, sizes and write times and compares a signature, which is the question
    /// actually being asked - "is what I scanned still what is on disk?". Returning to the lobby from
    /// the army picker, which happens constantly, therefore costs a directory walk rather than a reparse.
    /// </para>
    /// <para>
    /// Size is in the signature as well as the write time because a list edited and re-saved inside the
    /// same filesystem timestamp granularity would otherwise look unchanged - rare, but the failure
    /// would be the same invisible staleness this class exists to remove.
    /// </para>
    /// </summary>
    public static bool RefreshIfChanged()
    {
        string? now = TrySignature();
        lock (Gate)
        {
            // An unreadable folder (deleted, permissions, a file locked mid-write) is not evidence of a
            // change - keep what we have rather than throwing the cache away on a transient.
            if (now == null) return false;

            // The SIGNATURE alone decides, deliberately not "and we have a cached catalog". Invalidate
            // leaves _current null until someone next reads it, so a condition that also required a
            // live catalog would report a change on every call after any invalidation - which, for a
            // method the lobby runs on every entry, means permanently rescanning the folder. The
            // catalog's own laziness already covers "nothing cached": reading Current builds one.
            if (_signature == now) return false;

            _current = null;
            _signature = now;
            return true;
        }
    }

    /// <summary>
    /// A cheap fingerprint of the armies tree: every list's path, size and write time. Null when the
    /// folder cannot be read, which callers treat as "no information", not as "it changed".
    /// </summary>
    private static string? TrySignature()
    {
        if (ArmyPaths.FolderPath is not { } root) return null;

        try
        {
            var parts = new List<string>();
            foreach (string file in Directory.EnumerateFiles(root,
                         "*" + FDG.SaveLoad.ArmyListFile.EXTENSION_WITH_PERIOD, SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                parts.Add($"{file}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join("\n", parts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when a scan has completed and <see cref="Current"/> will not block.</summary>
    public static bool IsLoaded
    {
        get { lock (Gate) return _current is { IsLoaded: true }; }
    }
}
