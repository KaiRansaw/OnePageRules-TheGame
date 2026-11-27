using Godot;
using OprGodot.Hud;

namespace OprGodot.Pads;

/// <summary>
/// The on-screen keyboard for a controller (offered to Kai 2026-11-24, built by lane 3 on 2026-11-27): A on a text box
/// (menu highlight) or a pointer click into one opens it. Keys are plain buttons, so the d-pad walks them and A types,
/// exactly like any other menu; B (or Cancel) closes it and leaves the box as it was; Done types the text in for real -
/// the box sees the same "text changed" and "submitted" a keyboard would give it, so every screen's own handling
/// (the player name saving, a search filtering, a spin box taking a number) runs unchanged.
/// </summary>
public partial class OnScreenKeyboard : PopupPanel
{
    private static readonly string[] Rows =
    {
        "1234567890",
        "qwertyuiop",
        "asdfghjkl'",
        "zxcvbnm,.-",
    };
    private const string Extra = "_:/@()+&!?";

    private LineEdit _target = null!;
    private LineEdit _preview = null!;
    private string _text = "";
    private bool _shift;
    private readonly List<Button> _letters = new();

    /// <summary>The keyboard open right now, if any (one at a time).</summary>
    public static OnScreenKeyboard? Open { get; private set; }

    /// <summary>Opens the keyboard for <paramref name="target"/> (no-op for a box that can't be typed in).</summary>
    public static void For(LineEdit target)
    {
        if (!target.Editable || !target.IsVisibleInTree() || Open != null && IsInstanceValid(Open)) return;
        var kb = new OnScreenKeyboard { Name = "OnScreenKeyboard", _target = target, _text = target.Text };
        target.GetTree().Root.AddChild(kb);
        Open = kb;
        kb.Build();
        kb.PopupCentered();
        kb.CallDeferred(nameof(FocusFirst));
    }

    private void Build()
    {
        Exclusive = true;
        // The keys are letters: they need no hover text (the hover audit skips what is marked so).
        SetMeta("no_hover_needed", true);
        var col = new VBoxContainer { Name = "Keys" };
        col.AddThemeConstantOverride("separation", 8);
        string what = _target.PlaceholderText is { Length: > 0 } p ? p
            : (_target.GetParent() as SpinBox)?.TooltipText is { Length: > 0 } st ? st
            : _target.TooltipText is { Length: > 0 } t ? t : "Type";
        var head = HudKit.Text(what, 16, HudKit.Dim, HudKit.Semi);
        head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.CustomMinimumSize = new Vector2(620, 0);
        col.AddChild(head);
        _preview = new LineEdit { Name = "KeyboardText", Text = _text, Editable = false, CustomMinimumSize = new Vector2(620, 44), FocusMode = Control.FocusModeEnum.None };
        col.AddChild(_preview);
        foreach (string row in Rows.Append(Extra))
        {
            var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            line.AddThemeConstantOverride("separation", 6);
            foreach (char ch in row)
            {
                var b = KeyButton(ch.ToString(), ch.ToString(), 56);
                b.Pressed += () => Type(char.IsLetter(ch) && _shift ? char.ToUpperInvariant(ch).ToString() : ch.ToString());
                if (char.IsLetter(ch)) _letters.Add(b);
                line.AddChild(b);
            }
            col.AddChild(line);
        }
        var last = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        last.AddThemeConstantOverride("separation", 6);
        var shift = KeyButton("Shift", "Shift", 110);
        shift.Pressed += ToggleShift;
        last.AddChild(shift);
        var space = KeyButton("Space", "Space", 220);
        space.Pressed += () => Type(" ");
        last.AddChild(space);
        var del = KeyButton("Delete", "Del", 100);
        del.TooltipText = "Remove the last letter.";
        del.Pressed += () => { if (_text.Length > 0) { _text = _text[..^1]; Show(); } };
        last.AddChild(del);
        var clear = KeyButton("Clear", "Clear", 100);
        clear.TooltipText = "Empty the box.";
        clear.Pressed += () => { _text = ""; Show(); };
        last.AddChild(clear);
        col.AddChild(last);
        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        bottom.AddThemeConstantOverride("separation", 10);
        var cancel = Ui.Button("Cancel", Hide, "Close the keyboard and leave the box as it was (B).", "BackButton");
        cancel.Name = "KeyboardCancel";
        Ring(cancel);
        bottom.AddChild(cancel);
        var done = Ui.Button("Done", Done, "Put the text in the box.", "CommitButton");
        done.Name = "KeyboardDone";
        Ring(done);
        done.CustomMinimumSize = new Vector2(160, 48);
        bottom.AddChild(done);
        col.AddChild(bottom);
        AddChild(HudKit.Pad(col, 18, 14, 18, 14));
        PopupHide += () => { if (Open == this) Open = null; QueueFree(); if (IsInstanceValid(_target)) _target.GrabFocus(); };
    }

    private static Button KeyButton(string name, string text, int width) => Ring(new Button
    {
        Name = "Key_" + name, Text = text, CustomMinimumSize = new Vector2(width, 50), FocusMode = Control.FocusModeEnum.All,
    });

    /// <summary>The pad's highlight outline is drawn on the main screen only, not inside a popup window - so each key
    /// draws its own bright ring while highlighted, or the player can't see which key A will type.</summary>
    private static Button Ring(Button b)
    {
        b.AddThemeStyleboxOverride("focus", new ChamferBox
        {
            Fill = Colors.Transparent, Border = HudKit.Lime, BorderWidth = 3, Cut = 6, Glow = HudKit.Lime with { A = 0.5f }, GlowWidth = 6,
        });
        return b;
    }

    private void FocusFirst()
    {
        if (FindChild("Key_q", true, false) is Button q) q.GrabFocus();
    }

    private void Type(string s)
    {
        if (_target.MaxLength > 0 && _text.Length >= _target.MaxLength) return;
        _text += s;
        if (_shift) ToggleShift();   // one capital, like a phone keyboard
        Show();
    }

    private void ToggleShift()
    {
        _shift = !_shift;
        foreach (Button b in _letters) b.Text = _shift ? b.Text.ToUpperInvariant() : b.Text.ToLowerInvariant();
    }

    private void Show() => _preview.Text = _text;

    /// <summary>Types the text into the box for real: the box changes, then is submitted, as with a keyboard and Enter.</summary>
    private void Done()
    {
        if (IsInstanceValid(_target))
        {
            _target.Text = _text;
            _target.CaretColumn = _text.Length;
            _target.EmitSignal(LineEdit.SignalName.TextChanged, _text);
            _target.EmitSignal(LineEdit.SignalName.TextSubmitted, _text);
        }
        Hide();
    }

    /// <summary>For the test driver: types <paramref name="text"/> and presses Done.</summary>
    public void DriveType(string text)
    {
        foreach (char ch in text) Type(ch.ToString());
        Done();
    }
}
