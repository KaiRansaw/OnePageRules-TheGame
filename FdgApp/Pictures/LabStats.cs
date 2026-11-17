using FdgRaylib.Models3D;

namespace FdgRaylib.Pictures;

/// <summary>
/// "Where you're up to" for the Asset Lab's home cards (Kai, 2026-11-16, "Menu Art &amp; Themes Mockup" round 3): how many
/// models, pictures and terrain pieces are downloaded and how much space each kind takes. Counting walks folders that
/// can hold tens of thousands of files, so <see cref="Measure"/> is meant for a background task.
/// </summary>
public static class LabStats
{
    public sealed record Snapshot(
        int Models, int ModelArmies, long ModelBytes,
        long CacheBytes,
        int TerrainPieces, long TerrainBytes,
        int OfficialPictures, int PictureArmies, long PictureBytes,
        long MenuPictureBytes);

    public static Snapshot Measure()
    {
        string models = UnitModelPaths.Root;
        string cache = Path.Combine(models, ModelDownloader.CacheFolderName);
        string terrain = Path.Combine(models, FdgRaylib.Terrain.WarhammerTerrain.ModelsFolderName);
        int modelCount = 0, armies = 0;
        long modelBytes = 0;
        if (Directory.Exists(models))
            foreach (string army in Directory.EnumerateDirectories(models))
            {
                string name = Path.GetFileName(army);
                if (name.StartsWith('_') || name.Equals(FdgRaylib.Terrain.WarhammerTerrain.ModelsFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                int here = 0;
                foreach (string f in Safe(army))
                    if (UnitModelPaths.IsModel(f)) { here++; modelBytes += Size(f); }
                modelCount += here;
                if (here > 0) armies++;
            }
        long cacheBytes = Safe(cache).Sum(Size);
        var terrainFiles = Safe(terrain).ToList();
        int pieces = terrainFiles.Count(UnitModelPaths.IsModel);
        long terrainBytes = terrainFiles.Sum(Size);

        int pictures = 0, pictureArmies = 0;
        long pictureBytes = 0;
        string? images = new[] { Path.Combine(AppAssets.Root, "..", "UnitImages"), Path.Combine(AppAssets.Root, "UnitImages") }
            .Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
        if (images != null)
            foreach (string army in Directory.EnumerateDirectories(images))
            {
                string portraits = Path.Combine(army, "Portraits");
                int here = 0;
                foreach (string f in Safe(portraits))
                    if (UnitModelPaths.IsImage(f)) { here++; pictureBytes += Size(f); }
                pictures += here;
                if (here > 0) pictureArmies++;
            }
        long menuBytes = Safe(MenuPictures.Folder).Sum(Size);
        return new Snapshot(modelCount, armies, modelBytes, cacheBytes, pieces, terrainBytes, pictures, pictureArmies, pictureBytes, menuBytes);
    }

    /// <summary>"12.3 GB" / "640 MB" / "12 KB".</summary>
    public static string Bytes(long b) => b >= 1_000_000_000 ? $"{b / 1e9:0.0} GB" : b >= 1_000_000 ? $"{b / 1e6:0} MB" : $"{Math.Max(0, b) / 1e3:0} KB";

    private static IEnumerable<string> Safe(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static long Size(string f) { try { return new FileInfo(f).Length; } catch (IOException) { return 0; } }
}
