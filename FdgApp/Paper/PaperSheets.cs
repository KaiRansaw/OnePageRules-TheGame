using System.Security.Cryptography;
using FdgRaylib.Models3D;
using Newtonsoft.Json;

namespace FdgRaylib.Paper;

/// <summary>
/// One imported PDF of paper miniatures: where it came from, its pages, and the figures cut from it. Saved as
/// <c>UnitModels/_Paper/&lt;sheet name&gt;/sheet.json</c> beside the figures' pictures (<c>&lt;n&gt;.png</c>, and
/// <c>&lt;n&gt;_back.png</c> for a fold pair's rear). The page pictures the fix tools draw on are kept apart in
/// <c>_Paper/_pages/&lt;sheet&gt;/</c> - big, local only, never shipped.
/// </summary>
public sealed class PaperSheet
{
    [JsonProperty("name")] public string Name { get; set; } = "";
    /// <summary>The PDF's file name when it was imported (just the name - Kai's folders aren't anyone's business).</summary>
    [JsonProperty("source")] public string Source { get; set; } = "";
    /// <summary>SHA-1 of the PDF: importing the same file again finds this sheet instead of making a second one.</summary>
    [JsonProperty("hash")] public string Hash { get; set; } = "";
    [JsonProperty("dpi")] public int Dpi { get; set; } = PdfPages.DefaultDpi;
    [JsonProperty("pages")] public List<PageSize> Pages { get; set; } = new();
    [JsonProperty("figures")] public List<PaperFigure> Figures { get; set; } = new();
    /// <summary>The next figure number - numbers are never reused, so a match to a deleted figure can't land on another.</summary>
    [JsonProperty("next")] public int Next { get; set; } = 1;

    public sealed class PageSize
    {
        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }
    }

    /// <summary>A figure's id everywhere else (matches, the table): "&lt;sheet&gt;/&lt;n&gt;".</summary>
    public string IdOf(PaperFigure f) => Name + "/" + f.N;

    public PaperFigure? Figure(int n) => Figures.FirstOrDefault(f => f.N == n);
}

/// <summary>
/// Where paper models live and the edits made to them: importing a PDF, and Kai's fixes to a bad cut (redraw a box,
/// split one, merge several, delete, swap front and back, turn a picture over). Every edit re-cuts from the page
/// picture and saves straight away.
/// </summary>
public static class PaperStore
{
    public const string FolderName = "_Paper";
    public const string IndexName = "sheet.json";
    public const string PagesFolder = "_pages";
    /// <summary>The shipped copy, relative to Assets ("Send my settings to GitHub" fills it).</summary>
    public const string ShippedRelativeFolder = "PaperModels";
    /// <summary>The tallest a saved figure picture is (pixels) - the game's repo carries them, so they are kept small.</summary>
    public const int MaxFigurePixels = 512;

    /// <summary>This computer's paper models: UnitModels/_Paper.</summary>
    public static string LocalRoot => Path.Combine(UnitModelPaths.Root, FolderName);
    /// <summary>The ones shipped with the game: Assets/PaperModels.</summary>
    public static string ShippedRoot => Path.Combine(AppAssets.AssetsFolder, ShippedRelativeFolder);

    public static string SheetFolder(string root, string sheet) => Path.Combine(root, sheet);
    public static string FigurePath(string root, string sheet, int n, bool back = false) => Path.Combine(root, sheet, n + (back ? "_back" : "") + ".png");
    public static string PagePath(string root, string sheet, int page) => Path.Combine(root, PagesFolder, sheet, $"page{page + 1}.png");

