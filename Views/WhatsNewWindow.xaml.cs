using Limelight.Models;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Limelight.Views
{
    public partial class WhatsNewWindow : Window
    {
        private const string DocumentationUrl =
            "https://limelight-dead-as-disco-modding.github.io/LimelightWiki/";

        public WhatsNewWindow(
            ReleaseNotesContent content)
        {
            InitializeComponent();

            DataContext = content;

            CloseButton.Click += (_, _) => Close();
            ContinueButton.Click += (_, _) => Close();
            DocumentationButton.Click += DocumentationButton_Click;

            TitleBar.MouseLeftButtonDown += (_, eventArgs) =>
            {
                if (eventArgs.ChangedButton == MouseButton.Left)
                {
                    DragMove();
                }
            };

            KeyDown += (_, eventArgs) =>
            {
                if (eventArgs.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        private void DocumentationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = DocumentationUrl,
                        UseShellExecute = true
                    });
            }
            catch (Exception exception)
            {
                LimelightDialog.Open(
                    this,
                    "DOCUMENTATION UNAVAILABLE",
                    "Limelight could not open the documentation in your browser.",
                    LimelightDialogTone.Warning,
                    details: exception.Message,
                    eyebrow: "HELP LINK");
            }
        }
    }
}
