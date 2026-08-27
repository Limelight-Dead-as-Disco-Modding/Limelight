using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Limelight.Views
{
    public partial class BrowseNexusPage : UserControl
    {
        private const string NexusHomeUrl =
            "https://www.nexusmods.com/deadasdisco/mods/";

        private bool _isEmbeddedBrowserInitialized;

        public event Action<string>? ArchiveDownloaded;

        public BrowseNexusPage()
        {
            InitializeComponent();
            InitialiseNexusBrowserAsync();
        }

        public void SetTutorialOverlayActive(
            bool isActive)
        {
            // I hide WebView2's native child window so the WPF tour can sit
            // above this page without losing the signed-in browser session.
            NexusBrowser.Visibility =
                isActive
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        private async void InitialiseNexusBrowserAsync()
        {
            try
            {
                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: GetNexusBrowserDataFolder(),
                        options: null);

                await NexusBrowser.EnsureCoreWebView2Async(
                    environment);

                if (NexusBrowser.CoreWebView2 is null)
                {
                    throw new InvalidOperationException(
                        "WebView2 did not create a browser session.");
                }

                _isEmbeddedBrowserInitialized = true;

                NexusBrowser.CoreWebView2.Settings.IsStatusBarEnabled =
                    false;

                NexusBrowser.CoreWebView2.Settings.AreDevToolsEnabled =
                    false;

                NexusBrowser.CoreWebView2.NewWindowRequested +=
                    NexusBrowser_NewWindowRequested;

                NexusBrowser.CoreWebView2.DownloadStarting +=
                    NexusBrowser_DownloadStarting;

                NavigateTo(
                    NexusHomeUrl);
            }
            catch (Exception exception)
            {
                BrowserUnavailableText.Text =
                    "Install or repair Microsoft Edge WebView2, then reopen Limelight. " +
                    exception.Message;

                BrowserUnavailablePanel.Visibility =
                    Visibility.Visible;
            }
        }

        private static string GetNexusBrowserDataFolder()
        {
            string dataFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "NexusBrowser");

            Directory.CreateDirectory(
                dataFolder);

            return dataFolder;
        }

        private static string GetNexusDownloadFolder()
        {
            string downloadFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "NexusDownloads");

            Directory.CreateDirectory(
                downloadFolder);

            return downloadFolder;
        }

        private void NexusBrowser_NewWindowRequested(
            object? sender,
            CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            if (IsNxmLink(e.Uri))
            {
                ShowNxmUnavailable();
                return;
            }

            NavigateTo(
                e.Uri);
        }

        private void NexusBrowser_NavigationStarting(
            object sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsNxmLink(e.Uri))
            {
                e.Cancel = true;
                ShowNxmUnavailable();
                return;
            }

            NexusAddressBox.Text =
                e.Uri;
        }

        private void NexusBrowser_NavigationCompleted(
            object sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            if (NexusBrowser.Source is not null)
            {
                NexusAddressBox.Text =
                    NexusBrowser.Source.ToString();
            }

            if (!e.IsSuccess)
            {
                ShowStatus(
                    "NEXUS PAGE DID NOT LOAD",
                    "Check your connection, then refresh the page.",
                    isBusy: false);
            }
        }

        private void NexusBrowser_DownloadStarting(
            object? sender,
            CoreWebView2DownloadStartingEventArgs e)
        {
            string fileName =
                CreateSafeFileName(
                    Path.GetFileName(
                        e.ResultFilePath));

            string downloadPath =
                CreateUniqueDownloadPath(
                    Path.Combine(
                        GetNexusDownloadFolder(),
                        fileName));

            e.ResultFilePath =
                downloadPath;

            // I suppress WebView2's save prompt because Limelight owns this
            // download folder and can pass completed archives to its importer.
            e.Handled = true;

            CoreWebView2DownloadOperation operation =
                e.DownloadOperation;

            operation.BytesReceivedChanged +=
                (_, _) => UpdateDownloadProgress(
                    operation,
                    fileName);

            operation.StateChanged +=
                (_, _) => UpdateDownloadState(
                    operation,
                    downloadPath,
                    fileName);

            ShowStatus(
                "DOWNLOADING FROM NEXUS",
                fileName,
                isBusy: true);
        }

        private void UpdateDownloadProgress(
            CoreWebView2DownloadOperation operation,
            string fileName)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    long totalBytes =
                        operation.TotalBytesToReceive;

                    DownloadProgressBar.IsIndeterminate =
                        totalBytes <= 0;

                    if (totalBytes > 0)
                    {
                        DownloadProgressBar.Value =
                            Math.Clamp(
                                operation.BytesReceived /
                                (double)totalBytes *
                                100,
                                0,
                                100);
                    }

                    DownloadStatusText.Text =
                        fileName;
                }));
        }

        private void UpdateDownloadState(
            CoreWebView2DownloadOperation operation,
            string downloadPath,
            string fileName)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (operation.State ==
                        CoreWebView2DownloadState.Completed)
                    {
                        DownloadProgressBar.IsIndeterminate =
                            false;

                        DownloadProgressBar.Value =
                            100;

                        if (IsSupportedArchive(
                                downloadPath))
                        {
                            ShowStatus(
                                "SENDING TO LIMELIGHT",
                                fileName,
                                isBusy: false);

                            ArchiveDownloaded?.Invoke(
                                downloadPath);
                        }
                        else
                        {
                            ShowStatus(
                                "DOWNLOAD SAVED",
                                "Only ZIP, RAR, and 7Z archives are imported automatically.",
                                isBusy: false);
                        }

                        return;
                    }

                    if (operation.State ==
                        CoreWebView2DownloadState.Interrupted)
                    {
                        ShowStatus(
                            "DOWNLOAD INTERRUPTED",
                            fileName,
                            isBusy: false);
                    }
                }));
        }

        private static bool IsSupportedArchive(
            string path)
        {
            string extension =
                Path.GetExtension(path);

            return extension.Equals(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(
                    ".rar",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(
                    ".7z",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNxmLink(
            string? uri)
        {
            return !string.IsNullOrWhiteSpace(uri) &&
                uri.StartsWith(
                    "nxm://",
                    StringComparison.OrdinalIgnoreCase);
        }

        private void ShowNxmUnavailable()
        {
            ShowStatus(
                "USE MANUAL DOWNLOAD",
                "Nexus Mod Manager links require the API that was removed. Choose Manual Download on the file page instead.",
                isBusy: false);
        }

        private void ShowStatus(
            string title,
            string message,
            bool isBusy)
        {
            DownloadStatusPanel.Visibility =
                Visibility.Visible;

            DownloadStatusTitle.Text =
                title;

            DownloadStatusText.Text =
                message;

            DownloadProgressBar.IsIndeterminate =
                isBusy;

            if (!isBusy)
            {
                DownloadProgressBar.Value =
                    title.Equals(
                        "SENDING TO LIMELIGHT",
                        StringComparison.Ordinal)
                        ? 100
                        : 0;
            }
        }

        private void NavigateTo(
            string? address)
        {
            if (!_isEmbeddedBrowserInitialized ||
                NexusBrowser.CoreWebView2 is null ||
                string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            string candidate =
                address.Trim();

            if (!candidate.Contains(
                    "://",
                    StringComparison.Ordinal))
            {
                candidate =
                    "https://" +
                    candidate;
            }

            if (!Uri.TryCreate(
                    candidate,
                    UriKind.Absolute,
                    out Uri? uri) ||
                !IsNexusHost(uri))
            {
                ShowStatus(
                    "NEXUS ADDRESS REQUIRED",
                    "This browser stays on nexusmods.com so downloads remain predictable.",
                    isBusy: false);

                return;
            }

            NexusBrowser.CoreWebView2.Navigate(
                uri.ToString());
        }

        private static bool IsNexusHost(
            Uri uri)
        {
            return uri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase) &&
                (uri.Host.Equals(
                        "nexusmods.com",
                        StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(
                        ".nexusmods.com",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string CreateSafeFileName(
            string fileName)
        {
            string fallback =
                string.IsNullOrWhiteSpace(fileName)
                    ? "nexus-download"
                    : fileName;

            foreach (char invalidCharacter in
                Path.GetInvalidFileNameChars())
            {
                fallback =
                    fallback.Replace(
                        invalidCharacter,
                        '_');
            }

            return fallback;
        }

        private static string CreateUniqueDownloadPath(
            string preferredPath)
        {
            if (!File.Exists(preferredPath))
            {
                return preferredPath;
            }

            string directory =
                Path.GetDirectoryName(preferredPath) ??
                GetNexusDownloadFolder();

            string fileName =
                Path.GetFileNameWithoutExtension(
                    preferredPath);

            string extension =
                Path.GetExtension(
                    preferredPath);

            for (int suffix = 2;
                suffix < int.MaxValue;
                suffix++)
            {
                string candidate =
                    Path.Combine(
                        directory,
                        $"{fileName} ({suffix}){extension}");

                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(
                directory,
                $"{fileName}-{Guid.NewGuid():N}{extension}");
        }

        private void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (NexusBrowser.CanGoBack)
            {
                NexusBrowser.GoBack();
            }
        }

        private void ForwardButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (NexusBrowser.CanGoForward)
            {
                NexusBrowser.GoForward();
            }
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NexusBrowser.Reload();
        }

        private void HomeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateTo(
                NexusHomeUrl);
        }

        private void NexusAddressBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            NavigateTo(
                NexusAddressBox.Text);

            e.Handled = true;
        }
    }
}
