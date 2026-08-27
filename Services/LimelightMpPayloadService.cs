using Limelight.Models;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed partial class LimelightMpPayloadService
    {
        public const string ProductName = "LimelightMP";
        public const string Version = "0.1.4";
        public const int ProtocolVersion = 1;

        private const string ManifestResourceName =
            "Limelight.Payloads.Multiplayer.multiplayer-manifest.json";

        private const string ManagedModName =
            "LimelightMP";

        private const string NativeModName =
            "LimelightMPNative";

        private const string RoleMarkerName =
            ".limelightmp-role.json";

        private const string NativeMarkerName =
            ".limelightmp-native-managed";

        private const string UiPayloadBaseName =
            "LimelightMPUI";

        private const string UiMarkerName =
            ".limelightmp-ui-managed";

        private const string ModsMarker =
            "; LIMELIGHT_MP_MANAGED";

        private readonly Assembly _assembly =
            typeof(LimelightMpPayloadService).Assembly;

        private readonly JsonSerializerOptions _jsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            };

        public MultiplayerPayloadManifest Manifest =>
            LoadAndValidateManifest();

        public void ValidateEmbeddedPayloads()
        {
            MultiplayerPayloadManifest manifest =
                LoadAndValidateManifest();

            ValidateEmbeddedFile(
                manifest.HostScript,
                "host script");

            ValidateEmbeddedFile(
                manifest.ClientScript,
                "client script");

            ValidateEmbeddedFile(
                manifest.NativeBridge,
                "native controller bridge");

            ValidateEmbeddedFile(
                manifest.Relay,
                "controller relay");

            ValidateEmbeddedFile(
                manifest.UiPak,
                "loading-screen pak");

            ValidateEmbeddedFile(
                manifest.UiUtoc,
                "loading-screen table of contents");

            ValidateEmbeddedFile(
                manifest.UiUcas,
                "loading-screen asset container");
        }

        public MultiplayerInstalledRole Install(
            Ue4ssDetectionResult installation,
            MultiplayerRole role,
            string connectAddress,
            int gamePort)
        {
            if (role is not MultiplayerRole.Host and
                not MultiplayerRole.Client)
            {
                throw new InvalidOperationException(
                    "Choose a host or client multiplayer role.");
            }

            ValidateInstallation(installation);
            ValidateEmbeddedPayloads();

            if (string.IsNullOrWhiteSpace(connectAddress) ||
                connectAddress.Length > 255 ||
                !ConnectAddressPattern().IsMatch(connectAddress))
            {
                throw new InvalidOperationException(
                    "The multiplayer connection address is invalid.");
            }

            if (gamePort <= 1024 || gamePort > 65535)
            {
                throw new InvalidOperationException(
                    "The multiplayer game port is invalid.");
            }

            string modsDirectory =
                Path.GetFullPath(
                    installation.ModsDirectory);

            string modDirectory =
                GetChildPath(
                    modsDirectory,
                    ManagedModName);

            string nativeDirectory =
                GetChildPath(
                    modsDirectory,
                    NativeModName);

            string logicModsDirectory =
                GetLogicModsDirectory(
                    installation);

            EnsureManagedOrAbsent(
                modDirectory,
                RoleMarkerName,
                "A non-managed LimelightMP mod already exists. Limelight left it untouched.");

            EnsureManagedOrAbsent(
                nativeDirectory,
                NativeMarkerName,
                "A non-managed LimelightMP native bridge already exists. Limelight left it untouched.");

            EnsureManagedUiOrAbsent(
                logicModsDirectory);

            string transactionId =
                Guid.NewGuid().ToString("N");

            string stagedModDirectory =
                GetChildPath(
                    modsDirectory,
                    $".{ManagedModName}.installing-{transactionId}");

            string stagedNativeDirectory =
                GetChildPath(
                    modsDirectory,
                    $".{NativeModName}.installing-{transactionId}");

            string stagedUiDirectory =
                GetChildPath(
                    logicModsDirectory,
                    $".{UiPayloadBaseName}.installing-{transactionId}");

            string backupModDirectory =
                GetChildPath(
                    modsDirectory,
                    $".{ManagedModName}.backup-{transactionId}");

            string backupNativeDirectory =
                GetChildPath(
                    modsDirectory,
                    $".{NativeModName}.backup-{transactionId}");

            string backupUiDirectory =
                GetChildPath(
                    logicModsDirectory,
                    $".{UiPayloadBaseName}.backup-{transactionId}");

            Directory.CreateDirectory(modsDirectory);
            Directory.CreateDirectory(logicModsDirectory);

            MultiplayerInstalledRole marker =
                new()
                {
                    Product = ProductName,
                    Role = role,
                    Address = connectAddress,
                    Port = gamePort,
                    InstalledUtc = DateTimeOffset.UtcNow,
                    Version = Version
                };

            bool transactionCompleted =
                false;

            bool uiSwapStarted =
                false;

            try
            {
                CreateStagedRole(
                    stagedModDirectory,
                    role,
                    connectAddress,
                    gamePort,
                    marker);

                CreateStagedNativeBridge(
                    stagedNativeDirectory,
                    role);

                CreateStagedUi(
                    stagedUiDirectory);

                uiSwapStarted = true;
                MoveManagedUiToDirectory(
                    logicModsDirectory,
                    backupUiDirectory);

                if (Directory.Exists(modDirectory))
                {
                    Directory.Move(
                        modDirectory,
                        backupModDirectory);
                }

                Directory.Move(
                    stagedModDirectory,
                    modDirectory);

                if (Directory.Exists(nativeDirectory))
                {
                    Directory.Move(
                        nativeDirectory,
                        backupNativeDirectory);
                }

                Directory.Move(
                    stagedNativeDirectory,
                    nativeDirectory);

                MoveStagedUiToTarget(
                    stagedUiDirectory,
                    logicModsDirectory);

                UpdateModsFile(
                    installation.ModsDirectory);

                transactionCompleted = true;

                return marker;
            }
            catch
            {
                RestoreManagedDirectory(
                    modDirectory,
                    backupModDirectory,
                    RoleMarkerName);

                RestoreManagedDirectory(
                    nativeDirectory,
                    backupNativeDirectory,
                    NativeMarkerName);

                if (uiSwapStarted)
                {
                    RestoreManagedUi(
                        logicModsDirectory,
                        backupUiDirectory);
                }

                throw;
            }
            finally
            {
                DeleteDirectoryBestEffort(
                    stagedModDirectory);

                DeleteDirectoryBestEffort(
                    stagedNativeDirectory);

                DeleteDirectoryBestEffort(
                    stagedUiDirectory);

                if (transactionCompleted)
                {
                    DeleteDirectoryBestEffort(
                        backupModDirectory);

                    DeleteDirectoryBestEffort(
                        backupNativeDirectory);

                    DeleteDirectoryBestEffort(
                        backupUiDirectory);
                }
            }
        }

        public MultiplayerInstalledRole? ReadInstalledRole(
            Ue4ssDetectionResult installation)
        {
            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return null;
            }

            string markerPath =
                Path.Combine(
                    installation.ModsDirectory,
                    ManagedModName,
                    RoleMarkerName);

            if (!File.Exists(markerPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<MultiplayerInstalledRole>(
                    File.ReadAllText(markerPath),
                    _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public void Remove(
            Ue4ssDetectionResult installation)
        {
            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return;
            }

            string modsDirectory =
                Path.GetFullPath(
                    installation.ModsDirectory);

            string modDirectory =
                GetChildPath(
                    modsDirectory,
                    ManagedModName);

            string nativeDirectory =
                GetChildPath(
                    modsDirectory,
                    NativeModName);

            string logicModsDirectory =
                GetLogicModsDirectory(
                    installation);

            EnsureManagedUiOrAbsent(
                logicModsDirectory);

            RemoveManagedUi(
                logicModsDirectory);

            RemoveManagedDirectory(
                modDirectory,
                RoleMarkerName,
                "LimelightMP found an unmanaged mod folder and left it untouched.");

            RemoveManagedDirectory(
                nativeDirectory,
                NativeMarkerName,
                "LimelightMP found an unmanaged native folder and left it untouched.");

            string modsFile =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            if (File.Exists(modsFile))
            {
                List<string> lines =
                    File.ReadAllLines(modsFile)
                        .Where(line =>
                            !string.Equals(
                                line,
                                ModsMarker,
                                StringComparison.Ordinal) &&
                            !ManagedModLinePattern().IsMatch(line))
                        .ToList();

                WriteAllLinesAtomic(
                    modsFile,
                    lines);
            }
        }

        public void Deactivate(
            Ue4ssDetectionResult installation)
        {
            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory) ||
                !Directory.Exists(
                    installation.ModsDirectory))
            {
                return;
            }

            string modsDirectory =
                Path.GetFullPath(
                    installation.ModsDirectory);

            string modDirectory =
                GetChildPath(
                    modsDirectory,
                    ManagedModName);

            string nativeDirectory =
                GetChildPath(
                    modsDirectory,
                    NativeModName);

            if (Directory.Exists(modDirectory) &&
                !File.Exists(
                    Path.Combine(
                        modDirectory,
                        RoleMarkerName)))
            {
                return;
            }

            string modsFile =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            if (File.Exists(modsFile))
            {
                List<string> updated =
                    File.ReadAllLines(modsFile)
                        .Select(line =>
                            ManagedModLinePattern().IsMatch(line)
                                ? $"{ManagedModName} : 0"
                                : line)
                        .ToList();

                WriteAllLinesAtomic(
                    modsFile,
                    updated);
            }

            string nativeMarker =
                Path.Combine(
                    nativeDirectory,
                    NativeMarkerName);

            string nativeEnabled =
                Path.Combine(
                    nativeDirectory,
                    "enabled.txt");

            string nativeDisabled =
                Path.Combine(
                    nativeDirectory,
                    "enabled.disabled-by-limelightmp");

            if (File.Exists(nativeMarker) &&
                File.Exists(nativeEnabled))
            {
                File.Move(
                    nativeEnabled,
                    nativeDisabled,
                    overwrite: true);
            }
        }

        public string EnsureRelayExtracted()
        {
            MultiplayerPayloadManifest manifest =
                LoadAndValidateManifest();

            ValidateEmbeddedFile(
                manifest.Relay,
                "controller relay");

            string directory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Multiplayer",
                    Version);

            string targetPath =
                Path.Combine(
                    directory,
                    "LimelightMPRelay.exe");

            Directory.CreateDirectory(directory);

            if (FileMatches(
                    targetPath,
                    manifest.Relay))
            {
                return targetPath;
            }

            string temporaryPath =
                targetPath + ".installing";

            try
            {
                File.WriteAllBytes(
                    temporaryPath,
                    ReadEmbeddedBytes(
                        manifest.Relay.ResourceName));

                if (!FileMatches(
                        temporaryPath,
                        manifest.Relay))
                {
                    throw new InvalidOperationException(
                        "The embedded LimelightMP relay failed its integrity check.");
                }

                File.Move(
                    temporaryPath,
                    targetPath,
                    overwrite: true);
            }
            finally
            {
                DeleteFileBestEffort(
                    temporaryPath);
            }

            return targetPath;
        }

        private void CreateStagedRole(
            string stagedDirectory,
            MultiplayerRole role,
            string connectAddress,
            int gamePort,
            MultiplayerInstalledRole marker)
        {
            MultiplayerPayloadManifest manifest =
                LoadAndValidateManifest();

            MultiplayerPayloadFile scriptPayload =
                role == MultiplayerRole.Host
                    ? manifest.HostScript
                    : manifest.ClientScript;

            string script =
                Encoding.UTF8.GetString(
                    ReadEmbeddedBytes(
                        scriptPayload.ResourceName));

            string originalName =
                role == MultiplayerRole.Host
                    ? "LimelightMPLocalRenderHost"
                    : "LimelightMPLocalRenderClient";

            string originalNameLine =
                $"local MOD_NAME = \"{originalName}\"";

            string addressPlaceholder =
                "local CONNECT_ADDRESS = \"127.0.0.1:7777\" -- INSTALL_CONNECT_ADDRESS";

            if (!script.Contains(
                    originalNameLine,
                    StringComparison.Ordinal) ||
                !script.Contains(
                    addressPlaceholder,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The embedded multiplayer script is missing its installation markers.");
            }

            script =
                script
                    .Replace(
                        originalNameLine,
                        "local MOD_NAME = \"LimelightMP\"",
                        StringComparison.Ordinal)
                    .Replace(
                        addressPlaceholder,
                        $"local CONNECT_ADDRESS = \"{connectAddress}\" -- INSTALL_CONNECT_ADDRESS",
                        StringComparison.Ordinal);

            if (role == MultiplayerRole.Host)
            {
                string portPlaceholder =
                    "local LISTEN_PORT = 7777 -- INSTALL_LISTEN_PORT";

                if (!script.Contains(
                        portPlaceholder,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The embedded host script is missing its listen-port marker.");
                }

                script =
                    script.Replace(
                        portPlaceholder,
                        $"local LISTEN_PORT = {gamePort} -- INSTALL_LISTEN_PORT",
                        StringComparison.Ordinal);
            }

            string scriptsDirectory =
                Path.Combine(
                    stagedDirectory,
                    "Scripts");

            Directory.CreateDirectory(
                scriptsDirectory);

            File.WriteAllText(
                Path.Combine(
                    scriptsDirectory,
                    "main.lua"),
                script,
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(
                    stagedDirectory,
                    RoleMarkerName),
                JsonSerializer.Serialize(
                    marker,
                    _jsonOptions),
                new UTF8Encoding(false));
        }

        private void CreateStagedNativeBridge(
            string stagedDirectory,
            MultiplayerRole role)
        {
            MultiplayerPayloadManifest manifest =
                LoadAndValidateManifest();

            string dllDirectory =
                Path.Combine(
                    stagedDirectory,
                    "dlls");

            Directory.CreateDirectory(
                dllDirectory);

            string dllPath =
                Path.Combine(
                    dllDirectory,
                    "main.dll");

            File.WriteAllBytes(
                dllPath,
                ReadEmbeddedBytes(
                    manifest.NativeBridge.ResourceName));

            if (!FileMatches(
                    dllPath,
                    manifest.NativeBridge))
            {
                throw new InvalidOperationException(
                    "The staged multiplayer native bridge failed its integrity check.");
            }

            File.WriteAllText(
                Path.Combine(
                    stagedDirectory,
                    "enabled.txt"),
                string.Empty);

            File.WriteAllText(
                Path.Combine(
                    stagedDirectory,
                    NativeMarkerName),
                string.Empty);

            if (role == MultiplayerRole.Client)
            {
                File.WriteAllText(
                    Path.Combine(
                        stagedDirectory,
                        "client-mode.txt"),
                    string.Empty);
            }
        }

        private void CreateStagedUi(
            string stagedDirectory)
        {
            MultiplayerPayloadManifest manifest =
                LoadAndValidateManifest();

            Directory.CreateDirectory(
                stagedDirectory);

            foreach ((string fileName, MultiplayerPayloadFile payload) in
                     GetUiPayloads(manifest))
            {
                string targetPath =
                    Path.Combine(
                        stagedDirectory,
                        fileName);

                File.WriteAllBytes(
                    targetPath,
                    ReadEmbeddedBytes(
                        payload.ResourceName));

                if (!FileMatches(
                        targetPath,
                        payload))
                {
                    throw new InvalidOperationException(
                        $"The staged multiplayer UI file '{fileName}' failed its integrity check.");
                }
            }

            File.WriteAllText(
                Path.Combine(
                    stagedDirectory,
                    UiMarkerName),
                Version,
                new UTF8Encoding(false));
        }

        private static void MoveManagedUiToDirectory(
            string logicModsDirectory,
            string backupDirectory)
        {
            string markerPath =
                Path.Combine(
                    logicModsDirectory,
                    UiMarkerName);

            if (!File.Exists(markerPath))
            {
                return;
            }

            Directory.CreateDirectory(
                backupDirectory);

            foreach (string fileName in GetUiFileNames(includeMarker: true))
            {
                string sourcePath =
                    Path.Combine(
                        logicModsDirectory,
                        fileName);

                if (File.Exists(sourcePath))
                {
                    File.Move(
                        sourcePath,
                        Path.Combine(
                            backupDirectory,
                            fileName));
                }
            }
        }

        private static void MoveStagedUiToTarget(
            string stagedDirectory,
            string logicModsDirectory)
        {
            foreach (string fileName in GetUiFileNames(includeMarker: false))
            {
                File.Move(
                    Path.Combine(
                        stagedDirectory,
                        fileName),
                    Path.Combine(
                        logicModsDirectory,
                        fileName),
                    overwrite: true);
            }

            File.Move(
                Path.Combine(
                    stagedDirectory,
                    UiMarkerName),
                Path.Combine(
                    logicModsDirectory,
                    UiMarkerName),
                overwrite: true);
        }

        private static void UpdateModsFile(
            string modsDirectory)
        {
            string modsFile =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            List<string> lines =
                File.Exists(modsFile)
                    ? File.ReadAllLines(modsFile).ToList()
                    : new List<string>();

            List<string> updated =
                new();

            foreach (string line in lines)
            {
                if (string.Equals(
                        line,
                        ModsMarker,
                        StringComparison.Ordinal) ||
                    ManagedModLinePattern().IsMatch(line))
                {
                    continue;
                }

                Match oldProbe =
                    OldProbeLinePattern().Match(line);

                updated.Add(
                    oldProbe.Success
                        ? $"{oldProbe.Groups[1].Value} : 0"
                        : line);
            }

            updated.Add(ModsMarker);
            updated.Add($"{ManagedModName} : 1");

            WriteAllLinesAtomic(
                modsFile,
                updated);
        }

        private MultiplayerPayloadManifest LoadAndValidateManifest()
        {
            using Stream stream =
                _assembly.GetManifestResourceStream(
                    ManifestResourceName) ??
                throw new InvalidOperationException(
                    "The embedded LimelightMP manifest could not be found.");

            MultiplayerPayloadManifest? manifest =
                JsonSerializer.Deserialize<MultiplayerPayloadManifest>(
                    stream,
                    _jsonOptions);

            if (manifest is null ||
                manifest.SchemaVersion != 1 ||
                !string.Equals(
                    manifest.Version,
                    Version,
                    StringComparison.Ordinal) ||
                manifest.ProtocolVersion != ProtocolVersion)
            {
                throw new InvalidOperationException(
                    "The embedded LimelightMP manifest is invalid or incompatible.");
            }

            ValidateManifestFile(
                manifest.HostScript);

            ValidateManifestFile(
                manifest.ClientScript);

            ValidateManifestFile(
                manifest.NativeBridge);

            ValidateManifestFile(
                manifest.Relay);

            ValidateManifestFile(
                manifest.UiPak);

            ValidateManifestFile(
                manifest.UiUtoc);

            ValidateManifestFile(
                manifest.UiUcas);

            return manifest;
        }

        private static void ValidateManifestFile(
            MultiplayerPayloadFile file)
        {
            if (string.IsNullOrWhiteSpace(
                    file.ResourceName) ||
                file.Size <= 0 ||
                file.Sha256.Length != 64)
            {
                throw new InvalidOperationException(
                    "The embedded LimelightMP manifest contains an invalid file entry.");
            }
        }

        private void ValidateEmbeddedFile(
            MultiplayerPayloadFile file,
            string displayName)
        {
            byte[] bytes =
                ReadEmbeddedBytes(
                    file.ResourceName);

            if (bytes.LongLength != file.Size ||
                !string.Equals(
                    Convert.ToHexString(
                        SHA256.HashData(bytes)),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The embedded LimelightMP {displayName} failed its integrity check.");
            }
        }

        private byte[] ReadEmbeddedBytes(
            string resourceName)
        {
            using Stream stream =
                _assembly.GetManifestResourceStream(
                    resourceName) ??
                throw new InvalidOperationException(
                    $"The embedded LimelightMP resource '{resourceName}' could not be found.");

            using MemoryStream memory =
                new();

            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static bool FileMatches(
            string filePath,
            MultiplayerPayloadFile manifest)
        {
            if (!File.Exists(filePath) ||
                new FileInfo(filePath).Length != manifest.Size)
            {
                return false;
            }

            using FileStream stream =
                File.OpenRead(filePath);

            return string.Equals(
                Convert.ToHexString(
                    SHA256.HashData(stream)),
                manifest.Sha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateInstallation(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.ModsDirectory) ||
                !Directory.Exists(
                    installation.ModsDirectory) ||
                string.IsNullOrWhiteSpace(
                    installation.Win64Directory) ||
                !Directory.Exists(
                    installation.Win64Directory))
            {
                throw new InvalidOperationException(
                    "Limelight's Live Loader (UE4SS) must be installed or repaired before multiplayer can start.");
            }
        }

        private static string GetLogicModsDirectory(
            Ue4ssDetectionResult installation)
        {
            DirectoryInfo win64 =
                new(
                    Path.GetFullPath(
                        installation.Win64Directory));

            DirectoryInfo? binaries =
                win64.Parent;

            DirectoryInfo? pagoda =
                binaries?.Parent;

            if (!string.Equals(
                    win64.Name,
                    "Win64",
                    StringComparison.OrdinalIgnoreCase) ||
                binaries is null ||
                !string.Equals(
                    binaries.Name,
                    "Binaries",
                    StringComparison.OrdinalIgnoreCase) ||
                pagoda is null ||
                !string.Equals(
                    pagoda.Name,
                    "Pagoda",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "LimelightMP could not safely locate the game's LogicMods folder.");
            }

            string contentDirectory =
                GetChildPath(
                    pagoda.FullName,
                    "Content");

            string paksDirectory =
                GetChildPath(
                    contentDirectory,
                    "Paks");

            return GetChildPath(
                paksDirectory,
                "LogicMods");
        }

        private static (string FileName, MultiplayerPayloadFile Payload)[] GetUiPayloads(
            MultiplayerPayloadManifest manifest) =>
            new[]
            {
                ($"{UiPayloadBaseName}.pak", manifest.UiPak),
                ($"{UiPayloadBaseName}.utoc", manifest.UiUtoc),
                ($"{UiPayloadBaseName}.ucas", manifest.UiUcas)
            };

        private static string[] GetUiFileNames(
            bool includeMarker)
        {
            List<string> fileNames =
                new()
                {
                    $"{UiPayloadBaseName}.pak",
                    $"{UiPayloadBaseName}.utoc",
                    $"{UiPayloadBaseName}.ucas"
                };

            if (includeMarker)
            {
                fileNames.Add(
                    UiMarkerName);
            }

            return fileNames.ToArray();
        }

        private static void EnsureManagedUiOrAbsent(
            string logicModsDirectory)
        {
            if (!Directory.Exists(logicModsDirectory))
            {
                return;
            }

            bool hasMarker =
                File.Exists(
                    Path.Combine(
                        logicModsDirectory,
                        UiMarkerName));

            bool hasPayload =
                GetUiFileNames(includeMarker: false)
                    .Any(fileName =>
                        File.Exists(
                            Path.Combine(
                                logicModsDirectory,
                                fileName)));

            if (hasPayload && !hasMarker)
            {
                throw new InvalidOperationException(
                    "LimelightMP found unmanaged loading-screen files and left them untouched.");
            }
        }

        private static void RemoveManagedUi(
            string logicModsDirectory)
        {
            string markerPath =
                Path.Combine(
                    logicModsDirectory,
                    UiMarkerName);

            if (!File.Exists(markerPath))
            {
                return;
            }

            foreach (string fileName in GetUiFileNames(includeMarker: true))
            {
                string filePath =
                    Path.Combine(
                        logicModsDirectory,
                        fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        private static void RestoreManagedUi(
            string logicModsDirectory,
            string backupDirectory)
        {
            try
            {
                foreach (string fileName in GetUiFileNames(includeMarker: true))
                {
                    string targetPath =
                        Path.Combine(
                            logicModsDirectory,
                            fileName);

                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                }

                if (!Directory.Exists(backupDirectory))
                {
                    return;
                }

                foreach (string fileName in GetUiFileNames(includeMarker: true))
                {
                    string backupPath =
                        Path.Combine(
                            backupDirectory,
                            fileName);

                    if (File.Exists(backupPath))
                    {
                        File.Move(
                            backupPath,
                            Path.Combine(
                                logicModsDirectory,
                                fileName));
                    }
                }
            }
            catch
            {
                // I keep the original exception in charge. The backup stays
                // nearby if Windows decides this particular disco needs a lock.
            }
        }

        private static string GetChildPath(
            string parentDirectory,
            string childName)
        {
            string parent =
                Path.GetFullPath(parentDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string child =
                Path.GetFullPath(
                    Path.Combine(
                        parent,
                        childName));

            if (!child.StartsWith(
                    parent + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "LimelightMP refused an unsafe managed path.");
            }

            return child;
        }

        private static void EnsureManagedOrAbsent(
            string directory,
            string markerName,
            string errorMessage)
        {
            if (Directory.Exists(directory) &&
                !File.Exists(
                    Path.Combine(
                        directory,
                        markerName)))
            {
                throw new InvalidOperationException(
                    errorMessage);
            }
        }

        private static void RemoveManagedDirectory(
            string directory,
            string markerName,
            string errorMessage)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            if (!File.Exists(
                    Path.Combine(
                        directory,
                        markerName)))
            {
                throw new InvalidOperationException(
                    errorMessage);
            }

            Directory.Delete(
                directory,
                recursive: true);
        }

        private static void RestoreManagedDirectory(
            string targetDirectory,
            string backupDirectory,
            string markerName)
        {
            try
            {
                if (Directory.Exists(targetDirectory) &&
                    File.Exists(
                        Path.Combine(
                            targetDirectory,
                            markerName)))
                {
                    Directory.Delete(
                        targetDirectory,
                        recursive: true);
                }

                if (Directory.Exists(backupDirectory))
                {
                    Directory.Move(
                        backupDirectory,
                        targetDirectory);
                }
            }
            catch
            {
                // Preserve the original exception. A backup remains in the
                // Mods directory if Windows prevents the rollback.
            }
        }

        private static void WriteAllLinesAtomic(
            string path,
            IEnumerable<string> lines)
        {
            string temporaryPath =
                path + ".limelightmp-writing";

            try
            {
                File.WriteAllLines(
                    temporaryPath,
                    lines,
                    new UTF8Encoding(false));

                File.Move(
                    temporaryPath,
                    path,
                    overwrite: true);
            }
            finally
            {
                DeleteFileBestEffort(
                    temporaryPath);
            }
        }

        private static void DeleteDirectoryBestEffort(
            string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(
                        path,
                        recursive: true);
                }
            }
            catch
            {
                // Transaction cleanup must not hide the original result.
            }
        }

        private static void DeleteFileBestEffort(
            string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Transaction cleanup must not hide the original result.
            }
        }

        [GeneratedRegex(
            "^[A-Za-z0-9.\\-\\[\\]:]+$",
            RegexOptions.CultureInvariant)]
        private static partial Regex ConnectAddressPattern();

        [GeneratedRegex(
            "^\\s*LimelightMP\\s*:\\s*[01]\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ManagedModLinePattern();

        [GeneratedRegex(
            "^\\s*(DiscoOnlineProbe|DiscoOnlineClientProbe)\\s*:\\s*1\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex OldProbeLinePattern();
    }
}
