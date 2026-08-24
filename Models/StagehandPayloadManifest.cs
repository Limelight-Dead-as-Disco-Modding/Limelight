namespace Limelight.Models
{
    public sealed class StagehandPayloadManifest
    {
        public int SchemaVersion { get; init; }

        public string StagehandVersion { get; init; } =
            string.Empty;

        public string ApiVersion { get; init; } =
            string.Empty;

        public string TargetModName { get; init; } =
            string.Empty;

        public StagehandPayloadPackage Package { get; init; } =
            new();

        public List<StagehandPayloadFile> Files { get; init; } =
            new();
    }

    public sealed class StagehandPayloadPackage
    {
        public string FileName { get; init; } =
            string.Empty;

        public string ResourceName { get; init; } =
            string.Empty;

        public long Size { get; init; }

        public string Sha256 { get; init; } =
            string.Empty;
    }

    public sealed class StagehandPayloadFile
    {
        public string TargetRelativePath { get; init; } =
            string.Empty;

        public long Size { get; init; }

        public string Sha256 { get; init; } =
            string.Empty;
    }
}
