using FdgRaylib.Config;
using FdgRaylib.Lobby;
using Godot;

namespace OprGodot;

/// <summary>
/// Join a Game (Kai, 2026-11-07, decision 16): not a screen, a small popup with one box for the host's
/// Tailscale address - remembered from last time - and JOIN / Cancel. No servers, ports or passwords.
/// JOIN looks the address up, connects and asks the host for a seat (LobbySession.JoinAsync), saying each step
/// under the box; once in, the friend's lobby opens (its Back returns to the main menu).
///
/// <para>Friends list (Kai, 2026-11-12): "Add Friend" (bottom left) saves a name and a pasted Tailscale address; the
/// saved friends are listed under the address box, starred ones at the top. Click a friend to put their address in
/// the box, JOIN on their row to join them straight away. Rows show the name only (the address is in the hover);
/// Edit changes a friend's name or address. Kept in the settings file (<see cref="FriendList"/>).</para>
/// </summary>
public partial class JoinPopup : Control
{
    private bool _busy;
    private int _attempt;
    private LobbySession? _joined;
    private string? _status, _error;
    private readonly object _gate = new();
    private VBoxContainer _friends = null!;
    private ScrollContainer _friendsScroll = null!;
    private Control _card = null!, _addCard = null!;
    private LineEdit _newName = null!, _newAddress = null!;
    private Label _addError = null!, _addTitle = null!;
    private FriendEntry? _editing;

    public override void _Ready()
    {
        var address = GetNode<LineEdit>("%Address");
        address.Text = UserConfig.Current.LastHostAddress;
        address.GrabFocus();
        address.TextSubmitted += _ => Join();

        GetNode<Button>("%Cancel").Pressed += Cancel;
        GetNode<Button>("%Join").Pressed += Join;
        BuildFriends();
    }

    // ================================================================== friends list

