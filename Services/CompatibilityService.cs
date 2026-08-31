using Limelight.Models;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed class CompatibilityService
    {
        public const string SupportedSteamBuildId =
            "24599852";

        public const string SupportedGameVersion =
            "++brainjar+release-CL-31326";

        public const string SupportedGameUpdateName =
            "The Midsummer Update Takes the Stage!";

        public const string SupportedGameUpdateReleasedLabel =
            "3 AUGUST 2026";

        public const string SupportedBuildPublishedLabel =
            "6 AUGUST 2026 · 21:30 UTC";

        public const string SupportedNativeBridgeVersion =
            "0.1.14";

        private const string SteamAppId =
            "3404260";

        private readonly Ue4ssDetectionService _ue4ssDetectionService;
        private readonly DeadAsDiscoUe4ssConfigurationService _ue4ssConfigurationService;
        private readonly LiveLoaderBridgeService _liveLoaderBridgeService;
        private readonly NativeBridgeInstallerService _nativeBridgeInstallerService;
        private readonly StagehandPayloadService _stagehandPayloadService;

        public CompatibilityService(
            Ue4ssDetectionService ue4ssDetectionService,
            DeadAsDiscoUe4ssConfigurationService ue4ssConfigurationService,
            LiveLoaderBridgeService liveLoaderBridgeService,
            NativeBridgeInstallerService nativeBridgeInstallerService,
            StagehandPayloadService stagehandPayloadService)
        {
            _ue4ssDetectionService = ue4ssDetectionService;
            _ue4ssConfigurationService = ue4ssConfigurationService;
            _liveLoaderBridgeService = liveLoaderBridgeService;
            _nativeBridgeInstallerService = nativeBridgeInstallerService;
            _stagehandPayloadService = stagehandPayloadService;
        }

        public LocalCompatibilityResult Check(
            string? gameDirectory)
        {
            bool gameConnected =
                !string.IsNullOrWhiteSpace(gameDirectory) &&
                Directory.Exists(gameDirectory) &&
                File.Exists(
                    Path.Combine(
                        gameDirectory,
                        "Pagoda.exe"));

            string detectedSteamBuildId =
                gameConnected
                    ? ReadSteamBuildId(gameDirectory!)
                    : string.Empty;

            string detectedGameVersion =
                gameConnected
                    ? ReadGameVersion(gameDirectory!)
                    : string.Empty;

            bool gameBuildDetected =
                !string.IsNullOrWhiteSpace(detectedSteamBuildId) ||
                !string.IsNullOrWhiteSpace(detectedGameVersion);

            // I check both identifiers when Steam and the executable provide
            // them. This catches an interrupted update instead of trusting one
            // half of an installation that no longer matches the other.
            bool steamBuildCompatible =
                string.IsNullOrWhiteSpace(detectedSteamBuildId) ||
                string.Equals(
                    detectedSteamBuildId,
                    SupportedSteamBuildId,
                    StringComparison.OrdinalIgnoreCase);

            bool gameVersionCompatible =
                string.IsNullOrWhiteSpace(detectedGameVersion) ||
                string.Equals(
                    detectedGameVersion,
                    SupportedGameVersion,
                    StringComparison.OrdinalIgnoreCase);

            Ue4ssDetectionResult loader =
                gameConnected
                    ? _ue4ssDetectionService.Detect(gameDirectory)
                    : new Ue4ssDetectionResult();

            NativeBridgePayloadManifest? manifest =
                SafeManifest();

            return new LocalCompatibilityResult
            {
                LimelightVersion = ReadApplicationVersion(),
                SupportedSteamBuildId = SupportedSteamBuildId,
                SupportedGameVersion = SupportedGameVersion,
                SupportedGameUpdateName = SupportedGameUpdateName,
                SupportedGameUpdateReleasedLabel =
                    SupportedGameUpdateReleasedLabel,
                SupportedBuildPublishedLabel =
                    SupportedBuildPublishedLabel,
                DetectedSteamBuildId = detectedSteamBuildId,
                DetectedGameVersion = detectedGameVersion,
                NativeBridgeVersion = manifest?.BridgeVersion ?? "UNAVAILABLE",
                Ue4ssVersion = manifest?.Ue4ssVersion ?? Ue4ssReleaseService.CompatibleVersion,
                GameConnected = gameConnected,
                GameBuildDetected = gameBuildDetected,
                GameBuildCompatible =
                    gameBuildDetected &&
                    steamBuildCompatible &&
                    gameVersionCompatible,
                EmbeddedPayloadCompatible =
                    string.Equals(
                        manifest?.BridgeVersion,
                        SupportedNativeBridgeVersion,
                        StringComparison.OrdinalIgnoreCase) &&
                    SafeCheck(() =>
                        _nativeBridgeInstallerService.IsEmbeddedPayloadCompatible()) &&
                    SafeCheck(() =>
                        _stagehandPayloadService.IsEmbeddedPayloadCompatible()),
                Ue4ssInstalled = loader.IsInstalled,
                Ue4ssCompatible =
                    SafeCheck(() =>
                        _ue4ssConfigurationService.IsRuntimeCompatible(loader)),
                Ue4ssConfigured =
                    SafeCheck(() =>
                        _ue4ssConfigurationService.IsConfigured(loader)),
                LuaBridgeInstalled =
                    SafeCheck(() =>
                        _liveLoaderBridgeService.IsInstalled(loader)),
                NativeBridgeCurrent =
                    SafeCheck(() =>
                        _nativeBridgeInstallerService.IsCurrentVersionInstalled(loader)),
                StagehandCurrent =
                    SafeCheck(() =>
                        _stagehandPayloadService.IsCurrentVersionInstalled(loader))
            };
        }

        private NativeBridgePayloadManifest? SafeManifest()
        {
            try
            {
                return _nativeBridgeInstallerService.Manifest;
            }
            catch
            {
                return null;
            }
        }

        private static bool SafeCheck(
            Func<bool> check)
        {
            try
            {
                return check();
            }
            catch
            {
                return false;
            }
        }

        private static string ReadSteamBuildId(
            string gameDirectory)
        {
            try
            {
                DirectoryInfo? commonDirectory =
                    Directory.GetParent(gameDirectory);

                DirectoryInfo? steamAppsDirectory =
                    commonDirectory?.Parent;

                if (steamAppsDirectory is null)
                {
                    return string.Empty;
                }

                string manifestPath =
                    Path.Combine(
                        steamAppsDirectory.FullName,
                        $"appmanifest_{SteamAppId}.acf");

                if (!File.Exists(manifestPath))
                {
                    return string.Empty;
                }

                Match match =
                    Regex.Match(
                        File.ReadAllText(manifestPath),
                        "\\\"buildid\\\"\\s+\\\"(?<build>\\d+)\\\"",
                        RegexOptions.IgnoreCase);

                return match.Success
                    ? match.Groups["build"].Value
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ReadGameVersion(
            string gameDirectory)
        {
            string[] candidates =
            {
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Binaries",
                    "Win64",
                    "PagodaSteam-Win64-Shipping.exe"),
                Path.Combine(
                    gameDirectory,
                    "Pagoda.exe")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    FileVersionInfo version =
                        FileVersionInfo.GetVersionInfo(candidate);

                    string value =
                        version.ProductVersion ??
                        version.FileVersion ??
                        string.Empty;

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
                catch
                {
                    // The Steam build ID can still verify the installation if
                    // Windows cannot read version information from this file.
                }
            }

            return string.Empty;
        }

        private static string ReadApplicationVersion()
        {
            Assembly assembly =
                Assembly.GetEntryAssembly() ??
                typeof(CompatibilityService).Assembly;

            string version =
                assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ??
                assembly
                    .GetName()
                    .Version?
                    .ToString() ??
                "UNKNOWN";

            int metadataStart =
                version.IndexOf('+');

            return metadataStart >= 0
                ? version[..metadataStart]
                : version;
        }
    }
}
