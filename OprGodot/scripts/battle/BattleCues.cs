using System.Collections.Concurrent;
using System.Linq;
using FdgRaylib.Audio;
using FdgRaylib.Game;
using Godot;
using OprGodot.Hud;
using static FdgRaylib.Game.GameCues;

namespace OprGodot.Battle;

/// <summary>
/// The battle's own sounds - not the shot effects: the ambience beds while the table is up, the game's cues
/// (<see cref="GameCues"/>: dice, the battle starting, a round over, objectives, morale, units destroyed, victory /
/// defeat), the announcer's lines (<see cref="Announcer"/>) and what the music director needs to know (2026-11-27:
/// system, mode, the last round, this computer's armies, the result). Every sound is a slot, so Kai's own files in the
/// audio folder replace or fill them.
/// </summary>
public partial class BattleTable
{
    private readonly ConcurrentQueue<ECue> _beatCues = new();
    private readonly ConcurrentQueue<FDG.Presentation.PresentationBeat> _voiceBeats = new();
    private readonly Tracker _soundTracker = new();
    private ulong _lastDiceMs;
    private bool _soundsOn;

    private void StartSounds()
    {
        GameAudio.InBattle = true;
        GameAudio.BattleContext = Context(false, null);
        GameAudio.I?.SetAmbience(AmbienceFor(Session.Lobby.Vm.GameSystem, Session.Lobby.Vm.MapWeather)
            .Select(b => $"ambience/{b}").ToList());
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

    /// <summary>Engine thread: only queues.</summary>
    private void OnSoundBeat(FDG.Presentation.PresentationBeat beat)
    {
        if (FromBeat(beat) is { } cue && _beatCues.Count < 32) _beatCues.Enqueue(cue);
        if (beat is FDG.Presentation.Beats.BannerBeat or FDG.Presentation.Beats.UnitRoutedBeat && _voiceBeats.Count < 32) _voiceBeats.Enqueue(beat);
    }

    /// <summary>What the music director hears from this battle.</summary>
    private MusicContext Context(bool ended, bool? won)
    {
        var vm = Session.Lobby.Vm;
        FDG.IGameProgress? p = Session.Table.Progress;
        bool finalRound = p?.RoundCount is int r && p.TotalRounds > 0 && r >= p.TotalRounds;
        return new MusicContext(ended ? EMusicScene.GameOver : EMusicScene.Battle, vm.GameSystem, vm.GameMode, finalRound, won,
            Session.Seats.Where(s => s.Local && !s.Bot).Select(s => s.Faction).Distinct().ToList());
    }

    private void SyncSounds()
    {
        if (!_soundsOn || GameAudio.I is not { } audio) return;
        long now = (long)Time.GetTicksMsec();
        while (_beatCues.TryDequeue(out ECue cue))
        {
            // A volley rolls many times in a blink: one rattle every quarter second is plenty.
            if (cue == ECue.Dice)
            {
                if ((ulong)now - _lastDiceMs < 250) continue;
                _lastDiceMs = (ulong)now;
            }
            audio.Cue(SlotFor(cue), cue == ECue.Dice ? -4f : 0f);
        }
        while (_voiceBeats.TryDequeue(out FDG.Presentation.PresentationBeat? beat)) audio.Announcer.OnBeat(beat, now);

        bool ended = Session.EndedText != null;
        bool? won = ended ? Result(Session.EndedText, Session.Seats.Where(x => x.Local).Select(x => x.Name)) : null;
        foreach (ECue cue in _soundTracker.Update(Session.Table, Session.Game.LocalPlayerIDs, ended, won))
            audio.Cue(SlotFor(cue));

        // "Your turn" only when more than one person plays (hot-seat or online); against bots it would be every turn.
        bool turns = Session.Seats.Count(s => !s.Bot) > 1;
        audio.Announcer.Update(Session.Table, Session.Game.LocalPlayerIDs, ended, won, now, turns);
        GameAudio.BattleContext = Context(ended, won);
    }
}
