using FDG;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.SaveLoad;

namespace FdgRaylib.Terrain;

/// <summary>
/// One terrain piece on the Map Maker's table, in table inches. A rectangle (width along x, depth along z, turned by
/// <see cref="Angle"/> degrees about its centre) or a circle of diameter <see cref="W"/>. <see cref="Raw"/> keeps a
/// shape the editor can't reshape (an older multi-part piece) exactly as it was.
/// </summary>
public sealed class MapPiece
{
    public string Kind = "";
    public string Name = "";
    public ETerrainType Rules;
    public string Look = "";
    public float Height = 1f;
    public int Points = 1;
    public bool Round;
    public float X, Z;
    public float W = 6, D = 6;
    public float Angle;
    public IZone? Raw;

    public MapPiece Clone() => (MapPiece)MemberwiseClone();

    public IZone ToZone()
    {
        if (Raw != null) return Raw;
        if (Round) return new CircularZone(X, Z, W / 2f);
        var rect = new RectangularZone(X - W / 2f, X + W / 2f, Z - D / 2f, Z + D / 2f);
        return MathF.Abs(Angle % 360f) < 0.01f ? rect : new RotatedZoneWrapper(rect, Angle, new Float2(X, Z));
    }

    /// <summary>The four corners of the footprint (a circle's bounding square), for drawing handles and gaps.</summary>
    public (float X, float Z)[] Corners()
    {
        float a = Angle * MathF.PI / 180f, c = MathF.Cos(a), s = MathF.Sin(a), hw = W / 2, hd = (Round ? W : D) / 2;
        return new[] { (-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd) }
            .Select(p => (X + p.Item1 * c - p.Item2 * s, Z + p.Item1 * s + p.Item2 * c)).ToArray();
    }

    public bool Contains(float x, float z) => ToZone().IsPointWithinZone(new Float2(x, z));

    /// <summary>Area in square inches (the book's "cover at least X% of the table").</summary>
    public float Area => Raw is IBoundedZone b ? (b.Bounds.Right - b.Bounds.Left) * (b.Bounds.Top - b.Bounds.Bottom) : Round ? MathF.PI * W * W / 4f : W * D;

    public static MapPiece FromKind(TerrainBook.PieceKind k, float x, float z) => new()
    {
        Kind = k.Id, Name = k.Name, Rules = k.Rules, Round = k.Round, W = k.Width, D = k.Round ? k.Width : k.Depth,
        Height = k.Height, X = x, Z = z, Points = PointsFor(k.Width, k.Round ? k.Width : k.Depth),
    };

    /// <summary>"Alternating: Points" cost: 1 for scatter, 2 for a standard large piece, 3 for a big one.</summary>
    public static int PointsFor(float w, float d) => w * d <= 9 ? 1 : w * d <= 40 ? 2 : 3;
}

/// <summary>
/// The Map Maker's working copy of a map (Godot Map Maker, mockup approved 2026-11-13): pieces, objectives and
/// size-reference bases, with undo / redo, the book's terrain generator, and the book check. Nothing is written
/// until <see cref="Save"/>.
/// </summary>
public sealed class MapDraft
{
    public string? Path { get; private set; }
    public string Name = "New Map";
    public float TableW = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES, TableH = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;
    public List<MapPiece> Pieces { get; } = new();
    public List<(float X, float Z)> Objectives { get; } = new();
    public List<MapBaseMarkerEntry> Bases { get; } = new();

    private readonly List<string> _undo = new(), _redo = new();
    private string _saved = "";

    public static MapDraft New(float w, float h) { var d = new MapDraft { TableW = w, TableH = h }; d._saved = d.Snapshot(); return d; }

    public static MapDraft? Load(string path, out string? error)
    {
        TerrainLayoutFile? f = TerrainLayoutLoader.TryLoadFromFile(path, out error);
        if (f == null) return null;
        var d = new MapDraft { Path = path };
        d.Apply(f);
        d._saved = d.Snapshot();
        return d;
    }

    /// <summary>A copy of a saved map as a new, unsaved map ("Copy of ...").</summary>
    public static MapDraft? CopyOf(string path, out string? error)
    {
        MapDraft? d = Load(path, out error);
        if (d == null) return null;
        d.Path = null;
        d.Name = "Copy of " + d.Name;
        d._saved = "";
        return d;
    }

