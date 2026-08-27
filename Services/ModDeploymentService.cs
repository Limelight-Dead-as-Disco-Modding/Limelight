using Limelight.Models;
using System.IO;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class ModDeploymentService
    {
        private const string ManifestFilename =
            ".limelight-deployment.json";

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        public void Activate(
            InstalledMod mod,
            IEnumerable<InstalledMod> companionMods,
            string gameDirectory)
        {
            Synchronize(
                mod,
                companionMods,
                gameDirectory);
        }

        public void Deactivate(
            IEnumerable<InstalledMod> companionMods,
            string gameDirectory)
        {
            // I only put the spotlight away here. Imported slot mods stay
            // backstage because their in-game catalogues still need them.
            Synchronize(
                activeMod: null,
                companionMods,
                gameDirectory);
        }

        private static void Synchronize(
            InstalledMod? activeMod,
            IEnumerable<InstalledMod> companionMods,
            string gameDirectory)
        {
            string modsDirectory =
                GetGameModsDirectory(gameDirectory);

            Directory.CreateDirectory(modsDirectory);

            List<string> previouslyManagedFiles =
                LoadManifest(modsDirectory);

            List<DeploymentFile> newFiles =
                BuildDeploymentList(
                    activeMod,
                    companionMods,
                    modsDirectory);

            EnsureNoManualFileConflicts(
                newFiles,
                previouslyManagedFiles,
                modsDirectory);

            // Copy everything to temporary files first. The currently active
            // mod stays untouched if one of the source files cannot be read.
            foreach (DeploymentFile file in newFiles)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(file.StagingPath) ??
                    modsDirectory);

                File.Copy(
                    file.SourcePath,
                    file.StagingPath,
                    overwrite: true);
            }

            var backups =
                new List<BackupFile>();

            var deployedFiles =
                new List<string>();

            try
            {
                // Move Limelight's old files aside instead of deleting them
                // immediately. This lets us restore them if deployment fails.
                foreach (string relativePath in previouslyManagedFiles)
                {
                    if (!TryResolveManagedPath(
                            modsDirectory,
                            relativePath,
                            out string originalPath))
                    {
                        continue;
                    }

                    if (!File.Exists(originalPath))
                    {
                        continue;
                    }

                    string backupPath =
                        originalPath + ".limelight-backup";

                    File.Move(
                        originalPath,
                        backupPath,
                        overwrite: true);

                    backups.Add(
                        new BackupFile(
                            originalPath,
                            backupPath));
                }

                foreach (DeploymentFile file in newFiles)
                {
                    File.Move(
                        file.StagingPath,
                        file.FinalPath,
                        overwrite: true);

                    deployedFiles.Add(
                        file.FinalPath);
                }

                SaveManifest(
                    modsDirectory,
                    newFiles.Select(file =>
                        Path.GetRelativePath(
                            modsDirectory,
                            file.FinalPath)));
            }
            catch
            {
                // Remove any partially deployed new files.
                foreach (string deployedFile in deployedFiles)
                {
                    if (File.Exists(deployedFile))
                    {
                        File.Delete(deployedFile);
                    }
                }

                // Put the previous active mod back exactly as it was.
                RestoreBackups(backups);

                DeleteStagingFiles(newFiles);

                try
                {
                    SaveManifest(
                        modsDirectory,
                        previouslyManagedFiles);
                }
                catch
                {
                    // Preserve the original deployment exception.
                }

                throw;
            }

            // The new deployment is now committed, so old backups are no
            // longer needed. Failure to remove one does not break the mod.
            foreach (BackupFile backup in backups)
            {
                try
                {
                    if (File.Exists(backup.BackupPath))
                    {
                        File.Delete(backup.BackupPath);
                    }
                }
                catch (IOException)
                {
                    // A leftover backup is harmless and is ignored by Unreal.
                }
            }

            DeleteEmptyManagedDirectories(
                modsDirectory,
                previouslyManagedFiles);
        }

        public void PurgeAllMods(
            string gameDirectory)
        {
            string modsDirectory =
                GetGameModsDirectory(
                    gameDirectory);

            if (Directory.Exists(modsDirectory))
            {
                // I remove the whole folder so unmanaged files and stale
                // deployment metadata cannot survive a full purge.
                Directory.Delete(
                    modsDirectory,
                    recursive: true);
            }

            // Keeping an empty folder makes the finished state obvious and
            // gives future deployments a known clean destination.
            Directory.CreateDirectory(
                modsDirectory);
        }

        private static List<DeploymentFile> BuildDeploymentList(
            InstalledMod? activeMod,
            IEnumerable<InstalledMod> companionMods,
            string modsDirectory)
        {
            var deploymentFiles =
                new List<DeploymentFile>();

            var usedDestinations =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            IEnumerable<InstalledMod> modsToDeploy =
                (activeMod is null
                    ? Enumerable.Empty<InstalledMod>()
                    : new[] { activeMod })
                .Concat(companionMods.Where(mod =>
                    mod.IsCharacterSlotMod ||
                    mod.IsArenaSlotMod ||
                    mod.IsConventionalMod))
                .GroupBy(
                    mod => mod.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());

            foreach (InstalledMod mod in modsToDeploy)
            {
                AddModDeploymentFiles(
                    mod,
                    modsDirectory,
                    usedDestinations,
                    deploymentFiles);
            }

            return deploymentFiles;
        }

        private static void AddModDeploymentFiles(
            InstalledMod mod,
            string modsDirectory,
            ISet<string> usedDestinations,
            ICollection<DeploymentFile> deploymentFiles)
        {
            string destinationDirectory =
                mod.IsCharacterSlotMod || mod.IsArenaSlotMod
                    ? Path.Combine(
                        modsDirectory,
                        CreateSlotDirectoryName(mod))
                    : modsDirectory;

            IEnumerable<string> sourceFiles =
                mod.PackageFiles.Concat(
                    mod.IsCharacterSlotMod
                        ? new[] { mod.CharacterSlotInfoFile }
                        : mod.IsArenaSlotMod
                            ? new[] { mod.ArenaSlotInfoFile }
                        : Array.Empty<string>())
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase);

            foreach (string relativePath in sourceFiles)
            {
                string sourcePath = Path.GetFullPath(
                    Path.Combine(
                        mod.InstallDirectory,
                        relativePath));

                string safeLibraryRoot =
                    Path.GetFullPath(mod.InstallDirectory)
                        .TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;

                if (!sourcePath.StartsWith(
                        safeLibraryRoot,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(sourcePath))
                {
                    throw new InvalidDataException(
                        $"A package file is missing from {mod.DisplayName}.");
                }

                string finalPath =
                    Path.Combine(
                        destinationDirectory,
                        Path.GetFileName(sourcePath));

                if (!usedDestinations.Add(
                        Path.GetFullPath(finalPath)))
                {
                    throw new InvalidDataException(
                        $"Two managed mod files would share {Path.GetFileName(finalPath)}.");
                }

                deploymentFiles.Add(
                    new DeploymentFile(
                        sourcePath,
                        finalPath,
                        finalPath + ".limelight-new"));
            }
        }

        private static string CreateSlotDirectoryName(
            InstalledMod mod)
        {
            string slotName =
                mod.IsArenaSlotMod
                    ? mod.ArenaSlotName
                    : mod.CharacterSlotName;

            string safeSlotName =
                new string(
                    slotName
                        .Where(character =>
                            char.IsLetterOrDigit(character) ||
                            character == '_')
                        .ToArray());

            if (string.IsNullOrWhiteSpace(safeSlotName))
            {
                safeSlotName =
                    mod.IsArenaSlotMod
                        ? "Arena"
                        : "Character";
            }

            string idSuffix =
                mod.Id[..Math.Min(8, mod.Id.Length)];

            return
                $"Limelight_{safeSlotName}_{idSuffix}";
        }

        private static void EnsureNoManualFileConflicts(
            IEnumerable<DeploymentFile> newFiles,
            IEnumerable<string> managedFiles,
            string modsDirectory)
        {
            var managedSet =
                new HashSet<string>(
                    managedFiles.Select(path =>
                        NormalizeRelativePath(path)),
                    StringComparer.OrdinalIgnoreCase);

            foreach (DeploymentFile file in newFiles)
            {
                string relativePath =
                    NormalizeRelativePath(
                        Path.GetRelativePath(
                            modsDirectory,
                            file.FinalPath));

                // Limelight will never overwrite a matching file unless its
                // own manifest proves that Limelight deployed it.
                if (File.Exists(file.FinalPath) &&
                    !managedSet.Contains(relativePath))
                {
                    throw new IOException(
                        $"{relativePath} already exists in ~mods and is not managed by Limelight.");
                }
            }
        }

        private static string NormalizeRelativePath(
            string path)
        {
            return path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);
        }

        private static string GetGameModsDirectory(
            string gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                throw new InvalidOperationException(
                    "Connect the Dead as Disco installation first.");
            }

            return Path.Combine(
                gameDirectory,
                "Pagoda",
                "Content",
                "Paks",
                "~mods");
        }

        private static List<string> LoadManifest(
            string modsDirectory)
        {
            string manifestPath =
                Path.Combine(
                    modsDirectory,
                    ManifestFilename);

            if (!File.Exists(manifestPath))
            {
                return new List<string>();
            }

            try
            {
                string json =
                    File.ReadAllText(manifestPath);

                return JsonSerializer.Deserialize<List<string>>(json)
                       ?? new List<string>();
            }
            catch (JsonException)
            {
                // A damaged manifest is treated as untrusted. This prevents
                // Limelight from deleting files it cannot prove it owns.
                return new List<string>();
            }
        }

        private static void SaveManifest(
            string modsDirectory,
            IEnumerable<string> filenames)
        {
            string manifestPath =
                Path.Combine(
                    modsDirectory,
                    ManifestFilename);

            string temporaryPath =
                manifestPath + ".tmp";

            string json =
                JsonSerializer.Serialize(
                    filenames.ToList(),
                    JsonOptions);

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                manifestPath,
                overwrite: true);
        }

        private static void RestoreBackups(
            IEnumerable<BackupFile> backups)
        {
            foreach (BackupFile backup in backups.Reverse())
            {
                if (File.Exists(backup.BackupPath))
                {
                    File.Move(
                        backup.BackupPath,
                        backup.OriginalPath,
                        overwrite: true);
                }
            }
        }

        private static bool TryResolveManagedPath(
            string modsDirectory,
            string relativePath,
            out string resolvedPath)
        {
            resolvedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath))
            {
                return false;
            }

            string safeRoot =
                Path.GetFullPath(modsDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string safeRootPrefix =
                safeRoot + Path.DirectorySeparatorChar;

            string candidate =
                Path.GetFullPath(
                    Path.Combine(
                        safeRoot,
                        relativePath));

            if (!candidate.StartsWith(
                    safeRootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            resolvedPath = candidate;
            return true;
        }

        private static void DeleteEmptyManagedDirectories(
            string modsDirectory,
            IEnumerable<string> managedPaths)
        {
            IEnumerable<string> directories =
                managedPaths
                    .Select(path =>
                        Path.GetDirectoryName(path))
                    .Where(path =>
                        !string.IsNullOrWhiteSpace(path))
                    .Cast<string>()
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(path => path.Length);

            foreach (string relativeDirectory in directories)
            {
                if (!TryResolveManagedPath(
                        modsDirectory,
                        relativeDirectory,
                        out string directory) ||
                    !Directory.Exists(directory) ||
                    Directory.EnumerateFileSystemEntries(
                        directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory);
            }
        }

        private static void DeleteStagingFiles(
            IEnumerable<DeploymentFile> files)
        {
            foreach (DeploymentFile file in files)
            {
                if (File.Exists(file.StagingPath))
                {
                    File.Delete(file.StagingPath);
                }
            }
        }

        private sealed record DeploymentFile(
            string SourcePath,
            string FinalPath,
            string StagingPath);

        private sealed record BackupFile(
            string OriginalPath,
            string BackupPath);
    }
}
