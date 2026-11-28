using FdgRaylib.Config;
using FdgRaylib.Models3D;

namespace FdgRaylib.Paper;

/// <summary>
/// The paper figures a unit stands as on the table - read from the matches (shipped, with this computer's laid over)
/// and the pictures (this computer's UnitModels/_Paper first, else the game's Assets/PaperModels). Found by army book and
/// unit name like the 3D models (<see cref="UnitModelLibrary"/>): ignoring case and punctuation, through the other
/// games' names, and for an allied unit in whichever one book has it.
/// </summary>
public sealed class PaperLibrary
{
    /// <summary>One figure as the table needs it.</summary>
    public sealed record Figure(string Id, string FrontPath, string? BackPath, int Width, int Height)
    {
        /// <summary>Height over width of the picture (2 = twice as tall as wide).</summary>
        public float Aspect => Width <= 0 ? 2f : Height / (float)Width;
    }

    private readonly Dictionary<string, Dictionary<string, List<Figure>>> _byBook = new();
    private readonly Dictionary<string, Figure> _byId = new();

    public int UnitCount { get; private set; }
    public int FigureCount => _byId.Count;

    private static PaperLibrary? _current;
    private static readonly object Gate = new();

    /// <summary>The library for this computer, read once and kept (call <see cref="Reload"/> after matching).</summary>
    public static PaperLibrary Current
    {
        get { lock (Gate) return _current ??= Build(PaperStore.LocalRoot, PaperStore.ShippedRoot); }
    }

    public static void Reload() { lock (Gate) _current = null; }

    public static string Norm(string s) => UnitModelLibrary.Norm(s);

    /// <summary>Reads the matches and finds each figure's pictures (<paramref name="localRoot"/> first, then <paramref name="shippedRoot"/>).</summary>
    public static PaperLibrary Build(string localRoot, string? shippedRoot)
    {
        var lib = new PaperLibrary();
        var matches = PaperMatches.Merged(
            shippedRoot == null ? new PaperMatches() : PaperMatches.Load(Path.Combine(shippedRoot, PaperMatches.FileName)),
            PaperMatches.Load(Path.Combine(localRoot, PaperMatches.FileName)));
        var sizes = new Dictionary<string, (int W, int H)>();
        foreach (string root in new[] { shippedRoot, localRoot }.Where(r => r != null)!)
            foreach (PaperSheet s in PaperStore.LoadSheets(root!))
                foreach (PaperFigure f in s.Figures) sizes[s.IdOf(f)] = (f.PictureW, f.PictureH);

        Figure? Resolve(string id)
        {
            if (lib._byId.TryGetValue(id, out var had)) return had;
            int slash = id.LastIndexOf('/');
            if (slash <= 0 || !int.TryParse(id[(slash + 1)..], out int n)) return null;
            string sheet = id[..slash];
            foreach (string? root in new[] { localRoot, shippedRoot })
            {
                if (root == null) continue;
                string front = PaperStore.FigurePath(root, sheet, n);
                if (!File.Exists(front)) continue;
                string back = PaperStore.FigurePath(root, sheet, n, back: true);
                var (w, h) = sizes.TryGetValue(id, out var sz) && sz.W > 0 ? sz : PngSize(front);
                return lib._byId[id] = new Figure(id, front, File.Exists(back) ? back : null, w, h);
            }
            return null;
        }

        void Add(string book, string unit, List<Figure> figs)
        {
            if (!lib._byBook.TryGetValue(Norm(book), out var units)) lib._byBook[Norm(book)] = units = new();
            string k = Norm(unit);
            if (units.TryGetValue(k, out var had)) { foreach (var f in figs) if (!had.Contains(f)) had.Add(f); }
            else units[k] = new List<Figure>(figs);
        }

        foreach (var m in matches.Units)
        {
            var figs = m.Figures.Select(Resolve).Where(f => f != null).Select(f => f!).ToList();
            if (figs.Count == 0) continue;
            lib.UnitCount++;
            Add(m.Book, m.Unit, figs);
            foreach (var a in m.AlsoIn) Add(m.Book, a.Unit, figs);
        }
        return lib;
    }