    private void Apply(TerrainLayoutFile f)
    {
        Name = f.Name.Length > 0 ? f.Name : System.IO.Path.GetFileNameWithoutExtension(Path ?? "Map");
        TableW = f.TableWidthInches;
        TableH = f.TableHeightInches;
        Pieces.Clear();
        foreach (TerrainPieceEntry e in f.Pieces.Where(e => e.Shape != null)) Pieces.Add(FromEntry(e));
        Objectives.Clear();
        Objectives.AddRange(f.Objectives.Select(o => (o.X, o.Z)));
        Bases.Clear();
        Bases.AddRange(f.BaseMarkers);
    }

    private static MapPiece FromEntry(TerrainPieceEntry e)
    {
        var p = new MapPiece { Name = e.Name ?? "", Rules = e.TerrainType, Height = e.HeightInches > 0 ? e.HeightInches : 1f, Points = Math.Max(1, e.Points), Kind = e.Kind ?? "", Look = e.Look ?? "" };
        switch (e.Shape)
        {
            case RectangularZone r:
                p.X = (r.Left + r.Right) / 2f; p.Z = (r.Bottom + r.Top) / 2f; p.W = r.Right - r.Left; p.D = r.Top - r.Bottom;
                break;
            case CircularZone c:
                p.Round = true; p.X = c.Center.X; p.Z = c.Center.Y; p.W = p.D = c.Radius * 2;
                break;
            case RotatedZoneWrapper w when w.Inner is RectangularZone r:
            {
                float cx = (r.Left + r.Right) / 2f, cz = (r.Bottom + r.Top) / 2f, a = w.AngleDegrees * MathF.PI / 180f;
                float dx = cx - w.Pivot.X, dz = cz - w.Pivot.Y;
                p.X = w.Pivot.X + dx * MathF.Cos(a) - dz * MathF.Sin(a);
                p.Z = w.Pivot.Y + dx * MathF.Sin(a) + dz * MathF.Cos(a);
                p.W = r.Right - r.Left; p.D = r.Top - r.Bottom; p.Angle = w.AngleDegrees;
                break;
            }
            default:
                p.Raw = e.Shape;
                if (e.Shape is IBoundedZone b) { p.X = b.Bounds.CenterX; p.Z = b.Bounds.CenterZ; p.W = b.Bounds.Right - b.Bounds.Left; p.D = b.Bounds.Top - b.Bounds.Bottom; }
                break;
        }
        if (p.Kind.Length == 0) p.Kind = GuessKind(p);
        return p;
    }

    /// <summary>An older map's piece has no kind: the book kind with the same rules, else Building / Barricade by shape.</summary>
    private static string GuessKind(MapPiece p) =>
        TerrainBook.Kinds.FirstOrDefault(k => k.Rules == p.Rules && k.Round == p.Round)?.Id
        ?? TerrainBook.Kinds.FirstOrDefault(k => k.Rules == p.Rules)?.Id
        ?? (p.Rules.HasFlag(ETerrainType.Blocking) ? "Building" : "Barricade");

    public TerrainLayoutFile ToLayout() => new()
    {
        Name = Name.Trim(),
        TableWidthInches = TableW,
        TableHeightInches = TableH,
        Pieces = Pieces.Select(p => new TerrainPieceEntry
        {
            Name = p.Name, TerrainType = p.Rules, Shape = p.ToZone(), HeightInches = p.Height, Points = p.Points, Kind = p.Kind, Look = p.Look,
        }).ToList(),
        Objectives = Objectives.Select(o => new MapObjectiveEntry { X = o.X, Z = o.Z }).ToList(),
        BaseMarkers = Bases.ToList(),
        DeploymentZones = new List<MapDeploymentZoneEntry>(), // the lobby decides deployment (Kai, 2026-11-07)
    };

    public string Snapshot() => TerrainLoader.SerializeToJson(ToLayout());
    public bool Dirty => Snapshot() != _saved;

    /// <summary>Why the map can't be saved yet, or null.</summary>
    public string? SaveProblem() =>
        Name.Trim().Length == 0 ? "Give the map a name."
        : Name.Any(c => c > 126) ? "Map names are plain letters only (no accents or symbols)."
        : null;

