using Limelight.Models;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed class CharacterSlotModService
    {
        private const string CharacterAssetRoot =
            "/Game/Pagoda/Characters/Player/ModdedCharacters/";

        public bool RefreshMetadata(
            InstalledMod mod)
        {
            string previousName =
                mod.CharacterSlotName;

            string previousInfoFile =
                mod.CharacterSlotInfoFile;

            string previousMeshPackagePath =
                mod.CharacterSlotMeshPackagePath;

            string previousDefinitionPackagePath =
                mod.CharacterSlotDefinitionPackagePath;

            List<string> previousInfoFiles =
                mod.CharacterSlotInfoFiles?.ToList() ??
                new List<string>();

            List<string> previousDefinitionPackagePaths =
                mod.CharacterSlotDefinitionPackagePaths?.ToList() ??
                new List<string>();

            mod.CharacterSlotName =
                string.Empty;

            mod.CharacterSlotInfoFile =
                string.Empty;

            mod.CharacterSlotMeshPackagePath =
                string.Empty;

            mod.CharacterSlotDefinitionPackagePath =
                string.Empty;

            mod.CharacterSlotInfoFiles =
                new List<string>();

            mod.CharacterSlotDefinitionPackagePaths =
                new List<string>();

            CharacterSlotMetadata? metadata =
                Detect(mod);

            if (metadata is not null)
            {
                mod.CharacterSlotName =
                    metadata.CharacterName;

                mod.CharacterSlotInfoFile =
                    metadata.InfoFileRelativePath;

                mod.CharacterSlotMeshPackagePath =
                    metadata.MeshPackagePath;

                mod.CharacterSlotDefinitionPackagePath =
                    metadata.DefinitionPackagePath;

                mod.CharacterSlotInfoFiles.AddRange(
                    metadata.InfoFileRelativePaths);

                mod.CharacterSlotDefinitionPackagePaths.AddRange(
                    metadata.DefinitionPackagePaths);
            }

            return
                !string.Equals(
                    previousName,
                    mod.CharacterSlotName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousInfoFile,
                    mod.CharacterSlotInfoFile,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousMeshPackagePath,
                    mod.CharacterSlotMeshPackagePath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previousDefinitionPackagePath,
                    mod.CharacterSlotDefinitionPackagePath,
                    StringComparison.Ordinal) ||
                !previousInfoFiles.SequenceEqual(
                    mod.CharacterSlotInfoFiles,
                    StringComparer.OrdinalIgnoreCase) ||
                !previousDefinitionPackagePaths.SequenceEqual(
                    mod.CharacterSlotDefinitionPackagePaths,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static CharacterSlotMetadata? Detect(
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

            var candidates =
                new List<CharacterSlotCandidate>();

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

                string? characterName =
                    TryReadCharacterName(fullInfoFile);

                if (string.IsNullOrWhiteSpace(characterName) ||
                    !HasMatchingCharacterAssets(
                        mod,
                        characterName))
                {
                    continue;
                }

                candidates.Add(
                    new CharacterSlotCandidate(
                        characterName,
                        Path.GetRelativePath(
                            safeRoot,
                            fullInfoFile),
                        FindCharacterMeshPackagePath(
                            mod,
                            characterName)!,
                        CharacterAssetRoot +
                        characterName +
                        "/PPCD_" +
                        characterName));
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            CharacterSlotCandidate primary =
                candidates
                    .OrderByDescending(candidate =>
                        mod.DisplayName.Contains(
                            candidate.CharacterName,
                            StringComparison.OrdinalIgnoreCase))
                    .ThenBy(candidate =>
                        candidate.CharacterName.Length)
                    .ThenBy(candidate =>
                        candidate.CharacterName,
                        StringComparer.OrdinalIgnoreCase)
                    .First();

            return new CharacterSlotMetadata(
                primary.CharacterName,
                primary.InfoFileRelativePath,
                primary.MeshPackagePath,
                primary.DefinitionPackagePath,
                candidates
                    .Select(candidate =>
                        candidate.InfoFileRelativePath)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path =>
                        path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                candidates
                    .Select(candidate =>
                        candidate.DefinitionPackagePath)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path =>
                        path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        private static string? TryReadCharacterName(
            string infoFile)
        {
            try
            {
                string json =
                    File.ReadAllText(infoFile)
                        .TrimStart();

                if (json.StartsWith(
                        "$",
                        StringComparison.Ordinal))
                {
                    // I accept the marker used by original Character Loader
                    // packs because its Lua reader treated it as metadata.
                    json =
                        json[1..]
                            .TrimStart();
                }

                using JsonDocument document =
                    JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty(
                        "CharacterName",
                        out JsonElement characterNameElement) ||
                    characterNameElement.ValueKind !=
                        JsonValueKind.String)
                {
                    return null;
                }

                string characterName =
                    characterNameElement.GetString()?.Trim() ??
                    string.Empty;

                return Regex.IsMatch(
                           characterName,
                           "^[A-Za-z0-9_]{1,64}$")
                    ? characterName
                    : null;
            }
            catch (Exception exception)
                when (exception is IOException or JsonException)
            {
                return null;
            }
        }

        private static bool HasMatchingCharacterAssets(
            InstalledMod mod,
            string characterName)
        {
            string characterRoot =
                CharacterAssetRoot +
                characterName +
                "/";

            bool hasCharacterFolder =
                mod.AssetPackages.Any(package =>
                    package.PackagePath.StartsWith(
                        characterRoot,
                        StringComparison.OrdinalIgnoreCase));

            bool hasPlayerCharacterData =
                mod.AssetPackages.Any(package =>
                    package.PackagePath.Equals(
                        characterRoot +
                        "PPCD_" +
                        characterName,
                        StringComparison.OrdinalIgnoreCase));

            return hasCharacterFolder &&
                   hasPlayerCharacterData &&
                   FindCharacterMeshPackagePath(
                       mod,
                       characterName) is not null;
        }

        private static string? FindCharacterMeshPackagePath(
            InstalledMod mod,
            string characterName)
        {
            string characterRoot =
                CharacterAssetRoot +
                characterName +
                "/";

            string conventionalMeshPackagePath =
                characterRoot +
                characterName;

            ModAssetPackage? conventionalMesh =
                mod.AssetPackages.FirstOrDefault(package =>
                    package.PackagePath.Equals(
                        conventionalMeshPackagePath,
                        StringComparison.OrdinalIgnoreCase));

            if (conventionalMesh is not null)
            {
                return conventionalMesh.PackagePath;
            }

            List<ModAssetPackage> meshes =
                mod.AssetPackages
                    .Where(package =>
                        package.Kind ==
                            ModAssetKind.SkeletalMesh &&
                        package.PackagePath.StartsWith(
                            characterRoot,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            ModAssetPackage? namedMesh =
                meshes.FirstOrDefault(package =>
                    package.AssetName.Equals(
                        "SK_" + characterName,
                        StringComparison.OrdinalIgnoreCase));

            return namedMesh?.PackagePath ??
                   (meshes.Count == 1
                       ? meshes[0].PackagePath
                       : null);
        }

        private sealed record CharacterSlotCandidate(
            string CharacterName,
            string InfoFileRelativePath,
            string MeshPackagePath,
            string DefinitionPackagePath);

        private sealed record CharacterSlotMetadata(
            string CharacterName,
            string InfoFileRelativePath,
            string MeshPackagePath,
            string DefinitionPackagePath,
            IReadOnlyList<string> InfoFileRelativePaths,
            IReadOnlyList<string> DefinitionPackagePaths);
    }
}
