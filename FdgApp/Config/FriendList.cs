namespace FdgRaylib.Config;

/// <summary>One saved friend on the Join a Game popup (Kai, 2026-11-12): a name you chose and their Tailscale address.</summary>
public sealed class FriendEntry
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    /// <summary>Starred: listed above everyone else.</summary>
    public bool Favorite { get; set; }
}

/// <summary>
/// The friends list kept in <see cref="UserConfig.Friends"/> (shared by both builds, saved with the rest of the
/// settings). Favourites first, then A-Z by name. An address is stored the way JOIN wants it, whatever was pasted.
/// </summary>
public static class FriendList
{
    /// <summary>
    /// The address from whatever was pasted: a bare name ("kais-pc.tail1234.ts.net"), a Tailscale admin link
    /// ("https://kais-pc.tail1234.ts.net/"), or one with a port. Scheme, path, port and spaces go - the game always
    /// connects on its own port.
    /// </summary>
    public static string CleanAddress(string pasted)
    {
        string s = (pasted ?? "").Trim().Trim('"', '\'', '<', '>');
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        int at = s.IndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        int cut = s.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) s = s[..cut];
        if (s.Count(c => c == ':') == 1) s = s[..s.IndexOf(':')]; // "host:7777" (not an IPv6 address)
        return s.Trim().TrimEnd('.');
    }

    public static IEnumerable<FriendEntry> Sorted(IEnumerable<FriendEntry> friends) =>
        friends.OrderByDescending(f => f.Favorite).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a friend, or updates the one already saved under that address (a new name, same PC). Returns null, or
    /// what's wrong in a sentence.
    /// </summary>
    public static string? Add(List<FriendEntry> friends, string name, string pastedAddress)
    {
        string address = CleanAddress(pastedAddress);
        name = (name ?? "").Trim();
        if (address.Length == 0) return "Paste their Tailscale address first.";
        if (address.Contains(' ')) return "That doesn't look like an address - it has a space in it.";
        if (name.Length == 0) name = address.Split('.')[0];
        var same = friends.FirstOrDefault(f => f.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
        if (same != null) { same.Name = name; return null; }
        friends.Add(new FriendEntry { Name = name, Address = address });
        return null;
    }
}
