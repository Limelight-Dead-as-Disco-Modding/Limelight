using System.Text.Json.Serialization;

namespace Limelight.Models
{
    public sealed class MultiplayerSteamInvitation
    {
        public string LobbyId { get; init; } = string.Empty;

        [JsonIgnore]
        public string Token { get; init; } = string.Empty;
    }

    public sealed class MultiplayerSteamRequest
    {
        public MultiplayerRole Role { get; init; }
        public string RequestId { get; init; } = string.Empty;
        [JsonIgnore]
        public string Token { get; init; } = string.Empty;
        public string LobbyId { get; init; } = "0";
        public string PayloadVersion { get; init; } = string.Empty;
        public string GameSha256 { get; init; } = string.Empty;
        public int ProtocolVersion { get; init; }
        public int LauncherPid { get; init; }
        public long LauncherStartTime { get; init; }
        public bool NativePlayerMode { get; init; }
    }

    public sealed class MultiplayerSteamStatus
    {
        public string RequestId { get; init; } = string.Empty;
        public int Pid { get; init; }
        public string ProcessStartTime { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
        public string LobbyId { get; init; } = "0";
        public string LocalSteamId { get; init; } = "0";
        public string PeerSteamId { get; init; } = "0";
        public bool TransportReady { get; init; }
        public ulong WorldSent { get; init; }
        public ulong WorldReceived { get; init; }
        public ulong InputSent { get; init; }
        public ulong InputReceived { get; init; }
        public string FriendCode { get; init; } = string.Empty;
        public string PayloadVersion { get; init; } = string.Empty;
        public int ProtocolVersion { get; init; }
        public long UpdatedUnixMs { get; init; }
        public bool NativePlayerMode { get; init; }
    }
}
