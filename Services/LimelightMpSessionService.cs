using Limelight.Models;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Limelight.Services
{
    public sealed class LimelightMpSessionService : IDisposable
    {
        public const int GamePort = 7777;
        public const int InputPort = 7778;

        private readonly LimelightMpPayloadService _payloadService;
        private readonly LimelightMpRelayService _relayService;
        private readonly LimelightMpFriendCodeService _friendCodeService;
        private readonly object _logGate =
            new();

        private string _sessionLogPath =
            string.Empty;

        private CancellationTokenSource? _gameLogCancellation;
        private Task? _gameLogTask;

        public LimelightMpSessionService(
            LimelightMpPayloadService payloadService,
            LimelightMpRelayService relayService,
            LimelightMpFriendCodeService friendCodeService)
        {
            _payloadService = payloadService;
            _relayService = relayService;
            _friendCodeService = friendCodeService;

            _relayService.OutputReceived +=
                RelayOutputReceived;

            _relayService.Exited +=
                RelayExited;
        }

        public event Action<MultiplayerLogLevel, string>? LogEmitted;

        public MultiplayerRole ActiveRole { get; private set; }

        public bool IsActive =>
            ActiveRole != MultiplayerRole.None &&
            _relayService.IsRunning;

        public string SessionLogPath =>
            _sessionLogPath;

        public MultiplayerStartResult StartHost(
            Ue4ssDetectionResult installation,
            MultiplayerHostOptions? options = null)
        {
            MultiplayerResolvedRoute route =
                LimelightMpTransportPolicy.ResolveHost(
                    options ?? new MultiplayerHostOptions(),
                    FindTailscaleIpv4Address(),
                    FindLocalNetworkIpv4Addresses());

            EnsureNoActiveSession();
            BeginLog(MultiplayerRole.Host);

            string address = route.Address;

            MultiplayerFriendConnection connection =
                _friendCodeService.Create(
                    address,
                    InputPort);

            string friendCode =
                _friendCodeService.Encode(
                    connection);

            Log(
                MultiplayerLogLevel.Log,
                $"Verifying and installing the v{LimelightMpPayloadService.Version} host payload.");

            Log(MultiplayerLogLevel.Network, route.Description);
            Log(MultiplayerLogLevel.Warning, route.Notice);

            _payloadService.Install(
                installation,
                MultiplayerRole.Host,
                $"{address}:{GamePort}",
                GamePort);

            StartGameLogMonitor(
                installation);

            string relayPath =
                _payloadService.EnsureRelayExtracted();

            try
            {
                _relayService.StartHost(
                    relayPath,
                    InputPort,
                    connection.Token);

                ActiveRole = MultiplayerRole.Host;
            }
            catch
            {
                ActiveRole = MultiplayerRole.None;
                StopGameLogMonitor();
                throw;
            }

            Log(
                MultiplayerLogLevel.Network,
                $"Host input relay is listening on UDP {InputPort}.");

            Log(
                MultiplayerLogLevel.Gameplay,
                "Enter the Dive Bar, then press Ctrl+Shift+F5 once to begin hosting.");

            Log(
                MultiplayerLogLevel.Gameplay,
                "Ctrl+Shift+F7 travels both games to Infinite Disco; F8 returns both to the Dive Bar.");

            Log(
                MultiplayerLogLevel.Gameplay,
                "Ctrl+Shift+F10 teleports Chuckles beside Charlie if a sub-level transition separates them.");

            return new MultiplayerStartResult
            {
                Role = MultiplayerRole.Host,
                FriendCode = friendCode,
                Address = address,
                GamePort = GamePort,
                InputPort = InputPort,
                TailscaleDetected = route.TailscaleDetected,
                RouteDescription = route.Description,
                RouteNotice = route.Notice
            };
        }

        public MultiplayerStartResult StartClient(
            Ue4ssDetectionResult installation,
            string friendCode)
        {
            EnsureNoActiveSession();
            BeginLog(MultiplayerRole.Client);

            MultiplayerFriendConnection connection =
                _friendCodeService.Decode(
                    friendCode);

            Log(
                MultiplayerLogLevel.Log,
                $"Verifying and installing the v{LimelightMpPayloadService.Version} client payload.");

            _payloadService.Install(
                installation,
                MultiplayerRole.Client,
                $"{connection.Address}:{connection.GamePort}",
                connection.GamePort);

            StartGameLogMonitor(
                installation);

            string relayPath =
                _payloadService.EnsureRelayExtracted();

            try
            {
                _relayService.StartClient(
                    relayPath,
                    connection.Address,
                    connection.InputPort,
                    connection.Token);

                ActiveRole = MultiplayerRole.Client;
            }
            catch
            {
                ActiveRole = MultiplayerRole.None;
                StopGameLogMonitor();
                throw;
            }

            Log(
                MultiplayerLogLevel.Network,
                $"Reaching the host at {connection.Address}:{connection.InputPort}.");

            Log(
                MultiplayerLogLevel.Gameplay,
                "Your own game renders the session. Ctrl+Shift+F6 retries the game connection if needed.");

            return new MultiplayerStartResult
            {
                Role = MultiplayerRole.Client,
                Address = connection.Address,
                GamePort = connection.GamePort,
                InputPort = connection.InputPort,
                RouteDescription = "Friend-code IPv4 route",
                RouteNotice = "The code supplies the host address, not a connection guarantee. Both UDP 7777 and 7778 must be reachable."
            };
        }

        public void Stop(
            string reason = "Session stopped.")
        {
            MultiplayerRole previousRole =
                ActiveRole;

            ActiveRole = MultiplayerRole.None;
            _relayService.Stop();
            StopGameLogMonitor();

            if (previousRole != MultiplayerRole.None)
            {
                Log(
                    MultiplayerLogLevel.Network,
                    reason);
            }
        }

        public string? FindTailscaleIpv4Address()
        {
            foreach (NetworkInterface adapter in
                     NetworkInterface.GetAllNetworkInterfaces())
            {
                string identity =
                    $"{adapter.Name} {adapter.Description}";

                if (adapter.OperationalStatus !=
                        OperationalStatus.Up ||
                    !identity.Contains(
                        "Tailscale",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation address in
                         adapter.GetIPProperties()
                             .UnicastAddresses)
                {
                    if (address.Address.AddressFamily ==
                            AddressFamily.InterNetwork &&
                        address.Address.ToString().StartsWith(
                            "100.",
                            StringComparison.Ordinal))
                    {
                        return address.Address.ToString();
                    }
                }
            }

            return null;
        }

        public string[] FindLocalNetworkIpv4Addresses()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
                        not NetworkInterfaceType.Tunnel &&
                    !$"{adapter.Name} {adapter.Description}".Contains(
                        "Tailscale", StringComparison.OrdinalIgnoreCase))
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address.ToString())
                .Where(LimelightMpTransportPolicy.IsPrivateLanAddress)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();
        }

        public void Dispose()
        {
            _relayService.OutputReceived -=
                RelayOutputReceived;

            _relayService.Exited -=
                RelayExited;

            Stop("Limelight closed the multiplayer relay.");
            _relayService.Dispose();
        }

        private void EnsureNoActiveSession()
        {
            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Stop the current LimelightMP session before starting another one.");
            }

            _relayService.Stop();
            ActiveRole = MultiplayerRole.None;
        }

        private void BeginLog(
            MultiplayerRole role)
        {
            string logDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Multiplayer",
                    "Logs");

            Directory.CreateDirectory(logDirectory);

            _sessionLogPath =
                Path.Combine(
                    logDirectory,
                    $"LimelightMP-{role}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            File.WriteAllText(
                _sessionLogPath,
                $"LimelightMP {role} session started {DateTimeOffset.Now:O}{Environment.NewLine}",
                new UTF8Encoding(false));
        }

        private void StartGameLogMonitor(
            Ue4ssDetectionResult installation)
        {
            StopGameLogMonitor();

            string ue4ssLog =
                installation.LogPath;

            string engineLog =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Pagoda",
                    "Saved",
                    "Logs",
                    "Pagoda.log");

            long ue4ssOffset =
                GetCurrentLength(ue4ssLog);

            long engineOffset =
                GetCurrentLength(engineLog);

            _gameLogCancellation =
                new CancellationTokenSource();

            CancellationToken cancellationToken =
                _gameLogCancellation.Token;

            _gameLogTask =
                Task.Run(
                    async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            foreach (string line in
                                     ReadNewLines(
                                         ue4ssLog,
                                         ref ue4ssOffset))
                            {
                                EmitFilteredGameEvent(line);
                            }

                            foreach (string line in
                                     ReadNewLines(
                                         engineLog,
                                         ref engineOffset))
                            {
                                EmitFilteredGameEvent(line);
                            }

                            try
                            {
                                await Task.Delay(
                                    500,
                                    cancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }
                    },
                    cancellationToken);
        }

        private void StopGameLogMonitor()
        {
            CancellationTokenSource? cancellation =
                _gameLogCancellation;

            Task? task =
                _gameLogTask;

            _gameLogCancellation = null;
            _gameLogTask = null;

            if (cancellation is null)
            {
                return;
            }

            cancellation.Cancel();

            try
            {
                task?.Wait(1000);
            }
            catch
            {
                // Cancellation and a disappearing log are both expected here.
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void EmitFilteredGameEvent(
            string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            int markerIndex =
                line.IndexOf(
                    "[LimelightMP]",
                    StringComparison.OrdinalIgnoreCase);

            if (markerIndex >= 0)
            {
                string multiplayerLine =
                    line[markerIndex..];

                MultiplayerLogLevel level =
                    multiplayerLine.Contains(
                        "[ERROR]",
                        StringComparison.OrdinalIgnoreCase) ||
                    ContainsAny(
                        multiplayerLine,
                        "fatal",
                        "crash",
                        "exception",
                        "failed")
                        ? MultiplayerLogLevel.Error
                        : ContainsAny(
                            multiplayerLine,
                            "[NETWORK]",
                            "join=",
                            "listen",
                            "connect",
                            "admission",
                            "servertravel",
                            "map_transition")
                            ? MultiplayerLogLevel.Network
                            : ContainsAny(
                                multiplayerLine,
                                "keybind",
                                "hotkey",
                                "Ctrl+Shift",
                                "gameplay",
                                "camera=",
                                "couch_skills")
                                ? MultiplayerLogLevel.Gameplay
                                : MultiplayerLogLevel.Log;

                Log(level, multiplayerLine);
                return;
            }

            if (line.Contains(
                    "IpNetDriver listening on port",
                    StringComparison.OrdinalIgnoreCase))
            {
                Log(
                    MultiplayerLogLevel.Network,
                    $"Host game is listening on UDP {GamePort}.");
            }
            else if (line.Contains(
                         "Welcomed by server",
                         StringComparison.OrdinalIgnoreCase))
            {
                Log(
                    MultiplayerLogLevel.Network,
                    "The client was welcomed by the host.");
            }
            else if (ContainsAny(
                         line,
                         "NotifyAcceptedConnection",
                         "AddClientConnection"))
            {
                Log(
                    MultiplayerLogLevel.Network,
                    "The host accepted a client connection.");
            }
            else if (ContainsAny(
                         line,
                         "PreLogin failure",
                         "NetworkFailure",
                         "TravelFailure",
                         "incompatible_unique_net_id",
                         "ConnectionTimeout"))
            {
                Log(
                    MultiplayerLogLevel.Error,
                    line.Trim());
            }
            else if (line.Contains(
                         "UNetConnection::Close",
                         StringComparison.OrdinalIgnoreCase))
            {
                Log(
                    MultiplayerLogLevel.Network,
                    "A game network connection closed.");
            }
        }

        private static IReadOnlyList<string> ReadNewLines(
            string path,
            ref long offset)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return Array.Empty<string>();
            }

            try
            {
                long currentLength =
                    new FileInfo(path).Length;

                if (currentLength < offset)
                {
                    offset = 0;
                }

                if (currentLength == offset)
                {
                    return Array.Empty<string>();
                }

                List<string> lines =
                    new();

                using FileStream stream =
                    new(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite |
                        FileShare.Delete);

                stream.Seek(
                    offset,
                    SeekOrigin.Begin);

                using StreamReader reader =
                    new(
                        stream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true,
                        bufferSize: 4096,
                        leaveOpen: true);

                while (reader.ReadLine() is string line)
                {
                    lines.Add(line);
                }

                offset = stream.Length;
                return lines;
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static long GetCurrentLength(
            string path)
        {
            try
            {
                return File.Exists(path)
                    ? new FileInfo(path).Length
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool ContainsAny(
            string value,
            params string[] candidates)
        {
            return candidates.Any(candidate =>
                value.Contains(
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void RelayOutputReceived(
            string line)
        {
            MultiplayerLogLevel level =
                line.Contains(
                    "[ERROR]",
                    StringComparison.OrdinalIgnoreCase)
                    ? MultiplayerLogLevel.Error
                    : line.Contains(
                        "[NETWORK]",
                        StringComparison.OrdinalIgnoreCase)
                        ? MultiplayerLogLevel.Network
                        : line.Contains(
                            "[GAMEPLAY]",
                            StringComparison.OrdinalIgnoreCase)
                            ? MultiplayerLogLevel.Gameplay
                            : line.Contains(
                                "[WARNING]",
                                StringComparison.OrdinalIgnoreCase)
                                ? MultiplayerLogLevel.Warning
                                : MultiplayerLogLevel.Log;

            Log(level, line);
        }

        private void RelayExited(
            int exitCode)
        {
            if (ActiveRole == MultiplayerRole.None)
            {
                return;
            }

            ActiveRole = MultiplayerRole.None;
            StopGameLogMonitor();

            Log(
                exitCode == 0
                    ? MultiplayerLogLevel.Network
                    : MultiplayerLogLevel.Error,
                exitCode == 0
                    ? "The LimelightMP relay closed."
                    : $"The LimelightMP relay stopped unexpectedly (exit code {exitCode}).");
        }

        private void Log(
            MultiplayerLogLevel level,
            string message)
        {
            string cleaned =
                message.Trim();

            if (cleaned.Length == 0)
            {
                return;
            }

            lock (_logGate)
            {
                if (!string.IsNullOrWhiteSpace(
                        _sessionLogPath))
                {
                    try
                    {
                        File.AppendAllText(
                            _sessionLogPath,
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {cleaned}{Environment.NewLine}",
                            new UTF8Encoding(false));
                    }
                    catch
                    {
                        // A log file must never stop a live session.
                    }
                }
            }

            LogEmitted?.Invoke(
                level,
                cleaned);
        }
    }
}
