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

string lobby = ((1UL << 56) | (8UL << 52) | (0x40000UL << 32) | 1234UL).ToString();
string steamCode = $"LS1-{lobby}-{token}";
MultiplayerSteamRequest steamRequest = new()
{
    Role = MultiplayerRole.Host,
    RequestId = "aabbccddeeff00112233445566778899",
    Token = token,
    PayloadVersion = "0.1.5",
    GameSha256 = new string('A', 64),
    ProtocolVersion = 1,
    LauncherPid = 42,
    LauncherStartTime = 123456789
};
long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
MultiplayerSteamStatus validStatus = new()
{
    RequestId = steamRequest.RequestId,
    Pid = 1234,
    ProcessStartTime = "123456799",
    State = "lobby_ready",
    LobbyId = lobby,
    FriendCode = steamCode,
    PayloadVersion = "0.1.5",
    ProtocolVersion = 1,
    UpdatedUnixMs = now
};

MultiplayerSteamStatus ChangeStatus(string field, System.Text.Json.Nodes.JsonNode? value)
{
    var json = JsonSerializer.SerializeToNode(validStatus)!;
    json[field] = value;
    return json.Deserialize<MultiplayerSteamStatus>()!;
}

Check("LS1 invitation uses validated lobby ID and exact token", () =>
{
    MultiplayerSteamInvitation invitation = LimelightMpSteamContract.Decode(steamCode);
    Expect(invitation.LobbyId == lobby && invitation.Token == token);
});
Check("LS1 accepts uppercase token", () =>
    Expect(LimelightMpSteamContract.Decode($"LS1-{lobby}-{token.ToUpperInvariant()}").Token == token));
foreach (string invalid in new[] { $"LS1-1-{token}", $"LS1-76561197960265729-{token}", $"LS1-0{lobby}-{token}",
    $"LS1-18446744073709551616-{token}", $"LS1-{lobby}-{new string('0', 32)}", $"LS1-{lobby}-{token}\nextra", "L1-123" })
{
    Check("LS1 rejects malformed invitation " + (invalid == "L1-123" ? "L1" : invalid.Split('-')[1]), () =>
        Reject(() => LimelightMpSteamContract.Decode(invalid)));
}
Check("Steam config carries separate world and input tunnel ports", () =>
{
    string config = LimelightMpSteamContract.BuildConfiguration(steamRequest);
    Expect(config.Contains("game_port=7777\ninput_port=7778\ntunnel_game_port=7779\ntunnel_input_port=7780\n"));
    Expect(config.Contains("native_player_mode=false\n") && config.Contains("launcher_start_time=123456789\n"));
    Expect(config.Contains("game_sha256=" + new string('a', 64)));
});
Check("Steam request token excluded from diagnostic JSON", () =>
    Expect(!JsonSerializer.Serialize(steamRequest).Contains(token)));
Check("Steam invitation token excluded from diagnostic JSON", () =>
    Expect(!JsonSerializer.Serialize(LimelightMpSteamContract.Decode(steamCode)).Contains(token)));
Check("Steam config rejects missing identity", () => Reject(() => LimelightMpSteamContract.BuildConfiguration(new MultiplayerSteamRequest())));
Check("request matched before invitation publication", () => Expect(LimelightMpSteamContract.MatchesRequest(validStatus, steamRequest, now)));
foreach ((string field, System.Text.Json.Nodes.JsonNode? value) in new (string, System.Text.Json.Nodes.JsonNode?)[]
{
    ("RequestId", "wrong-request"), ("PayloadVersion", "0.1.4"), ("ProtocolVersion", 2),
    ("NativePlayerMode", true), ("Pid", 0), ("ProcessStartTime", "invalid"),
    ("UpdatedUnixMs", now - 10001), ("UpdatedUnixMs", now + 5001),
    ("FriendCode", $"LS1-{lobby}-{new string('f', 32)}"), ("LobbyId", "1"),
    ("TransportReady", true), ("State", "gameplay_verified")
})
{
    Check("Steam status rejects mismatched " + field, () =>
        Expect(!LimelightMpSteamContract.MatchesRequest(ChangeStatus(field, value), steamRequest, now)));
}
Check("Steam state text never equates transport with verified gameplay", () =>
    Expect(LimelightMpSteamContract.DescribeState("connected").Contains("not proof")));
