using System.Collections.Concurrent;
using System.Linq;
using FdgRaylib.Game;
using Godot;
using OprGodot.Hud;
using static FdgRaylib.Game.GameCues;

namespace OprGodot.Battle;

/// <summary>
/// The battle's own sounds (lane 4, 2026-11-27) - not the shot effects: the ambience beds while the table is up, and
/// the game's cues (<see cref="GameCues"/>: dice, the battle starting, a round over, objectives, morale, units
/// destroyed, victory / defeat), each on the Cues bus (Settings -> Game cues).
/// </summary>
public partial class BattleTable
{
    private readonly ConcurrentQueue<ECue> _beatCues = new();
    private readonly Tracker _soundTracker = new();
    private ulong _lastDiceMs;
    private int _cueRoll;
    private bool _soundsOn;

    private void StartSounds()
    {
        GameAudio.InBattle = true;
        GameAudio.I?.SetAmbience(AmbienceFor(Session.Lobby.Vm.GameSystem, Session.Lobby.Vm.MapWeather)
            .Select(b => $"res://sounds/ambience/{b}.ogg").ToList());
        Session.Report.BeatSeen += OnSoundBeat;
        _soundsOn = true;
    }

    private void StopSounds()
    {
        if (!_soundsOn) return;
        _soundsOn = false;
        Session.Report.BeatSeen -= OnSoundBeat;
        GameAudio.InBattle = false;
        GameAudio.I?.SetAmbience(System.Array.Empty<string>());
    }

    /// <summary>Engine thread: only queues the cue.</summary>
    private void OnSoundBeat(FDG.Presentation.PresentationBeat beat)
    {
        if (FromBeat(beat) is { } cue && _beatCues.Count < 32) _beatCues.Enqueue(cue);
    }

    private void SyncSounds()
    {
        if (!_soundsOn || GameAudio.I is not { } audio) return;
        while (_beatCues.TryDequeue(out ECue cue))
        {
            // A volley rolls many times in a blink: one rattle every quarter second is plenty.
            if (cue == ECue.Dice)
            {
                ulong now = Time.GetTicksMsec();
                if (now - _lastDiceMs < 250) continue;
                _lastDiceMs = now;
            }
            audio.Cue($"res://sounds/{FdgRaylib.Game.GameCues.FileFor(cue, _cueRoll++)}.ogg", cue == ECue.Dice ? -4f : 0f);
        }
        bool ended = Session.EndedText != null;
        bool? won = ended ? Result(Session.EndedText, Session.Seats.Where(x => x.Local).Select(x => x.Name)) : null;
        foreach (ECue cue in _soundTracker.Update(Session.Table, Session.Game.LocalPlayerIDs, ended, won))
            audio.Cue($"res://sounds/{FdgRaylib.Game.GameCues.FileFor(cue, _cueRoll++)}.ogg");
    }
}
