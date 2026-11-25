using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SupermarketPos.Infrastructure.Scanning;

public enum ScanTransport { Wifi, Bluetooth, Usb }

public sealed record LanEndpoint(string Name, IPAddress Address, IPAddress Mask)
{
    public string Label => $"{Name} · {Address}";

    public bool Contains(IPAddress? remote)
    {
        if (remote?.AddressFamily != AddressFamily.InterNetwork) return false;
        var local = Address.GetAddressBytes();
        var other = remote.GetAddressBytes();
        var mask = Mask.GetAddressBytes();
        return local.Zip(other, (a, b) => a ^ b).Zip(mask, (difference, maskByte) => (difference & maskByte) == 0).All(match => match);
    }

    public static IReadOnlyList<LanEndpoint> Available(ScanTransport transport = ScanTransport.Wifi) =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up
                && Supports(transport, network.NetworkInterfaceType, network.Name, network.Description))
            .OrderByDescending(network => network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork
                    && IsPrivate(ip.Address) && ip.IPv4Mask is not null && !ip.IPv4Mask.Equals(IPAddress.Any))
                .Select(ip => new LanEndpoint(network.Name, ip.Address, ip.IPv4Mask)))
            .ToArray();

    internal static bool Supports(ScanTransport transport, NetworkInterfaceType type, string name, string description)
    {
        if (name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)) return false;
        var adapter = name + " " + description;
        var bluetooth = adapter.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
        var usb = adapter.Contains("RNDIS", StringComparison.OrdinalIgnoreCase)
            || adapter.Contains("CDC NCM", StringComparison.OrdinalIgnoreCase)
            || adapter.Contains("USB NCM", StringComparison.OrdinalIgnoreCase)
            || adapter.Contains("USB Ethernet", StringComparison.OrdinalIgnoreCase)
            || adapter.Contains("USB Tether", StringComparison.OrdinalIgnoreCase);
        return transport switch
        {
            ScanTransport.Bluetooth => bluetooth,
            ScanTransport.Usb => usb && !bluetooth,
            _ => !bluetooth && !usb && type is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
        };
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254;
    }
}
