using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects static/config network detail via <see cref="System.Net.NetworkInformation"/>: host
/// identity, Wi-Fi radio/connection status, plus one card per active Ethernet/Wi-Fi adapter.
/// Live throughput lives elsewhere. Every read is defensive: a missing value yields "—" or is
/// skipped rather than throwing. <see cref="Collect"/> never throws.
/// </summary>
public static class NetworkInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection> { HostSection(), WiFiSection() };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsActiveLan(nic)) continue;
                var s = AdapterSection(nic);
                if (s is not null) sections.Add(s);
            }
        }
        catch { /* enumeration failed — earlier sections still render */ }
        return sections;
    }

    // ---- Host ----
    private static InfoSection HostSection()
    {
        var s = new InfoSection { Title = "Host" };
        s.Items.Add(new("Host name", Safe(Dns.GetHostName)));
        s.Items.Add(new("Domain", Safe(() => IPGlobalProperties.GetIPGlobalProperties().DomainName)));
        return s;
    }

    // ---- Wi-Fi ----
    private static InfoSection WiFiSection()
    {
        var s = new InfoSection { Title = "Wi-Fi" };
        try
        {
            NetworkInterface? wifi = null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                }
                catch { continue; }
                // Prefer an Up adapter if there is one.
                if (wifi is null) wifi = nic;
                if (IsUp(nic)) { wifi = nic; break; }
            }

            if (wifi is null)
            {
                s.Items.Add(new("Wi-Fi", "No wireless adapter"));
                return s;
            }

            if (!IsUp(wifi))
            {
                s.Items.Add(new("Status", "Off / disconnected"));
                return s;
            }

            s.Items.Add(new("Status", "On"));
            AddNetshWiFi(s);
        }
        catch { /* never throw */ }
        return s;
    }

    /// <summary>
    /// Parses <c>netsh wlan show interfaces</c> to describe the connected network. Best-effort:
    /// on failure, non-English labels, or missing fields the rows are simply skipped.
    /// </summary>
    private static void AddNetshWiFi(InfoSection s)
    {
        try
        {
            var fields = ParseNetshInterfaces();
            if (fields.Count == 0) return;

            AddIf(s, "SSID", NetshValue(fields, "SSID"));
            AddIf(s, "Signal", NetshValue(fields, "Signal"));
            AddIf(s, "Band/Radio type", NetshValue(fields, "Radio type"));
            AddIf(s, "Channel", NetshValue(fields, "Channel"));

            var rx = NetshValue(fields, "Receive rate (Mbps)");
            var tx = NetshValue(fields, "Transmit rate (Mbps)");
            var rate = rx is not null && tx is not null
                ? $"{rx} / {tx} Mbps (Rx/Tx)"
                : rx is not null ? $"{rx} Mbps" : tx is not null ? $"{tx} Mbps" : null;
            AddIf(s, "Link rate", rate);
        }
        catch { /* skip netsh-derived rows */ }
    }

    private static Dictionary<string, string> ParseNetshInterfaces()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = "wlan show interfaces",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null) return map;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            if (key.Length == 0 || val.Length == 0) continue;
            // First occurrence wins (skip duplicate keys from multiple interfaces).
            if (!map.ContainsKey(key)) map[key] = val;
        }
        return map;
    }

    private static string? NetshValue(Dictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static void AddIf(InfoSection s, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) s.Items.Add(new(label, value!));
    }

    // ---- Per-adapter ----
    private static bool IsActiveLan(NetworkInterface nic)
    {
        try
        {
            if (nic.OperationalStatus != OperationalStatus.Up) return false;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) return false;
            return nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.Wireless80211;
        }
        catch { return false; }
    }

    private static bool IsUp(NetworkInterface nic)
    {
        try { return nic.OperationalStatus == OperationalStatus.Up; }
        catch { return false; }
    }

    private static InfoSection? AdapterSection(NetworkInterface nic)
    {
        try
        {
            var s = new InfoSection { Title = Safe(() => nic.Name) };
            s.Items.Add(new("Description", Safe(() => nic.Description)));
            s.Items.Add(new("Type", Safe(() =>
                nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet")));
            s.Items.Add(new("Status", Safe(() => nic.OperationalStatus.ToString())));
            s.Items.Add(new("Link speed", Safe(() => Speed(nic.Speed))));
            s.Items.Add(new("MAC", Safe(() => Mac(nic.GetPhysicalAddress()))));

            IPInterfaceProperties? ip = SafeGet(() => nic.GetIPProperties());
            if (ip is not null)
            {
                AddIPv4(s, ip);
                AddIPv6(s, ip);
                AddGateways(s, ip);
                AddDns(s, ip);
                AddDhcp(s, ip);

                var suffix = Safe(() => ip.DnsSuffix);
                if (suffix != "—") s.Items.Add(new("DNS suffix", suffix));
            }
            return s;
        }
        catch { return null; }
    }

    private static void AddIPv4(InfoSection s, IPInterfaceProperties ip)
    {
        try
        {
            foreach (var u in ip.UnicastAddresses)
            {
                if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                s.Items.Add(new("IPv4", $"{u.Address}/{PrefixLen(u)}"));
            }
        }
        catch { }
    }

    private static void AddIPv6(InfoSection s, IPInterfaceProperties ip)
    {
        try
        {
            string? linkLocal = null;
            string? global = null;
            foreach (var u in ip.UnicastAddresses)
            {
                var addr = u.Address;
                if (addr.AddressFamily != AddressFamily.InterNetworkV6) continue;
                if (addr.IsIPv6LinkLocal)
                {
                    linkLocal ??= $"{addr}/{PrefixLen(u)}";
                }
                else if (!addr.IsIPv6Multicast && !addr.IsIPv6SiteLocal)
                {
                    global ??= $"{addr}/{PrefixLen(u)}";
                }
            }
            if (linkLocal is not null) s.Items.Add(new("IPv6", linkLocal));
            if (global is not null) s.Items.Add(new("IPv6", global));
        }
        catch { }
    }

    private static void AddGateways(InfoSection s, IPInterfaceProperties ip)
    {
        try
        {
            var gws = ip.GatewayAddresses
                .Select(g => g.Address?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
            if (gws.Count == 1)
            {
                s.Items.Add(new("Gateway", gws[0]!));
            }
            else
            {
                for (int i = 0; i < gws.Count; i++)
                    s.Items.Add(new($"Gateway {i + 1}", gws[i]!));
            }
        }
        catch { }
    }

    private static void AddDns(InfoSection s, IPInterfaceProperties ip)
    {
        try
        {
            var servers = ip.DnsAddresses
                .Select(d => d?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
            for (int i = 0; i < servers.Count; i++)
                s.Items.Add(new($"DNS server {i + 1}", servers[i]!));
        }
        catch { }
    }

    private static void AddDhcp(InfoSection s, IPInterfaceProperties ip)
    {
        try
        {
            var props = SafeGet(() => ip.GetIPv4Properties());
            if (props is null) return;

            if (props.IsDhcpEnabled)
            {
                s.Items.Add(new("DHCP", "Enabled"));
                var server = ip.DhcpServerAddresses
                    .Select(d => d?.ToString())
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (server is not null) s.Items.Add(new("DHCP server", server));
            }
            else
            {
                s.Items.Add(new("DHCP", "Disabled"));
            }
        }
        catch { }
    }

    // ---- formatting helpers ----
    private static int PrefixLen(UnicastIPAddressInformation u)
    {
        try { return u.PrefixLength; } catch { return 0; }
    }

    private static string Mac(PhysicalAddress? pa)
    {
        var bytes = pa?.GetAddressBytes();
        if (bytes is null || bytes.Length == 0) return "—";
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private static string Speed(long bps)
    {
        if (bps <= 0) return "—";
        return bps >= 1_000_000_000
            ? $"{bps / 1_000_000_000.0:0.#} Gbps"
            : $"{bps / 1_000_000.0:0.#} Mbps";
    }

    // ---- safe getters ----
    private static string Safe(Func<string?> get)
    {
        try { return get()?.Trim() is { Length: > 0 } v ? v : "—"; }
        catch { return "—"; }
    }

    private static T? SafeGet<T>(Func<T> get) where T : class
    {
        try { return get(); }
        catch { return null; }
    }
}
