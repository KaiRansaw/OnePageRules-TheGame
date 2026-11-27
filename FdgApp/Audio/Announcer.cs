using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;

namespace FdgRaylib.Audio;

/// <summary>One thing the announcer could say: the slots to try in order (the first with a sound is spoken).</summary>
public sealed record AnnouncerLine(IReadOnlyList<string> Slots, long QueuedMs, bool Final = false)
{
    public string Key => Slots.Count > 0 ? Slots[0] : "";
}

/// <summary>
/// The announcer (2026-11-27): decides WHEN a voice line is due - a round starting, the last round, objectives taken
/// or lost, units wiped out, a hero down, a Horde wave, the result - and keeps them in a short queue so lines never talk
/// over each other and a line that waited too long is dropped instead of arriving late. The Godot build speaks the line
/// on the Announcer channel and dips the music while it does. Every line is a <see cref="SoundSlot"/>, so with no voice
/// files the announcer is simply quiet.
/// </summary>
public sealed class Announcer
{
    /// <summary>A line that has waited this long is stale and dropped.</summary>
    public const int StaleMs = 5000;
    /// <summary>At most this many lines wait at once.</summary>
    public const int MaxQueued = 4;

    private readonly List<AnnouncerLine> _queue = new();
    private int _round = -1;
    private readonly Dictionary<string, string?> _owners = new();
    private readonly Dictionary<UnitID, PlayerID> _standing = new();
    private string? _lastActivator;
    private bool _ended;
    private bool _primed;

    /// <summary>Lines waiting (oldest first) - for tests and the test driver.</summary>
    public IReadOnlyList<AnnouncerLine> Queued => _queue;

    /// <summary>Add a line. The same line twice in the queue is kept once; the end-of-game line clears everything else.</summary>
    public void Say(long nowMs, bool final, params string[] slots)
    {
        if (slots.Length == 0) return;
        var line = new AnnouncerLine(slots, nowMs, final);
        if (final) _queue.Clear();
        else if (_queue.Any(l => l.Final)) return;
        if (_queue.Any(l => l.Key == line.Key)) return;
        if (_queue.Count >= MaxQueued) _queue.RemoveAt(0);
        _queue.Add(line);
    }

    /// <summary>The next line to speak when the voice is free, or null. Stale lines are dropped (the result never is).</summary>
    public AnnouncerLine? Next(long nowMs, bool speaking)
    {
        _queue.RemoveAll(l => !l.Final && nowMs - l.QueuedMs > StaleMs);
        if (speaking || _queue.Count == 0) return null;
        AnnouncerLine next = _queue[0];
        _queue.RemoveAt(0);
        return next;
    }

    /// <summary>Lines from an engine beat: a unit routing, and the headline banners (deployment, Horde waves, a Horde
    /// objective destroyed, Prolonged Battle). Matches the engine's own banner text.</summary>
    public void OnBeat(PresentationBeat beat, long nowMs)
    {
        switch (beat)
        {
            case UnitRoutedBeat:
                Say(nowMs, false, "announcer/unit-routed");
                break;
            case BannerBeat b:
                string t = b.BannerText ?? "";
                if (t == "Deployment") Say(nowMs, false, "announcer/deployment");
                else if (t.StartsWith("Wave ", StringComparison.Ordinal) && (t.EndsWith(" incoming!", StringComparison.Ordinal) || t.EndsWith(" arrives!", StringComparison.Ordinal)))
                    Say(nowMs, false, "announcer/wave-incoming");
                else if (t.StartsWith("Objective ", StringComparison.Ordinal) && t.EndsWith(" destroyed!", StringComparison.Ordinal))
                    Say(nowMs, false, "announcer/objective-destroyed");
                else if (t.StartsWith("Prolonged Battle: the fight goes on", StringComparison.Ordinal))
                    Say(nowMs, false, "announcer/prolonged-battle");
                break;
        }
    }

    /// <summary>
    /// Look at the table and queue whatever changed. <paramref name="mine"/> = the players this computer plays (empty when
    /// watching bots). <paramref name="turns"/>: say "your turn" when play passes to this computer (only worth it with
    /// more than one human). <paramref name="result"/> as in <see cref="FdgRaylib.Game.GameCues"/>: true won, false lost,
    /// null draw / bots.
    /// </summary>
    public void Update(ITableState table, IReadOnlyCollection<PlayerID> mine, bool ended, bool? result, long nowMs, bool turns = false)
    {
        bool Mine(string? id) => id != null && mine.Any(p => p.ID.ToString() == id);
        IGameProgress? progress = table.Progress;
        bool quest = progress != null && MusicDirector.IsQuest(progress.GameSystem);

        int round = progress?.RoundCount ?? 0;
        if (_round >= 0 && round > _round && round >= 1)
        {
            int total = progress?.TotalRounds ?? 0;
            string numbered = $"announcer/round-{round}";
            if (round == 1) Say(nowMs, false, "announcer/battle-start", numbered);
            else if (total > 0 && round == total) Say(nowMs, false, "announcer/final-round", numbered);
            else Say(nowMs, false, numbered);
        }
        _round = round;

        foreach (IObjective o in table.Objectives.Objects)
        {
            string key = $"{o.Position.x:0.0},{o.Position.z:0.0}";
            string? owner = o.OwnerID?.ID.ToString();
            if (_primed && _owners.TryGetValue(key, out string? was) && was != owner)
            {
                if (Mine(owner)) Say(nowMs, false, "announcer/objective-seized");
                else if (Mine(was)) Say(nowMs, false, "announcer/objective-lost");
            }
            _owners[key] = owner;
        }

        foreach (IUnit u in table.Units.Objects)
        {
            bool standing = u.Models.Any(m => m.GetIsAlive());
            if (standing) { _standing[u.ID] = u.PlayerID; continue; }
            if (!_standing.Remove(u.ID, out PlayerID owner) || !_primed) continue;
            if (Mine(owner.ID.ToString())) Say(nowMs, false, quest ? "announcer/hero-down" : "announcer/unit-lost");
            else if (mine.Count > 0) Say(nowMs, false, "announcer/enemy-unit-destroyed");
        }

        string? activator = progress?.ActivatingUnit?.PlayerID.ID.ToString();
        if (turns && _primed && activator != null && activator != _lastActivator && Mine(activator) && !Mine(_lastActivator))
            Say(nowMs, false, "announcer/your-turn");
        if (activator != null) _lastActivator = activator;

        if (ended && !_ended && (result != null || mine.Count > 0))
            Say(nowMs, true, result switch { true => "announcer/victory", false => "announcer/defeat", _ => "announcer/draw" });
        _ended = ended;
        _primed = true;
    }
}
