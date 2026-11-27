using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FdgRaylib.Audio;
using FdgRaylib.Config;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The game's sound channels and everything that isn't a shot effect: music, the announcer, the battle's ambience and
/// the game's cues. One bus per Settings slider - Music, Announcer, Ambience, Cues, UI - under Master; the battle effects
/// play on Master with their own slider. Master ends in a limiter so no sound in the game can clip, whatever its file.
/// <para>Every sound is asked for by its slot (<see cref="SoundSlots"/>, 2026-11-27) and the files come from
/// <see cref="SoundLibrary"/>: Kai's own audio folder first (<c>Assets/Audio</c>), then the built-in files under
/// <c>res://sounds</c>. A slot with no files is silent, so the music director and the announcer run before any audio
/// exists.</para>
/// </summary>
public partial class GameAudio : Node
{
    public const string MusicBus = "Music", AnnouncerBus = "Announcer", AmbienceBus = "Ambience", CueBus = "Cues", UiBus = "UI";

    public static GameAudio? I { get; private set; }

    /// <summary>True while a battle table is up: the battle's music and ambience instead of the menu's.</summary>
    public static bool InBattle { get; set; }

    /// <summary>What the battle tells the music director each frame (system, mode, last round, armies, result). Only read
    /// while <see cref="InBattle"/>.</summary>
    public static MusicContext BattleContext { get; set; } = new(EMusicScene.Battle, EGameSystem.GrimdarkFuture);

    /// <summary>The announcer's queue: the battle feeds it, this node speaks it.</summary>
    public readonly Announcer Announcer = new();

    private const float FadeSeconds = 1.5f;
    private const float DuckDb = -9f;

    private AudioStreamPlayer _musicA = null!, _musicB = null!, _voice = null!;
    private AudioStreamPlayer _current = null!;
    private readonly List<AudioStreamPlayer> _ambience = new();
    private readonly List<AudioStreamPlayer> _cues = new();
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private int _streamsVersion = -1;
    private string _musicSlot = "";
    private EMusicScene _musicScene;
    private List<SoundFile> _playlist = new();
    private int _track;
    private string _ambienceKey = "";
    private float _duck; // dB, 0 = none, eases to DuckDb while the announcer speaks
    private int _libraryVersion = -1;

    public static void Install(Node app)
    {
        EnsureBuses();
        SoundLibrary.EnsureFolder();
        SoundLibrary.Rescan();
        var a = new GameAudio { Name = "GameAudio", ProcessMode = ProcessModeEnum.Always };
        app.AddChild(a);
        I = a;
        ApplyVolumes();
    }

    public override void _Ready()
    {
        _musicA = new AudioStreamPlayer { Bus = MusicBus };
        _musicB = new AudioStreamPlayer { Bus = MusicBus, VolumeDb = -80f };
        _voice = new AudioStreamPlayer { Bus = AnnouncerBus };
        AddChild(_musicA);
        AddChild(_musicB);
        AddChild(_voice);
        _current = _musicA;
        _musicA.Finished += () => OnTrackFinished(_musicA);
        _musicB.Finished += () => OnTrackFinished(_musicB);
    }

