using Godot;

namespace OprGodot.Hud;

/// <summary>
/// Keeps every scrolling list where the player left it (Kai, 2026-11-26: "whenever a unit is selected in Army Builder,
/// the scrollable menu resets to the top ... ALL scrollable menus with selectable options have had this problem").
///
/// Most screens rebuild their rows after a click - some clear a list in place, some build a whole new ScrollContainer -
/// and either way the list jumps back to the top. Rather than patch every screen, this watches every vertical scroll
/// bar in the game (ScrollContainer, ItemList, Tree, text boxes all own one):
///  - a bar that drops to the top without the player scrolling (wheel, drag, keys, controller) is put back where it
///    was once its list has refilled;
///  - a bar that is thrown away and replaced by a new one in the same place (same node path) within a second takes
///    over the old one's position.
/// Either only when the list looks like the same list (about the same number of rows), so switching to a different
/// list or tab still starts at the top. A list that scrolls itself on purpose (the battle report jumping to its
/// newest line) sets the meta <see cref="Skip"/> on its owner.
/// </summary>
public partial class ScrollKeeper : Node
{
    /// <summary>Meta on a scroll owner (ScrollContainer, ItemList...): never restore its position.</summary>
    public const string Skip = "no_scroll_keep";

    private const ulong IntentMs = 300, RefillMs = 1200, ReplaceMs = 1000;

    private sealed class Entry
    {
        public required VScrollBar Bar;
        public required string Key;
        public double LastGood;
        public int LastSig = -1;
        public double Pending;      // > 0: waiting to put the bar back here
        public int PendingSig = -1;
        public ulong PendingUntil;
        public ulong TrackedMs;
    }

    private readonly Dictionary<ulong, Entry> _live = new();
    private readonly Dictionary<string, (double Value, int Sig, ulong Ms)> _gone = new();
    private ulong _intentMs;

    public static void Attach(Node app) => app.AddChild(new ScrollKeeper { Name = "ScrollKeeper" });

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree()
    {
        if (GetTree() is { } t) t.NodeAdded -= OnNodeAdded;
    }

    // Anything that is the player scrolling on purpose: then a jump to the top is theirs, not a rebuild's.
    public override void _Input(InputEvent e)
    {
        bool intent = e switch
        {
            InputEventMouseButton mb => mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight,
            InputEventMouseMotion mm => mm.ButtonMask != 0,
            InputEventKey k => k.Pressed,
            InputEventJoypadButton or InputEventJoypadMotion => true,
            InputEventScreenDrag => true,
            _ => false,
        };
        if (intent) _intentMs = Time.GetTicksMsec();
    }

    private void OnNodeAdded(Node n)
    {
        if (n is not VScrollBar bar || _live.ContainsKey(bar.GetInstanceId())) return;
        // Wait a frame: the path (and so the key) is only final once the whole new branch is in the tree.
        Callable.From(() => Track(bar)).CallDeferred();
    }

    private void Track(VScrollBar bar)
    {
        if (!IsInstanceValid(bar) || !bar.IsInsideTree() || _live.ContainsKey(bar.GetInstanceId())) return;
        var e = new Entry { Bar = bar, Key = KeyOf(bar), TrackedMs = Time.GetTicksMsec() };
        _live[bar.GetInstanceId()] = e;
        ulong id = bar.GetInstanceId();
        bar.TreeExiting += () => Gone(id);
        ulong now = Time.GetTicksMsec();
        // A new list in the same place as one just thrown away: take over its position.
        if (_gone.Remove(e.Key, out var old) && now - old.Ms < ReplaceMs && old.Value > 0)
        {
            e.Pending = old.Value;
            e.PendingSig = old.Sig;
            e.PendingUntil = now + RefillMs;
        }
    }