    /// <summary>Every sheet under <paramref name="root"/>, A-Z.</summary>
    public static List<PaperSheet> LoadSheets(string root)
    {
        var list = new List<PaperSheet>();
        if (!Directory.Exists(root)) return list;
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            if (Path.GetFileName(dir).StartsWith('_')) continue;
            if (LoadSheet(dir) is { } s) list.Add(s);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static PaperSheet? LoadSheet(string folder)
    {
        string f = Path.Combine(folder, IndexName);
        try
        {
            if (!File.Exists(f)) return null;
            var s = JsonConvert.DeserializeObject<PaperSheet>(File.ReadAllText(f));
            if (s == null) return null;
            s.Name = Path.GetFileName(folder); // the folder is the truth (renamed by hand, say)
            return s;
        }
        catch (JsonException) { return null; }
    }

    public static void Save(string root, PaperSheet sheet)
    {
        string dir = SheetFolder(root, sheet.Name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, IndexName), JsonConvert.SerializeObject(sheet, Formatting.Indented));
    }

    /// <summary>A safe, ASCII folder name from a PDF's name.</summary>
    public static string SafeName(string fileName)
    {
        // Either kind of slash: a Windows path read on any computer.
        string last = fileName.Split('/', '\\').Last();
        string stem = last.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
        var chars = stem.Select(c => c < 128 && (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '(' or ')' or '.' or ',' or '&' or '+') ? c : ' ').ToArray();
        string s = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (s.Length > 80) s = s[..80].Trim();
        return s.Length == 0 ? "Sheet" : s;
    }

