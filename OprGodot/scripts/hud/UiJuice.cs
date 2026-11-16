using FdgRaylib.Config;
using Godot;

namespace OprGodot.Hud;

/// <summary>
/// The feel of the menus (Kai, 2026-11-15: "menus/UI look and feel much better"): buttons that lift and brighten
/// under the mouse or the keyboard/controller cursor, dip when pressed and spring back, a small sound for each, and
/// screens that fade and slide in and out. One place, so screens opt in with one line (<see cref="Attach"/>)
/// instead of copying tween code around.
///
/// <para>Everything is a Tween made in code (Godot 4's <c>CreateTween</c>), short (0.06 - 0.25 s) and eased, so it
/// never slows a click down. A button's look itself (colours, borders) stays in <see cref="HudTheme"/>; this only
/// moves and brightens it.</para>
/// </summary>
public static class UiJuice
{
    /// <summary>True after a key or controller press, false after the mouse moves or clicks. The focus ring only
    /// shows in keyboard mode, so clicking a button with the mouse doesn't leave an outline on it.</summary>
    public static bool KeyboardMode { get; private set; }

    private const float HoverScale = 1.035f, PressScale = 0.965f;
    private static readonly Color Bright = new(1.14f, 1.14f, 1.14f);
    private const string TweenMeta = "juice_tween", DoneMeta = "juiced";

    /// <summary>Called by App for every input event, before any control sees it.</summary>
    public static void NoteInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } or InputEventJoypadButton { Pressed: true } or InputEventJoypadMotion)
            KeyboardMode = true;
        else if (e is InputEventMouseButton { Pressed: true } || (e is InputEventMouseMotion m && m.Relative.LengthSquared() > 4))
            KeyboardMode = false;
    }

    /// <summary>Gives every button under <paramref name="root"/> the feel - and any button added there later
    /// (screens that rebuild a row keep it). <paramref name="focusable"/> lets the keyboard/controller reach buttons
    /// that were made mouse-only.</summary>
    public static void Attach(Control root, bool focusable = true)
    {
        Walk(root, focusable);
        void Added(Node n)
        {
            if (n is BaseButton b && root.IsAncestorOf(b)) Juice(b, focusable);
        }
        SceneTree tree = root.GetTree();
        tree.NodeAdded += Added;
        root.TreeExiting += () => tree.NodeAdded -= Added;
    }

    private static void Walk(Node n, bool focusable)
    {
        if (n is BaseButton b) Juice(b, focusable);
        foreach (Node c in n.GetChildren()) Walk(c, focusable);
    }

    /// <summary>One button's lift / dip / sound.</summary>
    public static void Juice(BaseButton b, bool focusable = true)
    {
        if (b.HasMeta(DoneMeta)) return;
        b.SetMeta(DoneMeta, true);
        if (focusable && !b.Disabled) b.FocusMode = Control.FocusModeEnum.All;

        // Scale grows from the middle, not the top-left corner.
        void Centre() => b.PivotOffset = b.Size / 2;
        b.Resized += Centre;
        Centre();

        b.MouseEntered += () =>
        {
            if (b.Disabled) return;
            To(b, HoverScale, Bright, 0.12f, Tween.TransitionType.Quad);
            UiSound.Hover();
        };
        b.MouseExited += () =>
        {
            if (KeyboardMode && b.HasFocus()) return;
            To(b, 1f, Colors.White, 0.15f, Tween.TransitionType.Quad);
        };
        b.FocusEntered += () =>
        {
            if (!KeyboardMode) return;
            Ring(b, true);
            To(b, HoverScale, Bright, 0.12f, Tween.TransitionType.Quad);
            UiSound.Hover();
        };
        b.FocusExited += () =>
        {
            Ring(b, false);
            if (!b.IsHovered()) To(b, 1f, Colors.White, 0.15f, Tween.TransitionType.Quad);
        };
        b.ButtonDown += () => To(b, PressScale, Bright, 0.06f, Tween.TransitionType.Quad);
        // Spring back with a touch of overshoot (TRANS_BACK) - the "snap" that makes a press feel solid.
        b.ButtonUp += () => To(b, b.IsHovered() || b.HasFocus() ? HoverScale : 1f, b.IsHovered() ? Bright : Colors.White, 0.22f, Tween.TransitionType.Back);
        b.Pressed += UiSound.Click;
        if (b is BaseButton { ToggleMode: true }) b.Toggled += _ => UiSound.Click();
    }

    private static void To(Control c, float scale, Color modulate, float seconds, Tween.TransitionType trans)
    {
        if (!c.IsInsideTree()) return;
        if (c.HasMeta(TweenMeta) && c.GetMeta(TweenMeta).AsGodotObject() is Tween old && old.IsValid()) old.Kill();
        Tween t = c.CreateTween().SetParallel().SetTrans(trans).SetEase(Tween.EaseType.Out);
        t.TweenProperty(c, "scale", new Vector2(scale, scale), seconds);
        t.TweenProperty(c, "modulate", modulate, seconds);
        c.SetMeta(TweenMeta, t);
    }

    /// <summary>The keyboard/controller cursor: a lime outline in the button's own corner cut.</summary>
    private static void Ring(BaseButton b, bool on)
    {
        if (!on) { b.RemoveThemeStyleboxOverride("focus"); return; }
        float cut = b.GetThemeStylebox("normal") is ChamferBox box ? box.Cut : 8f;
        b.AddThemeStyleboxOverride("focus", new ChamferBox
        {
            Fill = Colors.Transparent, Border = HudKit.Lime, BorderWidth = 2, Cut = cut, Glow = HudKit.Lime with { A = 0.35f },
        });
    }

    // ================================================================== Screens

    /// <summary>A screen arriving: fades in and slides up a little.</summary>
    public static void ScreenIn(Control screen, float slide = 24f)
    {
        screen.Modulate = Colors.Transparent;
        Vector2 home = screen.Position;
        screen.Position = home + new Vector2(0, slide);
        Tween t = screen.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenProperty(screen, "modulate", Colors.White, 0.2f);
        t.TweenProperty(screen, "position", home, 0.25f);
    }

    /// <summary>A screen leaving: fades out and drops a little, then <paramref name="done"/> (usually QueueFree).
    /// Clicks are ignored while it goes, so nothing on it can be pressed twice.</summary>
    public static void ScreenOut(Control screen, Action done)
    {
        screen.MouseFilter = Control.MouseFilterEnum.Ignore;
        screen.SetProcessInput(false);
        foreach (Node n in screen.FindChildren("*", "BaseButton", true, false)) ((BaseButton)n).Disabled = true;
        Tween t = screen.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.TweenProperty(screen, "modulate", Colors.Transparent, 0.14f);
        t.TweenProperty(screen, "position", screen.Position + new Vector2(0, 16), 0.14f);
        t.Chain().TweenCallback(Callable.From(done));
    }

    /// <summary>A screen's pieces arriving one after another (the main menu's title, tiles, tools): each fades in and
    /// slides from the left, <paramref name="step"/> seconds apart.</summary>
    public static void Stagger(IEnumerable<Control> pieces, float step = 0.06f, float slide = -40f)
    {
        int i = 0;
        foreach (Control c in pieces)
        {
            float delay = i++ * step;
            Vector2 home = c.Position;
            c.Modulate = Colors.Transparent;
            c.Position = home + new Vector2(slide, 0);
            Tween t = c.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.TweenProperty(c, "modulate", Colors.White, 0.22f).SetDelay(delay);
            t.TweenProperty(c, "position", home, 0.3f).SetDelay(delay).SetTrans(Tween.TransitionType.Back);
        }
    }
}

