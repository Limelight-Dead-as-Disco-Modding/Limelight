using Limelight.Models;
using System.IO;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class DownloadHistoryService
    {
        private const int MaximumRecentDownloads = 50;

        private readonly string _historyFolder;
        private readonly string _historyFile;
        private readonly List<NexusDownloadRecord> _records =
            new();

        public DownloadHistoryService()
        {
            _historyFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight");

            _historyFile = Path.Combine(
                _historyFolder,
                "download-history.json");

            Load();
        }

        public IReadOnlyList<NexusDownloadRecord> Records =>
            _records
                .OrderByDescending(record => record.IsActive)
                .ThenByDescending(record => record.StartedAt)
                .ToList();

        public void MarkInstalling(
            string recordId)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Installing;

            record.StatusMessage =
                "Validating and installing the mod.";

            Save();
        }

        public void MarkCompleted(
            string recordId,
            InstalledMod installedMod)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Completed;

            record.CompletedAt =
                DateTimeOffset.UtcNow;

            record.InstalledModId =
                installedMod.Id;

            record.StatusMessage =
                $"{installedMod.DisplayName} is ready in My Mods.";

            Save();
        }

        public void MarkFailed(
            string recordId,
            string message)
        {
            NexusDownloadRecord? record =
                Find(recordId);

            if (record is null)
            {
                return;
            }

            record.Status =
                NexusDownloadStatus.Failed;

            record.CompletedAt =
                DateTimeOffset.UtcNow;

            record.StatusMessage =
                string.IsNullOrWhiteSpace(message)
                    ? "The download could not be installed."
                    : message.Trim();

            Save();
        }

        public void ClearFinished()
        {
            _records.RemoveAll(record =>
                !record.IsActive);

            Save();
        }

        private NexusDownloadRecord? Find(
            string recordId)
        {
            return _records.FirstOrDefault(record =>
                string.Equals(
                    record.Id,
                    recordId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void Load()
        {
            if (!File.Exists(_historyFile))
            {
                return;
            }

            try
            {
                string json =
                    File.ReadAllText(_historyFile);

                List<NexusDownloadRecord>? savedRecords =
                    JsonSerializer.Deserialize<List<NexusDownloadRecord>>(
                        json);

                if (savedRecords is not null)
                {
                    _records.AddRange(savedRecords);
                }

                bool recoveredInterruptedDownload =
                    false;

                foreach (NexusDownloadRecord record in
                    _records.Where(record => record.IsActive))
                {
                    // I cannot resume an interrupted browser download, so I keep
                    // its history entry and make the stopped state clear.
                    record.Status =
                        NexusDownloadStatus.Interrupted;

                    record.CompletedAt =
                        DateTimeOffset.UtcNow;

                    record.StatusMessage =
                        "Limelight closed before this download finished. Nexus downloads are paused during Early Access.";

                    recoveredInterruptedDownload =
                        true;
                }

                TrimFinishedRecords();

                if (recoveredInterruptedDownload)
                {
                    Save();
                }
            }
            catch (IOException)
            {
                _records.Clear();
            }
            catch (UnauthorizedAccessException)
            {
                _records.Clear();
            }
            catch (JsonException)
            {
                _records.Clear();
            }
        }

        private void TrimFinishedRecords()
        {
            List<NexusDownloadRecord> active =
                _records
                    .Where(record => record.IsActive)
                    .ToList();

            List<NexusDownloadRecord> finished =
                _records
                    .Where(record => !record.IsActive)
                    .OrderByDescending(record => record.StartedAt)
                    .Take(MaximumRecentDownloads)
                    .ToList();

            _records.Clear();
            _records.AddRange(active);
            _records.AddRange(finished);
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(
                    _historyFolder);

                string json =
                    JsonSerializer.Serialize(
                        _records,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                string temporaryFile =
                    _historyFile + ".tmp";

                File.WriteAllText(
                    temporaryFile,
                    json);

                File.Move(
                    temporaryFile,
                    _historyFile,
                    true);
            }
            catch (IOException)
            {
                // I let the transfer continue when Windows briefly prevents me
                // from updating the optional history file.
            }
            catch (UnauthorizedAccessException)
            {
                // I treat history as optional so it never blocks a valid download.
            }
        }
    }
}
