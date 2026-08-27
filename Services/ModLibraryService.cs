using Limelight.Models;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Security.Cryptography;
using System.Text;

namespace Limelight.Services
{
    public sealed class ModArchiveFingerprintResult
    {
        public bool IsValid { get; init; }

        public string Fingerprint { get; init; } =
            string.Empty;

        public string Message { get; init; } =
            string.Empty;
    }

    public sealed class ModLibraryService
    {
        private static readonly string[] PackageExtensions =
        {
            ".pak",
            ".utoc",
            ".ucas",
            ".sig"
        };

        private readonly string _libraryDirectory;
        private readonly ModArchiveValidator _validator;
        private readonly ModAssetScannerService _assetScanner;
        private readonly CharacterSlotModService _characterSlotModService;
        private readonly ArenaSlotModService _arenaSlotModService;

        public ModLibraryService()
        {
            _libraryDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Mods");

            _validator = new ModArchiveValidator();
            _assetScanner = new ModAssetScannerService();
            _characterSlotModService =
                new CharacterSlotModService();
            _arenaSlotModService =
                new ArenaSlotModService();
        }

        public InstalledMod Import(
            string archivePath,
            long nexusModId = 0,
            int nexusFileId = 0,
            string? displayName = null,
            string? contentFingerprint = null)
        {
            // Validate again here so this service is safe even when called
            // from somewhere other than the current Import button.
            ModArchiveValidationResult validation =
                _validator.Validate(archivePath);

            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    validation.Message);
            }

            string modId = Guid.NewGuid().ToString("N");

            string stagingDirectory = Path.Combine(
                _libraryDirectory,
                ".importing-" + modId);

            string finalDirectory = Path.Combine(
                _libraryDirectory,
                modId);

            Directory.CreateDirectory(stagingDirectory);