Check("failed or stopped native state cannot publish an invitation", () =>
{
    Expect(!LimelightMpSteamContract.MatchesRequest(ChangeStatus("State", "failed"), steamRequest, now));
    Expect(!LimelightMpSteamContract.MatchesRequest(ChangeStatus("State", "stopped"), steamRequest, now));
});
Check("ready requires real distinct individual Steam accounts", () =>
{
    var json = JsonSerializer.SerializeToNode(validStatus)!;
    json["State"] = "connected";
    json["TransportReady"] = true;
    json["LocalSteamId"] = "76561197960265729";
    json["PeerSteamId"] = "76561197960265730";
    Expect(LimelightMpSteamContract.MatchesRequest(json.Deserialize<MultiplayerSteamStatus>()!, steamRequest, now));
    foreach (string invalid in new[] { "1", "0", "76561197960265729", lobby, "not-an-id" })
    {
        json["PeerSteamId"] = invalid;
        Expect(!LimelightMpSteamContract.MatchesRequest(json.Deserialize<MultiplayerSteamStatus>()!, steamRequest, now));
    }
});
Check("client status binds to invited lobby after join", () =>
{
    MultiplayerSteamRequest clientRequest = new()
    {
        Role = MultiplayerRole.Client, RequestId = steamRequest.RequestId, Token = token, LobbyId = lobby,
        PayloadVersion = "0.1.5", ProtocolVersion = 1
    };
    var json = JsonSerializer.SerializeToNode(validStatus)!;
    json["FriendCode"] = string.Empty;
    json["State"] = "checking_lobby";
    Expect(LimelightMpSteamContract.MatchesRequest(json.Deserialize<MultiplayerSteamStatus>()!, clientRequest, now));
    json["LobbyId"] = "0";
    Expect(!LimelightMpSteamContract.MatchesRequest(json.Deserialize<MultiplayerSteamStatus>()!, clientRequest, now));
    json["State"] = "joining_lobby";
    Expect(LimelightMpSteamContract.MatchesRequest(json.Deserialize<MultiplayerSteamStatus>()!, clientRequest, now));
    json["LobbyId"] = "109212290963735763";
    Expect(!LimelightMpSteamContract.MatchesRequest(json.Deserialize<MultiplayerSteamStatus>()!, clientRequest, now));
});
Check("native status errors redact tokens and invitations", () =>
{
    string sanitized = LimelightMpSteamContract.RedactStatusMessage($"Error {steamCode}, token {token}");
    Expect(!sanitized.Contains(token) && !sanitized.Contains(steamCode));
});
Check("native status error length bounded", () =>
    Expect(LimelightMpSteamContract.RedactStatusMessage(new string('x', 800)).Length == 512));
Check("legacy IP policy does not silently handle Steam", () =>
    Reject(() => Route(MultiplayerHostRoute.Steam)));
Check("experimental ownership remains opt-in", () =>
    Expect(!new MultiplayerHostOptions().NativePlayerMode && !new MultiplayerJoinOptions().NativePlayerMode));
Check("older manifests cannot advertise unbuilt Steam or ownership", () =>
{
    MultiplayerPayloadManifest manifest = JsonSerializer.Deserialize<MultiplayerPayloadManifest>("{}")!;
    Expect(!manifest.SteamTransportSupported && !manifest.NativePlayerModeSupported);
});

Check("Steam file request and stop remain paired without launching a game", () =>
{
    string directory = Path.Combine(AppContext.BaseDirectory, "steam-contract-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(Path.Combine(directory, ".limelightmp-native-managed"), string.Empty);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using LimelightMpSteamSessionService service = new();
        using ManualResetEventSlim received = new();
        service.StatusChanged += status =>
        {
            if (status.State == "lobby_ready") received.Set();
        };
        service.Prepare(directory, process.MainModule!.FileName!, MultiplayerRole.Host, token, "0", "0.1.5", 1, false);
        string requestId = service.RequestId;
        string config = File.ReadAllText(Path.Combine(directory, "steam-session.ini"));
        Expect(config.Contains("request_id=" + requestId) && config.Contains("launcher_pid=" + process.Id));
        File.WriteAllText(Path.Combine(directory, "steam-status.json"), JsonSerializer.Serialize(new MultiplayerSteamStatus
        {
            RequestId = requestId,
            Pid = process.Id,
            ProcessStartTime = process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(),
            State = "lobby_ready",
            LobbyId = lobby,
            FriendCode = steamCode,
            PayloadVersion = "0.1.5",
            ProtocolVersion = 1,
            UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }));
        Expect(received.Wait(TimeSpan.FromSeconds(4)), "Matching process status was not delivered.");
        service.Stop();
        Expect(File.ReadAllText(Path.Combine(directory, "steam-stop.txt")) == requestId);
        Expect(service.RequestId == string.Empty);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
});

Console.WriteLine($"{passed} passed; {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;
