using Limelight.Models;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class LimelightMpSteamSessionService : IDisposable
    {
        private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
        private CancellationTokenSource? _cancellation;
        private MultiplayerSteamRequest? _request;
        private string _nativeDirectory = string.Empty;

        public event Action<MultiplayerSteamStatus>? StatusChanged;

        public string RequestId => _request?.RequestId ?? string.Empty;

        public void Prepare(string nativeDirectory, string gameExecutable,
            MultiplayerRole role, string token, string lobbyId, string version, int protocolVersion, bool nativePlayerMode)
        {
            Stop();
            string fullDirectory = Path.GetFullPath(nativeDirectory);
            string fullExecutable = Path.GetFullPath(gameExecutable);
            if (!File.Exists(Path.Combine(fullDirectory, ".limelightmp-native-managed")) ||
                !File.Exists(fullExecutable))
            {
                throw new InvalidOperationException("Steam transport needs the installed managed multiplayer bridge and game executable.");
            }

            using Process launcher = Process.GetCurrentProcess();
            using FileStream executable = File.OpenRead(fullExecutable);
            MultiplayerSteamRequest request = new()
            {
                Role = role,
                RequestId = Guid.NewGuid().ToString("N"),
                Token = token,
                LobbyId = lobbyId,
                PayloadVersion = version,
                ProtocolVersion = protocolVersion,
                GameSha256 = Convert.ToHexString(SHA256.HashData(executable)),
                LauncherPid = launcher.Id,
                LauncherStartTime = launcher.StartTime.ToUniversalTime().ToFileTimeUtc(),
                NativePlayerMode = nativePlayerMode
            };

            AtomicWrite(Path.Combine(fullDirectory, "steam-session.ini"),
                LimelightMpSteamContract.BuildConfiguration(request));
            _nativeDirectory = fullDirectory;
            _request = request;
            CancellationTokenSource cancellation = new();
            _cancellation = cancellation;
            _ = Task.Run(() => MonitorAsync(fullDirectory, fullExecutable, request, cancellation.Token));
        }

        public void Stop()
        {
            CancellationTokenSource? cancellation = Interlocked.Exchange(ref _cancellation, null);
            cancellation?.Cancel();
            cancellation?.Dispose();
            MultiplayerSteamRequest? request = _request;
            _request = null;
            if (request is null) return;
            try
            {
                AtomicWrite(Path.Combine(_nativeDirectory, "steam-stop.txt"), request.RequestId);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose() => Stop();

        private async Task MonitorAsync(string directory, string gameExecutable,
            MultiplayerSteamRequest request, CancellationToken cancellation)
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            DateTimeOffset lastValid = started;
            bool receivedStatus = false;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    MultiplayerSteamStatus? status = ReadStatus(Path.Combine(directory, "steam-status.json"));
                    if (status is not null &&
                        LimelightMpSteamContract.MatchesRequest(status, request, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) &&
                        MatchesGameProcess(status, gameExecutable))
                    {
                        receivedStatus = true;
                        lastValid = DateTimeOffset.UtcNow;
                        if (cancellation.IsCancellationRequested) return;
                        StatusChanged?.Invoke(status);
                        if (status.State is "failed" or "stopped") return;
                    }
                    else if (DateTimeOffset.UtcNow - lastValid > TimeSpan.FromSeconds(receivedStatus ? 20 : 180))
                    {
                        if (cancellation.IsCancellationRequested) return;
                        StatusChanged?.Invoke(new MultiplayerSteamStatus
                        {
                            RequestId = request.RequestId,
                            State = "failed",
                            Message = receivedStatus
                                ? "The game stopped reporting Steam transport status."
                                : "The game's Steam transport did not start within three minutes.",
                            NativePlayerMode = request.NativePlayerMode
                        });
                        return;
                    }

                    await Task.Delay(1000, cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        private MultiplayerSteamStatus? ReadStatus(string path)
        {
            try
            {
                using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (file.Length > 16384) return null;
                return JsonSerializer.Deserialize<MultiplayerSteamStatus>(file, _json);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (JsonException) { return null; }
        }

        private static bool MatchesGameProcess(MultiplayerSteamStatus status, string expectedExecutable)
        {
            try
            {
                using Process process = Process.GetProcessById(status.Pid);
                return !process.HasExited &&
                    process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture) == status.ProcessStartTime &&
                    string.Equals(process.MainModule?.FileName, expectedExecutable, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        private static void AtomicWrite(string path, string content)
        {
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