    private void Gone(ulong id)
    {
        if (!_live.Remove(id, out var e)) return;
        double v = e.Pending > 0 ? e.Pending : e.LastGood;
        int sig = e.Pending > 0 ? e.PendingSig : e.LastSig;
        if (v <= 0) return;
        ulong now = Time.GetTicksMsec();
        // The new list may already be here (built before the old one was freed): hand the position straight over.
        Entry? twin = _live.Values.FirstOrDefault(x => x.Key == e.Key && now - x.TrackedMs < ReplaceMs && x.LastGood <= 0 && x.Pending <= 0
                                                       && IsInstanceValid(x.Bar) && x.Bar.Value < 0.5);
        if (twin != null) { twin.Pending = v; twin.PendingSig = sig; twin.PendingUntil = now + RefillMs; return; }
        _gone[e.Key] = (v, sig, now);
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        bool scrolling = now - _intentMs < IntentMs;
        foreach (Entry e in _live.Values)
        {
            VScrollBar bar = e.Bar;
            if (!IsInstanceValid(bar) || !bar.IsInsideTree()) continue;
            Node? owner = bar.GetParent();
            if (owner == null || owner.HasMeta(Skip)) { e.Pending = 0; e.LastGood = 0; continue; }
            double v = bar.Value;

            if (e.Pending > 0)
            {
                if (scrolling || now > e.PendingUntil || v > 0.5) { e.Pending = 0; }
                else if (bar.MaxValue - bar.Page >= e.Pending - 0.5)
                {
                    // Refilled: put it back, if it is still the same list.
                    int sig = Sig(owner);
                    if (e.PendingSig < 0 || sig < 0 || Math.Abs(sig - e.PendingSig) <= 2) Restore(owner, bar, e.Pending);
                    e.Pending = 0;
                }
                continue;
            }

            if (v > 0.5) { e.LastGood = v; e.LastSig = Sig(owner); continue; }
            if (e.LastGood > 0 && !scrolling)
            {
                // Jumped to the top with nobody scrolling: a rebuild. Wait for the rows to come back.
                e.Pending = e.LastGood;
                e.PendingSig = e.LastSig;
                e.PendingUntil = now + RefillMs;
            }
            e.LastGood = 0;
        }
        if (_gone.Count > 0)
            foreach (string k in _gone.Where(g => now - g.Value.Ms > ReplaceMs * 2).Select(g => g.Key).ToList()) _gone.Remove(k);
    }

    private static void Restore(Node owner, VScrollBar bar, double v)
    {
        if (owner is ScrollContainer sc) sc.ScrollVertical = (int)Math.Round(v);
        else bar.Value = v;
    }

    /// <summary>Roughly how many rows the list has - to tell "the same list rebuilt" from "a different list".</summary>
    private static int Sig(Node owner)
    {
        switch (owner)
        {
            case ItemList il: return il.ItemCount;
            case Tree t: return t.GetRoot() is { } r ? Count(r) : 0;
            case ScrollContainer sc:
                Node? content = sc.GetChildren().FirstOrDefault(c => c is Control and not ScrollBar);
                // Walk down single wrappers (a margin round a column of rows...).
                while (content != null && content.GetChildCount() == 1 && content.GetChild(0) is Control) content = content.GetChild(0);
                return content?.GetChildCount() ?? 0;
            default: return -1;
        }
        static int Count(TreeItem i)
        {
            int n = 1;
            for (TreeItem? c = i.GetFirstChild(); c != null; c = c.GetNext()) n += Count(c);
            return n;
        }
    }

    /// <summary>The bar's place in the scene, with Godot's made-up names ("@VBoxContainer@123") swapped for type + position.</summary>
    private static string KeyOf(Node n)
    {
        var parts = new List<string>();
        for (Node? x = n; x != null; x = x.GetParent())
        {
            string name = x.Name;
            if (name.StartsWith('@'))
            {
                int i = 0;
                if (x.GetParent() is { } p)
                    foreach (Node sib in p.GetChildren(true)) { if (sib == x) break; if (sib.GetClass() == x.GetClass()) i++; }
                name = x.GetClass() + "#" + i;
            }
            parts.Add(name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
