using System.Text.Json.Serialization;

namespace Limelight.Models
{
    public enum MultiplayerRole
    {
        None,
        Host,
        Client
    }

    public enum MultiplayerLogLevel
    {
        Log,
        Network,
        Gameplay,
        Warning,
        Error
    }

    public sealed class MultiplayerFriendConnection
    {
        public string Address { get; init; } =
            string.Empty;

        public int InputPort { get; init; }

        [JsonIgnore]
        public string Token { get; init; } =
            string.Empty;

        public int GamePort =>
            InputPort - 1;
    }

    public sealed class MultiplayerInstalledRole
    {
        public string Product { get; init; } =
            "LimelightMP";

        public MultiplayerRole Role { get; init; }

        public string Address { get; init; } =
            string.Empty;

        public int Port { get; init; }

        public DateTimeOffset InstalledUtc { get; init; }

        public string Version { get; init; } =
            string.Empty;
    }

    public sealed class MultiplayerStartResult
    {
        public MultiplayerRole Role { get; init; }

        public string FriendCode { get; init; } =
            string.Empty;

        public string Address { get; init; } =
            string.Empty;

        public int GamePort { get; init; }

        public int InputPort { get; init; }

        public bool TailscaleDetected { get; init; }
    }

    public sealed class MultiplayerPayloadManifest
    {
        public int SchemaVersion { get; init; }

        public string Version { get; init; } =
            string.Empty;

        public int ProtocolVersion { get; init; }

        public string SourceCommit { get; init; } =
            string.Empty;

        public bool SourceDirty { get; init; }

        public string Ue4ssVersion { get; init; } =
            string.Empty;

        public string Ue4ssCommit { get; init; } =
            string.Empty;

        public MultiplayerPayloadFile HostScript { get; init; } =
            new();

        public MultiplayerPayloadFile ClientScript { get; init; } =
            new();

        public MultiplayerPayloadFile NativeBridge { get; init; } =
            new();

        public MultiplayerPayloadFile Relay { get; init; } =
            new();

        public MultiplayerPayloadFile UiPak { get; init; } =
            new();

        public MultiplayerPayloadFile UiUtoc { get; init; } =
            new();

        public MultiplayerPayloadFile UiUcas { get; init; } =
            new();
    }

    public sealed class MultiplayerPayloadFile
    {
        public string ResourceName { get; init; } =
            string.Empty;

        public long Size { get; init; }

        public string Sha256 { get; init; } =
            string.Empty;
    }
}
