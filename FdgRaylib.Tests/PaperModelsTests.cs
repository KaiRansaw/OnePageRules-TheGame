using System.Globalization;
using System.Text;
using FdgRaylib.Config;
using FdgRaylib.Models3D;
using FdgRaylib.Paper;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// Paper models (lane 5, Kai 2026-11-27): OPR's free paper miniatures, imported from PDF sheets, cut out, matched to
/// units and stood on the table as standees. These tests cover everything without drawing: the cutter on generated
/// sheets (no real OPR PDFs in the cloud - Kai's sheets are the real test), the PDF import through PDFium, the fix
/// tools, the matches file, the library lookup and the look picking order.
/// </summary>
[TestFixture]
public class PaperModelsTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-paper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    // ================================================================== a made-up sheet

    /// <summary>A rectangle of a stand-in figure, in a 0-1 box (0 = head end).</summary>
    private sealed record Bit(double X0, double Y0, double X1, double Y1, byte R, byte G, byte B);

    /// <summary>A little soldier: head, body, arms, legs (a gap between them over the base), a base strip, and a white
    /// highlight inside the body that must stay opaque.</summary>
    private static List<Bit> Soldier(byte r, byte g, byte b)
    {
        byte dr = (byte)(r / 2), dg = (byte)(g / 2), db = (byte)(b / 2);
        return new()
        {
            new(0.33, 0.02, 0.67, 0.2, 226, 180, 140),
            new(0.22, 0.19, 0.78, 0.55, r, g, b),
            new(0.05, 0.22, 0.22, 0.5, dr, dg, db),
            new(0.78, 0.22, 0.95, 0.5, dr, dg, db),
            new(0.24, 0.55, 0.42, 0.92, dr, dg, db),
            new(0.58, 0.55, 0.76, 0.92, dr, dg, db),
            new(0.0, 0.92, 1.0, 1.0, 70, 110, 60),
            new(0.45, 0.3, 0.52, 0.36, 255, 255, 255),
        };
    }

    /// <summary>A figure placed on a page (in points, 72 to the inch; y down), <paramref name="upsideDown"/> = the
    /// fold-back copy.</summary>
    private sealed record Placed(double X, double Y, double W, double H, List<Bit> Bits, bool UpsideDown);

    /// <summary>A sheet like OPR's: rows of fold pairs (back upside down on top, a thin fold line, front upright below),
    /// a single figure, and a title line.</summary>
    private static (List<Placed> Figures, List<(double X, double Y, double W, double H)> Lines) Sheet(int rows, int cols, bool single)
    {
        var figs = new List<Placed>();
        var lines = new List<(double, double, double, double)> { (30, 20, 300, 6) }; // the title: a thin dark bar
        double fw = 65, fh = 108, gap = 25;
        var rnd = new Random(7);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double x = 40 + c * (fw + gap), y = 50 + r * (2 * fh + gap + 4);
                var bits = Soldier((byte)rnd.Next(40, 200), (byte)rnd.Next(40, 200), (byte)rnd.Next(40, 200));
                figs.Add(new Placed(x, y, fw, fh, bits, true));
                lines.Add((x - 3, y + fh + 1, fw + 6, 1.2));
                figs.Add(new Placed(x, y + fh + 4, fw, fh, bits, false));
            }
        if (single) figs.Add(new Placed(470, 640, 60, 110, Soldier(30, 60, 160), false));
        return (figs, lines);
    }

    private static IEnumerable<(double X, double Y, double W, double H, byte R, byte G, byte B)> Rects(List<Placed> figs, List<(double X, double Y, double W, double H)> lines)
    {
        foreach (var f in figs)
            foreach (var b in f.Bits)
            {
                double y0 = f.UpsideDown ? 1 - b.Y1 : b.Y0, y1 = f.UpsideDown ? 1 - b.Y0 : b.Y1;
                yield return (f.X + b.X0 * f.W, f.Y + y0 * f.H, (b.X1 - b.X0) * f.W, (y1 - y0) * f.H, b.R, b.G, b.B);
            }
        foreach (var l in lines) yield return (l.X, l.Y, l.W, l.H, 20, 20, 20);
    }

    /// <summary>The sheet drawn straight into a picture at <paramref name="dpi"/> (A4).</summary>
    private static RgbaImage Raster(List<Placed> figs, List<(double X, double Y, double W, double H)> lines, int dpi = 150)
    {
        double k = dpi / 72.0;
        var img = RgbaImage.Blank((int)(595 * k), (int)(842 * k));
        foreach (var r in Rects(figs, lines))
            img.Fill((int)Math.Round(r.X * k), (int)Math.Round(r.Y * k), Math.Max(1, (int)Math.Round(r.W * k)), Math.Max(1, (int)Math.Round(r.H * k)), r.R, r.G, r.B);
        return img;
    }

    /// <summary>The sheet as a real (vector) PDF, one page per entry.</summary>
    private static byte[] Pdf(params (List<Placed> Figs, List<(double X, double Y, double W, double H)> Lines)[] pages)
    {
        var objs = new List<string>();
        int pagesObj = 2;
        var kids = new List<int>();
        var contents = new List<string>();
        foreach (var p in pages)
        {
            var sb = new StringBuilder();
            foreach (var r in Rects(p.Figs, p.Lines))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} rg {3:0.##} {4:0.##} {5:0.##} {6:0.##} re f",
                    r.R / 255.0, r.G / 255.0, r.B / 255.0, r.X, 842 - r.Y - r.H, r.W, r.H));
            contents.Add(sb.ToString());
        }
        objs.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objs.Add(""); // pages, filled below
        for (int i = 0; i < pages.Length; i++)
        {
            int pageNo = objs.Count + 1;
            kids.Add(pageNo);
            objs.Add($"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 595 842] /Contents {pageNo + 1} 0 R /Resources << >> >>");
            objs.Add($"<< /Length {Encoding.ASCII.GetByteCount(contents[i])} >>\nstream\n{contents[i]}endstream");
        }
        objs[1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.Select(k => k + " 0 R"))}] /Count {kids.Count} >>";
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objs.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (int o in offsets) pdf.Append($"{o:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static readonly PaperCutter.Options At150 = PaperCutter.Default with { Dpi = 150 };

    // ================================================================== the cutter

    [Test]
    public void TheCutter_FindsEveryFoldPair_AndDropsTheTitle()
    {
        var (figs, lines) = Sheet(2, 5, single: false);
        var found = PaperCutter.FindFigures(Raster(figs, lines), At150);

        Assert.That(found, Has.Count.EqualTo(10), "2 rows x 5 fold pairs; the title bar is too short to be a figure");
        Assert.That(found.All(f => f.Fold == PaperFold.TopBottom), "each strip is a fold pair");
        Assert.That(found.All(f => f.Back != null && f.Back.Turned && !f.Front.Turned),
            "the back copy is printed upside down and turned to stand; the front is printed upright");
        // reading order: first row left to right
        Assert.That(found.Take(5).Select(f => f.Box.X), Is.Ordered);
    }

    [Test]
    public void TheFront_IsTheCopyStandingOnItsBase_WhicheverWayTheStripRuns()
    {
        var (figs, lines) = Sheet(1, 1, single: false);
        double k = 150 / 72.0;
        var found = PaperCutter.FindFigures(Raster(figs, lines), At150);
        Assert.That(found, Has.Count.EqualTo(1));
        double frontTop = found[0].Front.Rect.Y / k, backTop = found[0].Back!.Rect.Y / k;
        Assert.That(frontTop, Is.GreaterThan(backTop), "back upside down on top, front upright below: the front is the lower copy");

        // The same strip the other way up (feet at the fold): now the top copy is the upright one.
        var flipped = figs.Select(f => f with { UpsideDown = !f.UpsideDown }).ToList();
        var found2 = PaperCutter.FindFigures(Raster(flipped, lines), At150);
        Assert.That(found2, Has.Count.EqualTo(1));
        Assert.That(found2[0].Front.Rect.Y, Is.LessThan(found2[0].Back!.Rect.Y), "feet at the fold: the upright copy is on top");
    }

    [Test]
    public void ASingleFigure_IsNotAPair()
    {
        var (figs, lines) = Sheet(1, 2, single: true);
        var found = PaperCutter.FindFigures(Raster(figs, lines), At150);
        Assert.That(found, Has.Count.EqualTo(3));
        var single = found.Single(f => f.Fold == PaperFold.None);
        Assert.That(single.Back, Is.Null);
        Assert.That(single.Front.Rect, Is.EqualTo(single.Box));
    }

    [Test]
    public void TheCutOut_ClearsThePaper_KeepsHighlights_AndClearsTheGapBetweenTheLegs()
    {
        var fig = new Placed(100, 100, 120, 220, Soldier(150, 40, 40), false);
        var page = Raster(new List<Placed> { fig }, new(), 150);
        var found = PaperCutter.FindFigures(page, At150);
        Assert.That(found, Has.Count.EqualTo(1));
        RgbaImage cut = PaperCutter.CutOut(page, found[0].Front, 4000, At150);

        double sx = cut.Width, sy = cut.Height;
        byte AlphaAt(double fx, double fy) => cut.Get((int)(fx * sx), (int)(fy * sy)).A;
        Assert.That(AlphaAt(0.02, 0.05), Is.EqualTo(0), "paper beside the head is see-through");
        Assert.That(AlphaAt(0.5, 0.45), Is.EqualTo(255), "the body is solid");
        Assert.That(AlphaAt(0.485, 0.33), Is.EqualTo(255), "the white highlight inside the body stays (it's paint, not paper)");
        Assert.That(AlphaAt(0.5, 0.75), Is.EqualTo(0), "the paper gap between the legs, closed in by the base strip, is cleared");
        Assert.That(AlphaAt(0.5, 0.97), Is.EqualTo(255), "the base strip is solid");
    }

    [Test]
    public void ANarrowSlitBetweenTheLegs_IsClearedToo()
    {
        // Legs 2 % apart over the base strip: too small an area for the 1 % rule, but half the figure's height.
        var bits = Soldier(40, 150, 40);
        bits[4] = bits[4] with { X0 = 0.30, X1 = 0.49 };
        bits[5] = bits[5] with { X0 = 0.51, X1 = 0.70 };
        var page = Raster(new List<Placed> { new(100, 100, 120, 220, bits, false) }, new(), 150);
        var f = PaperCutter.FindFigures(page, At150).Single();
        RgbaImage cut = PaperCutter.CutOut(page, f.Front, 4000, At150);
        Assert.That(cut.Get((int)(0.5 * cut.Width), (int)(0.75 * cut.Height)).A, Is.EqualTo(0), "the slit is paper, not paint");
        Assert.That(cut.Get((int)(0.485 * cut.Width), (int)(0.33 * cut.Height)).A, Is.EqualTo(255), "the small highlight still stays");
    }

    [Test]
    public void TheBackPicture_IsTurnedToStand()
    {
        var (figs, lines) = Sheet(1, 1, single: false);
        var page = Raster(figs, lines);
        var f = PaperCutter.FindFigures(page, At150).Single();
        RgbaImage back = PaperCutter.CutOut(page, f.Back!, 4000, At150);
        // The base strip (green 70,110,60) is at the bottom of a standing picture.
        var (r, g, b, a) = back.Get(back.Width / 2, back.Height - 2);
        Assert.That((r, g, b, a), Is.EqualTo(((byte)70, (byte)110, (byte)60, (byte)255)));
        var top = back.Get(back.Width / 2, 1);
        Assert.That(top.A == 0 || (top.R, top.G, top.B) == (226, 180, 140), "the head is at the top");
    }

    [Test]
    public void SavedFigures_AreAtMost512Tall_AndThePngReadsBack()
    {
        var fig = new Placed(50, 50, 300, 700, Soldier(10, 120, 200), false);
        var page = Raster(new List<Placed> { fig }, new(), 200);
        var f = PaperCutter.FindFigures(page, PaperCutter.Default).Single();
        RgbaImage cut = PaperCutter.CutOut(page, f.Front, PaperStore.MaxFigurePixels);
        Assert.That(cut.Height, Is.EqualTo(512));
        string png = Path.Combine(_dir, "x.png");
        cut.SavePng(png);
        var back = RgbaImage.LoadPng(png);
        Assert.That((back.Width, back.Height), Is.EqualTo((cut.Width, cut.Height)));
        Assert.That(back.Pixels, Is.EqualTo(cut.Pixels), "lossless round trip");
        Assert.That(PaperLibrary.PngSize(png), Is.EqualTo((cut.Width, cut.Height)));
    }

    // ================================================================== import from a PDF

    private string WritePdf(string name, byte[] bytes)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Test]
    public void ImportingAPdf_CutsEveryPage_AndTheSameFileTwiceIsNotImportedAgain()
    {
        var p1 = Sheet(2, 4, single: true);
        var p2 = Sheet(1, 3, single: false);
        string pdf = WritePdf("Battle Brothers Paper Minis.pdf", Pdf(p1, p2));
        string root = Path.Combine(_dir, "_Paper");
        var lines = new List<string>();

        var res = PaperStore.Import(pdf, root, (t, _) => lines.Add(t));

        Assert.That(res.Error, Is.Null);
        Assert.That(res.AlreadyThere, Is.False);
        Assert.That(res.Figures, Is.EqualTo(9 + 3));
        Assert.That(lines, Has.Some.Contains("page 2 of 2"));
        var sheet = PaperStore.LoadSheets(root).Single();
        Assert.That(sheet.Name, Is.EqualTo("Battle Brothers Paper Minis"));
        Assert.That(sheet.Pages, Has.Count.EqualTo(2));
        Assert.That(sheet.Figures.Select(f => f.N), Is.EqualTo(Enumerable.Range(1, 12)));
        foreach (var f in sheet.Figures)
        {
            Assert.That(File.Exists(PaperStore.FigurePath(root, sheet.Name, f.N)));
            Assert.That(File.Exists(PaperStore.FigurePath(root, sheet.Name, f.N, back: true)), Is.EqualTo(f.Back != null));
            Assert.That(f.PictureH, Is.InRange(1, PaperStore.MaxFigurePixels));
        }
        Assert.That(File.Exists(PaperStore.PagePath(root, sheet.Name, 1)), "page pictures kept for the fix tools");

        // the same contents under another name: found, not imported twice
        string copy = WritePdf("renamed.pdf", File.ReadAllBytes(pdf));
        var again = PaperStore.Import(copy, root);
        Assert.That(again.AlreadyThere, Is.True);
        Assert.That(again.Sheet, Is.EqualTo(sheet.Name));
        Assert.That(PaperStore.LoadSheets(root), Has.Count.EqualTo(1));
    }

    [Test]
    public void ABrokenPdf_SaysSo_AndLeavesNothingBehind()
    {
        string pdf = WritePdf("broken.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 not really"));
        string root = Path.Combine(_dir, "_Paper");
        var res = PaperStore.Import(pdf, root);
        Assert.That(res.Error, Is.Not.Null);
        Assert.That(PaperStore.LoadSheets(root), Is.Empty);
        Assert.That(Directory.Exists(PaperStore.SheetFolder(root, "broken")), Is.False);
    }

    [Test]
    public void SafeNames_AreAscii()
    {
        Assert.That(PaperStore.SafeName(@"C:\x\Orc Marauders – Paper Minis.pdf"), Is.EqualTo("Orc Marauders Paper Minis"));
        Assert.That(PaperStore.SafeName("???.pdf"), Is.EqualTo("Sheet"));
    }

    // ================================================================== fix tools

    private (string Root, PaperSheet Sheet, RgbaImage Page) OneSheet(List<Placed> figs, List<(double X, double Y, double W, double H)> lines)
    {
        string root = Path.Combine(_dir, "_Paper");
        var page = Raster(figs, lines, 150);
        var sheet = new PaperSheet { Name = "Test", Dpi = 150 };
        sheet.Pages.Add(new PaperSheet.PageSize { W = page.Width, H = page.Height });
        foreach (var f in PaperCutter.FindFigures(page, At150))
        {
            f.N = sheet.Next++;
            PaperStore.WritePictures(root, sheet, f, page);
            sheet.Figures.Add(f);
        }
        PaperStore.Save(root, sheet);
        return (root, sheet, page);
    }

    [Test]
    public void SplitAndMerge_FixTwoFiguresCutAsOne()
    {
        // Two single figures touching side by side: the cutter sees one blob.
        var a = new Placed(100, 100, 60, 110, Soldier(200, 40, 40), false);
        var b = new Placed(160, 100, 60, 110, Soldier(40, 40, 200), false);
        var (root, sheet, page) = OneSheet(new List<Placed> { a, b }, new());
        Assert.That(sheet.Figures, Has.Count.EqualTo(1), "touching figures come out as one");
        int n = sheet.Figures[0].N;

        double k = 150 / 72.0;
        var made = PaperStore.Split(root, sheet, n, (int)(160 * k), vertical: true, page);
        Assert.That(made, Has.Count.EqualTo(2));
        Assert.That(made[0].N, Is.EqualTo(n), "the left part keeps the number (and its matches)");
        Assert.That(made[1].N, Is.EqualTo(n + 1));
        Assert.That(PaperStore.LoadSheet(PaperStore.SheetFolder(root, "Test"))!.Figures, Has.Count.EqualTo(2), "saved");
        Assert.That(made[0].Box.Right, Is.LessThanOrEqualTo((int)(160 * k) + 1));

        var merged = PaperStore.Merge(root, sheet, new[] { made[0].N, made[1].N }, page);
        Assert.That(merged, Is.Not.Null);
        Assert.That(sheet.Figures, Has.Count.EqualTo(1));
        Assert.That(merged!.N, Is.EqualTo(n));
        Assert.That(File.Exists(PaperStore.FigurePath(root, "Test", n + 1)), Is.False, "the merged-away figure's picture goes");
        Assert.That(sheet.Next, Is.EqualTo(n + 2), "numbers are never reused");
    }

    [Test]
    public void RedrawDeleteSwapTurnAndSingle()
    {
        var (figs, lines) = Sheet(1, 2, single: false);
        var (root, sheet, page) = OneSheet(figs, lines);
        Assert.That(sheet.Figures, Has.Count.EqualTo(2));
        var f1 = sheet.Figures[0];

        // Redraw a box round only the front: same number, no fold any more.
        Assert.That(PaperStore.Redraw(root, sheet, f1.N, f1.Front.Rect, page), Is.True);
        var r = sheet.Figure(f1.N)!;
        Assert.That(r.Fold, Is.EqualTo(PaperFold.None));
        Assert.That(File.Exists(PaperStore.FigurePath(root, "Test", f1.N, back: true)), Is.False, "no back picture left over");

        var f2 = sheet.Figures[1];
        var front = f2.Front.Rect;
        PaperStore.SwapFrontBack(root, sheet, f2.N, page);
        Assert.That(sheet.Figure(f2.N)!.Back!.Rect, Is.EqualTo(front));
        PaperStore.TurnOver(root, sheet, f2.N, page);
        Assert.That(sheet.Figure(f2.N)!.Front.Turned, Is.False, "the swapped-in front was turned; turning over undoes it");
        PaperStore.MakeSingle(root, sheet, f2.N, page);
        Assert.That(sheet.Figure(f2.N)!.Back, Is.Null);

        PaperStore.Delete(root, sheet, new[] { f2.N });
        Assert.That(sheet.Figures, Has.Count.EqualTo(1));
        Assert.That(File.Exists(PaperStore.FigurePath(root, "Test", f2.N)), Is.False);

        var added = PaperStore.AddBox(root, sheet, 0, f2.Box, page);
        Assert.That(added, Is.Not.Null);
        Assert.That(added!.N, Is.EqualTo(sheet.Next - 1));
        Assert.That(PaperStore.AddBox(root, sheet, 0, new PixelRect(5, 300, 20, 20), page), Is.Null, "an empty box is no figure");
    }

    // ================================================================== matches and the library

    [Test]
    public void Matches_RoundTrip_AndLocalLiesOverShipped()
    {
        string path = Path.Combine(_dir, PaperMatches.FileName);
        var m = new PaperMatches();
        m.Assign("Grimdark Future", "Battle Brothers", "Assault Brothers", new[] { "Sheet A/1", "Sheet A/2" },
            new[] { new UnitModelMap.Alias { System = "GF Firefight", Unit = "Assault Brother" } });
        m.Assign("Grimdark Future", "Battle Brothers", "Assault Brothers", new[] { "Sheet A/2", "Sheet A/3" });
        m.Assign("AoF Skirmish", "Orc Marauders", "Goblin", new[] { "Sheet B/1" });
        m.Save(path);
        var back = PaperMatches.Load(path);
        Assert.That(back.Find("Grimdark Future", "Battle Brothers", "Assault Brothers")!.Figures, Is.EqualTo(new[] { "Sheet A/1", "Sheet A/2", "Sheet A/3" }));
        Assert.That(back.Find("Grimdark Future", "Battle Brothers", "Assault Brothers")!.AlsoIn.Single().Unit, Is.EqualTo("Assault Brother"));
        Assert.That(back.UnitsOf("Sheet A/2").Count(), Is.EqualTo(1));

        back.Unassign("Grimdark Future", "Battle Brothers", "Assault Brothers", new[] { "Sheet A/1" });
        Assert.That(back.IsMatched("Sheet A/1"), Is.False);
        Assert.That(back.UnassignEverywhere(new[] { "Sheet B/1" }), Is.EqualTo(1));
        Assert.That(back.Find("AoF Skirmish", "Orc Marauders", "Goblin"), Is.Null, "a unit with no figures left goes");

        var shipped = new PaperMatches();
        shipped.Assign("Grimdark Future", "Battle Brothers", "Assault Brothers", new[] { "Old/9" });
        shipped.Assign("Grimdark Future", "Alien Hives", "Hive Lord", new[] { "Old/1" });
        var merged = PaperMatches.Merged(shipped, back);
        Assert.That(merged.Find("Grimdark Future", "Battle Brothers", "Assault Brothers")!.Figures, Does.Not.Contain("Old/9"), "this computer's list wins");
        Assert.That(merged.Find("Grimdark Future", "Alien Hives", "Hive Lord"), Is.Not.Null, "a unit only the shipped copy has is kept");
    }

    private static void FakeFigure(string root, string sheet, int n, bool back = false)
    {
        RgbaImage.Blank(20, 50).SavePng(PaperStore.FigurePath(root, sheet, n));
        if (back) RgbaImage.Blank(20, 50).SavePng(PaperStore.FigurePath(root, sheet, n, back: true));
    }

    [Test]
    public void TheLibrary_FindsAUnitsFigures_ByAnyGamesName_AndMixesThemInASquad()
    {
        string local = Path.Combine(_dir, "local"), shipped = Path.Combine(_dir, "shipped");
        FakeFigure(local, "Sheet A", 1, back: true);
        FakeFigure(local, "Sheet A", 2);
        FakeFigure(shipped, "Sheet S", 1);
        var m = new PaperMatches();
        m.Assign("Grimdark Future", "Battle Brothers", "Assault Brothers", new[] { "Sheet A/1", "Sheet A/2", "Sheet A/404" },
            new[] { new UnitModelMap.Alias { System = "GF Star Quest", Unit = "Assault Brother" } });
        m.Save(PaperMatches.LocalPath(local));
        var s = new PaperMatches();
        s.Assign("Grimdark Future", "Alien Hives", "Hive Lord", new[] { "Sheet S/1" });
        s.Save(Path.Combine(shipped, PaperMatches.FileName));

        var lib = PaperLibrary.Build(local, shipped);
        var figs = lib.FiguresFor("Battle Brothers", "Assault Brothers");
        Assert.That(figs.Select(f => f.Id), Is.EqualTo(new[] { "Sheet A/1", "Sheet A/2" }), "a figure whose picture is gone is skipped");
        Assert.That(figs[0].BackPath, Is.Not.Null);
        Assert.That(figs[1].BackPath, Is.Null);
        Assert.That(figs[0].Aspect, Is.EqualTo(2.5f).Within(0.01f), "size read from the PNG when no sheet index says");
        Assert.That(lib.FiguresFor("battle brothers", "Assault Brother"), Has.Count.EqualTo(2), "Star Quest's name finds GF's match");
        Assert.That(lib.FiguresFor("Alien Hives", "Hive Lord").Single().FrontPath, Does.StartWith(shipped), "shipped figures are used where this PC has none");
        Assert.That(lib.FiguresFor("Some Ally Book", "Hive Lord"), Has.Count.EqualTo(1), "an allied unit is found in the one book that has it");
        Assert.That(lib.FiguresFor("Battle Brothers", "Destroyers"), Is.Empty);

        Assert.That(lib.FigureFor("Battle Brothers", "Assault Brothers", null, 0)!.Id, Is.EqualTo("Sheet A/1"));
        Assert.That(lib.FigureFor("Battle Brothers", "Assault Brothers", null, 1)!.Id, Is.EqualTo("Sheet A/2"));
        Assert.That(lib.FigureFor("Battle Brothers", "Assault Brothers", null, 2)!.Id, Is.EqualTo("Sheet A/1"), "a squad takes the figures in turn");
        Assert.That(lib.FigureFor("Battle Brothers", "Assault Brothers", "paper:Sheet A/2", 0)!.Id, Is.EqualTo("Sheet A/2"), "a picked figure wins");
        Assert.That(lib.FigureFor("Battle Brothers", "Assault Brothers", "Battle Brothers/Assault Brothers/x.glb", 0)!.Id, Is.EqualTo("Sheet A/1"), "a 3D look is not a paper pick");
    }

    [Test]
    public void SendMySettings_ShipsFiguresAndMatches_NotPages_AndMirrorsDeletes()
    {
        string local = Path.Combine(_dir, "local"), shipped = Path.Combine(_dir, "shipped");
        var (figs, lines) = Sheet(1, 2, single: false);
        var page = Raster(figs, lines);
        var sheet = new PaperSheet { Name = "S", Dpi = 150 };
        foreach (var f in PaperCutter.FindFigures(page, At150)) { f.N = sheet.Next++; PaperStore.WritePictures(local, sheet, f, page); sheet.Figures.Add(f); }
        PaperStore.Save(local, sheet);
        Directory.CreateDirectory(Path.GetDirectoryName(PaperStore.PagePath(local, "S", 0))!);
        File.WriteAllBytes(PaperStore.PagePath(local, "S", 0), page.ToPng(true));
        var m = new PaperMatches();
        m.Assign("Grimdark Future", "Battle Brothers", "Assault Brothers", new[] { "S/1" });
        m.Save(PaperMatches.LocalPath(local));
        Directory.CreateDirectory(Path.Combine(shipped, "Gone Sheet"));

        int n = PaperShipping.Harvest(local, shipped);

        Assert.That(n, Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(shipped, "S", "1.png")) && File.Exists(Path.Combine(shipped, "S", "1_back.png")));
        Assert.That(File.Exists(Path.Combine(shipped, "S", PaperStore.IndexName)));
        Assert.That(File.Exists(Path.Combine(shipped, PaperMatches.FileName)));
        Assert.That(Directory.Exists(Path.Combine(shipped, PaperStore.PagesFolder)), Is.False, "page pictures stay home");
        Assert.That(Directory.Exists(Path.Combine(shipped, "Gone Sheet")), Is.False, "a sheet deleted here goes from the shipped copy");

        PaperStore.Delete(local, sheet, new[] { 2 });
        PaperShipping.Harvest(local, shipped);
        Assert.That(File.Exists(Path.Combine(shipped, "S", "2.png")), Is.False);

        // A computer with no sheets never wipes the shipped ones.
        string fresh = Path.Combine(_dir, "fresh");
        Assert.That(PaperShipping.Harvest(fresh, shipped), Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(shipped, "S", "1.png")));

        Assert.That(SharedSettingsSync.ShippedFiles, Does.Contain(PaperStore.ShippedRelativeFolder), "Send my settings adds the folder to git");
    }

    // ================================================================== on the table

    [TestCase(UnitLookMode.Auto, true, true, UnitLookKind.Model3D)]
    [TestCase(UnitLookMode.Auto, false, true, UnitLookKind.Paper)]
    [TestCase(UnitLookMode.Auto, false, false, UnitLookKind.Token)]
    [TestCase(UnitLookMode.PaperFirst, true, true, UnitLookKind.Paper)]
    [TestCase(UnitLookMode.PaperFirst, true, false, UnitLookKind.Model3D)]
    [TestCase(UnitLookMode.PaperFirst, false, false, UnitLookKind.Token)]
    [TestCase(UnitLookMode.Tokens, true, true, UnitLookKind.Token)]
    public void TheUnitLook_PicksInTheSettingsOrder(UnitLookMode mode, bool has3D, bool hasPaper, UnitLookKind expected)
    {
        Assert.That(UnitLooks.Pick(mode, has3D, hasPaper), Is.EqualTo(expected));
    }

    [Test]
    public void AnOlderSettingsFile_KeepsItsChoice()
    {
        var v = new ViewPreferences { ShowUnitModels = false };
        Assert.That(UnitLooks.Mode(v), Is.EqualTo(UnitLookMode.Tokens), "3D models were off: tokens");
        v.ShowUnitModels = true;
        Assert.That(UnitLooks.Mode(v), Is.EqualTo(UnitLookMode.Auto));
        UnitLooks.Set(v, UnitLookMode.PaperFirst);
        Assert.That(UnitLooks.Mode(v), Is.EqualTo(UnitLookMode.PaperFirst));
        Assert.That(v.ShowUnitModels, Is.True);
        UnitLooks.Set(v, UnitLookMode.Tokens);
        Assert.That(v.ShowUnitModels, Is.False, "the old switch follows, for screens that still read it");
    }

    [Test]
    public void AStandee_IsAbout28mmScale_AndGrowsWithItsBase()
    {
        var (w, h) = UnitLooks.StandeeSize(0.492f, 2.2f); // 25 mm base, a trooper
        Assert.That(h, Is.EqualTo(1.3f).Within(0.02f));
        Assert.That(w, Is.EqualTo(h / 2.2f).Within(0.01f));
        var (bw, bh) = UnitLooks.StandeeSize(1.18f, 1.2f); // 60 mm base, a monster
        Assert.That(bh, Is.GreaterThan(h * 2));
        Assert.That(bw, Is.LessThanOrEqualTo(2.36f * 1.5f + 0.001f));
        var (ww, wh) = UnitLooks.StandeeSize(0.492f, 0.4f); // a wide picture (a vehicle on a small base)
        Assert.That(ww, Is.EqualTo(0.984f * 1.5f).Within(0.01f), "held to a base and a half wide");
        Assert.That(wh / ww, Is.EqualTo(0.4f).Within(0.01f), "keeping its shape");
    }
}
