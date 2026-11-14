using FDG;

namespace FdgRaylib.Terrain;

/// <summary>
/// What each terrain piece looks like on the 3D table (Kai, 2026-11-14). No drawing here - the Godot side
/// (<c>OprGodot/scripts/battle/TerrainDresser.cs</c>) builds the models from these recipes.
///
/// <para>Kai's decisions:</para>
/// <list type="bullet">
/// <item><b>Free packs, bundled in the release</b> (all CC0): Kenney's Nature, Castle, Fantasy Town, City Industrial,
/// Space and Survival kits and Quaternius' Medieval Village MegaKit, copied into <c>OprGodot/terrain/</c> by
/// <c>tools/terrain/bundle_terrain.py</c>. Each file here is named "&lt;folder&gt;/&lt;file&gt;" under that folder.</item>
/// <item><b>Per game</b>: Grimdark Future games (GF, Firefight, Star Quest) get <see cref="ETheme.Grimdark"/>, Age of
/// Fantasy games (AoF, Skirmish, Quest) <see cref="ETheme.Fantasy"/>.</item>
/// <item><b>Area terrain is scattered on a base</b>: the base shows the exact footprint the rules use, the models
/// stand on it. Every layout draws the base, so the edge is always visible.</item>
/// <item><b>Auto + override</b>: <see cref="Pick"/> chooses a look for the piece's kind (the same one on every
/// machine - <see cref="SeedFor"/>); the Map Maker can set one by id (<c>TerrainPieceEntry.Look</c>).</item>
/// </list>
/// Cosmetic only: nothing here changes a rule. A piece with no kind (older maps, the random tables) gets one
/// guessed from its name and rules (<see cref="GuessKind"/>).
/// </summary>
public static class TerrainLooks
{
    public enum ETheme { Grimdark, Fantasy }

    public enum ELayout
    {
        /// <summary>A base with the props scattered across it (forests, rubble, fields, swamps).</summary>
        Scatter,
        /// <summary>A base with props in rows (fields).</summary>
        Rows,
        /// <summary>A raised mound filling the footprint, a few props round it (hills).</summary>
        Mound,
        /// <summary>A flat surface of the ground colour (water, lava), props round the edge.</summary>
        Flat,
        /// <summary>One prop, fitted to the footprint (buildings, containers, wrecks).</summary>
        Single,
        /// <summary>A prop repeated along the footprint's long side (walls, barricades).</summary>
        Line,
        /// <summary>Walls along two sides of the footprint and rubble inside (ruins).</summary>
        Corner,
        /// <summary>Stacked parts, bottom to top: base, middle (repeated to height), roof (towers).</summary>
        Stack,
        /// <summary>A cottage built from Medieval Village walls with a roof sized to the footprint.</summary>
        Cottage,
    }

    /// <summary>One model the look can use, and how tall it stands on the table (inches).</summary>
    public sealed record Prop(string File, float MinHeight, float MaxHeight);

    /// <summary>A look: its <see cref="Id"/> (stored on the piece), the name the Map Maker shows, which kinds and theme
    /// it is for, how it is laid out, the base colour (hex) and its props. <see cref="Density"/> is props per square
    /// inch for the scatter layouts; <see cref="Extras"/> are the second set (bushes under trees, rubble in ruins).</summary>
    public sealed record Look(string Id, string Name, string[] Kinds, ETheme Theme, ELayout Layout, string Ground,
        Prop[] Props, float Density = 0.2f, Prop[]? Extras = null, float ExtraDensity = 0f);

    // ------------------------------------------------------------------ the catalogue

    private static Prop[] P(float min, float max, params string[] files) => files.Select(f => new Prop(f, min, max)).ToArray();

