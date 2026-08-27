using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Limelight.Models;

namespace Limelight.Services
{
    public sealed class CharacterSlotLoaderStatus
    {
        public bool IsInstalled { get; init; }

        public string LogicModsDirectory { get; init; } =
            string.Empty;

        public IReadOnlyList<string> MissingFiles { get; init; } =
            Array.Empty<string>();
    }

    public sealed class CharacterSlotLoaderService
    {
        public const string RuntimeCatalogueFilename =
            "character-slot-catalogue.txt";

        public const string RuntimeModeFilename =
            "character-slot-loader-mode.txt";

        private static readonly (
            string FileName,
            string ResourceName)[] ManagedPayloads =
        {
            (
                "LimelightCharacterLoader.pak",
                "Limelight.Payloads.CharacterLoader.LimelightCharacterLoader.pak"),
            (
                "LimelightCharacterLoader.utoc",
                "Limelight.Payloads.CharacterLoader.LimelightCharacterLoader.utoc"),
            (
                "LimelightCharacterLoader.ucas",
                "Limelight.Payloads.CharacterLoader.LimelightCharacterLoader.ucas")
        };

        private static readonly string[] LegacyRequiredFiles =
        {
            "CharacterLoader.pak",
            "CharacterLoader.utoc",
            "CharacterLoader.ucas"
        };

        private readonly Assembly _assembly =
            typeof(CharacterSlotLoaderService).Assembly;

        public CharacterSlotLoaderStatus Inspect(
            string gameDirectory)
        {
            string logicModsDirectory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Content",
                    "Paks",
                    "LogicMods");

            List<string> missingFiles =
                ManagedPayloads
                    .Where(payload =>
                        !FileMatchesEmbeddedPayload(
                            Path.Combine(
                                logicModsDirectory,
                                payload.FileName),
                            payload.ResourceName))
                    .Select(payload =>
                        payload.FileName)
                    .ToList();

            bool legacyLoaderInstalled =
                LegacyRequiredFiles.All(fileName =>
                    File.Exists(
                        Path.Combine(
                            logicModsDirectory,
                            fileName)));

            return new CharacterSlotLoaderStatus
            {
                // I keep the original loader working for existing installs,
                // while new setups receive Limelight's checked payload.
                IsInstalled =
                    missingFiles.Count == 0 ||
                    legacyLoaderInstalled,
                LogicModsDirectory = logicModsDirectory,
                MissingFiles = missingFiles
            };
        }

        public void EnsureInstalled(
            string gameDirectory)
        {
            CharacterSlotLoaderStatus status =
                Inspect(gameDirectory);

            if (status.IsInstalled)
            {
                return;
            }

            Directory.CreateDirectory(
                status.LogicModsDirectory);

            foreach ((string fileName, string resourceName) in
                     ManagedPayloads)
            {
                InstallPayload(
                    status.LogicModsDirectory,
                    fileName,
                    resourceName);
            }

            if (!Inspect(gameDirectory).IsInstalled)
            {
                throw new InvalidOperationException(
                    "Limelight could not verify its Character Loader Logic Mod after installation.");
            }
        }

        public void SynchronizeRuntimeCatalogue(
            IEnumerable<InstalledMod> characterSlotMods,
            string gameDirectory)
        {
            string runtimeDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Runtime");

            Directory.CreateDirectory(
                runtimeDirectory);

            List<string> definitionPaths =
                characterSlotMods
                    .Where(mod =>
                        mod.IsCharacterSlotMod &&
                        Directory.Exists(mod.InstallDirectory))
                    .SelectMany(mod =>
                        mod.CharacterSlotDefinitionPackagePaths is { Count: > 0 }
                            ? mod.CharacterSlotDefinitionPackagePaths.AsEnumerable()
                            : new[]
                            {
                                mod.CharacterSlotDefinitionPackagePath
                            })
                    .Where(packagePath =>
                        !string.IsNullOrWhiteSpace(packagePath))
                    .Select(packagePath =>
                        packagePath +
                        "." +
                        packagePath[
                            (packagePath.LastIndexOf('/') + 1)..])
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path =>
                        path,
                    StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (definitionPaths.Count > 0)
            {
                EnsureInstalled(gameDirectory);
            }

            WriteAllLinesAtomically(
                Path.Combine(
                    runtimeDirectory,
                    RuntimeCatalogueFilename),
                definitionPaths);

            // I step aside when the author's own Lua loader is both present
            // and enabled. One stage manager is charming; two make duplicates.
            WriteAllTextAtomically(
                Path.Combine(
                    runtimeDirectory,
                    RuntimeModeFilename),
                HasEnabledOfficialLuaLoader(gameDirectory)
                    ? "official"
                    : "limelight");
        }