    private void BuildFriends()
    {
        var column = GetNode<VBoxContainer>("Center/Card/Column");
        _card = GetNode<Control>("Center/Card");
        int at = GetNode<LineEdit>("%Address").GetIndex() + 1;

        var title = new Label { Name = "FriendsTitle", Text = "Friends", ThemeTypeVariation = "DimLabel" };
        column.AddChild(title);
        column.MoveChild(title, at);
        _friends = new VBoxContainer { Name = "Friends", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _friends.AddThemeConstantOverride("separation", 4);
        _friendsScroll = new ScrollContainer { Name = "FriendsScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _friendsScroll.AddChild(_friends);
        column.AddChild(_friendsScroll);
        column.MoveChild(_friendsScroll, at + 1);

        // "Add Friend" bottom left; Cancel / JOIN stay on the right.
        var buttons = GetNode<HBoxContainer>("Center/Card/Column/Buttons");
        var add = new Button { Name = "AddFriend", Text = "Add Friend", CustomMinimumSize = new Vector2(150, 44), TooltipText = "Save a friend's Tailscale address under a name, so you can join them from this list." };
        add.Pressed += () => OpenAdd(GetNode<LineEdit>("%Address").Text);
        buttons.AddChild(add);
        buttons.MoveChild(add, 0);
        var gap = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        buttons.AddChild(gap);
        buttons.MoveChild(gap, 1);

        BuildAddCard();
        FillFriends();
    }

    private void FillFriends()
    {
        foreach (Node c in _friends.GetChildren()) { _friends.RemoveChild(c); c.QueueFree(); }
        var list = FriendList.Sorted(UserConfig.Current.Friends).ToList();
        if (list.Count == 0)
            _friends.AddChild(new Label { Text = "No friends saved yet - press Add Friend.", ThemeTypeVariation = "DimLabel" });
        foreach (var f in list) _friends.AddChild(FriendRow(f));
        // Up to about six rows show; more scroll.
        _friendsScroll.CustomMinimumSize = new Vector2(0, Math.Min(Math.Max(list.Count, 1), 6) * 50);
    }

    private Control FriendRow(FriendEntry f)
    {
        var row = new HBoxContainer { Name = "Friend_" + f.Name.Replace(' ', '_') };
        row.AddThemeConstantOverride("separation", 8);

        var star = new Button
        {
            Name = "Star", Text = f.Favorite ? "\u2605" : "\u2606", Flat = true, CustomMinimumSize = new Vector2(44, 44), FocusMode = FocusModeEnum.None,
            TooltipText = f.Favorite ? "Favourite - listed at the top. Click to un-star." : "Star this friend to list them at the top.",
        };
        star.AddThemeFontSizeOverride("font_size", 26);
        star.AddThemeColorOverride("font_color", f.Favorite ? Palette.HeroGold : Palette.TextDim);
        star.AddThemeColorOverride("font_hover_color", Palette.HeroGold);
        star.Pressed += () => { f.Favorite = !f.Favorite; UserConfig.Save(); FillFriends(); };
        row.AddChild(star);

        var who = new Button
        {
            Name = "Pick", Flat = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left,
            Text = f.Name, ClipText = true, FocusMode = FocusModeEnum.None,
            TooltipText = $"{f.Address}\nClick to put this address in the box above.",
        };
        who.Pressed += () => GetNode<LineEdit>("%Address").Text = f.Address;
        row.AddChild(who);

        var edit = new Button { Name = "Edit", Text = "Edit", CustomMinimumSize = new Vector2(64, 40), FocusMode = FocusModeEnum.None, TooltipText = $"Change {f.Name}'s name or address." };
        edit.Pressed += () => OpenAdd(f.Address, f);
        row.AddChild(edit);

        var join = new Button { Name = "JoinFriend", Text = "JOIN", ThemeTypeVariation = "CommitButton", CustomMinimumSize = new Vector2(90, 40), FocusMode = FocusModeEnum.None, TooltipText = $"Join {f.Name}'s game now." };
        join.Pressed += () => JoinAddress(f.Address);
        row.AddChild(join);

        var remove = new Button { Name = "Remove", Text = "X", Flat = true, CustomMinimumSize = new Vector2(36, 40), FocusMode = FocusModeEnum.None, TooltipText = $"Remove {f.Name} from the list." };
        remove.AddThemeColorOverride("font_color", Palette.WoundsRed);
        remove.Pressed += () => { UserConfig.Current.Friends.Remove(f); UserConfig.Save(); FillFriends(); };
        row.AddChild(remove);
        return row;
    }

    private void BuildAddCard()
    {
        _addCard = new PanelContainer { Name = "AddCard", ThemeTypeVariation = "CardPanel", CustomMinimumSize = new Vector2(700, 0), Visible = false };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        _addTitle = new Label { Text = "Add Friend", ThemeTypeVariation = "HeaderLabel" };
        col.AddChild(_addTitle);
        col.AddChild(new Label { Text = "Their name" });
        _newName = new LineEdit { Name = "FriendName", PlaceholderText = "e.g. Marcus" };
        col.AddChild(_newName);
        col.AddChild(new Label { Text = "Their Tailscale address (paste it)" });
        _newAddress = new LineEdit { Name = "FriendAddress", PlaceholderText = "e.g. marcus-pc.tail1234.ts.net", TooltipText = "The name their game shows under Settings > Player. A pasted link works too." };
        _newAddress.TextSubmitted += _ => SaveFriend();
        _newName.TextSubmitted += _ => _newAddress.GrabFocus();
        col.AddChild(_newAddress);
        _addError = new Label { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _addError.AddThemeColorOverride("font_color", Palette.WoundsRed);
        col.AddChild(_addError);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 12);
        var back = new Button { Name = "CancelFriend", Text = "Cancel", ThemeTypeVariation = "BackButton", CustomMinimumSize = new Vector2(140, 44) };
        back.Pressed += CloseAdd;
        var save = new Button { Name = "SaveFriend", Text = "SAVE", ThemeTypeVariation = "CommitButton", CustomMinimumSize = new Vector2(140, 44) };
        save.Pressed += SaveFriend;
        buttons.AddChild(back);
        buttons.AddChild(save);
        col.AddChild(buttons);
        _addCard.AddChild(col);
        GetNode<CenterContainer>("Center").AddChild(_addCard);
    }

    private void OpenAdd(string address, FriendEntry? editing = null)
    {
        _editing = editing;
        _addTitle.Text = editing == null ? "Add Friend" : "Edit Friend";
        _newName.Text = editing?.Name ?? "";
        _newAddress.Text = address.Trim();
        _addError.Text = "";
        _card.Visible = false;
        _addCard.Visible = true;
        _newName.GrabFocus();
    }

    private void CloseAdd()
    {
        _editing = null;
        _addCard.Visible = false;
        _card.Visible = true;
        GetNode<LineEdit>("%Address").GrabFocus();
    }

    private void SaveFriend()
    {
        var friends = UserConfig.Current.Friends;
        if (_editing != null)
        {
            // Edit = take the old entry out and save the new details, keeping the star.
            bool star = _editing.Favorite;
            int at = friends.IndexOf(_editing);
            friends.Remove(_editing);
            if (FriendList.Add(friends, _newName.Text, _newAddress.Text) is { } err)
            {
                friends.Insert(Math.Max(0, Math.Min(at, friends.Count)), _editing);
                _addError.Text = err;
                return;
            }
            string address = FriendList.CleanAddress(_newAddress.Text);
            if (friends.FirstOrDefault(f => f.Address.Equals(address, StringComparison.OrdinalIgnoreCase)) is { } saved) saved.Favorite |= star;
        }
        else if (FriendList.Add(friends, _newName.Text, _newAddress.Text) is { } error) { _addError.Text = error; return; }
        UserConfig.Save();
        FillFriends();
        CloseAdd();
    }

    /// <summary>Test driver: add a friend as if typed into Add Friend.</summary>
    public void AddFriendForTest(string name, string address, bool favorite)
    {
        OpenAdd(address);
        _newName.Text = name;
        SaveFriend();
        if (favorite && UserConfig.Current.Friends.FirstOrDefault(f => f.Name == name) is { } f) { f.Favorite = true; UserConfig.Save(); FillFriends(); }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        if (_addCard.Visible) CloseAdd(); else Cancel();
        GetViewport().SetInputAsHandled();
    }

    private void Cancel()
    {
        _attempt++;   // a join still on its way is let go when it arrives
        QueueFree();
    }

    /// <summary>Test driver: join this address now.</summary>
    public void JoinAddress(string address)
    {
        GetNode<LineEdit>("%Address").Text = address;
        Join();
    }

    private void Join()
    {
        if (_busy) return;
        string host = GetNode<LineEdit>("%Address").Text.Trim();
        _busy = true;
        int attempt = ++_attempt;
        GetNode<Button>("%Join").Disabled = true;
        Task.Run(async () =>
        {
            var (session, error) = await LobbySession.JoinAsync(host, s => { lock (_gate) _status = s; });
            lock (_gate)
            {
                if (attempt != _attempt) { session?.Dispose(); return; }
                _joined = session;
                _error = session == null ? error : null;
            }
        });
    }

    public override void _Process(double delta)
    {
        LobbySession? joined;
        string? status, error;
        lock (_gate) { joined = _joined; _joined = null; status = _status; error = _error; _error = null; }
        if (status != null) GetNode<Label>("%Status").Text = status;
        if (error != null)
        {
            GetNode<Label>("%Status").Text = error;
            _busy = false;
            GetNode<Button>("%Join").Disabled = false;
            lock (_gate) _status = null;
        }
        if (joined == null) return;
        QueueFree();
        App.I.Open<LobbyScreen>(Scenes.Lobby, l => l.Session = joined);
    }
}
