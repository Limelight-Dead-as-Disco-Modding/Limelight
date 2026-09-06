using Limelight.Models;
using Limelight.Services;
using System.Text.Json;

int passed = 0;
List<string> failures = new();
string[] localAddresses = ["192.168.1.20", "10.0.0.3"];

void Check(string name, Action assertion)
{
    try
    {
        assertion();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures.Add($"{name}: {error.Message}");
        Console.WriteLine($"FAIL {name}: {error.Message}");
    }
}

void Expect(bool condition, string message = "Contract did not hold.")
{
    if (!condition) throw new InvalidOperationException(message);
}

void Reject(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new InvalidOperationException("Invalid input was accepted.");
}

MultiplayerResolvedRoute Route(MultiplayerHostRoute mode, string address = "", string? tailscale = null) =>
    LimelightMpTransportPolicy.ResolveHost(
        new MultiplayerHostOptions { Route = mode, Address = address }, tailscale, localAddresses);

Check("automatic preserves Tailscale", () =>
{
    MultiplayerResolvedRoute route = Route(MultiplayerHostRoute.Automatic, tailscale: "100.101.102.103");
    Expect(route.Address == "100.101.102.103" && route.TailscaleDetected);
});
Check("automatic without Tailscale stays same-PC", () =>
{
    MultiplayerResolvedRoute route = Route(MultiplayerHostRoute.Automatic);
    Expect(route.Address == "127.0.0.1" && !route.TailscaleDetected);
    Expect(route.Description.Contains("Same-PC", StringComparison.Ordinal));
});
Check("LAN ignores Tailscale and uses chosen local address", () =>
{
    MultiplayerResolvedRoute route = Route(MultiplayerHostRoute.LocalNetwork, "192.168.1.20", "100.101.102.103");
    Expect(route.Address == "192.168.1.20" && !route.TailscaleDetected);
    Expect(route.Notice.Contains("7777") && route.Notice.Contains("7778"));
});
Check("LAN supports multiple adapters without guessing", () =>
    Expect(Route(MultiplayerHostRoute.LocalNetwork, "10.0.0.3").Address == "10.0.0.3"));
Check("LAN rejects missing selection", () => Reject(() => Route(MultiplayerHostRoute.LocalNetwork)));
Check("LAN rejects stale adapter address", () => Reject(() => Route(MultiplayerHostRoute.LocalNetwork, "192.168.1.99")));
Check("LAN rejects public address", () => Reject(() => Route(MultiplayerHostRoute.LocalNetwork, "203.0.113.15")));
Check("direct preserves explicit address over detected Tailscale", () =>
{
    MultiplayerResolvedRoute route = Route(MultiplayerHostRoute.DirectAddress, "203.0.113.15", "100.101.102.103");
    Expect(route.Address == "203.0.113.15" && !route.TailscaleDetected);
    Expect(route.Notice.Contains("BOTH UDP 7777") && route.Notice.Contains("7778"));
});
Check("direct accepts private routed network", () =>
    Expect(Route(MultiplayerHostRoute.DirectAddress, " 172.20.1.5 ").Address == "172.20.1.5"));
Check("unsupported transport fails closed", () => Reject(() => Route((MultiplayerHostRoute)99)));

foreach (string address in new[] { "", "localhost", "127.1", "127.0.0.1", "0.0.0.0", "0.1.2.3",
    "224.1.2.3", "239.255.255.255", "255.255.255.255", "169.254.1.2", "::1", "192.168.1.2:7777",
    "192.168.1.999", "192.168.001.2", "192.168.1.2\n?listen", "0x7f000001", "2130706433" })
{
    Check($"reject direct address '{address.Replace("\n", "\\n")}'", () =>
        Reject(() => Route(MultiplayerHostRoute.DirectAddress, address)));
}

foreach ((string address, bool expected) in new[]
{
    ("10.0.0.1", true), ("172.16.0.1", true), ("172.31.255.254", true), ("192.168.0.1", true),
    ("172.15.255.254", false), ("172.32.0.1", false), ("100.64.0.1", false), ("127.0.0.1", false), ("::1", false)
})
{
    Check($"LAN classification {address}", () =>
        Expect(LimelightMpTransportPolicy.IsPrivateLanAddress(address) == expected));
}

LimelightMpFriendCodeService codes = new();
const string token = "00112233445566778899aabbccddeeff";
foreach (string address in new[] { "127.0.0.1", "100.101.102.103", "192.168.1.20", "203.0.113.15" })
{
    Check($"L1 route roundtrip {address}", () =>
    {
        MultiplayerFriendConnection decoded = codes.Decode(codes.Encode(new MultiplayerFriendConnection
        {
            Address = address, InputPort = 7778, Token = token
        }));
        Expect(decoded.Address == address && decoded.InputPort == 7778 && decoded.GamePort == 7777 && decoded.Token == token);
    });
}
Check("L1 wire bytes unchanged", () =>
{
    byte[] payload = [1, 192, 168, 1, 20, 0x1e, 0x62, .. Convert.FromHexString(token)];
    string expected = "L1-" + Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    Expect(codes.Encode(new MultiplayerFriendConnection { Address = "192.168.1.20", InputPort = 7778, Token = token }) == expected);
});
Check("legacy friend code remains supported", () =>
{
    MultiplayerFriendConnection decoded = codes.Decode($"192.168.1.20:7778#{token}");
    Expect(decoded.Address == "192.168.1.20" && decoded.GamePort == 7777 && decoded.Token == token);
});
Check("token stays out of diagnostic JSON", () =>
    Expect(!JsonSerializer.Serialize(new MultiplayerFriendConnection { Token = token }).Contains(token)));
Check("invalid friend code fails closed", () => Reject(() => codes.Decode("STEAM-12345")));
Check("invalid L1 payload fails closed", () => Reject(() => codes.Decode("L1-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")));
Check("malformed base64 rejected", () => Reject(() => codes.Decode("L1-!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")));
Check("reserved input port rejected", () => Reject(() => codes.Create("192.168.1.20", 1024)));
Check("out-of-range input port rejected", () => Reject(() => codes.Create("192.168.1.20", 65536)));

Console.WriteLine($"{passed} passed; {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;
