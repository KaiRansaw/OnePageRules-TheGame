using OprGodot.Forge;
using FdgRaylib.Game;
using FdgRaylib.Lobby;
using Godot;
using OprGodot.Hud;

namespace OprGodot;

/// <summary>
/// The loading screen as a battle opens (Kai, 2026-11-16, "Menu Art &amp; Themes Mockup": yes): your army's picture on the
/// left, theirs on the right, VS in the middle, names, players and points, and what is being played. It goes up a frame
/// before the battle table starts loading (which can take a while with real 3D models) and comes down once the table
/// has drawn. Sides are teams: the first seat's team on the left.
/// </summary>
public partial class VersusScreen : CanvasLayer
{
    /// <summary>Shows the screen, then runs <paramref name="open"/> (which builds the table), then takes the screen away.</summary>
    public static async void Show(SceneTree tree, GameSession session, LobbySession lobby, Action open)
    {
        bool scripted = ScriptedRun.IsRequested;
        bool keep = OS.GetCmdlineUserArgs().Contains("--show-versus");
        if (scripted && !keep) { open(); return; }
        var screen = new VersusScreen { Layer = 60 };
        screen.Build(session, lobby);
        tree.Root.AddChild(screen);
        // Two frames so it is on screen before the table's loading holds everything up.
        await screen.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await screen.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        open();
        if (keep) return;
        await screen.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await screen.ToSignal(tree.CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        if (!IsInstanceValid(screen)) return;
        var root = screen.GetChild<Control>(0);
        var t = screen.CreateTween();
        t.TweenProperty(root, "modulate:a", 0f, 0.35f);
        t.TweenCallback(Callable.From(screen.QueueFree));
    }

    private void Build(GameSession session, LobbySession lobby)
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        root.AddChild(new ColorRect { Color = Palette.Ink }.Also(c => c.SetAnchorsPreset(Control.LayoutPreset.FullRect)));

        var seats = session.Seats;
        int leftTeam = seats.Count > 0 ? seats[0].Team : 0;
        var left = seats.Where(s => s.Team == leftTeam).ToList();
        var right = seats.Where(s => s.Team != leftTeam).ToList();
        var halves = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        halves.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        halves.AddThemeConstantOverride("separation", 4);
        root.AddChild(halves);
        halves.AddChild(Half(left, lobby, true));
        halves.AddChild(Half(right, lobby, false));

        var vs = HudKit.Text("VS", 150, HudKit.Lime, HudKit.Exo(800), HorizontalAlignment.Center);
        vs.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        vs.AddThemeConstantOverride("shadow_offset_y", 6);
        vs.SetAnchorsPreset(Control.LayoutPreset.Center);
        vs.GrowHorizontal = Control.GrowDirection.Both; vs.GrowVertical = Control.GrowDirection.Both;
        vs.OffsetTop = -160;
        root.AddChild(vs);

        var bottom = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bottom.AddThemeConstantOverride("separation", 8);
        bottom.AddChild(HudKit.Text(lobby.HeaderTitle, 26, HudKit.Fg, HudKit.Bold, HorizontalAlignment.Center));
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(760, 10), ShowPercentage = false, Indeterminate = true };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f), BorderColor = HudKit.Edge, BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HudKit.Lime });
        bottom.AddChild(bar);
        bottom.AddChild(HudKit.Text("Setting up the table...", 18, HudKit.Dim, HudKit.Semi, HorizontalAlignment.Center));
        HudKit.Place(root, bottom, Control.LayoutPreset.CenterBottom, new Vector2(0, -60));
    }

    private static Control Half(List<GameSession.Seat> side, LobbySession lobby, bool left)
    {
        var holder = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        var system = lobby.Vm.GameSystem;
        string army = side.FirstOrDefault()?.Faction ?? "";
        Control art = army.Length > 0
            ? MenuArt.View(FdgRaylib.Pictures.MenuPictures.Army(system, army), system)
            : MenuArt.Fallback(Palette.AccentDim);
        art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(art);
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { Palette.Ink with { A = 0.1f }, Palette.Ink with { A = 0.95f } }, Offsets = new[] { 0.4f, 0.88f } },
                FillTo = new Vector2(0, 1), Width = 8, Height = 256,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(shade);

        var words = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        words.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        words.OffsetBottom = -190;
        words.OffsetLeft = 60; words.OffsetRight = -60;
        words.AddThemeConstantOverride("separation", 2);
        var align = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        string armies = string.Join(" + ", side.Select(s => s.Faction).Where(f => f.Length > 0).Distinct());
        var name = HudKit.Text(armies.Length > 0 ? armies : "-", 64, HudKit.Fg, HudKit.Exo(800), align);
        name.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        name.AddThemeConstantOverride("shadow_offset_y", 3);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        words.AddChild(name);
        foreach (var s in side)
        {
            var info = lobby.Vm.PlayerInfos.FirstOrDefault(p => p.PlayerID.Equals(s.Id));
            string pts = info != null && info.ArmyListSummary.IsAssigned ? $"  -  {info.ArmyListSummary.PointCost} pts" : "";
            words.AddChild(HudKit.Text(s.Name + (s.Bot ? " (bot)" : "") + pts, 24, HudKit.Lime, HudKit.Semi, align));
        }
        holder.AddChild(words);
        return holder;
    }
}