    private static readonly string[] Trees = { "nature/tree_oak", "nature/tree_default", "nature/tree_detailed", "nature/tree_fat", "nature/tree_tall", "nature/tree_simple" };
    private static readonly string[] DarkTrees = { "nature/tree_oak_dark", "nature/tree_default_dark", "nature/tree_detailed_dark", "nature/tree_fat_darkh", "nature/tree_blocks_dark", "nature/tree_plateau_dark" };
    private static readonly string[] FallTrees = { "nature/tree_oak_fall", "nature/tree_default_fall", "nature/tree_detailed_fall", "nature/tree_fat_fall" };
    private static readonly string[] Pines = { "nature/tree_pineTallA", "nature/tree_pineTallB", "nature/tree_pineTallC", "nature/tree_pineRoundA", "nature/tree_pineRoundB", "nature/tree_cone" };
    private static readonly string[] Undergrowth = { "nature/plant_bush", "nature/plant_bushLarge", "nature/plant_bushDetailed", "nature/grass_large", "nature/flower_redA", "nature/flower_yellowA", "nature/mushroom_red", "nature/stump_round", "nature/log_large" };
    private static readonly string[] SmallStones = { "nature/stone_smallA", "nature/stone_smallB", "nature/stone_smallC", "nature/rock_smallA", "nature/rock_smallB", "nature/rock_smallC", "nature/rock_smallFlatA" };
    private static readonly string[] BigRocks = { "nature/rock_largeA", "nature/rock_largeB", "nature/rock_largeC", "nature/rock_largeD", "nature/stone_largeA", "nature/stone_largeB" };
    private static readonly string[] TallRocks = { "nature/rock_tallA", "nature/rock_tallB", "nature/rock_tallC", "nature/rock_tallE", "nature/rock_tallG", "nature/stone_tallA", "nature/stone_tallB" };
    private static readonly string[] SpaceRocks = { "space/rock", "space/rock_largeA", "space/rock_largeB", "space/meteor", "space/meteor_detailed" };
    private static readonly string[] SpaceSmall = { "space/rocks_smallA", "space/rocks_smallB", "survival/rock-a", "survival/rock-b", "survival/rock-c", "survival/rock-flat" };
    private static readonly string[] Junk = { "space/barrel", "space/barrels", "survival/box", "survival/box-large", "survival/barrel", "space/machine_barrel", "space/pipe_straight", "space/bones" };

    private const string Forest = "Forest", Field = "Field", Hill = "Hill", Mountain = "Mountain", Rubble = "Rubble",
        Ruins = "Ruins", Building = "Building", Wall = "Wall", Barricade = "Barricade", LakeShallow = "LakeShallow",
        LakeDeep = "LakeDeep", RiverShallow = "RiverShallow", RiverDeep = "RiverDeep", Swamp = "Swamp", Lava = "Lava";

    /// <summary>Not book kinds - kinds <see cref="GuessKind"/> gives pieces from the random tables that fit none of the
    /// book's: a wreck, and a hazard (minefield, barbed wire).</summary>
    public const string Wreck = "Wreck", Hazard = "Hazard";

    private static readonly string[] Water = { LakeShallow, LakeDeep, RiverShallow, RiverDeep };