    /// <summary>The figures for a unit of this army (empty: none matched).</summary>
    public IReadOnlyList<Figure> FiguresFor(string faction, string unit)
    {
        string b = Norm(faction), u = Norm(unit);
        if (_byBook.TryGetValue(b, out var units) && units.TryGetValue(u, out var figs)) return figs;
        var others = _byBook.Where(kv => kv.Key != b && kv.Value.ContainsKey(u)).Select(kv => kv.Value[u]).ToList();
        return others.Count == 1 ? others[0] : Array.Empty<Figure>();
    }

    public Figure? ById(string id) => _byId.TryGetValue(id, out var f) ? f : null;

    /// <summary>
    /// The figure for one model: a figure picked for it in the army builder (look "paper:&lt;id&gt;"), else the unit's
    /// figures in turn by the model's place in the unit - a squad shows every figure matched to it, like a real paper
    /// squad.
    /// </summary>
    public Figure? FigureFor(string faction, string unit, string? look, int modelIndex)
    {
        if (look != null && look.StartsWith(LookPrefix, StringComparison.Ordinal) && ById(look[LookPrefix.Length..]) is { } picked) return picked;
        var figs = FiguresFor(faction, unit);
        return figs.Count == 0 ? null : figs[((modelIndex % figs.Count) + figs.Count) % figs.Count];
    }

    public const string LookPrefix = "paper:";

    /// <summary>A PNG's size from its header (0, 0 when it isn't one).</summary>
    public static (int W, int H) PngSize(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var b = new byte[24];
            if (fs.Read(b, 0, 24) < 24 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return (0, 0);
            return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        }
        catch (IOException) { return (0, 0); }
    }
}

/// <summary>Settings -> Display -> "Unit look" (lane 5, 2026-11-28).</summary>
public enum UnitLookMode
{
    /// <summary>A 3D model when there is one, else a paper standee, else the token.</summary>
    Auto,
    /// <summary>A paper standee when there is one, else a 3D model, else the token.</summary>
    PaperFirst,
    /// <summary>Everyone is the plain token (quickest).</summary>
    Tokens,
}

public enum UnitLookKind { Model3D, Paper, Token }

public static class UnitLooks
{
    public const string Auto = "auto", PaperFirst = "paper", Tokens = "tokens";

    /// <summary>The setting: the saved "Unit look", or for an older settings file its "3D unit models" switch.</summary>
    public static UnitLookMode Mode(ViewPreferences view) => view.UnitLook switch
    {
        PaperFirst => UnitLookMode.PaperFirst,
        Tokens => UnitLookMode.Tokens,
        Auto => UnitLookMode.Auto,
        _ => view.ShowUnitModels ? UnitLookMode.Auto : UnitLookMode.Tokens,
    };

    public static void Set(ViewPreferences view, UnitLookMode mode)
    {
        view.UnitLook = mode switch { UnitLookMode.PaperFirst => PaperFirst, UnitLookMode.Tokens => Tokens, _ => Auto };
        view.ShowUnitModels = mode != UnitLookMode.Tokens; // the older switch other screens still read
    }

    /// <summary>What a model stands as, given what this computer has for it.</summary>
    public static UnitLookKind Pick(UnitLookMode mode, bool has3D, bool hasPaper) => mode switch
    {
        UnitLookMode.Tokens => UnitLookKind.Token,
        UnitLookMode.PaperFirst => hasPaper ? UnitLookKind.Paper : has3D ? UnitLookKind.Model3D : UnitLookKind.Token,
        _ => has3D ? UnitLookKind.Model3D : hasPaper ? UnitLookKind.Paper : UnitLookKind.Token,
    };

    /// <summary>
    /// A standee's size in inches (width, height) on a round base of <paramref name="baseRadius"/>": a 25 mm base
    /// stands a figure about 1.3" (33 mm) tall - 28 mm scale - and bigger bases scale it up; a wide figure is held to
    /// about the base's width and a half, keeping its shape.
    /// </summary>
    public static (float W, float H) StandeeSize(float baseRadius, float aspect)
    {
        float diameter = Math.Max(0.6f, baseRadius * 2f);
        aspect = Math.Clamp(aspect, 0.3f, 5f);
        float h = 1.3f * Math.Max(1f, diameter / 0.984f);
        float w = h / aspect;
        float maxW = diameter * 1.5f;
        if (w > maxW) { w = maxW; h = w * aspect; }
        return (w, h);
    }
}
