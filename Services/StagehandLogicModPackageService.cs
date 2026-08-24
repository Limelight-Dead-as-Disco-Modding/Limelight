using Limelight.Models;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed partial class StagehandLogicModPackageService
    {
        private const long MaximumPackageSize = 2 * 1024 * 1024;
        private const long MaximumManifestSize = 64 * 1024;
        private const long MaximumScriptSize = 1024 * 1024;
        private const string RuntimeModName = "LimelightStagehand";
        private const string ManagedMarkerName = ".limelight-stagehand-script.json";
        private const string DisabledMarkerName = ".stagehand-disabled";
        private const string ManagedMarkerProduct = "Limelight Stagehand Script";

        private static readonly UTF8Encoding StrictUtf8 =
            new(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

        private static readonly JsonSerializerOptions CanonicalJsonOptions =
            new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

        private static readonly HashSet<string> SupportedPermissions =
            new(
                new[]
                {
                    "events.transition_started",
                    "events.game_ready",
                    "events.arena_loaded",
                    "events.player_spawned",
                    "events.song_changed",
                    "events.music_beat",
                    "events.combat_perfect_input",
                    "actions.notify",
                    "actions.visual_cue",
                    "state.read",
                    "settings.read",
                    "settings.write",
                    "storage.read",
                    "storage.write",
                    "logging"
                },
                StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, string> PermissionCapabilities =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["events.transition_started"] = "lifecycle.transitions",
                ["events.game_ready"] = "lifecycle.game-ready",
                ["events.arena_loaded"] = "lifecycle.arena",
                ["events.player_spawned"] = "lifecycle.player-spawn",
                ["events.song_changed"] = "music.song",
                ["events.music_beat"] = "music.beat",
                ["events.combat_perfect_input"] = "combat.perfect-input",
                ["actions.notify"] = "hud.notification",
                ["actions.visual_cue"] = "hud.visual-cue",
                ["state.read"] = "state.stable-read",
                ["settings.read"] = "data.settings",
                ["settings.write"] = "data.settings",
                ["storage.read"] = "data.storage",
                ["storage.write"] = "data.storage",
                ["logging"] = "diagnostics.logging"
            };

        private static readonly HashSet<string> SupportedCapabilities =
            PermissionCapabilities.Values.ToHashSet(StringComparer.Ordinal);

        private static readonly string[] FragileGameSymbols =
        {
            "RegisterHook",
            "RegisterLoadMapPreHook",
            "RegisterLoadMapPostHook",
            "FindFirstOf",
            "FindAllOf",
            "StaticFindObject",
            "UEHelpers",
            "/Script/"
        };

        private sealed class ManagedMarker
        {
            public string Product { get; init; } = ManagedMarkerProduct;

            public string Id { get; init; } = string.Empty;

            public string Version { get; init; } = string.Empty;

            public string Entrypoint { get; init; } = string.Empty;

            public string PackageSha256 { get; init; } = string.Empty;
        }

        private sealed record PackageContents(
            StagehandLogicModManifest Manifest,
            byte[] ManifestBytes,
            byte[] ScriptBytes,
            string PackageSha256,
            bool IsReviewCurrent);

        public StagehandLogicModPackageInspection Inspect(
            string archivePath)
        {
            bool namedAsStagehand =
                Path.GetFileName(archivePath).EndsWith(
                    ".stagehand.zip",
                    StringComparison.OrdinalIgnoreCase);

            if (!File.Exists(archivePath) ||
                !string.Equals(
                    Path.GetExtension(archivePath),
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new StagehandLogicModPackageInspection
                {
                    IsStagehandPackage = namedAsStagehand,
                    IsValid = false,
                    Message = "Stagehand script packages must be ZIP files."
                };
            }

            try
            {
                PackageContents? package = ReadAndValidatePackage(
                    archivePath,
                    requireStagehandManifest: false,
                    out bool hasStagehandManifest);

                if (!hasStagehandManifest)
                {
                    return new StagehandLogicModPackageInspection
                    {
                        IsStagehandPackage = namedAsStagehand,
                        IsValid = false,
                        Message = namedAsStagehand
                            ? "The package does not contain a root stagehand.json manifest."
                            : string.Empty
                    };
                }

                return new StagehandLogicModPackageInspection
                {
                    IsStagehandPackage = true,
                    IsValid = true,
                    Manifest = package!.Manifest,
                    Message = "Stagehand script package is valid.",
                    IsReviewCurrent = package.IsReviewCurrent,
                    ReviewMessage = package.IsReviewCurrent
                        ? "The local Stagehand review hashes match the exact package files."
                        : package.Manifest.Review is null
                            ? "This package has no local Stagehand review."
                            : "The local Stagehand review is stale or does not match these files."
                };
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                IOException or
                UnauthorizedAccessException or
                JsonException or
                DecoderFallbackException)
            {
                return new StagehandLogicModPackageInspection
                {
                    IsStagehandPackage = namedAsStagehand ||
                        ArchiveHasRootManifestBestEffort(archivePath),
                    IsValid = false,
                    Message = exception.Message
                };
            }
        }

        public StagehandLogicModInstallResult Install(
            string archivePath,
            Ue4ssDetectionResult installation)
        {
            ArgumentNullException.ThrowIfNull(installation);
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(installation.ModsDirectory))
            {
                throw new InvalidOperationException(
                    "Limelight's UE4SS runtime must be installed before adding a Stagehand script.");
            }

            PackageContents package = ReadAndValidatePackage(
                    archivePath,
                    requireStagehandManifest: true,
                    out _)
                ?? throw new InvalidDataException(
                    "The archive is not a Stagehand script package.");

            string modsDirectory = Path.GetFullPath(installation.ModsDirectory);
            string runtimeDirectory = GetChildPath(
                modsDirectory,
                RuntimeModName);
            string logicModsDirectory = GetChildPath(
                runtimeDirectory,
                "LogicMods");
            string installDirectory = GetChildPath(
                logicModsDirectory,
                package.Manifest.Id);
            string markerPath = Path.Combine(
                installDirectory,
                ManagedMarkerName);

            bool directoryExisted = Directory.Exists(installDirectory);
            ManagedMarker? existingMarker = ReadMarker(markerPath);
            if (directoryExisted &&
                (existingMarker is null ||
                 !string.Equals(
                     existingMarker.Product,
                     ManagedMarkerProduct,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     existingMarker.Id,
                     package.Manifest.Id,
                     StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "A script folder with this ID already exists outside Limelight's package manager. Limelight left it untouched.");
            }

            string manifestPath = Path.Combine(
                installDirectory,
                "stagehand.json");
            string entrypointPath = GetChildPath(
                installDirectory,
                package.Manifest.Entrypoint);
            string? previousEntrypointPath =
                existingMarker is not null &&
                IsSafeEntrypoint(existingMarker.Entrypoint)
                    ? GetChildPath(
                        installDirectory,
                        existingMarker.Entrypoint)
                    : null;

            List<string> managedPaths =
                new[]
                {
                    manifestPath,
                    entrypointPath,
                    markerPath,
                    previousEntrypointPath
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Dictionary<string, byte[]?> originals = CaptureFiles(managedPaths);
            try
            {
                Directory.CreateDirectory(installDirectory);
                WriteBytesAtomically(manifestPath, package.ManifestBytes);
                WriteBytesAtomically(entrypointPath, package.ScriptBytes);

                if (previousEntrypointPath is not null &&
                    !string.Equals(
                        previousEntrypointPath,
                        entrypointPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    DeleteFileBestEffort(previousEntrypointPath);
                }

                ManagedMarker marker = new()
                {
                    Id = package.Manifest.Id,
                    Version = package.Manifest.Version,
                    Entrypoint = package.Manifest.Entrypoint,
                    PackageSha256 = package.PackageSha256
                };

                WriteBytesAtomically(
                    markerPath,
                    new UTF8Encoding(false).GetBytes(
                        JsonSerializer.Serialize(marker, JsonOptions) +
                        Environment.NewLine));

                PackageContents installed = ReadAndValidateInstalled(
                    manifestPath,
                    entrypointPath,
                    package.PackageSha256);

                if (!string.Equals(
                    installed.Manifest.Id,
                    package.Manifest.Id,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The Stagehand script could not be verified after installation.");
                }

                return new StagehandLogicModInstallResult(
                    package.Manifest,
                    installDirectory,
                    directoryExisted);
            }
            catch
            {
                RestoreFiles(originals);
                if (!directoryExisted)
                {
                    DeleteEmptyDirectories(
                        installDirectory,
                        logicModsDirectory);
                }

                throw;
            }
        }

        public IReadOnlyList<InstalledStagehandScript> ListInstalled(
            Ue4ssDetectionResult installation)
        {
            ArgumentNullException.ThrowIfNull(installation);
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(installation.ModsDirectory))
            {
                return Array.Empty<InstalledStagehandScript>();
            }

            string logicModsDirectory = GetChildPath(
                GetChildPath(
                    Path.GetFullPath(installation.ModsDirectory),
                    RuntimeModName),
                "LogicMods");
            if (!Directory.Exists(logicModsDirectory))
            {
                return Array.Empty<InstalledStagehandScript>();
            }

            List<InstalledStagehandScript> scripts = new();
            foreach (string directory in Directory.EnumerateDirectories(
                         logicModsDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    ManagedMarker? marker = ReadMarker(
                        Path.Combine(directory, ManagedMarkerName));
                    if (marker is null ||
                        !string.Equals(marker.Product, ManagedMarkerProduct, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string manifestPath = Path.Combine(directory, "stagehand.json");
                    StagehandLogicModManifest? manifest = JsonSerializer.Deserialize<StagehandLogicModManifest>(
                        File.ReadAllText(manifestPath, StrictUtf8),
                        JsonOptions);
                    if (manifest is null || !IsSafeEntrypoint(manifest.Entrypoint))
                    {
                        continue;
                    }

                    string entrypointPath = GetChildPath(directory, manifest.Entrypoint);
                    byte[] scriptBytes = File.ReadAllBytes(entrypointPath);
                    scripts.Add(new InstalledStagehandScript
                    {
                        Id = manifest.Id,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        ApiVersion = manifest.ApiVersion,
                        DeclaredTrust = manifest.DeclaredTrust,
                        Permissions = manifest.Permissions.ToArray(),
                        Capabilities = manifest.Capabilities.ToArray(),
                        Dependencies = manifest.Dependencies.ToArray(),
                        IsEnabled = !File.Exists(Path.Combine(directory, DisabledMarkerName)),
                        IsReviewCurrent = IsReviewCurrent(manifest, scriptBytes),
                        IsBundled = marker.PackageSha256.StartsWith("bundled-runtime-", StringComparison.Ordinal),
                        InstallDirectory = directory,
                        RecentLog = ReadLastLogLine(Path.Combine(directory, "stagehand.log"))
                    });
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    JsonException or
                    DecoderFallbackException or
                    InvalidDataException)
                {
                    // A damaged directory remains untouched and is omitted from
                    // controls until its managed package is repaired/reinstalled.
                }
            }

            return scripts
                .OrderBy(script => script.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(script => script.Id, StringComparer.Ordinal)
                .ToList();
        }

        public void SetEnabled(
            Ue4ssDetectionResult installation,
            string id,
            bool enabled)
        {
            ArgumentNullException.ThrowIfNull(installation);
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(installation.ModsDirectory))
            {
                throw new InvalidOperationException("Limelight's Live Loader is not installed.");
            }
            if (!LogicModIdRegex().IsMatch(id))
            {
                throw new InvalidDataException("The Stagehand script ID is invalid.");
            }

            string logicModsDirectory = GetChildPath(
                GetChildPath(
                    Path.GetFullPath(installation.ModsDirectory),
                    RuntimeModName),
                "LogicMods");
            string installDirectory = ResolveManagedInstallDirectory(
                logicModsDirectory,
                id);
            ManagedMarker? marker = ReadMarker(Path.Combine(installDirectory, ManagedMarkerName));
            if (marker is null ||
                !string.Equals(marker.Product, ManagedMarkerProduct, StringComparison.Ordinal) ||
                !string.Equals(marker.Id, id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Limelight will only change scripts installed through its Stagehand manager.");
            }

            string disabledPath = Path.Combine(installDirectory, DisabledMarkerName);
            if (enabled)
            {
                DeleteFileBestEffort(disabledPath);
            }
            else
            {
                WriteBytesAtomically(disabledPath, Array.Empty<byte>());
            }
        }

        public void Remove(
            Ue4ssDetectionResult installation,
            string id)
        {
            ArgumentNullException.ThrowIfNull(installation);
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(installation.ModsDirectory))
            {
                throw new InvalidOperationException("Limelight's Live Loader is not installed.");
            }
            if (!LogicModIdRegex().IsMatch(id))
            {
                throw new InvalidDataException("The Stagehand script ID is invalid.");
            }

            string logicModsDirectory = GetChildPath(
                GetChildPath(
                    Path.GetFullPath(installation.ModsDirectory),
                    RuntimeModName),
                "LogicMods");
            string installDirectory = ResolveManagedInstallDirectory(
                logicModsDirectory,
                id);
            DirectoryInfo? parent = Directory.GetParent(installDirectory);
            if (parent is null ||
                !string.Equals(
                    parent.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    logicModsDirectory.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The managed script directory is outside Stagehand's LogicMods folder.");
            }

            ManagedMarker? marker = ReadMarker(
                Path.Combine(installDirectory, ManagedMarkerName));
            if (marker is null ||
                !string.Equals(marker.Product, ManagedMarkerProduct, StringComparison.Ordinal) ||
                !string.Equals(marker.Id, id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Limelight will only remove scripts installed through its Stagehand manager.");
            }
            if (marker.PackageSha256.StartsWith("bundled-runtime-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The bundled Stagehand proof script is part of the runtime. Disable it instead.");
            }
            if (!Directory.Exists(installDirectory))
            {
                return;
            }
            if (ContainsReparsePoint(installDirectory))
            {
                throw new InvalidOperationException(
                    "The script folder contains a filesystem link, so Limelight left it untouched.");
            }

            Directory.Delete(installDirectory, recursive: true);
            if (Directory.Exists(installDirectory))
            {
                throw new IOException("The Stagehand script folder could not be completely removed.");
            }
        }

        private static bool ContainsReparsePoint(string rootDirectory)
        {
            Stack<string> directories = new();
            directories.Push(rootDirectory);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(entry);
                    }
                }
            }
            return false;
        }

        private static string ResolveManagedInstallDirectory(
            string logicModsDirectory,
            string id)
        {
            if (!Directory.Exists(logicModsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Stagehand's managed LogicMods directory was not found.");
            }

            string directCandidate = GetChildPath(logicModsDirectory, id);
            IEnumerable<string> candidates = Directory.Exists(directCandidate)
                ? new[] { directCandidate }.Concat(
                    Directory.EnumerateDirectories(
                        logicModsDirectory,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(directory => !string.Equals(
                        directory,
                        directCandidate,
                        StringComparison.OrdinalIgnoreCase)))
                : Directory.EnumerateDirectories(
                    logicModsDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly);

            foreach (string candidate in candidates)
            {
                FileAttributes attributes = File.GetAttributes(candidate);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                ManagedMarker? marker = ReadMarker(
                    Path.Combine(candidate, ManagedMarkerName));
                if (marker is not null &&
                    string.Equals(marker.Product, ManagedMarkerProduct, StringComparison.Ordinal) &&
                    string.Equals(marker.Id, id, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                "Limelight could not find the managed folder for this Stagehand script.");
        }

        private static bool IsReviewCurrent(
            StagehandLogicModManifest manifest,
            byte[] scriptBytes)
        {
            StagehandReviewAttestation? review = manifest.Review;
            if (review is null ||
                review.SchemaVersion != 1 ||
                !string.Equals(review.Status, "locally-reviewed", StringComparison.Ordinal))
            {
                return false;
            }

            string scriptHash = Convert.ToHexString(SHA256.HashData(scriptBytes));
            return string.Equals(review.ScriptSha256, scriptHash, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(review.ManifestSha256, HashManifest(manifest), StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadLastLogLine(string path)
        {
            if (!File.Exists(path))
            {
                return "No runtime log yet. Launch the game through Limelight to run this script.";
            }

            string? line = File.ReadLines(path, StrictUtf8)
                .Reverse()
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
            return string.IsNullOrWhiteSpace(line)
                ? "Runtime log is empty."
                : line.Length <= 280
                    ? line
                    : line[..280] + "…";
        }

        private static string HashManifest(StagehandLogicModManifest manifest)
        {
            var canonical = new
            {
                manifest.SchemaVersion,
                manifest.Id,
                manifest.Name,
                manifest.Version,
                manifest.ApiVersion,
                manifest.Entrypoint,
                manifest.DeclaredTrust,
                manifest.NativeCode,
                Permissions = (manifest.Permissions ?? new List<string>())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray(),
                Capabilities = (manifest.Capabilities ?? new List<string>())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToArray(),
                Dependencies = (manifest.Dependencies ?? new List<StagehandDependency>())
                    .OrderBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => new
                    {
                        item.Id,
                        item.MinimumVersion,
                        item.Optional
                    })
                    .ToArray()
            };
            byte[] bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(canonical, CanonicalJsonOptions));
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static PackageContents? ReadAndValidatePackage(
            string archivePath,
            bool requireStagehandManifest,
            out bool hasStagehandManifest)
        {
            FileInfo packageInfo = new(archivePath);
            if (packageInfo.Length <= 0 || packageInfo.Length > MaximumPackageSize)
            {
                throw new InvalidDataException(
                    "The Stagehand package is empty or exceeds the 2 MB v1 limit.");
            }

            using FileStream packageStream = packageInfo.OpenRead();
            string packageHash = Convert.ToHexString(
                SHA256.HashData(packageStream));
            packageStream.Position = 0;

            using ZipArchive archive = new(
                packageStream,
                ZipArchiveMode.Read,
                leaveOpen: false);

            ZipArchiveEntry[] manifestEntries = archive.Entries
                .Where(entry =>
                    string.Equals(
                        entry.FullName,
                        "stagehand.json",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            hasStagehandManifest = manifestEntries.Length > 0;
            if (manifestEntries.Length == 0)
            {
                if (requireStagehandManifest)
                {
                    throw new InvalidDataException(
                        "The package does not contain a root stagehand.json manifest.");
                }

                return null;
            }

            if (manifestEntries.Length != 1)
            {
                throw new InvalidDataException(
                    "The Stagehand package contains duplicate stagehand.json entries.");
            }

            ZipArchiveEntry manifestEntry = manifestEntries[0];

            if (archive.Entries.Count != 2 ||
                archive.Entries.Any(entry =>
                    string.IsNullOrWhiteSpace(entry.Name) ||
                    !IsSafeRootFile(entry.FullName)))
            {
                throw new InvalidDataException(
                    "A Stagehand v1 package must contain only stagehand.json and one root Lua entrypoint.");
            }

            byte[] manifestBytes = ReadEntry(
                manifestEntry,
                MaximumManifestSize,
                "stagehand.json");
            string manifestJson = StrictUtf8.GetString(manifestBytes);
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            if (!document.RootElement.TryGetProperty(
                    "nativeCode",
                    out JsonElement nativeCode) ||
                nativeCode.ValueKind != JsonValueKind.False)
            {
                throw new InvalidDataException(
                    "nativeCode must be explicitly false. Limelight does not load DLLs from Stagehand packages.");
            }

            StagehandLogicModManifest manifest =
                JsonSerializer.Deserialize<StagehandLogicModManifest>(
                    manifestJson,
                    JsonOptions) ??
                throw new InvalidDataException(
                    "stagehand.json did not contain a manifest object.");

            ValidateManifest(manifest);

            ZipArchiveEntry scriptEntry = archive.Entries
                .SingleOrDefault(entry =>
                    string.Equals(
                        entry.FullName,
                        manifest.Entrypoint,
                        StringComparison.Ordinal)) ??
                throw new InvalidDataException(
                    $"The declared entrypoint '{manifest.Entrypoint}' is missing or has different casing.");

            byte[] scriptBytes = ReadEntry(
                scriptEntry,
                MaximumScriptSize,
                manifest.Entrypoint);
            string script = StrictUtf8.GetString(scriptBytes);
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new InvalidDataException(
                    "The Stagehand Lua entrypoint is empty.");
            }

            foreach (string symbol in FragileGameSymbols)
            {
                if (script.Contains(
                    symbol,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The script uses private game/UE4SS symbol '{symbol}'. Build against the Stagehand API instead.");
                }
            }

            return new PackageContents(
                manifest,
                manifestBytes,
                scriptBytes,
                packageHash,
                IsReviewCurrent(manifest, scriptBytes));
        }

        private static PackageContents ReadAndValidateInstalled(
            string manifestPath,
            string entrypointPath,
            string packageHash)
        {
            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            byte[] scriptBytes = File.ReadAllBytes(entrypointPath);
            StagehandLogicModManifest manifest =
                JsonSerializer.Deserialize<StagehandLogicModManifest>(
                    StrictUtf8.GetString(manifestBytes),
                    JsonOptions) ??
                throw new InvalidDataException(
                    "The installed Stagehand manifest could not be read.");

            ValidateManifest(manifest);
            return new PackageContents(
                manifest,
                manifestBytes,
                scriptBytes,
                packageHash,
                IsReviewCurrent(manifest, scriptBytes));
        }

        private static void ValidateManifest(
            StagehandLogicModManifest manifest)
        {
            if (manifest.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(manifest.Id) ||
                !LogicModIdRegex().IsMatch(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.Name) ||
                manifest.Name.Length > 80 ||
                string.IsNullOrWhiteSpace(manifest.Version) ||
                !VersionRegex().IsMatch(manifest.Version) ||
                string.IsNullOrWhiteSpace(manifest.ApiVersion) ||
                !ApiVersionRegex().IsMatch(manifest.ApiVersion) ||
                !IsSafeEntrypoint(manifest.Entrypoint) ||
                string.IsNullOrWhiteSpace(manifest.DeclaredTrust) ||
                manifest.DeclaredTrust.Length > 80 ||
                manifest.NativeCode ||
                manifest.Permissions is null ||
                manifest.Capabilities is null ||
                manifest.Dependencies is null)
            {
                throw new InvalidDataException(
                    "stagehand.json contains missing, unsafe, or incompatible Stagehand v1 metadata.");
            }

            if (manifest.Permissions.Count !=
                    manifest.Permissions.Distinct(StringComparer.Ordinal).Count() ||
                manifest.Permissions.Any(permission =>
                    !SupportedPermissions.Contains(permission)))
            {
                throw new InvalidDataException(
                    "stagehand.json contains duplicate or unsupported permissions.");
            }

            if (manifest.Capabilities.Count == 0 && manifest.Permissions.Count > 0)
            {
                manifest.Capabilities.AddRange(
                    manifest.Permissions
                        .Where(PermissionCapabilities.ContainsKey)
                        .Select(permission => PermissionCapabilities[permission])
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(capability => capability, StringComparer.Ordinal));
            }

            if (manifest.Capabilities.Count !=
                    manifest.Capabilities.Distinct(StringComparer.Ordinal).Count() ||
                manifest.Capabilities.Any(capability =>
                    !SupportedCapabilities.Contains(capability)))
            {
                throw new InvalidDataException(
                    "stagehand.json contains duplicate or unsupported capabilities.");
            }

            HashSet<string> declaredCapabilities =
                manifest.Capabilities.ToHashSet(StringComparer.Ordinal);
            foreach (string permission in manifest.Permissions)
            {
                if (PermissionCapabilities.TryGetValue(permission, out string? capability) &&
                    !declaredCapabilities.Contains(capability))
                {
                    throw new InvalidDataException(
                        $"Permission '{permission}' requires capability '{capability}'.");
                }
            }

            HashSet<string> dependencyIds = new(StringComparer.Ordinal);
            foreach (StagehandDependency dependency in manifest.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.Id) ||
                    !LogicModIdRegex().IsMatch(dependency.Id) ||
                    string.Equals(dependency.Id, manifest.Id, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(dependency.MinimumVersion) ||
                    !VersionRegex().IsMatch(dependency.MinimumVersion) ||
                    !dependencyIds.Add(dependency.Id))
                {
                    throw new InvalidDataException(
                        "stagehand.json contains an invalid, duplicate, or self-referencing dependency.");
                }
            }
        }

        private static byte[] ReadEntry(
            ZipArchiveEntry entry,
            long maximumSize,
            string label)
        {
            if (entry.Length <= 0 || entry.Length > maximumSize)
            {
                throw new InvalidDataException(
                    $"{label} is empty or exceeds the Stagehand v1 size limit.");
            }

            using Stream source = entry.Open();
            using MemoryStream destination = new();
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maximumSize)
                {
                    throw new InvalidDataException(
                        $"{label} exceeds the Stagehand v1 size limit.");
                }

                destination.Write(buffer, 0, read);
            }

            byte[] bytes = destination.ToArray();
            if (bytes.LongLength != entry.Length || bytes.LongLength > maximumSize)
            {
                throw new InvalidDataException(
                    $"{label} could not be read safely from the package.");
            }

            return bytes;
        }

        private static bool IsSafeRootFile(
            string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   !Path.IsPathRooted(path) &&
                   !path.Contains('/') &&
                   !path.Contains('\\') &&
                   path != "." &&
                   path != ".." &&
                   path.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static bool IsSafeEntrypoint(
            string entrypoint)
        {
            return IsSafeRootFile(entrypoint) &&
                   entrypoint.EndsWith(
                       ".lua",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool ArchiveHasRootManifestBestEffort(
            string archivePath)
        {
            try
            {
                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                return archive.Entries.Any(entry =>
                    string.Equals(
                        entry.FullName,
                        "stagehand.json",
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private static ManagedMarker? ReadMarker(
            string markerPath)
        {
            if (!File.Exists(markerPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ManagedMarker>(
                    File.ReadAllText(markerPath),
                    JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static string GetChildPath(
            string parentDirectory,
            string childName)
        {
            if (string.IsNullOrWhiteSpace(childName) ||
                childName == "." ||
                childName == ".." ||
                childName.Contains('/') ||
                childName.Contains('\\') ||
                childName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException(
                    "The Stagehand package contains an unsafe folder or filename.");
            }

            string parent = Path.GetFullPath(parentDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string child = Path.GetFullPath(
                Path.Combine(parent, childName));
            if (!child.StartsWith(
                parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The Stagehand package attempted to leave its managed directory.");
            }

            return child;
        }

        private static Dictionary<string, byte[]?> CaptureFiles(
            IEnumerable<string> paths)
        {
            return paths.ToDictionary(
                path => path,
                path => File.Exists(path) ? File.ReadAllBytes(path) : null,
                StringComparer.OrdinalIgnoreCase);
        }

        private static void RestoreFiles(
            IReadOnlyDictionary<string, byte[]?> files)
        {
            foreach ((string path, byte[]? contents) in files)
            {
                try
                {
                    if (contents is null)
                    {
                        DeleteFileBestEffort(path);
                    }
                    else
                    {
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, contents);
                    }
                }
                catch
                {
                    // Continue restoring the remaining managed script files.
                }
            }
        }

        private static void WriteBytesAtomically(
            string path,
            byte[] contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporaryPath = path + ".limelight-writing";
            try
            {
                File.WriteAllBytes(temporaryPath, contents);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                DeleteFileBestEffort(temporaryPath);
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
                // A later update can replace a stale managed file.
            }
        }

        private static void DeleteEmptyDirectories(
            string startDirectory,
            string stopDirectory)
        {
            string stop = Path.GetFullPath(stopDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            string start = Path.GetFullPath(startDirectory);
            if (!start.StartsWith(
                    stop + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(start))
            {
                return;
            }

            foreach (string directory in Directory
                .GetDirectories(start, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .Append(start))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch
                {
                    // A non-empty or locked parent is safe to leave in place.
                }
            }
        }

        [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,79}$", RegexOptions.CultureInvariant)]
        private static partial Regex LogicModIdRegex();

        [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
        private static partial Regex VersionRegex();

        [GeneratedRegex("^1(?:\\.[0-9]+){0,2}$", RegexOptions.CultureInvariant)]
        private static partial Regex ApiVersionRegex();
    }
}