    public static readonly IReadOnlyList<Look> All = new Look[]
    {
        // ---- Age of Fantasy
        new("oak-wood", "Oak wood", new[] { Forest }, ETheme.Fantasy, ELayout.Scatter, "#3b5a2a", P(2.5f, 4f, Trees), 0.16f, P(0.4f, 0.9f, Undergrowth), 0.25f),
        new("pine-wood", "Pine wood", new[] { Forest }, ETheme.Fantasy, ELayout.Scatter, "#34492c", P(3f, 4.5f, Pines), 0.2f, P(0.4f, 0.8f, "nature/plant_bush", "nature/grass_large", "nature/stump_round", "nature/log_large"), 0.15f),
        new("autumn-wood", "Autumn wood", new[] { Forest }, ETheme.Fantasy, ELayout.Scatter, "#5a4a2a", P(2.5f, 4f, FallTrees), 0.16f, P(0.4f, 0.8f, "nature/plant_bush", "nature/mushroom_red", "nature/stump_round"), 0.2f),
        new("wheat-field", "Wheat field", new[] { Field }, ETheme.Fantasy, ELayout.Rows, "#6b5434", P(0.5f, 0.8f, "nature/crops_wheatStageA", "nature/crops_wheatStageB"), 1.2f),
        new("corn-field", "Corn field", new[] { Field }, ETheme.Fantasy, ELayout.Rows, "#6b5434", P(0.7f, 1f, "nature/crops_cornStageC", "nature/crops_cornStageD"), 0.9f),
        new("grassy-hill", "Grassy hill", new[] { Hill }, ETheme.Fantasy, ELayout.Mound, "#58773a", P(0.4f, 0.9f, "nature/plant_bush", "nature/rock_smallA", "nature/grass_large", "nature/flower_yellowA"), 0.12f),
        new("rocky-hill", "Rocky hill", new[] { Hill }, ETheme.Fantasy, ELayout.Mound, "#6d6a58", P(0.6f, 1.4f, BigRocks), 0.1f),
        new("crag", "Crag", new[] { Mountain }, ETheme.Fantasy, ELayout.Scatter, "#5b5a52", P(3.5f, 6.5f, TallRocks), 0.2f, P(1.2f, 2.5f, BigRocks), 0.15f),
        new("stone-rubble", "Stone rubble", new[] { Rubble }, ETheme.Fantasy, ELayout.Scatter, "#6a645a", P(0.3f, 0.8f, SmallStones), 0.9f, P(0.6f, 1.2f, "castle/rocks-small", "nature/statue_columnDamaged", "village/Prop_Brick1", "village/Prop_Brick2"), 0.15f),
        new("castle-ruins", "Castle ruins", new[] { Ruins }, ETheme.Fantasy, ELayout.Corner, "#6a645a", P(2f, 2.8f, "castle/wall-half", "castle/wall"), 0.6f, P(0.3f, 0.8f, SmallStones.Append("castle/rocks-small").ToArray()), 0.5f),
        new("town-ruins", "Town ruins", new[] { Ruins }, ETheme.Fantasy, ELayout.Corner, "#6b5f4c", P(2f, 2.6f, "town/wall-broken", "town/wall-wood-broken"), 0.6f, P(0.3f, 0.9f, "town/planks", "town/cart", "nature/stone_smallA", "village/Prop_Brick1", "village/Prop_Crate"), 0.35f),
        new("plaster-cottage", "Plaster cottage", new[] { Building }, ETheme.Fantasy, ELayout.Cottage, "#5d5446", P(3f, 6f, "village/Wall_Plaster_Straight", "village/Wall_Plaster_Window_Wide_Round", "village/Wall_Plaster_Door_Round", "village/Corner_Exterior_Wood")),
        new("brick-cottage", "Brick cottage", new[] { Building }, ETheme.Fantasy, ELayout.Cottage, "#5d5446", P(3f, 6f, "village/Wall_UnevenBrick_Straight", "village/Wall_UnevenBrick_Window_Wide_Round", "village/Wall_UnevenBrick_Door_Round", "village/Corner_Exterior_Brick")),
        new("watchtower", "Watchtower", new[] { Building }, ETheme.Fantasy, ELayout.Stack, "#5d5446", P(0f, 0f, "castle/tower-square-base", "castle/tower-square-mid-windows", "castle/tower-square-top-roof")),
        new("castle-wall", "Castle wall", new[] { Wall }, ETheme.Fantasy, ELayout.Line, "#5d5446", P(1.8f, 2.6f, "castle/wall")),
        new("low-stone-wall", "Low stone wall", new[] { Wall, Barricade }, ETheme.Fantasy, ELayout.Line, "#5d5446", P(1f, 1.6f, "castle/wall-narrow")),
        new("wooden-fence", "Wooden fence", new[] { Barricade }, ETheme.Fantasy, ELayout.Line, "#5a5236", P(0.7f, 1f, "town/fence", "town/fence", "town/fence-broken")),
        new("hedge", "Hedge", new[] { Barricade }, ETheme.Fantasy, ELayout.Line, "#4a5a30", P(0.8f, 1.1f, "town/hedge", "town/hedge-large")),
        new("palisade", "Palisade", new[] { Barricade, Wall }, ETheme.Fantasy, ELayout.Line, "#5a5236", P(1.2f, 1.8f, "castle/wall-narrow-wood-fence")),
        new("clear-water", "Clear water", Water, ETheme.Fantasy, ELayout.Flat, "#2f6f9a", P(0.3f, 0.7f, "nature/stone_smallA", "nature/rock_smallFlatA", "nature/grass_large", "nature/plant_flatTall"), 0.25f),
        new("marsh", "Marsh", new[] { Swamp }, ETheme.Fantasy, ELayout.Scatter, "#3d4a2c", P(0.4f, 1.1f, "nature/plant_flatTall", "nature/grass_leafsLarge", "nature/hanging_moss", "nature/stump_round", "nature/log_large"), 0.6f),
        new("lava-flow", "Lava flow", new[] { Lava }, ETheme.Fantasy, ELayout.Flat, "#b8401a", P(0.4f, 1f, "nature/rock_smallA", "nature/rock_smallB", "nature/rock_largeA"), 0.3f),
        new("siege-wreck", "Siege wreck", new[] { Wreck }, ETheme.Fantasy, ELayout.Single, "#5d5446", P(1.5f, 3f, "castle/siege-tower-demolished", "castle/siege-catapult-demolished", "castle/siege-ram-demolished", "town/cart-high")),
        new("stake-field", "Stake field", new[] { Hazard }, ETheme.Fantasy, ELayout.Scatter, "#5a3b30", P(0.6f, 1.2f, "town/poles", "nature/rock_smallA", "nature/stone_smallB"), 0.6f),

        // ---- Grimdark Future
        new("alien-forest", "Alien forest", new[] { Forest }, ETheme.Grimdark, ELayout.Scatter, "#2c3a2a", P(2.8f, 4.2f, DarkTrees), 0.18f, P(0.4f, 0.9f, "nature/plant_bushLarge", "nature/grass_leafsLarge", "space/rock_crystals", "nature/log_large"), 0.2f),
        new("dark-pines", "Dark pines", new[] { Forest }, ETheme.Grimdark, ELayout.Scatter, "#2a3328", P(3f, 4.5f, Pines), 0.2f, P(0.4f, 0.8f, "nature/stump_round", "space/rocks_smallA", "nature/log_large"), 0.15f),
        new("crop-field", "Crop field", new[] { Field }, ETheme.Grimdark, ELayout.Rows, "#5a4a36", P(0.6f, 1f, "nature/crops_cornStageC", "nature/crops_leafsStageB"), 0.9f),
        new("dust-hill", "Dust hill", new[] { Hill }, ETheme.Grimdark, ELayout.Mound, "#7a6a52", P(0.4f, 1f, SpaceSmall), 0.12f),
        new("crystal-crag", "Crystal crag", new[] { Mountain }, ETheme.Grimdark, ELayout.Scatter, "#4f4a46", P(3.5f, 6.5f, "space/rock_largeA", "space/rock_largeB", "space/rock_crystalsLargeA", "space/rock_crystalsLargeB", "space/meteor_detailed"), 0.2f, P(1f, 2f, SpaceRocks), 0.15f),
        new("scrap-rubble", "Scrap rubble", new[] { Rubble }, ETheme.Grimdark, ELayout.Scatter, "#5a564f", P(0.3f, 0.8f, SpaceSmall), 0.8f, P(0.4f, 0.9f, Junk), 0.25f),
        new("craters", "Craters", new[] { Rubble, Hazard }, ETheme.Grimdark, ELayout.Scatter, "#4e4740", P(0.3f, 0.6f, "space/crater", "space/craterLarge"), 0.2f, P(0.3f, 0.7f, SpaceSmall), 0.4f),
        new("industrial-ruins", "Industrial ruins", new[] { Ruins }, ETheme.Grimdark, ELayout.Corner, "#55524c", P(2f, 2.8f, "survival/structure-metal-wall", "survival/metal-panel-screws", "survival/metal-panel"), 0.6f, P(0.3f, 0.9f, SpaceSmall.Concat(Junk).ToArray()), 0.45f),
        new("scaffold-ruins", "Scaffold ruins", new[] { Ruins }, ETheme.Grimdark, ELayout.Corner, "#55524c", P(2f, 2.8f, "space/structure", "space/structure_detailed", "space/supports_high"), 0.5f, P(0.3f, 0.9f, Junk), 0.4f),
        new("factory", "Factory", new[] { Building }, ETheme.Grimdark, ELayout.Single, "#4d4a46", P(3f, 6f, Enumerable.Range(0, 20).Select(i => "industrial/building-" + (char)('a' + i)).ToArray())),
        new("hangar", "Hangar", new[] { Building }, ETheme.Grimdark, ELayout.Single, "#4d4a46", P(3f, 5f, "space/hangar_smallA", "space/hangar_smallB", "space/hangar_largeA", "space/hangar_roundA")),
        new("tanks", "Storage tanks", new[] { Building }, ETheme.Grimdark, ELayout.Single, "#4d4a46", P(3f, 5f, "industrial/detail-tank-large", "industrial/water-tower", "industrial/detail-tank")),
        new("containers", "Containers", new[] { Building, Wall }, ETheme.Grimdark, ELayout.Line, "#4d4a46", P(1.6f, 2.4f, "industrial/shipping-container-a", "industrial/shipping-container-b", "industrial/shipping-container-c")),
        new("metal-wall", "Metal wall", new[] { Wall }, ETheme.Grimdark, ELayout.Line, "#4d4a46", P(1.8f, 2.6f, "survival/structure-metal-wall", "survival/metal-panel-screws")),
        new("fortified-fence", "Fortified fence", new[] { Barricade }, ETheme.Grimdark, ELayout.Line, "#4d4a46", P(0.8f, 1.2f, "survival/fence-fortified")),
        new("barricade", "Barrels and crates", new[] { Barricade }, ETheme.Grimdark, ELayout.Line, "#4d4a46", P(0.6f, 1f, "space/barrels", "survival/box-large", "space/barrels_rail", "survival/metal-panel-screws-half")),
        new("toxic-water", "Murky water", Water, ETheme.Grimdark, ELayout.Flat, "#2c5a5a", P(0.3f, 0.7f, SpaceSmall), 0.25f),
        new("toxic-swamp", "Toxic swamp", new[] { Swamp }, ETheme.Grimdark, ELayout.Scatter, "#3a4a2a", P(0.4f, 1f, "nature/plant_flatTall", "nature/grass_leafsLarge", "space/rock_crystals", "nature/log_large", "space/pipe_straight"), 0.5f),
        new("magma", "Magma", new[] { Lava }, ETheme.Grimdark, ELayout.Flat, "#b8401a", P(0.4f, 1f, SpaceRocks), 0.25f),
        new("crashed-craft", "Crashed craft", new[] { Wreck }, ETheme.Grimdark, ELayout.Single, "#4d4a46", P(1.5f, 2.6f, "space/craft_cargoA", "space/craft_cargoB", "space/craft_miner", "space/rover")),
        new("minefield", "Minefield", new[] { Hazard }, ETheme.Grimdark, ELayout.Scatter, "#4e3f36", P(0.2f, 0.5f, "space/crater", "space/machine_barrel", "space/rocks_smallA"), 0.5f),
    };

