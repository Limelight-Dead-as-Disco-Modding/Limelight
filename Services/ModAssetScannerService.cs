using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Limelight.Models;
using System.IO;

namespace Limelight.Services
{
    public sealed class ModAssetScannerService
    {
        public const int CurrentManifestVersion = 5;

        public List<ModAssetPackage> Scan(
            string modDirectory)
        {
            if (!Directory.Exists(modDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The extracted mod folder could not be found.");
            }

            using var provider =
                new DefaultFileProvider(
                    modDirectory,
                    SearchOption.AllDirectories,
                    new VersionContainer(
                        EGame.GAME_UE5_LATEST),
                    StringComparer.OrdinalIgnoreCase);

            // Initialize reads each archive's directory index. IoStore mods do
            // not need to enter the game; this mount is only CUE4Parse filling
            // its in-memory file tables from the mod's own container index.
            provider.Initialize();
            provider.Mount();

            IEnumerable<string> archiveFiles =
                provider.UnloadedVfs
                    .Concat(provider.MountedVfs)
                    .SelectMany(archive =>
                        archive.Files.Keys);

            return archiveFiles
                .Where(path =>
                    path.EndsWith(
                        ".uasset",
                        StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(
                        ".umap",
                        StringComparison.OrdinalIgnoreCase))
                .Select(TryCreatePackage)
                .Where(package => package != null)
                .Cast<ModAssetPackage>()
                .GroupBy(
                    package => package.PackagePath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(package => package.ReloadPriority)
                .ThenBy(package => package.PackagePath)
                .ToList();
        }

        private static ModAssetPackage? TryCreatePackage(
            string archivePath)
        {
            string normalizedPath =
                archivePath.Replace('\\', '/');

            const string contentMarker =
                "/Content/";

            int contentIndex =
                normalizedPath.IndexOf(
                    contentMarker,
                    StringComparison.OrdinalIgnoreCase);

            if (contentIndex < 0)
            {
                return null;
            }

            string contentPath =
                normalizedPath[
                    (contentIndex + contentMarker.Length)..];

            string extension =
                Path.GetExtension(contentPath);

            if (!extension.Equals(
                    ".uasset",
                    StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(
                    ".umap",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string packagePath =
                "/Game/" +
                contentPath[..^extension.Length];

            return new ModAssetPackage
            {
                PackagePath = packagePath,
                Kind = extension.Equals(
                    ".umap",
                    StringComparison.OrdinalIgnoreCase)
                    ? ModAssetKind.Map
                    : Classify(packagePath)
            };
        }

        private static ModAssetKind Classify(
            string packagePath)
        {
            string assetName =
                packagePath[(packagePath.LastIndexOf('/') + 1)..];

            if (packagePath.Contains(
                    "/Localization/",
                    StringComparison.OrdinalIgnoreCase) &&
                assetName.StartsWith(
                    "ST_",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.StringTable;
            }

            if (packagePath.Contains(
                    "/UI/",
                    StringComparison.OrdinalIgnoreCase) &&
                packagePath.Contains(
                    "/Portraits/",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Dead as Disco keeps its dialogue portraits outside the
                // normal Textures directory, despite the asset being a texture.
                return ModAssetKind.UserInterfaceTexture;
            }

            if (packagePath.Contains(
                    "/Textures/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.Texture;
            }

            if (packagePath.Contains(
                    "/Materials/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.Material;
            }

            // I untangle Character Slot Loader's singular Texture cupboard,
            // where textures and materials are apparently happy roommates.
            if (packagePath.Contains(
                    "/ModdedCharacters/",
                    StringComparison.OrdinalIgnoreCase) &&
                packagePath.Contains(
                    "/Texture/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return assetName.EndsWith(
                        "Mat",
                        StringComparison.OrdinalIgnoreCase) ||
                       assetName.StartsWith(
                        "M_",
                        StringComparison.OrdinalIgnoreCase) ||
                       assetName.StartsWith(
                        "MI_",
                        StringComparison.OrdinalIgnoreCase)
                    ? ModAssetKind.Material
                    : ModAssetKind.Texture;
            }

            if (assetName.EndsWith(
                    "_Skeleton",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.Skeleton;
            }

            if (assetName.EndsWith(
                    "_PhysicsAsset",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.PhysicsAsset;
            }

            if (assetName.StartsWith(
                    "ABP_",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.AnimationBlueprint;
            }

            if (assetName.StartsWith(
                    "SK_",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ModAssetKind.SkeletalMesh;
            }

            return ModAssetKind.Other;
        }
    }
}
