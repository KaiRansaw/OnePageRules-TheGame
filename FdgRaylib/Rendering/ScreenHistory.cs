namespace FdgRaylib.Rendering;

/// <summary>
/// The Back history (Kai, 2026-11-07: "Every Back button goes to the previous screen, never straight to
/// the main menu"). Pure bookkeeping - the renderer does the actual switching.
/// </summary>
public sealed class ScreenHistory
{
    private readonly List<IAppScreen> _stack = new();
    private readonly IAppScreen _home;

    /// <param name="home">Where Back ends up when there is nothing left (the main menu).</param>
    public ScreenHistory(IAppScreen home) => _home = home;

    /// <summary>The screen Back would go to now, or null for <c>home</c>.</summary>
    public IAppScreen? Previous => _stack.Count > 0 ? _stack[^1] : null;

    public int Count => _stack.Count;

    /// <summary>Leaving <paramref name="from"/> for <paramref name="to"/>: remember where to come back to.</summary>
    public void Forward(IAppScreen from, IAppScreen to)
    {
        if (!ReferenceEquals(from, to)) _stack.Add(from);
    }

    /// <summary>Back: the screen to go to (and forget).</summary>
    public IAppScreen Back()
    {
        if (_stack.Count == 0) return _home;
        IAppScreen back = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return back;
    }

    /// <summary>
    /// Arriving at <paramref name="screen"/> by a jump (after a game, Load Game, Join): everything opened
    /// after it is dropped. A screen that is not in the history starts a fresh one with just home under it.
    /// </summary>
    public void Unwind(IAppScreen screen)
    {
        int at = _stack.FindLastIndex(s => ReferenceEquals(s, screen));
        if (at >= 0) _stack.RemoveRange(at, _stack.Count - at);
        else
        {
            _stack.Clear();
            if (!ReferenceEquals(screen, _home)) _stack.Add(_home);
        }
    }
}