    /// <summary>Writes the map. The saved path, or null with the reason.</summary>
    public string? Save(out string? error)
    {
        error = SaveProblem();
        if (error != null) return null;
        try
        {
            Path = MapPaths.Save(ToLayout(), Path);
            _saved = Snapshot();
            return Path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { error = e.Message; return null; }
    }

    // ------------------------------------------------------------------ undo / redo

    /// <summary>Call BEFORE a change. A drag calls it once, at the start.</summary>
    public void Remember()
    {
        string s = Snapshot();
        if (_undo.Count > 0 && _undo[^1] == s) return;
        _undo.Add(s);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool Undo() => Step(_undo, _redo);
    public bool Redo() => Step(_redo, _undo);

    private bool Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return false;
        to.Add(Snapshot());
        string s = from[^1];
        from.RemoveAt(from.Count - 1);
        string? path = Path;
        if (TerrainLoader.TryLoadFromJson(s) is { } f) Apply(f);
        Path = path;
        return true;
    }

    // ------------------------------------------------------------------ editing

    public static float SnapInch(float v, bool snap) => snap ? MathF.Round(v) : MathF.Round(v * 4) / 4;

    /// <summary>Keeps a piece's centre on the table.</summary>
    public void Clamp(MapPiece p)
    {
        p.X = Math.Clamp(p.X, 0, TableW);
        p.Z = Math.Clamp(p.Z, 0, TableH);
    }

    public MapPiece? PieceAt(float x, float z) => Pieces.LastOrDefault(p => p.Contains(x, z));

    public int ObjectiveAt(float x, float z) => Objectives.FindIndex(o => (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) <= 1.5f * 1.5f);

    public int BaseAt(float x, float z) => Bases.FindIndex(b => (b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z) <= MathF.Max(0.6f, b.Base.DiameterInches / 2f) * MathF.Max(0.6f, b.Base.DiameterInches / 2f));

    // ------------------------------------------------------------------ the book's generator

    /// <summary>
    /// "Generate...": replaces the terrain with the book's generator roll for <paramref name="theme"/>, spread out.
    /// Objectives and size bases stay. Returns a line saying what happened.
    /// </summary>
    public string Generate(EGameSystem system, TerrainBook.ETheme theme, Random rng)
    {
        Remember();
        var rolled = TerrainBook.GrowToCover(TerrainBook.Generate(system, theme, () => rng.Next(1, 7)), TableW, TableH, system);
        var placed = TerrainBook.Spread(rolled, TableW, TableH, rng.NextDouble, out int left);
        Pieces.Clear();
        foreach (var pp in placed)
        {
            TerrainBook.PieceKind k = TerrainBook.Kind(pp.Piece.KindId)!;
            Pieces.Add(new MapPiece
            {
                Kind = k.Id, Name = pp.Piece.Name, Rules = pp.Piece.Rules, Round = pp.Piece.Round, Height = k.Height,
                X = pp.X, Z = pp.Z, W = pp.Piece.Width, D = pp.Piece.Round ? pp.Piece.Width : pp.Piece.Depth, Angle = pp.Turned ? 90 : 0,
                Points = MapPiece.PointsFor(pp.Piece.Width, pp.Piece.Depth),
            });
        }
        string name = TerrainBook.ThemeName(theme, system);
        return $"{name}: rolled {rolled.Count} pieces" + (left > 0 ? $", {left} didn't fit on this table and were left out (book: \"simply remove it\")." : ".");
    }

    // ------------------------------------------------------------------ the book check

    public sealed record CheckLine(string Label, string Value, bool Ok);

    /// <summary>
    /// The book check panel (mockup item 7): the table against this game's own placement advice. Counts only - it never
    /// stops a save. Empty when the game's book gives no advice (Star Quest), with the reason as the only line.
    /// </summary>
    public List<CheckLine> BookCheck(EGameSystem system)
    {
        var lines = new List<CheckLine>();
        TerrainBook.Guidance? g = TerrainBook.GuidanceFor(system);
        if (g == null) { lines.Add(new("No advice", $"{GameSystemInfo.DisplayName(system)}'s book gives no terrain placement advice", true)); return lines; }
        int n = Pieces.Count;
        lines.Add(new("Pieces", $"{n} (book: {g.MinPieces}-{g.MaxPieces})", n >= g.MinPieces));
        int cover = (int)MathF.Round(100f * Pieces.Sum(p => p.Area) / (TableW * TableH));
        lines.Add(new("Table covered", $"{cover}% (book: at least {g.CoverTablePercent}%)", cover >= g.CoverTablePercent));
        Line(lines, "Block line of sight", Pieces.Count(p => p.Rules.HasFlag(ETerrainType.Blocking)), n, TerrainBook.BlockPercent);
        Line(lines, "Give cover", Pieces.Count(p => p.Rules.HasFlag(ETerrainType.Cover)), n, TerrainBook.CoverPercent);
        Line(lines, "Difficult", Pieces.Count(p => p.Rules.HasFlag(ETerrainType.Difficult)), n, TerrainBook.DifficultPercent);
        if (n >= 2)
        {
            float biggest = BiggestGap();
            lines.Add(new("Biggest gap", $"{biggest:0}\" (book: no gaps over {g.MaxGap:0}\")", biggest <= g.MaxGap));
        }
        return lines;
    }

    private static void Line(List<CheckLine> lines, string label, int count, int n, int percent)
    {
        int need = (int)MathF.Ceiling(n * percent / 100f);
        lines.Add(new(label, $"{count} of {n} (book: at least {percent}%)", n == 0 || count >= need));
    }

    /// <summary>
    /// The widest empty stretch between neighbouring pieces: for each piece, the gap to its nearest neighbour; the
    /// biggest of those. Footprints are measured as their bounding boxes.
    /// </summary>
    public float BiggestGap()
    {
        var boxes = Pieces.Select(p =>
        {
            var c = p.Corners();
            return new TerrainBook.PlacedPiece(new TerrainBook.GeneratedPiece("", 0, "", c.Max(q => q.X) - c.Min(q => q.X), c.Max(q => q.Z) - c.Min(q => q.Z), false),
                (c.Max(q => q.X) + c.Min(q => q.X)) / 2, (c.Max(q => q.Z) + c.Min(q => q.Z)) / 2, false);
        }).ToList();
        float biggest = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            float nearest = float.MaxValue;
            for (int j = 0; j < boxes.Count; j++) if (i != j) nearest = MathF.Min(nearest, TerrainBook.Gap(boxes[i], boxes[j]));
            biggest = MathF.Max(biggest, nearest);
        }
        return biggest;
    }