            try
            {
                ExtractArchiveSafely(
                    archivePath,
                    stagingDirectory);

                List<string> packageFiles =
                    FindPackageFiles(stagingDirectory);

                string resolvedFingerprint =
                    string.IsNullOrWhiteSpace(contentFingerprint)
                        ? CalculatePackageFileSetFingerprint(
                            stagingDirectory,
                            packageFiles)
                        : contentFingerprint.Trim();

                // I settle the extracted files into their permanent location before
                // CUE4Parse opens the containers and begins reading their indexes.
                MoveDirectoryWithRetry(
                    stagingDirectory,
                    finalDirectory);

                List<ModAssetPackage> assetPackages =
                    _assetScanner.Scan(finalDirectory);

                var installedMod = new InstalledMod
                {
                    Id = modId,
                    Name = string.IsNullOrWhiteSpace(displayName)
                        ? CreateDisplayName(archivePath)
                        : displayName.Trim(),
                    InstallDirectory = finalDirectory,
                    PackageFiles = packageFiles,
                    ContentFingerprint = resolvedFingerprint,
                    AssetPackages = assetPackages,
                    AssetManifestVersion =
                        ModAssetScannerService.CurrentManifestVersion,
                    InstalledAt = DateTimeOffset.Now,
                    NexusModId = nexusModId,
                    NexusFileId = nexusFileId
                };

                _characterSlotModService.RefreshMetadata(
                    installedMod);

                _arenaSlotModService.RefreshMetadata(
                    installedMod);

                return installedMod;
            }
            catch
            {
                // I make cleanup best-effort so it never hides the original
                // import error that the user actually needs to see.
                TryDeleteDirectory(
                    stagingDirectory);

                TryDeleteDirectory(
                    finalDirectory);

                throw;
            }
        }

        public ModArchiveFingerprintResult GetArchiveFingerprintResult(
            string archivePath)
        {
            ModArchiveValidationResult validation =
                _validator.Validate(archivePath);

            if (!validation.IsValid)
            {
                return new ModArchiveFingerprintResult
                {
                    IsValid = false,
                    Message = validation.Message
                };
            }

            using IArchive archive =
                ModArchiveSupport.OpenArchive(
                    archivePath);

            List<string> packageParts =
                new List<string>();

            if (RequiresSequentialReader(
                    archive,
                    archivePath))
            {
                using IReader reader =
                    archive.ExtractAllEntries();

                while (reader.MoveToNextEntry())
                {
                    AddPackageFingerprintPart(
                        reader.Entry,
                        () => reader.OpenEntryStream(),
                        packageParts);
                }
            }
            else
            {
                foreach (IArchiveEntry entry in archive.Entries)
                {
                    AddPackageFingerprintPart(
                        entry,
                        () => entry.OpenEntryStream(),
                        packageParts);
                }
            }

            return new ModArchiveFingerprintResult
            {
                IsValid = true,
                Fingerprint =
                    CreatePackageSetFingerprint(
                        packageParts)
            };
        }

        private static bool RequiresSequentialReader(
            IArchive archive,
            string archivePath)
        {
            return archive.IsSolid ||
                   string.Equals(
                       Path.GetExtension(archivePath),
                       ".7z",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void AddPackageFingerprintPart(
            IEntry entry,
            Func<Stream> openEntryStream,
            ICollection<string> packageParts)
        {
            string entryPath =
                ModArchiveSupport.EntryPath(
                    entry);

            if (entry.IsDirectory ||
                string.IsNullOrWhiteSpace(entryPath) ||
                !PackageExtensions.Contains(
                    Path.GetExtension(entryPath),
                    StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            using Stream entryStream =
                openEntryStream();

            packageParts.Add(
                CreatePackageFingerprintPart(
                    Path.GetExtension(entryPath),
                    entry.Size,
                    entryStream));
        }

        public string CalculateInstalledModFingerprint(
            InstalledMod mod)
        {
            if (!string.IsNullOrWhiteSpace(
                    mod.ContentFingerprint))
            {
                return mod.ContentFingerprint.Trim();
            }

            return CalculatePackageFileSetFingerprint(
                mod.InstallDirectory,
                mod.PackageFiles);
        }

        private static void MoveDirectoryWithRetry(
    string sourceDirectory,
    string destinationDirectory)
        {
            const int maximumAttempts = 6;

            for (int attempt = 1;
                 attempt <= maximumAttempts;
                 attempt++)
            {
                try
                {
                    Directory.Move(
                        sourceDirectory,
                        destinationDirectory);

                    return;
                }
                catch (UnauthorizedAccessException)
                    when (attempt < maximumAttempts)
                {
                    // Windows Security may inspect newly extracted package files
                    // for a moment, so I give it time to release the folder.
                    Thread.Sleep(
                        attempt * 250);
                }
                catch (IOException)
                    when (attempt < maximumAttempts)
                {
                    Thread.Sleep(
                        attempt * 250);
                }
            }
        }

        private static void TryDeleteDirectory(
            string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(
                        directory,
                        recursive: true);
                }
            }
            catch
            {
                // Cleanup is helpful, but I preserve the original import error.
            }
        }

        public List<ModAssetPackage> ScanAssets(
            InstalledMod mod)
        {
            // Older Limelight libraries predate asset manifests, so they are
            // scanned lazily the first time the live loader needs one.
            return _assetScanner.Scan(
                mod.InstallDirectory);
        }

        private static void ExtractArchiveSafely(
    string archivePath,
    string destinationDirectory)
        {
            using IArchive archive =
                ModArchiveSupport.OpenArchive(
                    archivePath);

            string safeRoot =
                Path.GetFullPath(destinationDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string safeRootPrefix =
                safeRoot +
                Path.DirectorySeparatorChar;

            if (RequiresSequentialReader(
                    archive,
                    archivePath))
            {
                using IReader reader =
                    archive.ExtractAllEntries();

                while (reader.MoveToNextEntry())
                {
                    ExtractEntrySafely(
                        reader.Entry,
                        () => reader.OpenEntryStream(),
                        destinationDirectory,
                        safeRootPrefix);
                }
            }
            else
            {
                foreach (IArchiveEntry entry in archive.Entries)
                {
                    ExtractEntrySafely(
                        entry,
                        () => entry.OpenEntryStream(),
                        destinationDirectory,
                        safeRootPrefix);
                }
            }
        }

        private static void ExtractEntrySafely(
            IEntry entry,
            Func<Stream> openEntryStream,
            string destinationDirectory,
            string safeRootPrefix)
        {
            string entryPath =
                ModArchiveSupport.EntryPath(entry);

            // Some ZIP tools add "." as an entry for the archive root.
            // I skip it because the destination folder already represents it.
            if (ModArchiveSupport.IsRootMarker(
                    entryPath))
            {
                return;
            }

            if (entry.IsEncrypted)
            {
                throw new InvalidDataException(
                    "Password-protected archives are not supported.");
            }

            if (ModArchiveSupport.ContainsLink(entry) ||
                ModArchiveSupport.ContainsUnsafePath(entryPath))
            {
                throw new InvalidDataException(
                    "The archive contains an unsafe path or link.");
            }

            string targetPath =
                Path.GetFullPath(
                    Path.Combine(
                        destinationDirectory,
                        entryPath));

            // I keep every extracted file inside Limelight's private library.
            if (!targetPath.StartsWith(
                    safeRootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The archive contains an unsafe file path.");
            }

            bool isDirectory =
                entry.IsDirectory ||
                entryPath.EndsWith(
                    "/",
                    StringComparison.Ordinal) ||
                entryPath.EndsWith(
                    "\\",
                    StringComparison.Ordinal);

            if (isDirectory)
            {
                Directory.CreateDirectory(
                    targetPath);

                return;
            }

            string? targetFolder =
                Path.GetDirectoryName(
                    targetPath);

            if (targetFolder != null)
            {
                Directory.CreateDirectory(
                    targetFolder);
            }

            using Stream entryStream =
                openEntryStream();

            using FileStream targetStream =
                new FileStream(
                    targetPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

            entryStream.CopyTo(
                targetStream);
        }

        private static List<string> FindPackageFiles(
            string modDirectory)
        {
            return Directory
                .EnumerateFiles(
                    modDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Where(file =>
                    PackageExtensions.Contains(
                        Path.GetExtension(file),
                        StringComparer.OrdinalIgnoreCase))
                .Select(file =>
                    Path.GetRelativePath(
                        modDirectory,
                        file))
                .ToList();
        }

        private static string CalculatePackageFileSetFingerprint(
            string modDirectory,
            IEnumerable<string> packageFiles)
        {
            string safeRoot =
                Path.GetFullPath(modDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string safeRootPrefix =
                safeRoot + Path.DirectorySeparatorChar;

            List<string> packageParts =
                new List<string>();

            foreach (string relativePath in packageFiles)
            {
                string packagePath =
                    Path.GetFullPath(
                        Path.Combine(
                            modDirectory,
                            relativePath));

                if (!packagePath.StartsWith(
                        safeRootPrefix,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(packagePath))
                {
                    throw new FileNotFoundException(
                        "An installed package file could not be fingerprinted.",
                        packagePath);
                }

                FileInfo packageInfo =
                    new FileInfo(packagePath);

                using FileStream packageStream =
                    File.OpenRead(packagePath);

                packageParts.Add(
                    CreatePackageFingerprintPart(
                        packageInfo.Extension,
                        packageInfo.Length,
                        packageStream));
            }

            return CreatePackageSetFingerprint(
                packageParts);
        }

        private static string CreatePackageFingerprintPart(
            string extension,
            long length,
            Stream content)
        {
            string contentHash =
                Convert.ToHexString(
                    SHA256.HashData(content));

            return
                $"{extension.ToLowerInvariant()}:{length}:{contentHash}";
        }

        private static string CreatePackageSetFingerprint(
            IEnumerable<string> packageParts)
        {
            string[] orderedParts =
                packageParts
                    .OrderBy(
                        part => part,
                        StringComparer.Ordinal)
                    .ToArray();

            if (orderedParts.Length == 0)
            {
                throw new InvalidDataException(
                    "No Unreal package files were available to identify this mod.");
            }

            byte[] fingerprintSource =
                Encoding.UTF8.GetBytes(
                    string.Join(
                        "\n",
                        orderedParts));

            return Convert.ToHexString(
                SHA256.HashData(fingerprintSource));
        }

        private static string CreateDisplayName(
            string archivePath)
        {
            string filename =
                Path.GetFileNameWithoutExtension(archivePath);

            // Archive names commonly use underscores in place of spaces.
            return filename
                .Replace('_', ' ')
                .Trim();
        }
    }
}
