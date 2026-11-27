using System;
using Godot;
using FdgRaylib.Game;
using OprGodot.Hud;

namespace OprGodot.Battle;

/// <summary>
/// Battle replays (homebrew, Kai 2026-11-27): the replay bar across the top-middle of the battle screen when a replay is
/// being watched - step back / forward an activation, back / forward a round, play / pause, speed, and where we are.
/// The rest of the screen is its ordinary watch-only mode (no seat, no Actions panel).
/// </summary>
public partial class BattleScreen
{
    private Label? _replayWhere;
    private Button? _replayPlay;
    private bool _replayPlaying;
    private double _replayClock;
    private int _replaySpeed = 1;

    /// <summary>Seconds each activation stays on the table while playing, at 1x.</summary>
    private const double ReplaySecondsPerFrame = 2.5;

    private void BuildReplayBar()
    {
        if (Session.Watching is not { } replay) return;
        var row = new HBoxContainer { Name = "ReplayBar" };
        row.AddThemeConstantOverride("separation", 8);
        Button B(string name, string text, string tip, Action go)
        {
            var b = Ui.Button(text, go, tip);
            b.Name = name;
            b.CustomMinimumSize = new Vector2(48, 40);
            row.AddChild(b);
            return b;
        }
        B("ReplayRoundBack", "|<", "Back to the start of the round (or the round before).", () => ReplayJump(() => replay.PreviousRound()));
        B("ReplayBack", "<", "One activation back (,).", () => ReplayStep(-1));
        _replayPlay = B("ReplayPlay", "Play", "Play the replay, an activation at a time (P).", ReplayPlayPause);
        _replayPlay.CustomMinimumSize = new Vector2(90, 40);
        B("ReplayForward", ">", "One activation forward (.).", () => ReplayStep(+1));
        B("ReplayRoundForward", ">|", "On to the next round.", () => ReplayJump(() => replay.NextRound()));
        var speed = B("ReplaySpeed", "1x", "How fast Play goes: 1x, 2x or 4x.", () => { });
        speed.Pressed += () => { _replaySpeed = _replaySpeed >= 4 ? 1 : _replaySpeed * 2; speed.Text = $"{_replaySpeed}x"; };
        _replayWhere = HudKit.Text(replay.Where(), 15, HudKit.Fg, HudKit.Bold);
        _replayWhere.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _replayWhere.CustomMinimumSize = new Vector2(320, 0);
        row.AddChild(_replayWhere);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(HudKit.Text("REPLAY - " + replay.Manifest.Title, 13, HudKit.Lime, HudKit.Bold));
        box.AddChild(row);
        var panel = HudKit.Panel(HudKit.PanelBox(12).With(x => { x.ContentMarginLeft = 12; x.ContentMarginRight = 12; x.ContentMarginTop = 6; x.ContentMarginBottom = 8; }), box);
        HudKit.Place(_hud, panel, LayoutPreset.CenterTop, new Vector2(0, 64));
        replay.Changed += () => CallDeferred(nameof(RefreshReplayBar));
        RefreshReplayBar();
    }

    /// <summary>The Game Over card's "Watch replay": this game, from its first activation.</summary>
    private Button ReplayButton()
    {
        string? path = Session.ReplayPath;
        var b = Ui.Button("Watch replay", () => { if (path != null) { App.I.ToMainMenu(); App.I.Later(() => OpenReplay(path)); } },
            path != null ? "Watch this game again from the first activation - every move and roll as it happened. It is also kept in Load Game -> Replays."
                : Session.Lobby.Vm.HasHostPrivileges ? "This game has no replay (nothing was recorded)." : "The host keeps the replay - ask them to send you the file.");
        b.Name = "WatchReplay";
        b.Disabled = path == null;
        return b;
    }

    /// <summary>Opens a replay file on the battle screen (Load Game -> Replays, or the Game Over card).</summary>
    public static void OpenReplay(string path)
    {
        var replay = FdgRaylib.Game.ReplayPlayer.Open(path, out string error);
        if (replay == null) { App.I.Message("Can't watch that replay", error); return; }
        GameSession session;
        try { session = GameSession.ForReplay(replay); }
        catch (Exception e)
        {
            GD.PrintErr($"Replay: {e}");
            replay.Dispose();
            App.I.Message("Can't watch that replay", "It couldn't be set up on the table.");
            return;
        }
        App.I.Open<BattleScreen>(Scenes.Battle, s => s.Session = session);
    }

    private void RefreshReplayBar()
    {
        if (Session.Watching is not { } replay) return;
        if (_replayWhere != null) _replayWhere.Text = replay.AtEnd ? replay.Where() + " - end: " + replay.Manifest.Result : replay.Where();
        if (_replayPlay != null) _replayPlay.Text = _replayPlaying ? "Pause" : "Play";
    }

    private void ReplayPlayPause()
    {
        if (Session.Watching is not { } replay) return;
        if (!_replayPlaying && replay.AtEnd) replay.Show(0);
        _replayPlaying = !_replayPlaying;
        _replayClock = 0;
        RefreshReplayBar();
    }

    private void ReplayStep(int by)
    {
        if (Session.Watching is not { } replay) return;
        _replayPlaying = false;
        bool moved = by > 0 ? replay.Next() : replay.Previous();
        if (!moved) Toast(by > 0 ? "That's the end of the game." : "That's the first activation.");
        RefreshReplayBar();
    }

    private void ReplayJump(Func<bool> go)
    {
        _replayPlaying = false;
        go();
        RefreshReplayBar();
    }

    private void ReplayTick(double delta)
    {
        if (!_replayPlaying || Session.Watching is not { } replay) return;
        _replayClock += delta * _replaySpeed;
        if (_replayClock < ReplaySecondsPerFrame) return;
        _replayClock = 0;
        if (!replay.Next())
        {
            _replayPlaying = false;
            RefreshReplayBar();
        }
    }
}