    public static string HashOf(string path)
    {
        using var sha = SHA1.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    // ================================================================== import

    public sealed record ImportResult(string Sheet, int Figures, bool AlreadyThere, string? Error = null);

    /// <summary>
    /// Imports one PDF into <paramref name="root"/>: every page drawn at the sheet resolution, every figure found and
    /// cut out. The same file imported again (same contents, any name) is not imported twice. <paramref name="progress"/>
    /// hears "page 2 of 5" style lines and a 0-1 share of this file.
    /// </summary>
    public static ImportResult Import(string pdfPath, string root, Action<string, double>? progress = null, CancellationToken cancel = default,
        PaperCutter.Options? options = null)
    {
        options ??= PaperCutter.Default;
        string hash;
        try { hash = HashOf(pdfPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new ImportResult(Path.GetFileName(pdfPath), 0, false, e.Message); }
        if (LoadSheets(root).FirstOrDefault(s => s.Hash == hash) is { } had)
            return new ImportResult(had.Name, had.Figures.Count, true);

        string name = SafeName(pdfPath), baseName = name;
        for (int k = 2; Directory.Exists(SheetFolder(root, name)); k++) name = $"{baseName} ({k})";
        var sheet = new PaperSheet { Name = name, Source = Path.GetFileName(pdfPath), Hash = hash, Dpi = options.Dpi };
        try
        {
            byte[] pdf = File.ReadAllBytes(pdfPath);
            int pages = PdfPages.PageCount(pdfPath);
            for (int p = 0; p < pages; p++)
            {
                cancel.ThrowIfCancellationRequested();
                progress?.Invoke($"{sheet.Name}: page {p + 1} of {pages}", p / (double)pages);
                RgbaImage page = PdfPages.Render(pdf, p, options.Dpi);
                sheet.Pages.Add(new PaperSheet.PageSize { W = page.Width, H = page.Height });
                string pagePng = PagePath(root, sheet.Name, p);
                Directory.CreateDirectory(Path.GetDirectoryName(pagePng)!);
                File.WriteAllBytes(pagePng, page.ToPng(opaque: true));
                var paper = PaperCutter.PaperColour(page);
                foreach (PaperFigure f in PaperCutter.FindFigures(page, options))
                {
                    f.Page = p;
                    f.N = sheet.Next++;
                    WritePictures(root, sheet, f, page, paper, options);
                    sheet.Figures.Add(f);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Remove(root, sheet.Name);
            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Remove(root, sheet.Name);
            return new ImportResult(sheet.Name, 0, false, e.Message);
        }
        Save(root, sheet);
        progress?.Invoke($"{sheet.Name}: {sheet.Figures.Count} figures", 1);
        return new ImportResult(sheet.Name, sheet.Figures.Count, false);
    }

    /// <summary>Every PDF in a folder (and its sub-folders).</summary>
    public static IEnumerable<string> PdfsIn(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

    /// <summary>Deletes a sheet, its pictures and its page pictures.</summary>
    public static void Remove(string root, string sheet)
    {
        foreach (string d in new[] { SheetFolder(root, sheet), Path.Combine(root, PagesFolder, sheet) })
            try { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Writes a figure's front (and back) pictures from the page.</summary>
    public static void WritePictures(string root, PaperSheet sheet, PaperFigure f, RgbaImage page, (byte R, byte G, byte B)? paper = null, PaperCutter.Options? o = null)
    {
        o ??= PaperCutter.Default with { Dpi = sheet.Dpi };
        paper ??= PaperCutter.PaperColour(page);
        RgbaImage front = PaperCutter.CutOut(page, f.Front, MaxFigurePixels, o, paper);
        front.SavePng(FigurePath(root, sheet.Name, f.N));
        f.PictureW = front.Width;
        f.PictureH = front.Height;
        string backPath = FigurePath(root, sheet.Name, f.N, back: true);
        if (f.Back != null) PaperCutter.CutOut(page, f.Back, MaxFigurePixels, o, paper).SavePng(backPath);
        else if (File.Exists(backPath)) File.Delete(backPath);
    }

    private static void DeletePictures(string root, PaperSheet sheet, int n)
    {
        foreach (bool back in new[] { false, true })
        {
            string p = FigurePath(root, sheet.Name, n, back);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    // ================================================================== fixing a bad cut

    /// <summary>A sheet's page picture (the fix tools' drawing surface), or null when it was never kept here.</summary>
    public static RgbaImage? LoadPage(string root, PaperSheet sheet, int page)
    {
        string p = PagePath(root, sheet.Name, page);
        return File.Exists(p) ? RgbaImage.LoadPng(p) : null;
    }

    /// <summary>A new figure from a box drawn on the page (all the ink inside it). Null when the box holds no ink.</summary>
    public static PaperFigure? AddBox(string root, PaperSheet sheet, int page, PixelRect box, RgbaImage pageImage)
    {
        var o = PaperCutter.Default with { Dpi = sheet.Dpi };
        if (PaperCutter.Detect(pageImage, box, null, o) is not { } f) return null;
        f.Page = page;
        f.N = sheet.Next++;
        WritePictures(root, sheet, f, pageImage, null, o);
        sheet.Figures.Add(f);
        Save(root, sheet);
        return f;
    }

    /// <summary>The figure's box redrawn: cut again from what is inside the new box (same number, so matches stay).</summary>
    public static bool Redraw(string root, PaperSheet sheet, int n, PixelRect box, RgbaImage pageImage)
    {
        if (sheet.Figure(n) is not { } old) return false;
        var o = PaperCutter.Default with { Dpi = sheet.Dpi };
        if (PaperCutter.Detect(pageImage, box, null, o) is not { } f) return false;
        f.N = old.N;
        f.Page = old.Page;
        sheet.Figures[sheet.Figures.IndexOf(old)] = f;
        WritePictures(root, sheet, f, pageImage, null, o);
        Save(root, sheet);
        return true;
    }

    /// <summary>
    /// Splits a figure in two along a line across its box (<paramref name="vertical"/>: a line straight down at page x =
    /// <paramref name="at"/>; else straight across at page y). The left / top part keeps the number, the other is new.
    /// Returns the new figures (none when the line misses the box or one side has no ink).
    /// </summary>
    public static List<PaperFigure> Split(string root, PaperSheet sheet, int n, int at, bool vertical, RgbaImage pageImage)
    {
        var made = new List<PaperFigure>();
        if (sheet.Figure(n) is not { } old) return made;
        PixelRect b = old.Box;
        var (a, c) = vertical
            ? (new PixelRect(b.X, b.Y, at - b.X, b.H), new PixelRect(at, b.Y, b.Right - at, b.H))
            : (new PixelRect(b.X, b.Y, b.W, at - b.Y), new PixelRect(b.X, at, b.W, b.Bottom - at));
        if (a.W <= 0 || a.H <= 0 || c.W <= 0 || c.H <= 0) return made;
        var o = PaperCutter.Default with { Dpi = sheet.Dpi };
        if (PaperCutter.Detect(pageImage, a, null, o) is not { } fa || PaperCutter.Detect(pageImage, c, null, o) is not { } fc) return made;
        fa.N = old.N; fa.Page = old.Page;
        fc.N = sheet.Next++; fc.Page = old.Page;
        int i = sheet.Figures.IndexOf(old);
        sheet.Figures[i] = fa;
        sheet.Figures.Insert(i + 1, fc);
        WritePictures(root, sheet, fa, pageImage, null, o);
        WritePictures(root, sheet, fc, pageImage, null, o);
        Save(root, sheet);
        made.Add(fa);
        made.Add(fc);
        return made;
    }

    /// <summary>Merges figures (same page) into one cut over their joined boxes. The lowest number is kept; the others'
    /// pictures are deleted. Returns the merged figure, or null (fewer than two, or on different pages).</summary>
    public static PaperFigure? Merge(string root, PaperSheet sheet, IReadOnlyCollection<int> ns, RgbaImage pageImage)
    {
        var figs = sheet.Figures.Where(f => ns.Contains(f.N)).ToList();
        if (figs.Count < 2 || figs.Select(f => f.Page).Distinct().Count() != 1) return null;
        PixelRect box = figs.Skip(1).Aggregate(figs[0].Box, (acc, f) => acc.Union(f.Box));
        var o = PaperCutter.Default with { Dpi = sheet.Dpi };
        if (PaperCutter.Detect(pageImage, box, null, o) is not { } merged) return null;
        var keep = figs.OrderBy(f => f.N).First();
        merged.N = keep.N;
        merged.Page = keep.Page;
        int at = sheet.Figures.IndexOf(keep);
        foreach (var f in figs) if (f != keep) { sheet.Figures.Remove(f); DeletePictures(root, sheet, f.N); }
        sheet.Figures[sheet.Figures.IndexOf(keep)] = merged;
        _ = at;
        WritePictures(root, sheet, merged, pageImage, null, o);
        Save(root, sheet);
        return merged;
    }

    /// <summary>Deletes figures (not a figure - a title, a logo).</summary>
    public static void Delete(string root, PaperSheet sheet, IEnumerable<int> ns)
    {
        foreach (int n in ns.ToList())
            if (sheet.Figure(n) is { } f) { sheet.Figures.Remove(f); DeletePictures(root, sheet, n); }
        Save(root, sheet);
    }

    /// <summary>Front and back the other way round (the cutter guessed the wrong copy as the front).</summary>
    public static void SwapFrontBack(string root, PaperSheet sheet, int n, RgbaImage pageImage)
    {
        if (sheet.Figure(n) is not { Back: { } back } f) return;
        (f.Front, f.Back) = (back, f.Front);
        WritePictures(root, sheet, f, pageImage);
        Save(root, sheet);
    }

    /// <summary>Turns the front picture (and the back with it) half a turn - a figure standing on its head.</summary>
    public static void TurnOver(string root, PaperSheet sheet, int n, RgbaImage pageImage)
    {
        if (sheet.Figure(n) is not { } f) return;
        f.Front.Turned = !f.Front.Turned;
        if (f.Back != null) f.Back.Turned = !f.Back.Turned;
        WritePictures(root, sheet, f, pageImage);
        Save(root, sheet);
    }

    /// <summary>Not a fold pair after all: the whole box is the front, no back.</summary>
    public static void MakeSingle(string root, PaperSheet sheet, int n, RgbaImage pageImage)
    {
        if (sheet.Figure(n) is not { } f) return;
        f.Front = new PaperPart { Rect = f.Box };
        f.Back = null;
        f.Fold = PaperFold.None;
        WritePictures(root, sheet, f, pageImage);
        Save(root, sheet);
    }
}
