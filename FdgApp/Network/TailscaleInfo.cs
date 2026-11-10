using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FdgRaylib.Network;

/// <summary>
/// What a friend on Tailscale types into Join to reach this computer (Kai, 2026-11-05: play with friends
/// over Tailscale). Tailscale gives every device a 100.64.0.0/10 address and, with MagicDNS on, a name
/// that defaults to the computer's own name. No Tailscale API is used - this only reads the network
/// adapters, so it works whether or not the Tailscale command line is installed.
/// </summary>
public static class TailscaleInfo
{
    /// <summary>Tailscale's address block, 100.64.0.0/10 (the carrier-grade NAT range it assigns from).</summary>
    public static bool IsTailscaleAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    /// <summary>This computer's Tailscale IPv4 addresses (usually one), or none when Tailscale is not running.</summary>
    public static IReadOnlyList<string> Addresses()
    {
        var found = new List<string>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    if (IsTailscaleAddress(ua.Address)) found.Add(ua.Address.ToString());
            }
        }
        catch
        {
            // Adapter enumeration can throw on some platforms; no Tailscale address is the safe answer.
        }
        return found.Distinct().ToList();
    }

    /// <summary>
    /// The short MagicDNS name - Tailscale's default is the computer's name in lower case. Works for people
    /// on the SAME tailnet only; a friend the machine was SHARED with needs <see cref="FullName"/>.
    /// </summary>
    public static string LikelyName() => Environment.MachineName.ToLowerInvariant();

    private static string? _fullName;
    private static int _lookupStarted;

    /// <summary>
    /// This computer's full Tailscale name (e.g. <c>kais-pc.tail1a2b.ts.net</c>), or null until known.
    /// Tailscale's sharing docs: <i>"Shared machines can only be reached by using their fully qualified
    /// domain name"</i>, and a shared machine's 100.x address can be different on the friend's side - so
    /// this, not the number, is what to give a friend. Read once in the background from the Tailscale
    /// command line (<c>tailscale status --json</c>); stays null when that is not installed.
    /// </summary>
    public static string? FullName()
    {
        if (Interlocked.Exchange(ref _lookupStarted, 1) == 0) _ = Task.Run(LookUpFullName);
        return _fullName;
    }

    private static readonly string[] CliCandidates =
    {
        "tailscale",
        @"C:\Program Files\Tailscale\tailscale.exe",
        "/Applications/Tailscale.app/Contents/MacOS/Tailscale",
        "/usr/bin/tailscale", "/usr/local/bin/tailscale",
    };

    private static void LookUpFullName()
    {
        foreach (string cli in CliCandidates)
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(cli, "status --json")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(start);
                if (process == null) continue;
                string json = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(3000)) { try { process.Kill(); } catch { } continue; }
                if (ParseDnsName(json) is { } name) { _fullName = name; return; }
            }
            catch
            {
                // Not installed at this path, or not runnable - try the next.
            }
        }
    }

    /// <summary>The <c>Self.DNSName</c> of <c>tailscale status --json</c>, without its trailing dot.</summary>
    public static string? ParseDnsName(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Self", out var self)) return null;
            if (!self.TryGetProperty("DNSName", out var dns)) return null;
            string? name = dns.GetString()?.TrimEnd('.');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>What a friend should type, best first: the full name when known, else the address.</summary>
    public static string? JoinAddress()
    {
        if (FullName() is { } full) return full;
        IReadOnlyList<string> addresses = Addresses();
        return addresses.Count > 0 ? addresses[0] : null;
    }
}
