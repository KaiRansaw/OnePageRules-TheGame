using System;
using System.Collections.Generic;
using FdgRaylib.Config;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The game's sound channels and everything that isn't a shot effect (lane 4, 2026-11-27): menu music, the battle's
/// ambience (wind, distant rumbles, rain) and the game's cues (dice, rounds, objectives, morale, units destroyed,
/// victory / defeat). One bus per Settings slider - Music, Ambience, Cues, UI - under Master; the battle effects play
/// on Master with their own slider. Master ends in a limiter so no sound in the game can clip, whatever its file.
/// Files: <c>res://sounds/{music,ambience,cues,dice}</c>, made by <c>tools/audio/make_audio.py</c> (CC0).
/// </summary>
public partial class GameAudio : Node
{
    public const string MusicBus = "Music", AmbienceBus = "Ambience", CueBus = "Cues", UiBus = "UI";

    public static GameAudio? I { get; private set; }

    /// <summary>True while a battle table is up: no menu music, the battle's ambience instead.</summary>
    public static bool InBattle { get; set; }

    private AudioStreamPlayer _music = null!;
    private readonly List<AudioStreamPlayer> _ambience = new();
    private readonly List<AudioStreamPlayer> _cues = new();
    private string _musicFile = "";
    private string _ambienceKey = "";

    public static void Install(Node app)
    {
        EnsureBuses();
        var a = new GameAudio { Name = "GameAudio", ProcessMode = ProcessModeEnum.Always };
        app.AddChild(a);
        I = a;
        ApplyVolumes();
    }

    public override void _Ready()
    {
        _music = new AudioStreamPlayer { Bus = MusicBus };
        AddChild(_music);
    }

    /// <summary>The buses, once: Music / Ambience / Cues / UI under Master, and a limiter at the end of Master.</summary>
    private static void EnsureBuses()
    {
        foreach (string bus in new[] { MusicBus, AmbienceBus, CueBus, UiBus })
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
        Set(AudioServer.GetBusIndex(MusicBus), v.MusicVolume);
        Set(AudioServer.GetBusIndex(AmbienceBus), v.AmbienceVolume);
        Set(AudioServer.GetBusIndex(CueBus), v.CueVolume);
        Set(AudioServer.GetBusIndex(UiBus), v.MenuSoundVolume);
    }

    private static void Set(int bus, float linear)
    {
        if (bus < 0) return;
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Math.Max(linear, 0.0001f)));
        AudioServer.SetBusMute(bus, linear <= 0.001f);
    }

    public override void _Process(double delta)
    {
        // Menu music by the look on screen (Grimdark / Fantasy); none in battle.
        string want = InBattle ? "" : Looks.Current == Looks.Fantasy ? "res://sounds/music/menu_fantasy.ogg" : "res://sounds/music/menu_grimdark.ogg";
        if (want != _musicFile)
        {
            _musicFile = want;
            _music.Stop();
            if (want.Length > 0 && Loop(want) is { } s) { _music.Stream = s; _music.Play(); }
        }
        if (!InBattle && _ambienceKey.Length > 0) SetAmbience(Array.Empty<string>());
    }

    /// <summary>The battle's background beds, looping (Settings -> Ambience). An empty list stops them.</summary>
    public void SetAmbience(IReadOnlyList<string> files)
    {
        string key = string.Join("|", files);
        if (key == _ambienceKey) return;
        _ambienceKey = key;
        foreach (AudioStreamPlayer p in _ambience) p.QueueFree();
        _ambience.Clear();
        foreach (string f in files)
        {
            if (Loop(f) is not { } s) continue;
            var p = new AudioStreamPlayer { Stream = s, Bus = AmbienceBus };
            AddChild(p);
            p.Play();
            _ambience.Add(p);
        }
    }

    /// <summary>A one-shot cue on the Cues bus (a few can overlap).</summary>
    public void Cue(string file, float db = 0f)
    {
        Played.Add(System.IO.Path.GetFileNameWithoutExtension(file));
        if (Played.Count > 200) Played.RemoveAt(0);
        if (!ResourceLoader.Exists(file)) { GD.PrintErr($"GameAudio: no sound {file}"); return; }
        AudioStreamPlayer? p = null;
        foreach (AudioStreamPlayer c in _cues) if (!c.Playing) { p = c; break; }
        if (p == null)
        {
            if (_cues.Count >= 8) return;
            p = new AudioStreamPlayer { Bus = CueBus };
            AddChild(p);
            _cues.Add(p);
        }
        p.Stream = GD.Load<AudioStream>(file);
        p.VolumeDb = db;
        p.Play();
    }

    /// <summary>The cues played so far (newest last) - for the test driver's "audiocheck".</summary>
    public readonly List<string> Played = new();

    /// <summary>One line on what is playing now (test driver).</summary>
    public string Report() =>
        $"music={(_music.Playing ? _musicFile : "-")} ambience=[{_ambienceKey}] buses={AudioServer.BusCount} cues={string.Join(",", Played)}";

    private static AudioStream? Loop(string file)
    {
        if (!ResourceLoader.Exists(file)) return null;
        var s = GD.Load<AudioStream>(file);
        if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        return s;
    }
}
