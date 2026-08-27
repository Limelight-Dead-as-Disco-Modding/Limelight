using System.Text.Json.Serialization;

namespace Limelight.Models
{
    public enum ModAssetKind
    {
        Other,
        Texture,
        Material,
        Skeleton,
        PhysicsAsset,
        AnimationBlueprint,
        SkeletalMesh,
        StringTable,
        UserInterfaceTexture,
        Map
    }

    public sealed class ModAssetPackage
    {
        public string PackagePath { get; set; } =
            string.Empty;

        public ModAssetKind Kind { get; set; } =
            ModAssetKind.Other;

        [JsonIgnore]
        public string AssetName =>
            PackagePath[(PackagePath.LastIndexOf('/') + 1)..];

        [JsonIgnore]
        public string ObjectPath =>
            $"{PackagePath}.{AssetName}";

        [JsonIgnore]
        public bool IsCharlieMesh =>
            PackagePath.Equals(
                "/Game/Pagoda/Characters/Player/Meshes/SK_Charlie",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsCharlieAsset =>
            IsCharlieMesh ||
            (!IsCharacterSlotAsset &&
             (PackagePath.StartsWith(
                  "/Game/Pagoda/Characters/Player/",
                  StringComparison.OrdinalIgnoreCase) ||
              PackagePath.StartsWith(
                  "/Game/Pagoda/Characters/Materials/",
                  StringComparison.OrdinalIgnoreCase)));

        [JsonIgnore]
        public bool IsCharlieAppearanceAsset =>
            IsCharlieAsset ||
            PackagePath.StartsWith(
                "/Game/Pagoda/Characters/Player/ModdedCharacters/",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsSafeForLiveReload =>
            Kind switch
            {
                ModAssetKind.Texture => true,
                ModAssetKind.Material => true,
                ModAssetKind.StringTable => true,
                ModAssetKind.UserInterfaceTexture => true,
                ModAssetKind.Skeleton => IsCharacterAsset,
                ModAssetKind.PhysicsAsset => IsCharacterAsset,
                ModAssetKind.AnimationBlueprint => IsCharacterAsset,
                ModAssetKind.SkeletalMesh => IsCharacterAsset,
                ModAssetKind.Other => IsCharacterSlotAsset,
                _ => false
            };

        [JsonIgnore]
        public int ReloadPriority =>
            Kind switch
            {
                ModAssetKind.Texture => 10,
                ModAssetKind.UserInterfaceTexture => 10,
                ModAssetKind.Material => 20,
                ModAssetKind.StringTable => 25,
                ModAssetKind.Skeleton => 30,
                ModAssetKind.PhysicsAsset => 30,
                ModAssetKind.AnimationBlueprint => 30,
                ModAssetKind.SkeletalMesh => 40,
                ModAssetKind.Other when IsCharacterSlotAsset => 25,
                _ => 100
            };

        [JsonIgnore]
        public bool IsCharacterSlotAsset =>
            PackagePath.StartsWith(
                "/Game/Pagoda/Characters/Player/ModdedCharacters/",
                StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        private bool IsCharacterAsset =>
            PackagePath.StartsWith(
                "/Game/Pagoda/Characters/Player/",
                StringComparison.OrdinalIgnoreCase) ||
            PackagePath.StartsWith(
                "/Game/Pagoda/Characters/Materials/",
                StringComparison.OrdinalIgnoreCase);
    }
}
