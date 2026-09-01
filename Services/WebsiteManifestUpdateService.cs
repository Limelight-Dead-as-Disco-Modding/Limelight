using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Limelight.Services
{
    public enum LimelightUpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        Unavailable
    }

    public sealed record LimelightUpdateNotice(
        string Version,
        string Name,
        string Url,
        string? Body,
        string? InstallerUrl);

    public sealed record LimelightUpdateCheckResult(
        LimelightUpdateCheckStatus Status,
        LimelightUpdateNotice? Update = null,
        string? Message = null);

    public sealed class WebsiteManifestUpdateService
    {
        private const string ManifestEndpoint =
            "https://limelight-dead-as-disco-modding.github.io/LimelightWiki/updates/limelight-early-access.json";

        private const int SupportedSchemaVersion =
            1;

        private static readonly HttpClient Client =
            CreateClient();

        public async Task<LimelightUpdateCheckResult> CheckForUpdateAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            if (!TryParseVersion(
                    currentVersion,
                    out ParsedVersion? installedVersion) ||
                installedVersion == null)
            {
                return Unavailable(
                    "Limelight could not read the version of this installation.");
            }

            try
            {
                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                timeout.CancelAfter(
                    TimeSpan.FromSeconds(6));

                string requestUrl =
                    $"{ManifestEndpoint}?check={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                using HttpRequestMessage request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        requestUrl);

                request.Headers.CacheControl =
                    new CacheControlHeaderValue
                    {
                        NoCache = true,
                        NoStore = true
                    };

                using HttpResponseMessage response =
                    await Client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return Unavailable(
                        "The Limelight website did not return update information.");
                }

                await using Stream content =
                    await response.Content.ReadAsStreamAsync(
                        timeout.Token);

                using JsonDocument document =
                    await JsonDocument.ParseAsync(
                        content,
                        cancellationToken: timeout.Token);

                JsonElement manifest =
                    document.RootElement;

                if (manifest.ValueKind != JsonValueKind.Object ||
                    !TryReadInteger(
                        manifest,
                        "schemaVersion",
                        out int schemaVersion) ||
                    schemaVersion != SupportedSchemaVersion ||
                    !TryReadString(
                        manifest,
                        "product",
                        out string product) ||
                    !string.Equals(
                        product,
                        "Limelight",
                        StringComparison.Ordinal) ||
                    !TryReadString(
                        manifest,
                        "channel",
                        out string channel) ||
                    !string.Equals(
                        channel,
                        "early-access",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Unavailable(
                        "The Limelight website returned unsupported update information.");
                }

                if (!TryReadString(
                        manifest,
                        "latestVersion",
                        out string latestVersionText) ||
                    !TryParseVersion(
                        latestVersionText,
                        out ParsedVersion? latestVersion) ||
                    latestVersion == null ||
                    !TryReadTrustedGitHubUrl(
                        manifest,
                        "releaseUrl",
                        out string releaseUrl))
                {
                    return Unavailable(
                        "The Limelight website returned incomplete update information.");
                }

                if (CompareVersions(
                        latestVersion,
                        installedVersion) <= 0)
                {
                    return new LimelightUpdateCheckResult(
                        LimelightUpdateCheckStatus.UpToDate);
                }

                string releaseName =
                    TryReadString(
                        manifest,
                        "releaseName",
                        out string name)
                        ? name
                        : latestVersionText;

                string? releaseNotes =
                    TryReadOptionalString(
                        manifest,
                        "notes");

                string? downloadUrl =
                    TryReadOptionalTrustedGitHubUrl(
                        manifest,
                        "downloadUrl");

                return new LimelightUpdateCheckResult(
                    LimelightUpdateCheckStatus.UpdateAvailable,
                    new LimelightUpdateNotice(
                        latestVersionText,
                        releaseName,
                        releaseUrl,
                        releaseNotes,
                        downloadUrl));
            }
            catch (OperationCanceledException)
            {
                return Unavailable(
                    "The update check timed out. Please try again shortly.");
            }
            catch (HttpRequestException)
            {
                return Unavailable(
                    "Limelight could not reach the update website.");
            }
            catch (JsonException)
            {
                return Unavailable(
                    "The update website returned unreadable information.");
            }
            catch (IOException)
            {
                return Unavailable(
                    "Limelight could not finish reading the update information.");
            }
        }

        private static LimelightUpdateCheckResult Unavailable(
            string message)
        {
            return new LimelightUpdateCheckResult(
                LimelightUpdateCheckStatus.Unavailable,
                Message: message);
        }

        private static HttpClient CreateClient()
        {
            HttpClient client =
                new HttpClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Limelight-Update-Checker");

            client.DefaultRequestHeaders.Accept.ParseAdd(
                "application/json");

            return client;
        }

        private static bool TryReadInteger(
            JsonElement element,
            string propertyName,
            out int value)
        {
            value =
                0;

            return element.TryGetProperty(
                       propertyName,
                       out JsonElement property) &&
                   property.ValueKind == JsonValueKind.Number &&
                   property.TryGetInt32(
                       out value);
        }

        private static bool TryReadString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            value =
                string.Empty;

            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value =
                property.GetString() ??
                string.Empty;

            return !string.IsNullOrWhiteSpace(value);
        }

        private static string? TryReadOptionalString(
            JsonElement element,
            string propertyName)
        {
            return TryReadString(
                element,
                propertyName,
                out string value)
                    ? value
                    : null;
        }

        private static bool TryReadTrustedGitHubUrl(
            JsonElement element,
            string propertyName,
            out string value)
        {
            value =
                string.Empty;

            if (!TryReadString(
                    element,
                    propertyName,
                    out string candidate) ||
                !Uri.TryCreate(
                    candidate,
                    UriKind.Absolute,
                    out Uri? uri) ||
                !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    uri.Host,
                    "github.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value =
                uri.AbsoluteUri;

            return true;
        }

        private static string? TryReadOptionalTrustedGitHubUrl(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement property) ||
                property.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return TryReadTrustedGitHubUrl(
                element,
                propertyName,
                out string value)
                    ? value
                    : null;
        }

        private static bool TryParseVersion(
            string value,
            out ParsedVersion? version)
        {
            version =
                null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int versionStart =
                -1;

            for (int index = 0;
                index < value.Length;
                index++)
            {
                if (char.IsDigit(value[index]))
                {
                    versionStart =
                        index;
                    break;
                }
            }

            if (versionStart < 0)
            {
                return false;
            }

            string cleanVersion =
                value[versionStart..];

            int metadataStart =
                cleanVersion.IndexOf('+');

            if (metadataStart >= 0)
            {
                cleanVersion =
                    cleanVersion[..metadataStart];
            }

            string coreText =
                cleanVersion;

            string prereleaseText =
                string.Empty;

            int prereleaseStart =
                cleanVersion.IndexOf('-');

            if (prereleaseStart >= 0)
            {
                coreText =
                    cleanVersion[..prereleaseStart];

                prereleaseText =
                    cleanVersion[(prereleaseStart + 1)..];
            }

            string[] coreParts =
                coreText.Split(
                    '.',
                    StringSplitOptions.RemoveEmptyEntries);

            if (coreParts.Length == 0 ||
                coreParts.Length > 4)
            {
                return false;
            }

            int[] core =
                new int[4];

            for (int index = 0;
                index < coreParts.Length;
                index++)
            {
                if (!int.TryParse(
                        coreParts[index],
                        out core[index]))
                {
                    return false;
                }
            }

            string[] prerelease =
                string.IsNullOrWhiteSpace(prereleaseText)
                    ? Array.Empty<string>()
                    : prereleaseText.Split(
                        new[] { '.', '-' },
                        StringSplitOptions.RemoveEmptyEntries);

            version =
                new ParsedVersion(
                    core,
                    prerelease);

            return true;
        }

        private static int CompareVersions(
            ParsedVersion left,
            ParsedVersion right)
        {
            for (int index = 0;
                index < left.Core.Length;
                index++)
            {
                int coreComparison =
                    left.Core[index].CompareTo(
                        right.Core[index]);

                if (coreComparison != 0)
                {
                    return coreComparison;
                }
            }

            bool leftIsStable =
                left.Prerelease.Length == 0;

            bool rightIsStable =
                right.Prerelease.Length == 0;

            if (leftIsStable || rightIsStable)
            {
                return leftIsStable.CompareTo(
                    rightIsStable);
            }

            int sharedLength =
                Math.Min(
                    left.Prerelease.Length,
                    right.Prerelease.Length);

            for (int index = 0;
                index < sharedLength;
                index++)
            {
                string leftPart =
                    left.Prerelease[index];

                string rightPart =
                    right.Prerelease[index];

                bool leftIsNumber =
                    int.TryParse(
                        leftPart,
                        out int leftNumber);

                bool rightIsNumber =
                    int.TryParse(
                        rightPart,
                        out int rightNumber);

                int partComparison;

                if (leftIsNumber &&
                    rightIsNumber)
                {
                    partComparison =
                        leftNumber.CompareTo(
                            rightNumber);
                }
                else if (leftIsNumber !=
                    rightIsNumber)
                {
                    partComparison =
                        leftIsNumber
                            ? -1
                            : 1;
                }
                else
                {
                    partComparison =
                        string.Compare(
                            leftPart,
                            rightPart,
                            StringComparison.OrdinalIgnoreCase);
                }

                if (partComparison != 0)
                {
                    return partComparison;
                }
            }

            return left.Prerelease.Length.CompareTo(
                right.Prerelease.Length);
        }

        private sealed record ParsedVersion(
            int[] Core,
            string[] Prerelease);
    }
}
