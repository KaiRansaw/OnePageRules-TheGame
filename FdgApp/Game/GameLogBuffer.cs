using FDG;
using FDG.TextInterface;
using FDG.Players;

namespace FdgRaylib.Game;

/// <summary>
/// The battle report and chat for a front end that has no ImGui log: every line the engine sends, kept in
/// order with its colour, readable from the UI thread while the engine thread writes. Shared by any front end
/// (the Godot build reads it each frame).
/// </summary>
public sealed class GameLogBuffer : ILogMessageUI, IPlayerMessageUI
{
    public readonly record struct Line(string Text, TextColor Colour, bool Debug, bool Chat);

    private readonly List<Line> _lines = new();
    private readonly object _lock = new();

    /// <summary>Goes up by one for every line, so a reader can tell cheaply whether anything is new.</summary>
    public int Version { get; private set; }

    public event Action<string, EChatMessageType>? OnMessageSentByPlayer;

    public void DisplayLogMessage(string message, TextColor color) => Add(new Line(message, color, false, false));
    public void DisplayDebugMessage(string message, TextColor color) => Add(new Line(message, color, true, false));

    public void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) =>
        Add(new Line($"[{sendingPlayerName}] {message}", new TextColor(150, 220, 255, 255), false, true));

    /// <summary>Sends a chat line from this player to the others.</summary>
    public void SendChat(string message, EChatMessageType type = EChatMessageType.Global) => OnMessageSentByPlayer?.Invoke(message, type);

    private void Add(Line line)
    {
        lock (_lock)
        {
            _lines.Add(line);
            Version++;
        }
    }

    /// <summary>The last <paramref name="count"/> report lines (debug lines left out unless asked for).</summary>
    public IReadOnlyList<Line> Last(int count, bool includeDebug = false)
    {
        lock (_lock)
            return _lines.Where(l => includeDebug || !l.Debug).TakeLast(count).ToList();
    }

    public IReadOnlyList<Line> All(bool includeDebug = false)
    {
        lock (_lock)
            return _lines.Where(l => includeDebug || !l.Debug).ToList();
    }
}
