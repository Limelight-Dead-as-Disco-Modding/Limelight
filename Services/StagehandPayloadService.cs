using Limelight.Models;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class StagehandPayloadService
    {
        public const string ProductName =
            "Limelight Stagehand";

        private const string ManifestResourceName =
            "Limelight.Payloads.Stagehand.stagehand-payload-manifest.json";

        private const string ManagedMarkerName =
            ".limelight-stagehand-managed.json";

        private readonly Assembly _assembly =
            typeof(StagehandPayloadService).Assembly;

        private readonly JsonSerializerOptions _jsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

        private sealed class ManagedMarker
        {
            public string Product { get; init; } =
                string.Empty;

            public string StagehandVersion { get; init; } =
                string.Empty;

            public string ApiVersion { get; init; } =
                string.Empty;
        }

        public StagehandPayloadManifest Manifest =>
            LoadAndValidateManifest();

        public bool IsEmbeddedPayloadCompatible()
        {
            try
            {
                StagehandPayloadManifest manifest =
                    LoadAndValidateManifest();

                ReadAndValidatePackage(
                    manifest);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public StagehandPayloadManifest EnsureInstalled(
            Ue4ssDetectionResult installation)
        {
            ValidateInstallation(installation);

            StagehandPayloadManifest manifest =
                LoadAndValidateManifest();

            IReadOnlyDictionary<string, byte[]> packageFiles =
                ReadAndValidatePackage(
                    manifest);

            string modsDirectory =
                Path.GetFullPath(
                    installation.ModsDirectory);

            string modDirectory =
                GetChildPath(
                    modsDirectory,
                    manifest.TargetModName);

            string markerPath =
                Path.Combine(
                    modDirectory,
                    ManagedMarkerName);

            bool directoryAlreadyExisted =
                Directory.Exists(modDirectory);

            if (directoryAlreadyExisted &&
                !IsManagedMarker(markerPath))
            {
                throw new InvalidOperationException(
                    "A non-managed LimelightStagehand folder already exists. Limelight left it untouched.");
            }

            List<string> targetPaths =
                manifest.Files
                    .Select(file =>
                        GetTargetPath(
                            modDirectory,
                            file.TargetRelativePath))
                    .Append(markerPath)
                    .Append(
                        Path.Combine(
                            modsDirectory,
                            "mods.txt"))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            Dictionary<string, byte[]?> originalFiles =
                CaptureFiles(targetPaths);

            try
            {
                Directory.CreateDirectory(modDirectory);

                foreach (StagehandPayloadFile file in manifest.Files)
                {
                    string targetPath =
                        GetTargetPath(
                            modDirectory,
                            file.TargetRelativePath);

                    if (!FileMatchesManifest(
                            targetPath,
                            file))
                    {
                        InstallFile(
                            targetPath,
                            file,
                            packageFiles[file.TargetRelativePath]);
                    }
                }

                ManagedMarker marker =
                    new()
                    {
                        Product = ProductName,
                        StagehandVersion = manifest.StagehandVersion,
                        ApiVersion = manifest.ApiVersion
                    };

                WriteTextAtomically(
                    markerPath,
                    JsonSerializer.Serialize(
                        marker,
                        _jsonOptions) +
                    Environment.NewLine);

                EnableInModsFile(
                    modsDirectory,
                    manifest.TargetModName);

                if (!IsCurrentVersionInstalled(
                        installation))
                {
                    throw new InvalidOperationException(
                        "Limelight Stagehand could not be verified after installation.");
                }

                return manifest;
            }
            catch
            {
                RestoreFiles(originalFiles);

                if (!directoryAlreadyExisted)
                {
                    DeleteEmptyDirectories(
                        modDirectory,
                        modsDirectory);
                }

                throw;
            }
        }

        public bool IsCurrentVersionInstalled(
            Ue4ssDetectionResult installation)
        {
            try
            {
                ValidateInstallation(installation);

                StagehandPayloadManifest manifest =
                    LoadAndValidateManifest();

                string modsDirectory =
                    Path.GetFullPath(
                        installation.ModsDirectory);

                string modDirectory =
                    GetChildPath(
                        modsDirectory,
                        manifest.TargetModName);

                ManagedMarker? marker =
                    ReadManagedMarker(
                        Path.Combine(
                            modDirectory,
                            ManagedMarkerName));

                return marker is not null &&
                       string.Equals(
                           marker.Product,
                           ProductName,
                           StringComparison.Ordinal) &&
                       string.Equals(
                           marker.StagehandVersion,
                           manifest.StagehandVersion,
                           StringComparison.Ordinal) &&
                       string.Equals(
                           marker.ApiVersion,
                           manifest.ApiVersion,
                           StringComparison.Ordinal) &&
                       manifest.Files.All(file =>
                           FileMatchesManifest(
                               GetTargetPath(
                                   modDirectory,
                                   file.TargetRelativePath),
                               file)) &&
                       IsEnabledInModsFile(
                           Path.Combine(
                               modsDirectory,
                               "mods.txt"),
                           manifest.TargetModName);
            }
            catch
            {
                return false;
            }
        }

        public string ReadRuntimeHealthSummary(
            Ue4ssDetectionResult installation)
        {
            try
            {
                ValidateInstallation(installation);
                StagehandPayloadManifest manifest =
                    LoadAndValidateManifest();
                string healthPath = Path.Combine(
                    GetChildPath(
                        Path.GetFullPath(installation.ModsDirectory),
                        manifest.TargetModName),
                    "stagehand-health.json");

                if (!File.Exists(healthPath))
                {
                    return "RUNTIME HEALTH · Not reported yet. Launch the game once, then refresh.";
                }

                FileInfo file = new(healthPath);
                if (file.Length <= 0 || file.Length > 512 * 1024)
                {
                    return "RUNTIME HEALTH · The report is empty or unexpectedly large.";
                }

                using JsonDocument document = JsonDocument.Parse(
                    File.ReadAllText(healthPath));
                JsonElement root = document.RootElement;
                string status = root.TryGetProperty("status", out JsonElement statusElement)
                    ? statusElement.GetString() ?? "unknown"
                    : "unknown";
                string runtimeVersion = root.TryGetProperty("runtime_version", out JsonElement runtimeElement)
                    ? runtimeElement.GetString() ?? "unknown"
                    : "unknown";
                string apiVersion = root.TryGetProperty("api_version", out JsonElement apiElement)
                    ? apiElement.GetString() ?? "unknown"
                    : "unknown";

                int loaded = 0;
                int rejected = 0;
                if (root.TryGetProperty("discovery", out JsonElement discovery))
                {
                    if (discovery.TryGetProperty("loaded_count", out JsonElement loadedElement))
                    {
                        loadedElement.TryGetInt32(out loaded);
                    }
                    if (discovery.TryGetProperty("rejected_count", out JsonElement rejectedElement))
                    {
                        rejectedElement.TryGetInt32(out rejected);
                    }
                }

                bool coexistenceSafe =
                    root.TryGetProperty("ownership", out JsonElement ownership) &&
                    ownership.TryGetProperty("replaces_ue4ss_runtime", out JsonElement replacesRuntime) &&
                    replacesRuntime.ValueKind == JsonValueKind.False &&
                    ownership.TryGetProperty("replaces_signatures", out JsonElement replacesSignatures) &&
                    replacesSignatures.ValueKind == JsonValueKind.False &&
                    ownership.TryGetProperty("replaces_third_party_mods", out JsonElement replacesMods) &&
                    replacesMods.ValueKind == JsonValueKind.False;

                return string.Format(
                    "RUNTIME HEALTH · {0} · Stagehand {1} · API {2} · {3} loaded / {4} rejected{5}",
                    status.ToUpperInvariant(),
                    runtimeVersion,
                    apiVersion,
                    loaded,
                    rejected,
                    coexistenceSafe
                        ? " · UE4SS, signatures, and third-party mods untouched"
                        : " · ownership boundary not confirmed");
            }
            catch
            {
                return "RUNTIME HEALTH · Report unreadable or incompatible.";
            }
        }

        private StagehandPayloadManifest LoadAndValidateManifest()
        {
            using Stream stream =
                _assembly.GetManifestResourceStream(
                    ManifestResourceName) ??
                throw new InvalidOperationException(
                    "The embedded Stagehand payload manifest could not be found.");

            StagehandPayloadManifest? manifest =
                JsonSerializer.Deserialize<StagehandPayloadManifest>(
                    stream,
                    _jsonOptions);

            if (manifest is null ||
                manifest.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(
                    manifest.StagehandVersion) ||
                string.IsNullOrWhiteSpace(
                    manifest.ApiVersion) ||
                string.IsNullOrWhiteSpace(
                    manifest.TargetModName) ||
                !IsSafePathSegment(
                    manifest.TargetModName) ||
                manifest.Package is null ||
                string.IsNullOrWhiteSpace(
                    manifest.Package.FileName) ||
                !IsSafePathSegment(
                    manifest.Package.FileName) ||
                string.IsNullOrWhiteSpace(
                    manifest.Package.ResourceName) ||
                manifest.Package.Size <= 0 ||
                !IsValidHash(
                    manifest.Package.Sha256) ||
                manifest.Files is null ||
                manifest.Files.Count == 0 ||
                manifest.Files
                    .Select(file => file.TargetRelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != manifest.Files.Count)
            {
                throw new InvalidOperationException(
                    "The embedded Stagehand payload manifest is invalid.");
            }

            foreach (StagehandPayloadFile file in manifest.Files)
            {
                if (string.IsNullOrWhiteSpace(
                        file.TargetRelativePath) ||
                    !IsSafePayloadRelativePath(
                        file.TargetRelativePath) ||
                    file.Size <= 0 ||
                    !IsValidHash(
                        file.Sha256))
                {
                    throw new InvalidOperationException(
                        "The embedded Stagehand payload manifest contains an invalid file entry.");
                }
            }

            return manifest;
        }

        private IReadOnlyDictionary<string, byte[]> ReadAndValidatePackage(
            StagehandPayloadManifest manifest)
        {
            using Stream resource =
                _assembly.GetManifestResourceStream(
                    manifest.Package.ResourceName) ??
                throw new InvalidOperationException(
                    $"The Stagehand package '{manifest.Package.ResourceName}' could not be found.");

            using MemoryStream packageStream =
                new();

            resource.CopyTo(
                packageStream);

            byte[] packageBytes =
                packageStream.ToArray();

            if (packageBytes.LongLength != manifest.Package.Size ||
                !string.Equals(
                    Convert.ToHexString(
                        SHA256.HashData(packageBytes)),
                    manifest.Package.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Limelight's embedded Stagehand package failed its integrity check.");
            }

            packageStream.Position = 0;

            using ZipArchive archive =
                new(
                    packageStream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);

            Dictionary<string, StagehandPayloadFile> manifestFiles =
                manifest.Files.ToDictionary(
                    file => file.TargetRelativePath,
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<string, byte[]> packageFiles =
                new(
                    StringComparer.OrdinalIgnoreCase);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) ||
                    !IsSafePayloadRelativePath(entry.FullName) ||
                    !manifestFiles.TryGetValue(
                        entry.FullName,
                        out StagehandPayloadFile? file) ||
                    packageFiles.ContainsKey(entry.FullName))
                {
                    throw new InvalidOperationException(
                        "The embedded Stagehand package contains an unexpected or unsafe entry.");
                }

                using Stream entryStream =
                    entry.Open();

                using MemoryStream contents =
                    new();

                entryStream.CopyTo(
                    contents);

                byte[] bytes =
                    contents.ToArray();

                if (entry.Length != file.Size ||
                    bytes.LongLength != file.Size ||
                    !string.Equals(
                        Convert.ToHexString(
                            SHA256.HashData(bytes)),
                        file.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"The Stagehand package entry '{entry.FullName}' failed its integrity check.");
                }

                packageFiles.Add(
                    file.TargetRelativePath,
                    bytes);
            }

            if (packageFiles.Count != manifest.Files.Count)
            {
                throw new InvalidOperationException(
                    "The embedded Stagehand package is missing one or more declared files.");
            }

            return packageFiles;
        }

        private void InstallFile(
            string targetPath,
            StagehandPayloadFile file,
            byte[] contents)
        {
            string? directory =
                Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath =
                targetPath + ".limelight-installing";

            try
            {
                using (FileStream destination =
                    new(
                        temporaryPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                {
                    destination.Write(
                        contents);
                }

                if (!FileMatchesManifest(
                        temporaryPath,
                        file))
                {
                    throw new InvalidOperationException(
                        $"The staged Stagehand file '{file.TargetRelativePath}' failed its integrity check.");
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
        }

        private static bool IsValidHash(
            string? hash)
        {
            return !string.IsNullOrEmpty(hash) &&
                   hash.Length == 64 &&
                   hash.All(
                       Uri.IsHexDigit);
        }

        private static bool IsSafePathSegment(
            string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value != "." &&
                   value != ".." &&
                   !value.Contains('/') &&
                   !value.Contains('\\') &&
                   value.IndexOfAny(
                       Path.GetInvalidFileNameChars()) < 0;
        }

        private static bool IsSafePayloadRelativePath(
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                relativePath.Contains('\\') ||
                relativePath.StartsWith('/') ||
                relativePath.EndsWith('/'))
            {
                return false;
            }

            return relativePath
                .Split(
                    '/',
                    StringSplitOptions.None)
                .All(IsSafePathSegment);
        }

        private static string GetTargetPath(
            string modDirectory,
            string relativePath)
        {
            string normalizedRelativePath =
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalizedRelativePath))
            {
                throw new InvalidOperationException(
                    "The Stagehand payload contains an unsafe target path.");
            }

            string targetPath =
                Path.GetFullPath(
                    Path.Combine(
                        modDirectory,
                        normalizedRelativePath));

            string requiredPrefix =
                Path.GetFullPath(modDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!targetPath.StartsWith(
                    requiredPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Stagehand payload attempted to leave its managed mod directory.");
            }

            return targetPath;
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

            string requiredPrefix =
                parent +
                Path.DirectorySeparatorChar;

            if (!child.StartsWith(
                    requiredPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Stagehand mod name attempted to leave the UE4SS Mods directory.");
            }

            return child;
        }

        private static bool FileMatchesManifest(
            string path,
            StagehandPayloadFile file)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            FileInfo info =
                new(path);

            if (info.Length != file.Size)
            {
                return false;
            }

            using FileStream stream =
                File.OpenRead(path);

            string hash =
                Convert.ToHexString(
                    SHA256.HashData(stream));

            return string.Equals(
                hash,
                file.Sha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private ManagedMarker? ReadManagedMarker(
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
                    _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private bool IsManagedMarker(
            string markerPath)
        {
            ManagedMarker? marker =
                ReadManagedMarker(markerPath);

            return marker is not null &&
                   string.Equals(
                       marker.Product,
                       ProductName,
                       StringComparison.Ordinal);
        }

        private static void EnableInModsFile(
            string modsDirectory,
            string modName)
        {
            string modsFile =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            List<string> lines =
                File.Exists(modsFile)
                    ? File.ReadAllLines(modsFile).ToList()
                    : new List<string>();

            string enabledLine =
                $"{modName} : 1";

            int existingIndex =
                lines.FindIndex(line =>
                    IsModLine(
                        line,
                        modName));

            if (existingIndex >= 0)
            {
                lines[existingIndex] =
                    enabledLine;
            }
            else
            {
                lines.Add(enabledLine);
            }

            WriteAllLinesAtomically(
                modsFile,
                lines);
        }

        private static bool IsEnabledInModsFile(
            string modsFile,
            string modName)
        {
            if (!File.Exists(modsFile))
            {
                return false;
            }

            string expected =
                $"{modName} : 1";

            return File.ReadLines(modsFile)
                .Any(line =>
                    string.Equals(
                        line.Trim(),
                        expected,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsModLine(
            string line,
            string modName)
        {
            string[] parts =
                line.Split(
                    ':',
                    2,
                    StringSplitOptions.TrimEntries);

            return parts.Length == 2 &&
                   string.Equals(
                       parts[0],
                       modName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateInstallation(
            Ue4ssDetectionResult installation)
        {
            ArgumentNullException.ThrowIfNull(
                installation);

            if (!installation.IsInstalled)
            {
                throw new InvalidOperationException(
                    "UE4SS must be installed before adding Limelight Stagehand.");
            }

            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The UE4SS Mods directory could not be determined.");
            }
        }

        private static Dictionary<string, byte[]?> CaptureFiles(
            IEnumerable<string> paths)
        {
            Dictionary<string, byte[]?> files =
                new(StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                files[path] =
                    File.Exists(path)
                        ? File.ReadAllBytes(path)
                        : null;
            }

            return files;
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
                        continue;
                    }

                    string? directory =
                        Path.GetDirectoryName(path);

                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllBytes(
                        path,
                        contents);
                }
                catch
                {
                    // Continue restoring the rest of this managed payload.
                }
            }
        }

        private static void DeleteEmptyDirectories(
            string startDirectory,
            string stopDirectory)
        {
            string stop =
                Path.GetFullPath(stopDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string start =
                Path.GetFullPath(startDirectory);

            if (!start.StartsWith(
                    stop + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(start))
            {
                return;
            }

            IEnumerable<string> candidates =
                Directory.GetDirectories(
                    start,
                    "*",
                    SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length)
                    .Append(start);

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(
                            candidate).Any())
                    {
                        Directory.Delete(candidate);
                    }
                }
                catch
                {
                    // Stop only for this directory; a parent with contents is safe to keep.
                }
            }
        }

        private static void WriteTextAtomically(
            string path,
            string contents)
        {
            string temporaryPath =
                path + ".limelight-writing";

            try
            {
                File.WriteAllText(
                    temporaryPath,
                    contents,
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

        private static void WriteAllLinesAtomically(
            string path,
            IEnumerable<string> lines)
        {
            string temporaryPath =
                path + ".limelight-writing";

            try
            {
                File.WriteAllLines(
                    temporaryPath,
                    lines);

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
                // A later repair can remove a stale temporary file.
            }
        }
    }
}