    // ------------------------------------------------------------------ choosing

    /// <summary>The theme a game system is drawn in.</summary>
    public static ETheme ThemeFor(EGameSystem system) =>
        GameSystemInfo.Slug(system).StartsWith("grimdark", StringComparison.Ordinal) ? ETheme.Grimdark : ETheme.Fantasy;

    /// <summary>Every look this theme has for this kind, in the catalogue's order (what the Map Maker lists).</summary>
    public static IReadOnlyList<Look> For(string kind, ETheme theme) =>
        All.Where(l => l.Theme == theme && l.Kinds.Contains(kind, StringComparer.OrdinalIgnoreCase)).ToList();

    public static Look? ById(string? id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(l => l.Id == id);

    /// <summary>
    /// The look a piece is drawn with: the one the map picked if it is for this kind and theme, otherwise one of the
    /// theme's looks for the kind chosen by <paramref name="seed"/> (null when the theme has none - drawn as a block).
    /// A picked look of the other theme counts as unpicked, so a map drawn for Grimdark Future still dresses itself
    /// in an Age of Fantasy game.
    /// </summary>
    public static Look? Pick(string kind, string? look, ETheme theme, int seed)
    {
        var options = For(kind, theme);
        if (ById(look) is { } chosen && options.Contains(chosen)) return chosen;
        return options.Count == 0 ? null : options[(int)((uint)seed % (uint)options.Count)];
    }

    /// <summary>A number every machine works out the same way for the same piece: from where it stands and its name, so
    /// two players see the same trees in the same places. (Not <see cref="string.GetHashCode"/>, which changes per run.)</summary>
    public static int SeedFor(float x, float z, string name)
    {
        unchecked
        {
            int h = (int)2166136261;
            foreach (char c in $"{MathF.Round(x * 4)}|{MathF.Round(z * 4)}|{name}") h = (h ^ c) * 16777619;
            return h & 0x7fffffff;
        }
    }

    // ------------------------------------------------------------------ pieces with no kind

    /// <summary>
    /// The book kind a piece most likely is, from its name first and its rules second - for pieces placed without one
    /// (maps made before the Map Maker, the lobby's random tables, scenario files). Always returns a kind with looks.
    /// </summary>
    public static string GuessKind(string? kind, string? name, ETerrainType rules, float heightInches, float area)
    {
        if (!string.IsNullOrWhiteSpace(kind) && (TerrainBook.Kind(kind) != null || kind is Wreck or Hazard)) return kind;
        string n = (name ?? "").ToLowerInvariant();
        bool Has(params string[] words) => words.Any(w => n.Contains(w, StringComparison.Ordinal));

        if (Has("lava", "magma")) return Lava;
        if (Has("mine", "wire", "trap")) return rules.HasFlag(ETerrainType.Impassible) ? Barricade : Hazard;
        if (Has("swamp", "marsh", "mire", "bog")) return Swamp;
        if (Has("river", "stream", "creek")) return rules.HasFlag(ETerrainType.Unstable) ? RiverDeep : RiverShallow;
        if (Has("lake", "pond", "pool", "water")) return rules.HasFlag(ETerrainType.Impassible) ? LakeDeep : LakeShallow;
        if (Has("wreck", "crash", "hauler", "vehicle")) return Wreck;
        if (Has("forest", "wood", "copse", "tree", "grove", "jungle")) return Forest;
        if (Has("field", "crop", "wheat", "farm")) return Field;
        if (Has("ruin", "shell", "collapsed")) return Ruins;
        if (Has("container", "building", "house", "hab", "bunker", "tower", "warehouse", "cathedral", "shrine", "temple", "manufactor")) return Building;
        if (Has("wall")) return Wall;
        if (Has("sandbag", "barricade", "fence", "hedge", "barrier", "trench", "palisade")) return Barricade;
        if (Has("hill", "mound")) return Hill;
        if (Has("mountain", "cliff", "crag", "ridge", "outcrop", "boulder", "standing stone", "rock")) return rules.HasFlag(ETerrainType.Impassible) ? Mountain : Rubble;
        if (Has("rubble", "debris", "crater", "scrap")) return Rubble;

        if (rules.HasFlag(ETerrainType.Dangerous)) return rules.HasFlag(ETerrainType.Difficult) ? Hazard : Lava;
        if (rules.HasFlag(ETerrainType.Impassible) || rules.HasFlag(ETerrainType.Blocking))
            return heightInches > 0 && heightInches < 1f ? LakeDeep : Building;
        if (rules.HasFlag(ETerrainType.Climbable)) return Wall;
        if (rules.HasFlag(ETerrainType.Steep)) return Hill;
        if (rules.HasFlag(ETerrainType.Cover) && rules.HasFlag(ETerrainType.Difficult)) return Forest;
        if (rules.HasFlag(ETerrainType.Cover) && rules.HasFlag(ETerrainType.Unstable)) return Ruins;
        if (rules.HasFlag(ETerrainType.Unstable)) return RiverDeep;
        if (rules.HasFlag(ETerrainType.Cover)) return area <= 8f ? Barricade : Ruins;
        if (rules.HasFlag(ETerrainType.Difficult)) return Rubble;
        return Rubble;
    }
}
