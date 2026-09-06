using Limelight.Models;
using System.Net;
using System.Net.Sockets;

namespace Limelight.Services
{
    public static class LimelightMpTransportPolicy
    {
        public static MultiplayerResolvedRoute ResolveHost(
            MultiplayerHostOptions options,
            string? tailscaleAddress,
            IReadOnlyCollection<string> localAddresses)
        {
            switch (options.Route)
            {
                case MultiplayerHostRoute.Automatic:
                    if (!string.IsNullOrWhiteSpace(tailscaleAddress))
                    {
                        return new MultiplayerResolvedRoute
                        {
                            Address = ValidateDirectAddress(tailscaleAddress),
                            Description = "Tailscale private route",
                            Notice = "Both PCs need access to this Tailscale route. Share only with the friend joining your session.",
                            TailscaleDetected = true
                        };
                    }

                    return new MultiplayerResolvedRoute
                    {
                        Address = IPAddress.Loopback.ToString(),
                        Description = "Same-PC test only",
                        Notice = "Automatic mode found no Tailscale route. This code only reaches this PC; choose Local network for two PCs on the same LAN or Direct IPv4 for an existing routed connection."
                    };

                case MultiplayerHostRoute.LocalNetwork:
                    string localAddress = ValidateDirectAddress(options.Address);
                    if (!IsPrivateLanAddress(localAddress) ||
                        !localAddresses.Contains(localAddress, StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Choose a current local-network address from the list. No suitable address? Check Wi-Fi/Ethernet or use Direct IPv4.");
                    }

                    return new MultiplayerResolvedRoute
                    {
                        Address = localAddress,
                        Description = "Local network (no VPN required)",
                        Notice = "Both PCs must be on a reachable local network. Windows Firewall must allow the game and relay on UDP 7777 and 7778; Limelight does not change firewall rules."
                    };

                case MultiplayerHostRoute.DirectAddress:
                    return new MultiplayerResolvedRoute
                    {
                        Address = ValidateDirectAddress(options.Address),
                        Description = "Direct IPv4 (routing required)",
                        Notice = "Use an address your friend can reach. Internet play requires existing routing/port forwarding for BOTH UDP 7777 (game) and 7778 (input); Limelight does not open ports or configure your router."
                    };

                default:
                    throw new InvalidOperationException("This multiplayer route is not supported by this build.");
            }
        }

        public static string ValidateDirectAddress(string address)
        {
            string candidate = address?.Trim() ?? string.Empty;
            string[] parts = candidate.Split('.');
            if (parts.Length != 4 ||
                parts.Any(part => part.Length == 0 ||
                    part.Any(character => character is < '0' or > '9')) ||
                !IPAddress.TryParse(candidate, out IPAddress? parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork ||
                !string.Equals(candidate, parsed.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Enter a full IPv4 address, such as 192.168.1.20. Do not include a port or hostname.");
            }

            byte[] bytes = parsed.GetAddressBytes();
            if (bytes[0] == 0 || bytes[0] == 127 || bytes[0] >= 224 ||
                (bytes[0] == 169 && bytes[1] == 254))
            {
                throw new InvalidOperationException("Use a reachable unicast IPv4 address. Loopback, link-local, unspecified, multicast and broadcast addresses cannot host this route.");
            }

            return parsed.ToString();
        }

        public static bool IsPrivateLanAddress(string address)
        {
            if (!IPAddress.TryParse(address, out IPAddress? parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] bytes = parsed.GetAddressBytes();
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}
