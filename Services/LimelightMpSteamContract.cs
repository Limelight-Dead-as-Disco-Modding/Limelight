using Limelight.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public static partial class LimelightMpSteamContract
    {
        public const int TunnelGamePort = 7779;
        public const int TunnelInputPort = 7780;

        public static MultiplayerSteamInvitation Decode(string code)
        {
            Match match = InvitationPattern().Match(code?.Trim() ?? string.Empty);
            if (!match.Success || !IsLobbyId(match.Groups["lobby"].Value) ||
                match.Groups["token"].Value.All(character => character == '0'))
            {
                throw new InvalidOperationException("That Steam invitation is invalid. Copy the complete LS1- code from the host.");
            }

            return new MultiplayerSteamInvitation
            {
                LobbyId = match.Groups["lobby"].Value,
                Token = match.Groups["token"].Value.ToLowerInvariant()
            };
        }

        public static bool IsLobbyId(string value)
        {
            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) &&
                id.ToString(CultureInfo.InvariantCulture) == value &&
                (id >> 56) == 1 && ((id >> 52) & 15) == 8 &&
                (((id >> 32) & 0xfffff) & 0x40000) != 0 && (uint)id != 0;
        }

        public static string BuildConfiguration(MultiplayerSteamRequest request)
        {
            if (request.Role is not MultiplayerRole.Host and not MultiplayerRole.Client ||
                !Hex32Pattern().IsMatch(request.RequestId) || !Hex32Pattern().IsMatch(request.Token) ||
                request.RequestId.All(character => character == '0') || request.Token.All(character => character == '0') ||
                !VersionPattern().IsMatch(request.PayloadVersion) ||
                !Hex64Pattern().IsMatch(request.GameSha256) || request.ProtocolVersion != 1 ||
                request.LauncherPid <= 0 || request.LauncherStartTime <= 0 ||
                (request.Role == MultiplayerRole.Client ? !IsLobbyId(request.LobbyId) : request.LobbyId != "0"))
            {
                throw new InvalidOperationException("Steam session configuration is invalid.");
            }

            return string.Join('\n',
                $"role={(request.Role == MultiplayerRole.Host ? "host" : "client")}",
                $"request_id={request.RequestId}",
                $"invitation_token={request.Token}",
                $"lobby_id={request.LobbyId}",
                "game_port=7777", "input_port=7778",
                $"tunnel_game_port={TunnelGamePort}", $"tunnel_input_port={TunnelInputPort}",
                $"payload_version={request.PayloadVersion}",
                $"game_sha256={request.GameSha256.ToLowerInvariant()}",
                $"protocol_version={request.ProtocolVersion}",
                $"launcher_pid={request.LauncherPid.ToString(CultureInfo.InvariantCulture)}",
                $"launcher_start_time={request.LauncherStartTime.ToString(CultureInfo.InvariantCulture)}",
                $"native_player_mode={(request.NativePlayerMode ? "true" : "false")}",
                string.Empty);
        }

        public static bool MatchesRequest(MultiplayerSteamStatus status, MultiplayerSteamRequest request, long nowUnixMs)
        {
            if (status.RequestId != request.RequestId || status.PayloadVersion != request.PayloadVersion ||
                status.ProtocolVersion != request.ProtocolVersion || status.NativePlayerMode != request.NativePlayerMode ||
                status.Pid <= 0 || !long.TryParse(status.ProcessStartTime, NumberStyles.None, CultureInfo.InvariantCulture, out long start) ||
                start <= 0 || status.UpdatedUnixMs < nowUnixMs - 10000 || status.UpdatedUnixMs > nowUnixMs + 5000)
            {
                return false;
            }

            if (status.TransportReady && (status.State != "connected" ||
                !IsIndividualSteamId(status.LocalSteamId) || !IsIndividualSteamId(status.PeerSteamId) ||
                status.LocalSteamId == status.PeerSteamId))
            {
                return false;
            }

            if (request.Role == MultiplayerRole.Client && status.LobbyId != request.LobbyId &&
                !(status.LobbyId == "0" && status.State is "starting" or "joining_lobby" or "failed" or "stopped"))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(status.FriendCode))
            {
                try
                {
                    MultiplayerSteamInvitation invitation = Decode(status.FriendCode);
                    if (request.Role != MultiplayerRole.Host || status.State is "failed" or "stopped" ||
                        invitation.Token != request.Token.ToLowerInvariant() ||
                        invitation.LobbyId != status.LobbyId)
                    {
                        return false;
                    }
                }
                catch (InvalidOperationException) { return false; }
            }

            return status.State is "starting" or "creating_lobby" or "joining_lobby" or "lobby_ready" or
                "checking_lobby" or "handshaking" or "connected" or "failed" or "stopped";
        }

        public static string DescribeState(string state) => state switch
        {
            "creating_lobby" => "Creating a Steam invitation.",
            "joining_lobby" or "checking_lobby" => "Checking the Steam invitation and build compatibility.",
            "lobby_ready" => "Steam invitation ready. Waiting for the friend to join; gameplay is not verified.",
            "handshaking" => "Verifying the peer and opening the Steam transport.",
            "connected" => "Steam transport ready. Packet traffic is not proof of working gameplay.",
            "failed" => "Steam transport failed. Stop this session before retrying or selecting a direct route.",
            "stopped" => "Steam transport stopped.",
            _ => "Waiting for the game's Steam connection."
        };

        private static bool IsIndividualSteamId(string value)
        {
            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) &&
                id.ToString(CultureInfo.InvariantCulture) == value &&
                id >> 56 == 1 && ((id >> 52) & 15) == 1 &&
                ((id >> 32) & 0xfffff) == 1 && (uint)id != 0;
        }

        public static string RedactStatusMessage(string message)
        {
            string redacted = SecretPattern().Replace(message ?? string.Empty, "[redacted]");
            return redacted.Length > 512 ? redacted[..512] : redacted;
        }

        [GeneratedRegex("\\ALS1-(?<lobby>[0-9]{1,20})-(?<token>[0-9a-fA-F]{32})\\z", RegexOptions.CultureInvariant)]
        private static partial Regex InvitationPattern();
        [GeneratedRegex("\\A[0-9a-fA-F]{32}\\z", RegexOptions.CultureInvariant)]
        private static partial Regex Hex32Pattern();
        [GeneratedRegex("\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant)]
        private static partial Regex Hex64Pattern();
        [GeneratedRegex("\\A[0-9]+\\.[0-9]+\\.[0-9]+\\z", RegexOptions.CultureInvariant)]
        private static partial Regex VersionPattern();
        [GeneratedRegex("LS1-[0-9]+-[0-9a-fA-F]{32}|[0-9a-fA-F]{32,64}", RegexOptions.CultureInvariant)]
        private static partial Regex SecretPattern();
    }
}
