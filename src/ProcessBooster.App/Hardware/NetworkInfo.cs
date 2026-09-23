using System.Net;
using System.Net.NetworkInformation;

namespace ProcessBooster.App.Hardware;

/// <summary>
/// Collects static/config network detail via <see cref="System.Net.NetworkInformation"/>: host
/// identity plus one card per active Ethernet/Wi-Fi adapter. Live throughput lives elsewhere.
/// Every read is defensive: a missing value yields "—" or is skipped rather than throwing.
/// </summary>
public static class NetworkInfoService
{
    public static List<InfoSection> Collect()
    {
        var sections = new List<InfoSection> { HostSection() };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsActiveLan(nic)) continue;
                var s = AdapterSection(nic);
                if (s is not null) sections.Add(s);
            }
        }
        catch { /* enumeration failed — host section still renders */ }
        return sections;
    }

    private static InfoSection HostSection()
    {
        var s = new InfoSection { Title = "Host" };
        s.Items.Add(new("Host name", Safe(Dns.GetHostName)));
        s.Items.Add(new("Domain", Safe(() => IPGlobalProperties.GetIPGlobalProperties().DomainName)));
        return s;
    }

    private static bool IsActiveLan(NetworkInterface nic)
    {
        try
        {
            if (nic.OperationalStatus != OperationalStatus.Up) return false;
            return nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.Wireless80211;
        }
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

            IPInterfaceProperties? ip = null;
            try { ip = nic.GetIPProperties(); } catch { }
            if (ip is not null)
            {
                var v4 = ip.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(u => $"{u.Address}/{PrefixLen(u)}")
                    .ToList();
                if (v4.Count > 0) s.Items.Add(new("IPv4", string.Join("  ·  ", v4)));

                var v6 = ip.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(u => u.Address.ToString())
                    .Take(2)
                    .ToList();
                if (v6.Count > 0) s.Items.Add(new("IPv6", string.Join("  ·  ", v6)));

                var gw = Join(ip.GatewayAddresses.Select(g => g.Address.ToString()));
                if (gw != "—") s.Items.Add(new("Gateway", gw));

                var dns = Join(ip.DnsAddresses.Select(d => d.ToString()));
                if (dns != "—") s.Items.Add(new("DNS servers", dns));

                var props = SafeGet(() => ip.GetIPv4Properties());
                if (props is not null)
                {
                    if (props.IsDhcpEnabled)
                    {
                        var server = Join(ip.DhcpServerAddresses.Select(d => d.ToString()));
                        s.Items.Add(new("DHCP", server == "—" ? "Enabled" : $"Enabled  ·  {server}"));
                    }
                    else
                    {
                        s.Items.Add(new("DHCP", "Disabled"));
                    }
                }

                var suffix = Safe(() => ip.DnsSuffix);
                if (suffix != "—") s.Items.Add(new("DNS suffix", suffix));
            }
            return s;
        }
        catch { return null; }
    }

    // ---- helpers ----
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

    private static string Join(IEnumerable<string> values)
    {
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return list.Count > 0 ? string.Join("  ·  ", list) : "—";
    }

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
