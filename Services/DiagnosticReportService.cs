using Limelight.Models;
using System.IO;
using System.Reflection;
using System.Text;

namespace Limelight.Services
{
    public sealed class DiagnosticReportService
    {
        public string CreateReport(
            AppSettings settings,
            LiveSessionState session,
            string? gameDirectory,
            bool isGameRunning,
            Ue4ssDetectionResult loader,
            LocalCompatibilityResult compatibility,
            LiveSessionCleanupResult stagingSnapshot)
        {
            var report =
                new StringBuilder();

            Assembly? entryAssembly =
                Assembly.GetEntryAssembly();

            string version =
                entryAssembly?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ??
                entryAssembly?
                    .GetName()
                    .Version?
                    .ToString() ??
                "Unknown";

            InstalledMod? activeMod =
                settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            report.AppendLine("LIMELIGHT DIAGNOSTIC REPORT");
            report.AppendLine("===========================");
            report.AppendLine($"Created (UTC): {DateTimeOffset.UtcNow:O}");
            report.AppendLine($"Limelight version: {version}");
            report.AppendLine($"Windows: {Environment.OSVersion}");
            report.AppendLine($".NET: {Environment.Version}");
            report.AppendLine();

            report.AppendLine("APPLICATION");
            report.AppendLine($"Game connected: {!string.IsNullOrWhiteSpace(gameDirectory)}");
            report.AppendLine($"Game running: {isGameRunning}");
            report.AppendLine($"Installed mods: {settings.InstalledMods.Count}");
            report.AppendLine($"Active mod: {activeMod?.DisplayName ?? "None"}");
            report.AppendLine($"Pending deployment: {!string.IsNullOrWhiteSpace(settings.PendingDeploymentModId)}");
            report.AppendLine();

            report.AppendLine("COMPATIBILITY");
            report.AppendLine($"Overall status: {compatibility.Status}");
            report.AppendLine($"Live Loader allowed: {compatibility.IsLiveLoaderCompatible}");
            report.AppendLine($"Supported Steam build: {compatibility.SupportedSteamBuildId}");
            report.AppendLine($"Detected Steam build: {ValueOrNone(compatibility.DetectedSteamBuildId)}");
            report.AppendLine($"Supported game version: {compatibility.SupportedGameVersion}");
            report.AppendLine($"Detected game version: {ValueOrNone(compatibility.DetectedGameVersion)}");
            report.AppendLine($"Game build compatible: {compatibility.GameBuildCompatible}");
            report.AppendLine($"Compatibility detail: {compatibility.Detail}");
            report.AppendLine();

            report.AppendLine("LIVE LOADER");
            NativeBridgePayloadManifest? bridgeManifest =
                SafeBridgeManifest();

            report.AppendLine(
                $"Expected UE4SS: {bridgeManifest?.Ue4ssVersion ?? "Unknown"}");
            report.AppendLine(
                $"Expected native bridge: {bridgeManifest?.BridgeVersion ?? "Unknown"}");
            report.AppendLine($"UE4SS installed: {loader.IsInstalled}");
            report.AppendLine($"UE4SS partial install: {loader.IsPartiallyInstalled}");
            report.AppendLine($"Runtime compatible: {SafeRuntimeCompatibility(loader)}");
            report.AppendLine($"Lua bridge installed: {SafeBridgeInstalled(loader)}");
            report.AppendLine($"Native bridge current: {SafeNativeBridgeCurrent(loader)}");
            report.AppendLine($"Stagehand current: {compatibility.StagehandCurrent}");
            report.AppendLine($"Lua bridge online: {SafeBridgeOnline()}");
            report.AppendLine();

            report.AppendLine("LIVE SESSION");
            report.AppendLine($"Session: {ShortSessionId(session.SessionId)}");
            report.AppendLine($"Status: {session.Status}");
            report.AppendLine($"Activation in progress: {session.ActivationInProgress}");
            report.AppendLine($"Successful switches: {session.SuccessfulSwitches}");
            report.AppendLine($"Currently mounted containers: {LiveSessionService.CountMountedContainers(session)}");
            report.AppendLine($"Retired containers: {session.Mounts.Count(record => record.WasUnmounted)}");
            report.AppendLine($"Staged files: {stagingSnapshot.DeletedFileCount}");
            report.AppendLine($"Staged bytes: {stagingSnapshot.DeletedBytes}");
            report.AppendLine($"Last error: {ValueOrNone(session.LastError)}");
            report.AppendLine($"Last recovery: {ValueOrNone(session.LastRecoveryMessage)}");

            foreach (LiveSessionMountRecord mount in session.Mounts)
            {
                report.AppendLine(
                    $"  Container: mod={mount.ModName}; file={Path.GetFileName(mount.PakPath)}; " +
                    $"generation={ShortSessionId(mount.GenerationId)}; order={mount.MountOrder}; " +
                    $"mounted={mount.WasMounted}; unmounted={mount.WasUnmounted}; " +
                    $"mountedAt={mount.MountedAt:O}; unmountedAt={mount.UnmountedAt:O}; " +
                    $"retirementError={ValueOrNone(mount.RetirementError)}");
            }

            report.AppendLine();
            report.AppendLine("RECENT UE4SS EVENTS");
            AppendRelevantLogLines(
                report,
                loader.LogPath,
                gameDirectory);

            return SanitizeText(
                report.ToString(),
                gameDirectory);
        }

