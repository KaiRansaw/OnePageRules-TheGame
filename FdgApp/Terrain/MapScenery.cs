using System.Globalization;

namespace FdgRaylib.Terrain;

/// <summary>
/// The scenery round the table (Kai, 2026-11-26, option C: "generate it, then tweak single pieces by hand"): trees, bushes
/// and rocks standing on the land outside the play area. Looks only - no rules. Saved with the map as one line of text
/// (TerrainLayoutFile.Scenery): the set, how dense, the seed, and - once edited by hand - every piece.
/// Positions are inches from the table's middle, x across, z toward the player who sits at the near long edge (the same
/// axes the 3D table uses). Heights are the piece's height in inches, from the ground.
/// </summary>
public sealed class MapScenery
{
    /// <summary>"" or "auto" = by the horizon style; or one of <see cref="Sets"/>.</summary>
    public string Set { get; set; } = "";
    /// <summary>1 = the usual amount; 0 = none; up to 2.</summary>
    public float Density { get; set; } = 1f;
    public int Seed { get; set; }
    /// <summary>Null = generated from the set / density / seed; else the pieces as placed by hand.</summary>
    public List<Piece>? Pieces { get; set; }

    public sealed record Piece(string Model, float X, float Z, float Yaw, float Height);

    public static readonly IReadOnlyList<(string Id, string Name)> Sets = new[]
    {
        ("", "Auto (by the horizon)"), ("woods", "Woods"), ("pines", "Pine forest"), ("dead", "Dead forest"),
        ("twisted", "Twisted forest"), ("rocks", "Rocks and bushes"), ("none", "None"),
    };

    /// <summary>The models offered to place by hand (the stylised nature pack's names, no folder or extension).</summary>
    public static readonly IReadOnlyList<(string Group, string[] Models)> Palette = new[]
    {
        ("Trees", Many("CommonTree", 5).Concat(Many("Pine", 5)).ToArray()),
        ("Dead and twisted trees", Many("DeadTree", 5).Concat(Many("TwistedTree", 5)).ToArray()),
        ("Bushes and plants", new[] { "Bush_Common", "Bush_Common_Flowers", "Plant_1_Big", "Plant_7_Big" }),
        ("Rocks", Many("Rock_Medium", 3)),
    };

    private static string[] Many(string name, int n) => Enumerable.Range(1, n).Select(i => $"{name}_{i}").ToArray();

    /// <summary>A model's usual height (inches) when placed by hand: trees tall, the rest about table height.</summary>
    public static float DefaultHeight(string model) => model.Contains("Tree") || model.StartsWith("Pine") ? 130f : 22f;

    public bool IsDefault => Set is "" or "auto" && Math.Abs(Density - 1f) < 0.001f && Seed == 0 && Pieces == null;

    // ---------------------------------------------------------------- text

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string ToText()
    {
        if (IsDefault) return "";
        var parts = new List<string> { $"set={Set}", "d=" + Density.ToString("0.##", Inv), $"seed={Seed}" };
        if (Pieces != null)
            parts.Add("p=" + string.Join("|", Pieces.Select(p =>
                string.Join(",", p.Model, F(p.X), F(p.Z), F(p.Yaw), F(p.Height)))));
        return string.Join(";", parts);
        static string F(float v) => v.ToString("0.#", Inv);
    }

    public static MapScenery Parse(string? text)
    {
        var s = new MapScenery();
        if (string.IsNullOrWhiteSpace(text)) return s;
        foreach (string part in text.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq < 0) continue;
            string key = part[..eq], val = part[(eq + 1)..];
            switch (key)
            {
                case "set": s.Set = Sets.Any(x => x.Id == val) ? val : ""; break;
                case "d": if (float.TryParse(val, NumberStyles.Float, Inv, out float d)) s.Density = Math.Clamp(d, 0f, 2f); break;
                case "seed": if (int.TryParse(val, NumberStyles.Integer, Inv, out int seed)) s.Seed = seed; break;
                case "p":
                    s.Pieces = new List<Piece>();
                    foreach (string one in val.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] f = one.Split(',');
                        if (f.Length < 5 || !Palette.Any(g => g.Models.Contains(f[0]))) continue;
                        float N(int i) => float.TryParse(f[i], NumberStyles.Float, Inv, out float v) ? v : 0f;
                        s.Pieces.Add(new Piece(f[0], N(1), N(2), N(3), Math.Clamp(N(4), 2f, 400f)));
                    }
                    break;
            }
        }
        return s;
    }

    // ---------------------------------------------------------------- generating

    /// <summary>The pieces to stand round a table of <paramref name="w"/> x <paramref name="d"/>: the hand-placed ones,
    /// or a ring generated from the set (Auto = by the horizon style's name), the density and the seed.</summary>
    public List<Piece> Resolve(string horizon, float w, float d) => Pieces ?? Generate(horizon, w, d);

    public List<Piece> Generate(string horizon, float w, float d)
    {
        string set = Set is "" or "auto" ? horizon switch
        {
            "Death" => "twisted",
            "Wasteland" => "dead",
            "Hospitable" => "woods",
            _ => "woods",
        } : Set;
        if (set == "none" || Density <= 0f) return new List<Piece>();
        string[] trees = set switch
        {
            "pines" => Many("Pine", 5),
            "dead" => Many("DeadTree", 5),
            "twisted" => Many("DeadTree", 5).Concat(Many("TwistedTree", 5)).ToArray(),
            "rocks" => Array.Empty<string>(),
            _ => Many("CommonTree", 5).Concat(Many("Pine", 5)).ToArray(),
        };
        string[] small = set is "dead" or "twisted"
            ? Many("Rock_Medium", 3)
            : Many("Rock_Medium", 3).Concat(new[] { "Bush_Common", "Bush_Common_Flowers", "Plant_7_Big", "Plant_1_Big" }).ToArray();
        // Same seeding as before the editor existed, so a map nobody touched looks as it did.
        var rng = new Random(7919 + ThemeIndex(horizon) * 31 + (int)w * 7 + (int)d + Seed * 1_000_003);
        var list = new List<Piece>();
        float clear = Math.Max(w, d) * 0.5f + 120f;
        void Place(string[] pool, int count, float near, float far, float minH, float maxH)
        {
            if (pool.Length == 0) return;
            count = (int)Math.Round(count * Density);
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.Tau);
                float r = near + (far - near) * (float)Math.Sqrt(rng.NextDouble());
                float x = MathF.Cos(a) * r * (w >= d ? 1.3f : 1f), z = MathF.Sin(a) * r * (w >= d ? 1f : 1.3f);
                string model = pool[rng.Next(pool.Length)];
                float h = minH + (maxH - minH) * (float)rng.NextDouble();
                float yaw = (float)(rng.NextDouble() * 360);
                list.Add(new Piece(model, x, z, yaw, h));
            }
        }
        Place(trees, set == "rocks" ? 0 : 70, clear, clear + 450f, 90f, 170f);
        Place(small, set == "rocks" ? 110 : 50, clear - 30f, clear + 300f, 12f, 30f);
        return list;
    }

    private static int ThemeIndex(string horizon) => horizon switch
    {
        "Generic" => 0, "Hospitable" => 1, "Wasteland" => 2, "Death" => 3, _ => 0,
    };
}
