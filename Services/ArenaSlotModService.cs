using Limelight.Models;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed class ArenaSlotModService
    {
        private static readonly string[] ProtectedPackagePaths =
        {
            "/Game/Pagoda/Levels/Arenas/DA_Arenas",
            "/Game/Pagoda/Levels/Arenas/LI_Arenas",
            "/Game/Pagoda/Levels/Arenas/Default/LI_Arena_Default"
        };

        public bool RefreshMetadata(
            InstalledMod mod)
        {
            string previousName =
                mod.ArenaSlotName;

            string previousInfoFile =
                mod.ArenaSlotInfoFile;

            string previousArenaId =
                mod.ArenaSlotId;

            string previousDefinition =
                mod.ArenaSlotDefinitionObjectPath;

            string previousMap =
                mod.ArenaSlotMapPackagePath;

            mod.ArenaSlotName =
                string.Empty;

            mod.ArenaSlotInfoFile =
                string.Empty;

            mod.ArenaSlotId =
                string.Empty;

            mod.ArenaSlotDefinitionObjectPath =
                string.Empty;

            mod.ArenaSlotMapPackagePath =
                string.Empty;

            ArenaSlotMetadata? metadata =
                Detect(mod);

            if (metadata is not null)
            {
                mod.ArenaSlotName =
                    metadata.ArenaName;

                mod.ArenaSlotInfoFile =
                    metadata.InfoFileRelativePath;

                mod.ArenaSlotId =
                    metadata.ArenaId;

                mod.ArenaSlotDefinitionObjectPath =
                    metadata.ArenaDefinitionObjectPath;

                mod.ArenaSlotMapPackagePath =
                    metadata.ArenaMapPackagePath;
            }

            return
                !string.Equals(
                    previousName,
                    mod.ArenaSlotName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousInfoFile,
                    mod.ArenaSlotInfoFile,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousArenaId,
                    mod.ArenaSlotId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousDefinition,
                    mod.ArenaSlotDefinitionObjectPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousMap,
                    mod.ArenaSlotMapPackagePath,
                    StringComparison.Ordinal);
        }

        private static ArenaSlotMetadata? Detect(
            InstalledMod mod)
        {
            if (!Directory.Exists(mod.InstallDirectory))
            {
                return null;
            }

            string safeRoot =
                Path.GetFullPath(mod.InstallDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string safeRootPrefix =
                safeRoot + Path.DirectorySeparatorChar;

            foreach (string infoFile in
                     Directory.EnumerateFiles(
                         safeRoot,
                         "info.json",
                         SearchOption.AllDirectories))
            {
                string fullInfoFile =
                    Path.GetFullPath(infoFile);

                if (!fullInfoFile.StartsWith(
                        safeRootPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ArenaSlotManifest? manifest =
                    TryReadManifest(fullInfoFile);

                if (manifest is null ||
                    !HasMatchingAssets(
                        mod,
                        manifest))
                {
                    continue;
                }

                return new ArenaSlotMetadata(
                    manifest.ArenaName,
                    Path.GetRelativePath(
                        safeRoot,
                        fullInfoFile),
                    manifest.ArenaId,
                    manifest.ArenaDefinition,
                    manifest.ArenaMap);
            }

            return null;
        }

        private static ArenaSlotManifest? TryReadManifest(
            string infoFile)
        {
            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(
                        File.ReadAllText(infoFile));

                JsonElement root =
                    document.RootElement;

                string? arenaName =
                    ReadString(
                        root,
                        "ArenaName");

                string? arenaId =
                    ReadString(
                        root,
                        "ArenaId");

                string? arenaDefinition =
                    ReadString(
                        root,
                        "ArenaDefinition");

                string? arenaMap =
                    ReadString(
                        root,
                        "ArenaMap");

                if (!IsValidArenaName(arenaName) ||
                    !IsValidArenaId(arenaId) ||
                    !TrySplitDefinitionPath(
                        arenaDefinition,
                        out string definitionPackagePath) ||
                    !IsValidMapPath(arenaMap) ||
                    IsProtectedPackagePath(
                        definitionPackagePath) ||
                    IsProtectedPackagePath(
                        arenaMap!))
                {
                    return null;
                }

                return new ArenaSlotManifest(
                    arenaName!,
                    arenaId!,
                    arenaDefinition!,
                    definitionPackagePath,
                    arenaMap!);
            }
            catch (Exception exception)
                when (exception is IOException or JsonException)
            {
                return null;
            }
        }

        private static string? ReadString(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString()?.Trim();
        }

        private static bool IsValidArenaName(
            string? arenaName)
        {
            return !string.IsNullOrWhiteSpace(arenaName) &&
                   arenaName.Length <= 96 &&
                   !arenaName.Any(char.IsControl);
        }

        private static bool IsValidArenaId(
            string? arenaId)
        {
            return !string.IsNullOrWhiteSpace(arenaId) &&
                   arenaId.Length <= 128 &&
                   Regex.IsMatch(
                       arenaId,
                       @"^Environment\.Arena\.Mod(?:\.[A-Za-z0-9_]+){2,}$",
                       RegexOptions.CultureInvariant);
        }

        private static bool TrySplitDefinitionPath(
            string? objectPath,
            out string packagePath)
        {
            packagePath =
                string.Empty;

            if (string.IsNullOrWhiteSpace(objectPath) ||
                objectPath.Length > 256)
            {
                return false;
            }

            Match match =
                Regex.Match(
                    objectPath,
                    @"^(?<package>/Game/[A-Za-z0-9_/]+)\.(?<object>[A-Za-z0-9_]+)$",
                    RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return false;
            }

            packagePath =
                match.Groups["package"].Value;

            string packageName =
                packagePath[
                    (packagePath.LastIndexOf('/') + 1)..];

            return packageName.Equals(
                match.Groups["object"].Value,
                StringComparison.Ordinal);
        }

        private static bool IsValidMapPath(
            string? mapPath)
        {
            return !string.IsNullOrWhiteSpace(mapPath) &&
                   mapPath.Length <= 256 &&
                   Regex.IsMatch(
                       mapPath,
                       @"^/Game/[A-Za-z0-9_/]*[A-Za-z0-9_]$",
                       RegexOptions.CultureInvariant);
        }

        private static bool IsProtectedPackagePath(
            string packagePath)
        {
            return ProtectedPackagePaths.Any(path =>
                packagePath.Equals(
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasMatchingAssets(
            InstalledMod mod,
            ArenaSlotManifest manifest)
        {
            return mod.AssetPackages.Any(package =>
                       package.PackagePath.Equals(
                           manifest.ArenaDefinitionPackagePath,
                           StringComparison.OrdinalIgnoreCase)) &&
                   mod.AssetPackages.Any(package =>
                       package.Kind == ModAssetKind.Map &&
                       package.PackagePath.Equals(
                           manifest.ArenaMap,
                           StringComparison.OrdinalIgnoreCase));
        }

        private sealed record ArenaSlotManifest(
            string ArenaName,
            string ArenaId,
            string ArenaDefinition,
            string ArenaDefinitionPackagePath,
            string ArenaMap);

        private sealed record ArenaSlotMetadata(
            string ArenaName,
            string InfoFileRelativePath,
            string ArenaId,
            string ArenaDefinitionObjectPath,
            string ArenaMapPackagePath);
    }
}
