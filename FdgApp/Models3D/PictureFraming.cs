using System.Text.Json;

namespace FdgRaylib.Models3D;

/// <summary>
/// Where each unit picture is centred and how far it is zoomed (Kai, 2026-11-14: "an in-game editor to change where the
/// portrait pictures are centred and how zoomed in they are... drag and drop"). Set in the Asset Lab's picture framing
/// window; every banner the game draws for that unit crops the picture this way.
///
/// <para>Kept in <c>UnitImages/framing.json</c>, beside the pictures it is about - on the player's computer, like them.
/// A unit with no entry is drawn centred at zoom 1 (the picture just fills the space).</para>
/// </summary>
public static class PictureFraming
{
    public const string FileName = "framing.json";

    /// <summary>The point of the picture that sits in the middle of the frame (0-1 across and down), and the zoom (1 =
    /// the picture just fills the frame, 2 = twice as close).</summary>
    public sealed record Frame(float X = 0.5f, float Y = 0.5f, float Zoom = 1f)
    {
        public static readonly Frame Default = new();
        [System.Text.Json.Serialization.JsonIgnore] public bool IsDefault => this == Default;
    }

    public const float MaxZoom = 4f;

    private static Dictionary<string, Frame>? _frames;
    private static string? _loadedFrom;

    public static string Path => System.IO.Path.Combine(OfficialPictures.Root, FileName);

    /// <summary>The key for a unit: its army and name with case, spaces and punctuation ignored (as pictures are found).</summary>
    public static string KeyFor(string faction, string unit) => OfficialPictures.Normalise(faction) + "/" + OfficialPictures.Normalise(unit);

    public static Frame For(string faction, string unit, string? file = null)
    {
        var all = Load(file);
        return all.TryGetValue(KeyFor(faction, unit), out var f) ? f : Frame.Default;
    }

    public static void Set(string faction, string unit, Frame frame, string? file = null)
    {
        var all = Load(file);
        string key = KeyFor(faction, unit);
        frame = Clamp(frame);
        if (frame.IsDefault) all.Remove(key); else all[key] = frame;
        Save(file);
    }

    public static Frame Clamp(Frame f) => new(Math.Clamp(f.X, 0f, 1f), Math.Clamp(f.Y, 0f, 1f), Math.Clamp(f.Zoom, 1f, MaxZoom));

    /// <summary>
    /// The part of a <paramref name="picW"/> x <paramref name="picH"/> picture to show in a frame of the given aspect
    /// (width / height): the largest such rectangle that fits, shrunk by the zoom, centred on the frame's point and kept
    /// inside the picture. Returns x, y, width, height in picture pixels.
    /// </summary>
    public static (float X, float Y, float W, float H) Region(float picW, float picH, float aspect, Frame f)
    {
        if (picW <= 0 || picH <= 0 || aspect <= 0) return (0, 0, picW, picH);
        float w = picW, h = picW / aspect;
        if (h > picH) { h = picH; w = picH * aspect; }
        float zoom = Math.Clamp(f.Zoom, 1f, MaxZoom);
        w /= zoom; h /= zoom;
        float x = Math.Clamp(f.X * picW - w / 2, 0, picW - w);
        float y = Math.Clamp(f.Y * picH - h / 2, 0, picH - h);
        return (x, y, w, h);
    }

    private static Dictionary<string, Frame> Load(string? file)
    {
        file ??= Path;
        if (_frames != null && _loadedFrom == file) return _frames;
        _loadedFrom = file;
        try { _frames = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Frame>>(File.ReadAllText(file)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _frames = new(); }
        return _frames;
    }

    private static void Save(string? file)
    {
        file ??= Path;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(_frames ?? new(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Forget what was read (tests, or after the file changed on disk).</summary>
    public static void Forget() { _frames = null; _loadedFrom = null; }
}