        private void InstallPayload(
            string logicModsDirectory,
            string fileName,
            string resourceName)
        {
            string targetPath =
                Path.Combine(
                    logicModsDirectory,
                    fileName);

            if (FileMatchesEmbeddedPayload(
                    targetPath,
                    resourceName))
            {
                return;
            }

            string temporaryPath =
                targetPath +
                ".limelight-installing";

            try
            {
                using Stream source =
                    OpenPayloadResource(resourceName);

                using (FileStream destination =
                    new(
                        temporaryPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                {
                    source.CopyTo(destination);
                }

                // I verify each travelling companion before it can replace a
                // working loader file.
                if (!FileMatchesEmbeddedPayload(
                        temporaryPath,
                        resourceName))
                {
                    throw new InvalidOperationException(
                        $"The embedded {fileName} payload failed its integrity check.");
                }

                File.Move(
                    temporaryPath,
                    targetPath,
                    overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                    // I leave temporary cleanup best-effort because the
                    // verified Logic Mod files are what matter.
                }
            }
        }

        private bool FileMatchesEmbeddedPayload(
            string filePath,
            string resourceName)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            using Stream embeddedPayload =
                OpenPayloadResource(resourceName);

            FileInfo installedFile =
                new(filePath);

            if (installedFile.Length != embeddedPayload.Length)
            {
                return false;
            }

            using FileStream installedPayload =
                File.OpenRead(filePath);

            return SHA256.HashData(embeddedPayload)
                .SequenceEqual(
                    SHA256.HashData(installedPayload));
        }

        private Stream OpenPayloadResource(
            string resourceName)
        {
            return _assembly.GetManifestResourceStream(resourceName) ??
                throw new InvalidOperationException(
                    $"The embedded Character Loader resource {resourceName} could not be found.");
        }

        private static bool HasEnabledOfficialLuaLoader(
            string gameDirectory)
        {
            string logicModsDirectory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Content",
                    "Paks",
                    "LogicMods");

            // I only step aside when the original script still has its own
            // complete actor payload to talk to.
            if (!LegacyRequiredFiles.All(fileName =>
                    File.Exists(
                        Path.Combine(
                            logicModsDirectory,
                            fileName))))
            {
                return false;
            }

            string win64Directory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Binaries",
                    "Win64");

            string[] candidateModsDirectories =
            {
                Path.Combine(
                    win64Directory,
                    "ue4ss",
                    "Mods"),
                Path.Combine(
                    win64Directory,
                    "Mods")
            };

            return candidateModsDirectories.Any(
                HasEnabledOfficialLuaLoaderInDirectory);
        }

        private static bool HasEnabledOfficialLuaLoaderInDirectory(
            string modsDirectory)
        {
            string modsTextPath =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            if (!Directory.Exists(modsDirectory) ||
                !File.Exists(modsTextPath))
            {
                return false;
            }

            HashSet<string> enabledMods =
                File.ReadLines(modsTextPath)
                    .Select(line =>
                        line.Trim())
                    .Where(line =>
                        !line.StartsWith(";", StringComparison.Ordinal) &&
                        !line.StartsWith("#", StringComparison.Ordinal))
                    .Select(line =>
                        line.Split(
                            ':',
                            2,
                            StringSplitOptions.TrimEntries))
                    .Where(parts =>
                        parts.Length == 2 &&
                        parts[1] == "1")
                    .Select(parts =>
                        parts[0])
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            foreach (string modDirectory in
                     Directory.EnumerateDirectories(modsDirectory))
            {
                string modName =
                    Path.GetFileName(modDirectory);

                if (!enabledMods.Contains(modName) ||
                    modName.Equals(
                        "LimelightBridge",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string scriptPath =
                    Path.Combine(
                        modDirectory,
                        "Scripts",
                        "main.lua");

                if (!File.Exists(scriptPath))
                {
                    continue;
                }

                try
                {
                    string script =
                        File.ReadAllText(scriptPath);

                    if (script.Contains(
                            "AddToModDefinitions",
                            StringComparison.Ordinal) &&
                        script.Contains(
                            "CharacterName",
                            StringComparison.Ordinal) &&
                        script.Contains(
                            "35005383",
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // I can safely use Limelight's catalogue if Windows is
                    // momentarily hiding an optional third-party script.
                }
            }

            return false;
        }

        private static void WriteAllLinesAtomically(
            string path,
            IEnumerable<string> lines)
        {
            string temporaryPath =
                path + ".tmp";

            File.WriteAllLines(
                temporaryPath,
                lines);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }

        private static void WriteAllTextAtomically(
            string path,
            string text)
        {
            string temporaryPath =
                path + ".tmp";

            File.WriteAllText(
                temporaryPath,
                text);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
    }
}
