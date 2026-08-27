using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Limelight.Services
{
    public sealed class ArenaSlotLoaderStatus
    {
        public bool IsInstalled { get; init; }

        public bool Ue4ssInstalled { get; init; }

        public string LogicModsDirectory { get; init; } =
            string.Empty;

        public string Ue4ssModDirectory { get; init; } =
            string.Empty;

        public IReadOnlyList<string> MissingFiles { get; init; } =
            Array.Empty<string>();
    }

    public sealed class ArenaSlotLoaderService
    {
        public const string ProductName =
            "Limelight Arena Slot Loader";

        private const string Ue4ssModName =
            "LimelightArenaSlotLoader";

        private const string ManagedMarkerName =
            ".limelight-arena-slot-loader-managed";

        private static readonly (
            string FileName,
            string ResourceName)[] LogicModPayloads =
        {
            (
                "LimelightArenaSlotLoader.pak",
                "Limelight.Payloads.ArenaSlotLoader.LimelightArenaSlotLoader.pak"),
            (
                "LimelightArenaSlotLoader.utoc",
                "Limelight.Payloads.ArenaSlotLoader.LimelightArenaSlotLoader.utoc"),
            (
                "LimelightArenaSlotLoader.ucas",
                "Limelight.Payloads.ArenaSlotLoader.LimelightArenaSlotLoader.ucas")
        };

        private static readonly (
            string RelativePath,
            string ResourceName)[] ScriptPayloads =
        {
            (
                Path.Combine(
                    "Scripts",
                    "main.lua"),
                "Limelight.Payloads.ArenaSlotLoader.main.lua"),
            (
                Path.Combine(
                    "Scripts",
                    "arena_slot_loader",
                    "json.lua"),
                "Limelight.Payloads.ArenaSlotLoader.json.lua")
        };

        private readonly Assembly _assembly =
            typeof(ArenaSlotLoaderService).Assembly;

        private readonly Ue4ssDetectionService _detectionService =
            new();

        public ArenaSlotLoaderStatus Inspect(
            string gameDirectory)
        {
            string logicModsDirectory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Content",
                    "Paks",
                    "LogicMods");

            Ue4ssDetectionResult installation =
                _detectionService.Detect(
                    gameDirectory);

            string ue4ssModDirectory =
                Path.Combine(
                    installation.ModsDirectory,
                    Ue4ssModName);

            List<string> missingFiles =
                new();

            foreach ((string fileName, string resourceName) in
                     LogicModPayloads)
            {
                if (!FileMatchesEmbeddedPayload(
                        Path.Combine(
                            logicModsDirectory,
                            fileName),
                        resourceName))
                {
                    missingFiles.Add(fileName);
                }
            }

            if (!installation.IsInstalled)
            {
                missingFiles.Add("UE4SS");
            }
            else
            {
                foreach ((string relativePath, string resourceName) in
                         ScriptPayloads)
                {
                    if (!FileMatchesEmbeddedPayload(
                            Path.Combine(
                                ue4ssModDirectory,
                                relativePath),
                            resourceName))
                    {
                        missingFiles.Add(
                            Path.Combine(
                                Ue4ssModName,
                                relativePath));
                    }
                }

                string bpModLoaderScript =
                    Path.Combine(
                        installation.ModsDirectory,
                        "BPModLoaderMod",
                        "Scripts",
                        "main.lua");

                if (!File.Exists(bpModLoaderScript))
                {
                    missingFiles.Add(
                        Path.Combine(
                            "BPModLoaderMod",
                            "Scripts",
                            "main.lua"));
                }

                string modsTextPath =
                    Path.Combine(
                        installation.ModsDirectory,
                        "mods.txt");

                if (!IsEnabled(
                        modsTextPath,
                        Ue4ssModName))
                {
                    missingFiles.Add(
                        "mods.txt:" + Ue4ssModName);
                }

                if (!IsEnabled(
                        modsTextPath,
                        "BPModLoaderMod"))
                {
                    missingFiles.Add(
                        "mods.txt:BPModLoaderMod");
                }
            }

            return new ArenaSlotLoaderStatus
            {
                IsInstalled =
                    missingFiles.Count == 0,
                Ue4ssInstalled =
                    installation.IsInstalled,
                LogicModsDirectory =
                    logicModsDirectory,
                Ue4ssModDirectory =
                    ue4ssModDirectory,
                MissingFiles =
                    missingFiles
            };
        }

        public void EnsureInstalled(
            string gameDirectory)
        {
            // I accept a complete standalone installation so Limelight can share it without taking ownership of the user's files.
            if (Inspect(gameDirectory).IsInstalled)
            {
                return;
            }

            Ue4ssDetectionResult installation =
                _detectionService.Detect(
                    gameDirectory);

            if (!installation.IsInstalled)
            {
                throw new InvalidOperationException(
                    "Arena Slot Loader needs UE4SS. Run Limelight's Live Loader setup, then retry the arena deployment.");
            }

            string bpModLoaderScript =
                Path.Combine(
                    installation.ModsDirectory,
                    "BPModLoaderMod",
                    "Scripts",
                    "main.lua");

            if (!File.Exists(bpModLoaderScript))
            {
                throw new InvalidOperationException(
                    "Arena Slot Loader needs UE4SS's BPModLoaderMod, but its script is missing.");
            }

            Directory.CreateDirectory(
                installation.ModsDirectory);

            string modDirectory =
                Path.Combine(
                    installation.ModsDirectory,
                    Ue4ssModName);

            string markerPath =
                Path.Combine(
                    modDirectory,
                    ManagedMarkerName);

            bool hasUnmanagedFiles =
                Directory.Exists(modDirectory) &&
                Directory.EnumerateFileSystemEntries(
                    modDirectory).Any() &&
                !File.Exists(markerPath);

            bool isRecognizedStandaloneInstallation =
                hasUnmanagedFiles &&
                ScriptPayloads.All(payload =>
                    FileMatchesEmbeddedPayload(
                        Path.Combine(
                            modDirectory,
                            payload.RelativePath),
                        payload.ResourceName));

            if (hasUnmanagedFiles &&
                !isRecognizedStandaloneInstallation)
            {
                throw new InvalidOperationException(
                    "A non-managed LimelightArenaSlotLoader folder already exists. Limelight left it untouched.");
            }

            string logicModsDirectory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Content",
                    "Paks",
                    "LogicMods");

            Directory.CreateDirectory(
                logicModsDirectory);

            foreach ((string fileName, string resourceName) in
                     LogicModPayloads)
            {
                InstallPayload(
                    Path.Combine(
                        logicModsDirectory,
                        fileName),
                    resourceName);
            }

            Directory.CreateDirectory(
                modDirectory);

            foreach ((string relativePath, string resourceName) in
                     ScriptPayloads)
            {
                InstallPayload(
                    Path.Combine(
                        modDirectory,
                        relativePath),
                    resourceName);
            }

            if (!isRecognizedStandaloneInstallation)
            {
                // I mark only folders Limelight created so a standalone copy
                // can remain user-owned while still sharing the same payload.
                WriteTextAtomically(
                    markerPath,
                    "1" + Environment.NewLine);
            }

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            EnableMod(
                modsTextPath,
                "BPModLoaderMod");

            EnableMod(
                modsTextPath,
                Ue4ssModName);

            if (!Inspect(gameDirectory).IsInstalled)
            {
                throw new InvalidOperationException(
                    "Limelight could not verify the Arena Slot Loader after installation.");
            }
        }

        private void InstallPayload(
            string targetPath,
            string resourceName)
        {
            if (FileMatchesEmbeddedPayload(
                    targetPath,
                    resourceName))
            {
                return;
            }

            string? directory =
                Path.GetDirectoryName(
                    targetPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
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

                if (!FileMatchesEmbeddedPayload(
                        temporaryPath,
                        resourceName))
                {
                    throw new InvalidOperationException(
                        "An embedded Arena Slot Loader file failed its integrity check.");
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
                    // I keep cleanup best-effort because the verified loader
                    // file is the state that affects the user's game.
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
                    $"The embedded Arena Slot Loader resource {resourceName} could not be found.");
        }

        private static bool IsEnabled(
            string modsTextPath,
            string modName)
        {
            if (!File.Exists(modsTextPath))
            {
                return false;
            }

            return File.ReadLines(modsTextPath)
                .Select(line =>
                    line.Trim())
                .Where(line =>
                    !line.StartsWith(
                        ";",
                        StringComparison.Ordinal) &&
                    !line.StartsWith(
                        "#",
                        StringComparison.Ordinal))
                .Select(line =>
                    line.Split(
                        ':',
                        2,
                        StringSplitOptions.TrimEntries))
                .Any(parts =>
                    parts.Length == 2 &&
                    parts[0].Equals(
                        modName,
                        StringComparison.OrdinalIgnoreCase) &&
                    parts[1] == "1");
        }

        private static void EnableMod(
            string modsTextPath,
            string modName)
        {
            List<string> lines =
                File.Exists(modsTextPath)
                    ? File.ReadAllLines(modsTextPath).ToList()
                    : new List<string>();

            bool found =
                false;

            for (int index = 0;
                 index < lines.Count;
                 index++)
            {
                string[] parts =
                    lines[index]
                        .Trim()
                        .Split(
                            ':',
                            2,
                            StringSplitOptions.TrimEntries);

                if (parts.Length != 2 ||
                    !parts[0].Equals(
                        modName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                lines[index] =
                    modName + " : 1";
                found = true;
            }

            if (!found)
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(
                        lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(
                    modName + " : 1");
            }

            WriteLinesAtomically(
                modsTextPath,
                lines);
        }

        private static void WriteLinesAtomically(
            string path,
            IEnumerable<string> lines)
        {
            string temporaryPath =
                path + ".limelight-installing";

            File.WriteAllLines(
                temporaryPath,
                lines);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }

        private static void WriteTextAtomically(
            string path,
            string text)
        {
            string temporaryPath =
                path + ".limelight-installing";

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
