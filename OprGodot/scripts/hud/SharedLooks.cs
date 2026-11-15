using FDG;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.SaveLoad;
using FdgRaylib.Lobby;
using FdgRaylib.Models3D;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// Each player's unit pictures and model picks, shared with the others (Kai, 2026-11-14: "does the game on multiplayer
/// matches use the players' selected unit portraits for the other players? if not, can we make that work?" and "could
/// you also do the same for the unit models in game?").
///
/// <para>While the lobby is open, every seat this computer controls sends small copies of its units' pictures (as this
/// computer shows them), how each is framed, and which model file stands for each unit and look. Another player's units
/// are then drawn with that player's pictures and framing; their models are that player's picks wherever this computer
/// has the same file, else this computer's own pick. The seats this computer controls (its own, and a host's bots)
/// always use the local files.</para>
/// </summary>
public static class SharedLooks
{
    /// <summary>Longest side of a picture sent - enough for the biggest banner, small enough to send 30 of.</summary>
    public const int SentSize = 256;

    /// <summary>The open lobby (it lives on through the battle). Null = nothing shared (a single-player test screen).</summary>
    public static ILobbyViewModel? Lobby { get; set; }

    private static readonly Dictionary<(PlayerID, string), (string Data, Texture2D? Tex)> Decoded = new();
    private static readonly Dictionary<PlayerID, string> SentSignature = new();

    /// <summary>Clears what was sent and decoded (a new lobby).</summary>
    public static void Reset(ILobbyViewModel? lobby)
    {
        Lobby = lobby;
        Decoded.Clear();
        SentSignature.Clear();
    }

    private static SeatPictures? Of(PlayerID? owner)
    {
        if (owner is not { } id || Lobby is not { } vm) return null;
        try
        {
            if (vm.CheckCanModifyPlayerIDInfo(id)) return null; // ours: the local files
            return vm.SeatPictures.TryGetValue(id, out var p) ? p : null;
        }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>Another player's picture for this unit and look, or null (then the local lookup is used).</summary>
    public static Texture2D? Picture(PlayerID? owner, string unit, string? look, out bool modelMade)
    {
        modelMade = false;
        if (Of(owner) is not { } p) return null;
        string key = SeatPictures.Key(unit, look);
        // A look with no picture of its own falls back to the unit's first version's, as the local lookup does.
        if (!p.Pictures.TryGetValue(key, out var data) && !p.Pictures.TryGetValue(key = SeatPictures.Key(unit, null), out data))
            return null;
        modelMade = p.ModelMade.Contains(key);
        var cacheKey = (owner!.Value, key);
        if (Decoded.TryGetValue(cacheKey, out var held) && ReferenceEquals(held.Data, data)) return held.Tex;
        Texture2D? tex = null;
        try
        {
            byte[] bytes = Convert.FromBase64String(data);
            var img = new Image();
            if (img.LoadWebpFromBuffer(bytes) == Error.Ok || img.LoadPngFromBuffer(bytes) == Error.Ok)
                tex = ImageTexture.CreateFromImage(img);
        }
        catch (FormatException) { }
        Decoded[cacheKey] = (data, tex);
        return tex;
    }

    /// <summary>Another player's framing for this unit, or null (then this computer's).</summary>
    public static PictureFraming.Frame? Framing(PlayerID? owner, string unit)
    {
        if (Of(owner) is not { } p || !p.Pictures.Keys.Any(k => k.StartsWith(unit + "|", StringComparison.Ordinal))) return null;
        return p.Framing.TryGetValue(unit, out var f) && f.Length == 3 ? PictureFraming.Clamp(new(f[0], f[1], f[2])) : PictureFraming.Frame.Default;
    }

    /// <summary>This computer's copy of the model another player picked for this unit and look, or null.</summary>
    public static string? Model(PlayerID? owner, string unit, string? look, UnitModelLibrary? library)
    {
        if (Of(owner) is not { } p) return null;
        return p.Models.TryGetValue(SeatPictures.Key(unit, look), out var rel) ? SharedModels.Local(rel, library) : null;
    }

    // ---- sending

    /// <summary>
    /// Called by the lobby a few times a second: sends the pictures of every seat this computer controls whose army
    /// changed since it was last sent.
    /// </summary>
    public static void Tick(LobbySession session)
    {
        var vm = session.Vm;
        if (vm.IsResumeMode) return;
        foreach (var info in vm.PlayerInfos)
        {
            if (!vm.CheckCanModifyPlayerIDInfo(info.PlayerID)) continue;
            if (session.SeatArmy(info.PlayerID).Army is not { } army) continue;
            string sig = Signature(army);
            if (SentSignature.TryGetValue(info.PlayerID, out var was) && was == sig) continue;
            SentSignature[info.PlayerID] = sig;
            try { vm.SetMyPictures(info.PlayerID, Build(army)); }
            catch (Exception e) { GD.PushWarning("Couldn't send unit pictures: " + e.Message); }
        }
    }

    private static string Signature(ArmyListFile army) =>
        army.Faction + "#" + string.Join(";", army.Units.Select(u => u.Name + ":" + string.Join(",", u.ModelLooks ?? new())));

    /// <summary>What this computer shows for an army's units: pictures, framing and model picks.</summary>
    public static SeatPictures Build(ArmyListFile army)
    {
        var pictures = new Dictionary<string, string>();
        var made = new List<string>();
        var framing = new Dictionary<string, float[]>();
        var models = new Dictionary<string, string>();
        var lib = UnitPortraits.Library;
        foreach (var u in army.Units.Concat(army.AuxiliaryUnits ?? new()))
        {
            string? look = UnitPortraits.LookOf(u.ModelLooks);
            string key = SeatPictures.Key(u.Name, look);
            if (!pictures.ContainsKey(key) && HudKit.UnitPicture(army.Faction, u.Name, look) is { } tex && Encode(tex) is { } data)
            {
                pictures[key] = data;
                if (HudKit.IsModelMade(tex)) made.Add(key);
            }
            var f = PictureFraming.For(army.Faction, u.Name);
            if (!f.IsDefault) framing[u.Name] = new[] { f.X, f.Y, f.Zoom };
            if (lib == null) continue;
            foreach (string? l in (u.ModelLooks ?? new()).Append(null).Distinct())
            {
                string mk = SeatPictures.Key(u.Name, string.IsNullOrEmpty(l) ? null : l);
                if (models.ContainsKey(mk)) continue;
                if (UnitPortraits.ModelFor(lib, army.Faction, u.Name, l) is { } file && SharedModels.Relative(file) is { } rel)
                    models[mk] = rel;
            }
        }
        return new SeatPictures { Pictures = pictures, ModelMade = made, Framing = framing, Models = models };
    }

    /// <summary>A texture shrunk to <see cref="SentSize"/> and packed as WebP (keeps a model-made picture's clear background).</summary>
    private static string? Encode(Texture2D tex)
    {
        Image? img = tex.GetImage();
        if (img == null || img.IsEmpty()) return null;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        int w = img.GetWidth(), h = img.GetHeight();
        float scale = Math.Min(1f, (float)SentSize / Math.Max(w, h));
        if (scale < 1f) img.Resize(Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)), Image.Interpolation.Lanczos);
        byte[] bytes = img.SaveWebpToBuffer(true, 0.85f);
        if (bytes.Length == 0) bytes = img.SavePngToBuffer();
        string s = Convert.ToBase64String(bytes);
        return s.Length <= SeatPictures.MaxPictureChars ? s : null;
    }
}
