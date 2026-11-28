namespace FdgRaylib.Paper;

/// <summary>
/// "Send my settings to GitHub" for paper models: copies this computer's cut-out figures, each sheet's index and the
/// matches into the game's Assets/PaperModels, so every download stands the same figures. The page pictures stay
/// behind (big, and only the fix tools need them). The shipped folder mirrors this computer's: a sheet or figure
/// deleted here goes from the shipped copy too - unless this computer has no sheets at all (a fresh PC must never
/// wipe what is shipped).
/// </summary>
public static class PaperShipping
{
    /// <summary>Returns how many figures the shipped copy holds afterwards.</summary>
    public static int Harvest(string localRoot, string shippedRoot)
    {
        var sheets = PaperStore.LoadSheets(localRoot);
        int figures = 0;
        if (sheets.Count == 0)
        {
            CopyIfChanged(PaperMatches.LocalPath(localRoot), Path.Combine(shippedRoot, PaperMatches.FileName));
            return PaperStore.LoadSheets(shippedRoot).Sum(s => s.Figures.Count);
        }
        Directory.CreateDirectory(shippedRoot);
        var keepSheets = new HashSet<string>(sheets.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        foreach (string dir in Directory.EnumerateDirectories(shippedRoot))
            if (!keepSheets.Contains(Path.GetFileName(dir))) TryDelete(dir);
        foreach (PaperSheet s in sheets)
        {
            string from = PaperStore.SheetFolder(localRoot, s.Name), to = PaperStore.SheetFolder(shippedRoot, s.Name);
            Directory.CreateDirectory(to);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PaperStore.IndexName };
            foreach (PaperFigure f in s.Figures)
            {
                keep.Add(f.N + ".png");
                if (f.Back != null) keep.Add(f.N + "_back.png");
            }
            foreach (string name in keep) CopyIfChanged(Path.Combine(from, name), Path.Combine(to, name));
            foreach (string file in Directory.EnumerateFiles(to))
                if (!keep.Contains(Path.GetFileName(file))) TryDelete(file);
            figures += s.Figures.Count;
        }
        CopyIfChanged(PaperMatches.LocalPath(localRoot), Path.Combine(shippedRoot, PaperMatches.FileName));
        return figures;
    }

    private static void CopyIfChanged(string from, string to)
    {
        try
        {
            if (!File.Exists(from)) return;
            if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(from).Length && File.ReadAllBytes(to).AsSpan().SequenceEqual(File.ReadAllBytes(from))) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