    /// <summary>The buses, once: one per channel under Master, and a limiter at the end of Master.</summary>
    private static void EnsureBuses()
    {
        foreach (string bus in new[] { MusicBus, AnnouncerBus, AmbienceBus, CueBus, UiBus })
        {
            if (AudioServer.GetBusIndex(bus) >= 0) continue;
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, bus);
            AudioServer.SetBusSend(i, "Master");
        }
        bool limited = false;
        for (int e = 0; e < AudioServer.GetBusEffectCount(0); e++) limited |= AudioServer.GetBusEffect(0, e) is AudioEffectHardLimiter;
        if (!limited) AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -1f });
    }

    /// <summary>Every slider's value onto its bus (Settings calls this on every change).</summary>
    public static void ApplyVolumes()
    {
        var v = UserConfig.Current.View;
        Set(0, v.MasterVolume);
        Set(AudioServer.GetBusIndex(MusicBus), v.MusicVolume, I?._duck ?? 0f);
        Set(AudioServer.GetBusIndex(AnnouncerBus), v.Announcer ? v.AnnouncerVolume : 0f);
        Set(AudioServer.GetBusIndex(AmbienceBus), v.AmbienceVolume);
        Set(AudioServer.GetBusIndex(CueBus), v.CueVolume);
        Set(AudioServer.GetBusIndex(UiBus), v.MenuSoundVolume);
    }

    private static void Set(int bus, float linear, float offsetDb = 0f)
    {
        if (bus < 0) return;
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Math.Max(linear, 0.0001f)) + offsetDb);
        AudioServer.SetBusMute(bus, linear <= 0.001f);
    }

    public override void _Process(double delta)
    {
        if (_libraryVersion != SoundLibrary.Version) { _libraryVersion = SoundLibrary.Version; _musicSlot = "\0"; }
        UpdateMusic();
        if (!InBattle && _ambienceKey.Length > 0) SetAmbience(Array.Empty<string>());
        UpdateAnnouncer();

        // The music dips while the announcer speaks and comes back up after.
        float want = _voice.Playing ? DuckDb : 0f;
        if (Math.Abs(_duck - want) > 0.05f)
        {
            _duck = Mathf.MoveToward(_duck, want, (float)delta * (want < _duck ? 40f : 12f));
            ApplyVolumes();
        }
    }

    // ── Music ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Where the player is now, for the music director.</summary>
    public static MusicContext CurrentContext()
    {
        var view = UserConfig.Current.View;
        if (InBattle) return BattleContext with { BattleMusicOn = view.BattleMusic };
        EGameSystem family = Looks.Current == Looks.Fantasy ? EGameSystem.AgeOfFantasy : EGameSystem.GrimdarkFuture;
        EMusicScene scene = App.I?.TopScreen switch
        {
            LobbyScreen => EMusicScene.Lobby,
            Campaign.ArmyCampaignsScreen or Campaign.MapCampaignScreen or Quest.QuestCampaignsScreen => EMusicScene.Campaign,
            _ => EMusicScene.Menu,
        };
        return new MusicContext(scene, family);
    }

    private void UpdateMusic()
    {
        MusicContext ctx = CurrentContext();
        string slot = MusicDirector.Choose(ctx) ?? "";
        if (slot == _musicSlot && ctx.Scene == _musicScene) return;
        // Same music across screens that share it (menu -> lobby falling back to menu music): keep playing.
        bool sameTracks = slot == _musicSlot;
        _musicSlot = slot;
        _musicScene = ctx.Scene;
        if (sameTracks) return;
        _playlist = slot.Length > 0 ? SoundLibrary.Playlist(slot) : new List<SoundFile>();
        _track = 0;
        PlayTrack(_playlist.Count > 0 ? _playlist[0] : null);
    }

    /// <summary>Crossfades to a track (null: fades the music out).</summary>
    private void PlayTrack(SoundFile? file)
    {
        AudioStreamPlayer old = _current, next = _current == _musicA ? _musicB : _musicA;
        // A fade still running on the player about to be reused would turn the new track down and stop it.
        Fade(next)?.Kill();
        Fade(old)?.Kill();
        if (old.Playing)
        {
            Tween fadeOut = CreateTween();
            fadeOut.TweenProperty(old, "volume_db", -60f, FadeSeconds);
            fadeOut.TweenCallback(Callable.From(old.Stop));
            SetFade(old, fadeOut);
        }
        if (file is not { } f || Load(f) is not { } stream) return;
        // One track that should loop loops itself; a playlist moves on when each track ends (OnTrackFinished).
        SetLoop(stream, MusicDirector.Loops(_musicScene) && _playlist.Count == 1);
        Log(Tracks, $"{_musicSlot}:{System.IO.Path.GetFileName(f.Path)}");
        next.Stream = stream;
        next.VolumeDb = old.Playing ? -60f : 0f;
        next.Play();
        if (next.VolumeDb < 0f)
        {
            Tween fadeIn = CreateTween();
            fadeIn.TweenProperty(next, "volume_db", 0f, FadeSeconds);
            SetFade(next, fadeIn);
        }
        _current = next;
    }

    private Tween? _fadeA, _fadeB;
    private Tween? Fade(AudioStreamPlayer p) => p == _musicA ? _fadeA : _fadeB;
    private void SetFade(AudioStreamPlayer p, Tween t) { if (p == _musicA) _fadeA = t; else _fadeB = t; }

    private void OnTrackFinished(AudioStreamPlayer who)
    {
        if (who != _current || !MusicDirector.Loops(_musicScene) || _playlist.Count == 0) return;
        _track = (_track + 1) % _playlist.Count;
        if (_track == 0 && _playlist.Count > 2) _playlist = SoundLibrary.Playlist(_musicSlot); // a fresh shuffle each time round
        PlayTrack(_playlist[_track]);
    }

    // ── Announcer ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The music tracks started so far (newest last) - for the test driver's "audiocheck".</summary>
    public readonly List<string> Tracks = new();

    /// <summary>The lines spoken so far (newest last) - for the test driver's "audiocheck".</summary>
    public readonly List<string> Spoken = new();

    private void UpdateAnnouncer()
    {
        long now = (long)Time.GetTicksMsec();
        while (Announcer.Next(now, _voice.Playing) is { } line)
        {
            if (!UserConfig.Current.View.Announcer) continue;
            string? slot = line.Slots.FirstOrDefault(SoundLibrary.HasSound);
            Log(Spoken, slot ?? line.Key + " (no file)");
            if (slot == null || SoundLibrary.Pick(slot) is not { } f || Load(f) is not { } stream) continue;
            SetLoop(stream, false);
            _voice.Stream = stream;
            _voice.Play();
            break;
        }
    }

    // ── Ambience and cues ────────────────────────────────────────────────────────────────────────

    /// <summary>The battle's background beds by slot (<c>ambience/war</c>...), looping. An empty list stops them.</summary>
    public void SetAmbience(IReadOnlyList<string> slots)
    {
        string key = string.Join("|", slots);
        if (key == _ambienceKey) return;
        _ambienceKey = key;
        foreach (AudioStreamPlayer p in _ambience) p.QueueFree();
        _ambience.Clear();
        foreach (string slot in slots)
        {
            if (SoundLibrary.Pick(slot) is not { } f || Load(f) is not { } s) continue;
            SetLoop(s, true);
            var p = new AudioStreamPlayer { Stream = s, Bus = AmbienceBus };
            AddChild(p);
            p.Play();
            _ambience.Add(p);
        }
    }

    /// <summary>A one-shot cue by slot (<c>cues/victory</c>...) on the Cues bus; a few can overlap.</summary>
    public void Cue(string slot, float db = 0f)
    {
        Log(Played, slot);
        if (SoundLibrary.Pick(slot) is not { } f || Load(f) is not { } stream) return;
        AudioStreamPlayer? p = null;
        foreach (AudioStreamPlayer c in _cues) if (!c.Playing) { p = c; break; }
        if (p == null)
        {
            if (_cues.Count >= 8) return;
            p = new AudioStreamPlayer { Bus = CueBus };
            AddChild(p);
            _cues.Add(p);
        }
        SetLoop(stream, false);
        p.Stream = stream;
        p.VolumeDb = db;
        p.Play();
    }

    /// <summary>The cues played so far (newest last) - for the test driver's "audiocheck".</summary>
    public readonly List<string> Played = new();

    private static void Log(List<string> list, string s)
    {
        list.Add(s);
        if (list.Count > 200) list.RemoveAt(0);
    }

    /// <summary>One line on what is playing now (test driver).</summary>
    public string Report() =>
        $"music={(_current.Playing ? _musicSlot + ":" + System.IO.Path.GetFileName(_playlist.ElementAtOrDefault(_track).Path ?? "") : "-")} " +
        $"scene={_musicScene} ambience=[{_ambienceKey}] buses={AudioServer.BusCount} tracks={string.Join(",", Tracks.TakeLast(6))} cues={string.Join(",", Played.TakeLast(12))} said={string.Join(",", Spoken)}";

    // ── Loading ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A slot's file as a stream: built-in ones from the project, Kai's from disk (.ogg / .mp3 / .wav). Cached
    /// until the folder is rescanned. Null (and a line in the log) when the file can't be read.</summary>
    public AudioStream? Load(SoundFile f)
    {
        if (_streamsVersion != SoundLibrary.Version) { _streams.Clear(); _streamsVersion = SoundLibrary.Version; }
        if (_streams.TryGetValue(f.Path, out AudioStream? cached)) return cached;
        AudioStream? s = null;
        try
        {
            if (f.BuiltIn)
            {
                string res = $"res://{f.Path}.ogg";
                if (ResourceLoader.Exists(res)) s = (AudioStream)GD.Load<AudioStream>(res).Duplicate();
            }
            else
            {
                s = System.IO.Path.GetExtension(f.Path).ToLowerInvariant() switch
                {
                    ".ogg" => AudioStreamOggVorbis.LoadFromFile(f.Path),
                    ".mp3" => AudioStreamMP3.LoadFromFile(f.Path),
                    ".wav" => AudioStreamWav.LoadFromFile(f.Path),
                    _ => null,
                };
            }
        }
        catch (Exception e) { GD.PrintErr($"GameAudio: can't read {f.Path}: {e.Message}"); }
        if (s == null) GD.PrintErr($"GameAudio: no sound in {f.Path}");
        return _streams[f.Path] = s;
    }

    private static void SetLoop(AudioStream s, bool loop)
    {
        switch (s)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = loop; break;
            case AudioStreamMP3 mp3: mp3.Loop = loop; break;
            case AudioStreamWav wav:
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                if (loop && wav.LoopEnd <= 0) wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
                break;
        }
    }
}