        private static string ShortSessionId(
            string sessionId)
        {
            return string.IsNullOrWhiteSpace(sessionId)
                ? "None"
                : sessionId[..Math.Min(8, sessionId.Length)];
        }

        private static string ValueOrNone(
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "None"
                : value;
        }

        private static void AppendRelevantLogLines(
            StringBuilder report,
            string logPath,
            string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(logPath) ||
                !File.Exists(logPath))
            {
                report.AppendLine("UE4SS log was not available.");
                return;
            }

            try
            {
                using FileStream logStream =
                    new(
                        logPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite |
                        FileShare.Delete);

                using StreamReader logReader =
                    new(
                        logStream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true);

                Queue<string> relevantLineQueue =
                    new();

                // UE4SS keeps writing for the whole game session. I share its
                // live handle and retain only the newest useful report lines.
                while (logReader.ReadLine() is string line)
                {
                    bool isRelevant =
                        line.Contains(
                            "limelight",
                            StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(
                            "error",
                            StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(
                            "warning",
                            StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(
                            "exception",
                            StringComparison.OrdinalIgnoreCase);

                    if (!isRelevant)
                    {
                        continue;
                    }

                    relevantLineQueue.Enqueue(line);

                    if (relevantLineQueue.Count > 200)
                    {
                        relevantLineQueue.Dequeue();
                    }
                }

                string[] relevantLines =
                    relevantLineQueue.ToArray();

                if (relevantLines.Length == 0)
                {
                    report.AppendLine("No matching warning or Limelight events were found.");
                    return;
                }

                foreach (string line in relevantLines)
                {
                    report.AppendLine(
                        SanitizeText(
                            line,
                            gameDirectory));
                }
            }
            catch (Exception exception)
            {
                report.AppendLine(
                    $"UE4SS log could not be read: {exception.Message}");
            }
        }

        public static string SanitizeText(
            string text,
            string? gameDirectory,
            params string?[] secrets)
        {
            var replacements =
                new List<KeyValuePair<string, string>>
                {
                    new(
                        AppContext.BaseDirectory,
                        "<APPLICATION_DIRECTORY>"),
                    new(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "<LOCAL_APP_DATA>"),
                    new(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile),
                        "<USER_PROFILE>")
                };

            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                replacements.Add(
                    new KeyValuePair<string, string>(
                        gameDirectory,
                        "<GAME_DIRECTORY>"));
            }

            string redacted = text;

            foreach ((string path, string replacement) in
                     replacements.OrderByDescending(item =>
                         item.Key.Length))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    redacted = redacted.Replace(
                        path,
                        replacement,
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            if (!string.IsNullOrWhiteSpace(Environment.UserName))
            {
                redacted = redacted.Replace(
                    Environment.UserName,
                    "<WINDOWS_USER>",
                    StringComparison.OrdinalIgnoreCase);
            }

            foreach (string? secret in secrets)
            {
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    redacted = redacted.Replace(
                        secret,
                        "<PRIVATE_VALUE>",
                        StringComparison.Ordinal);
                }
            }

            return redacted;
        }

        private static bool SafeRuntimeCompatibility(
            Ue4ssDetectionResult loader)
        {
            try
            {
                return new DeadAsDiscoUe4ssConfigurationService()
                    .IsRuntimeCompatible(loader);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeBridgeInstalled(
            Ue4ssDetectionResult loader)
        {
            try
            {
                return new LiveLoaderBridgeService()
                    .IsInstalled(loader);
            }
            catch
            {
                return false;
            }
        }

        private static NativeBridgePayloadManifest? SafeBridgeManifest()
        {
            try
            {
                return new NativeBridgeInstallerService()
                    .Manifest;
            }
            catch
            {
                return null;
            }
        }

        private static bool SafeNativeBridgeCurrent(
            Ue4ssDetectionResult loader)
        {
            try
            {
                return new NativeBridgeInstallerService()
                    .IsCurrentVersionInstalled(loader);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeBridgeOnline()
        {
            try
            {
                return new LiveLoaderBridgeService()
                    .IsOnline();
            }
            catch
            {
                return false;
            }
        }
    }
}
