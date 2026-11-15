namespace FdgRaylib.Models3D;

/// <summary>
/// Model picks shared with the other players (Kai, 2026-11-14: "could you also do the same for the unit models in
/// game?"). The files are too big to send, so a pick travels as its path inside the UnitModels folder; another computer
/// uses it when it has that file - in the same folder, or anywhere in its library under the same file name - and
/// otherwise draws its own pick for the unit.
/// </summary>
public static class SharedModels
{
    /// <summary>A model file as a path inside <paramref name="root"/> ("Book/Unit/file.glb"), or null when it is outside.</summary>
    public static string? Relative(string file, string? root = null)
    {
        root = Path.GetFullPath(root ?? UnitModelPaths.Root);
        string full = Path.GetFullPath(file);
        string rel = Path.GetRelativePath(root, full);
        if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return null;
        return rel.Replace('\\', '/');
    }

    /// <summary>
    /// This computer's copy of another player's pick, or null. Only plain paths inside the UnitModels folder are
    /// followed (never "..", a drive or an absolute path), and only model files.
    /// </summary>
    public static string? Local(string? relative, UnitModelLibrary? library, string? root = null)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        string[] parts = relative.Split('/', '\\');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains(':'))) return null;
        string name = parts[^1];
        if (!UnitModelPaths.IsModel(name)) return null;
        string full = Path.Combine(new[] { root ?? UnitModelPaths.Root }.Concat(parts).ToArray());
        if (File.Exists(full)) return full;
        return library?.FileNamed(name);
    }
}