/// <summary>
/// The menu sounds: a soft tick on hover/focus and a firmer click on press. Made from sine waves at start-up rather
/// than sound files, so there is nothing to license or ship. Off with Settings -> Sound -> Menu sounds; volume follows
/// Master volume (the Master bus).
/// </summary>
public static class UiSound
{
    private static AudioStreamPlayer? _hover, _click;
    private static ulong _lastHoverMs;

    public static void Install(Node owner)
    {
        _hover = new AudioStreamPlayer { Stream = Tone(1900, 0.028f, 0.16f), VolumeDb = -16, MaxPolyphony = 2 };
        _click = new AudioStreamPlayer { Stream = ClickSound(), VolumeDb = -9, MaxPolyphony = 3 };
        owner.AddChild(_hover);
        owner.AddChild(_click);
    }

    private static bool On => UserConfig.Current.View.MenuSounds;

    public static void Hover()
    {
        // Sweeping the mouse along a row shouldn't buzz.
        ulong now = Time.GetTicksMsec();
        if (!On || _hover == null || now - _lastHoverMs < 45) return;
        _lastHoverMs = now;
        _hover.Play();
    }

    public static void Click()
    {
        if (On && _click != null) _click.Play();
    }

    private const int Rate = 44100;

    /// <summary>A short sine blip with a fast fade-out.</summary>
    private static AudioStreamWav Tone(float hz, float seconds, float level) => Wav(Samples(seconds, (i, t) =>
        Mathf.Sin(Mathf.Tau * hz * t) * level * Mathf.Exp(-t * 90f)));

    /// <summary>Two quick tones falling in pitch - a "tock".</summary>
    private static AudioStreamWav ClickSound() => Wav(Samples(0.07f, (i, t) =>
    {
        float hz = Mathf.Lerp(1300f, 620f, Mathf.Min(1f, t / 0.04f));
        return Mathf.Sin(Mathf.Tau * hz * t) * 0.32f * Mathf.Exp(-t * 55f);
    }));

    private static float[] Samples(float seconds, Func<int, float, float> f)
    {
        var s = new float[(int)(Rate * seconds)];
        for (int i = 0; i < s.Length; i++) s[i] = f(i, i / (float)Rate);
        return s;
    }

    private static AudioStreamWav Wav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(v & 0xff);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
    }
}