    // ------------------------------------------------------------------ the map list

    /// <summary>A saved map for the list: its name, table size, counts, and the file.</summary>
    public sealed record ListEntry(string Path, string Name, float W, float H, int Pieces, int Objectives);

    public static List<ListEntry> ListAll()
    {
        var list = new List<ListEntry>();
        foreach (string p in MapPaths.List())
            if (TerrainLayoutLoader.TryLoadFromFile(p, out _) is { } f)
                list.Add(new(p, f.Name.Length > 0 ? f.Name : System.IO.Path.GetFileNameWithoutExtension(p), f.TableWidthInches, f.TableHeightInches, f.Pieces.Count, f.Objectives.Count));
        return list;
    }

    /// <summary>"6' x 4'" - and which games play on it (the hover in mockup #17).</summary>
    public static string SizeName(float w, float h) => $"{w / 12f:0.#}' x {h / 12f:0.#}'";

    public static string SizeGames(float w, float h)
    {
        var book = GameSystemInfo.DisplayOrder.Where(s => TableSizing.Same(GameWideConstants.TableSizeFor(s), (w, h))).Select(GameSystemInfo.DisplayName).ToList();
        var big = GameSystemInfo.DisplayOrder
            .Where(s => !TableSizing.Same(GameWideConstants.TableSizeFor(s), (w, h)) && TableSizing.SizesFor(s).Any(z => TableSizing.Same(z, (w, h))))
            .Select(s => $"{GameSystemInfo.DisplayName(s)} ~{TableSizing.SuggestedPoints(s, w, h)} pts").ToList();
        var parts = new List<string>();
        if (book.Count > 0) parts.Add("The book table for " + string.Join(", ", book));
        if (big.Count > 0) parts.Add("big games: " + string.Join(", ", big));
        return parts.Count == 0 ? "No game plays on this size by default." : string.Join(" - ", parts);
    }

    public static string? Delete(string path)
    {
        try { File.Delete(path); return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }
}
