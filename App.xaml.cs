using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Limelight.Views;

namespace Limelight
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly TimeSpan MinimumSplashTime =
            TimeSpan.FromSeconds(10);

        protected override async void OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);

            // The splash owns startup until the main window is ready.
            // Explicit shutdown keeps WPF alive between the two windows.
            ShutdownMode =
                ShutdownMode.OnExplicitShutdown;

            var splash =
                new StartupSplashWindow();

            var startupTimer =
                Stopwatch.StartNew();

            try
            {
                splash.Show();

                // I let WPF paint the splash before constructing the
                // full manager and all of its pages.
                await System.Windows.Threading.Dispatcher.Yield(
                    DispatcherPriority.Loaded);

                var mainWindow =
                    new MainWindow();

                TimeSpan remainingTime =
                    MinimumSplashTime -
                    startupTimer.Elapsed;

                if (remainingTime > TimeSpan.Zero)
                {
                    await Task.Delay(
                        remainingTime);
                }

                await splash.FadeOutAsync();
                splash.Close();

                MainWindow =
                    mainWindow;

                ShutdownMode =
                    ShutdownMode.OnMainWindowClose;

                mainWindow.Show();
                await mainWindow.HandleStartupArgumentsAsync(e.Args);
            }
            catch (Exception exception)
            {
                splash.Close();

                LimelightDialog.Open(
                    owner: null,
                    heading: "LIMELIGHT COULD NOT START",
                    message: "The manager could not finish preparing its main window.",
                    tone: LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "STARTUP FAILED");

                Shutdown(1);
            }
        }
    }
}
