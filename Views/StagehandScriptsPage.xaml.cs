using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Limelight.Views
{
    public partial class StagehandScriptsPage : UserControl
    {
        private readonly List<InstalledStagehandScript> _scripts = new();

        public event Action? RefreshRequested;
        public event Action? UpdateRuntimeRequested;
        public event Action<string, bool>? SetEnabledRequested;
        public event Action<string>? RemoveRequested;

        public StagehandScriptsPage()
        {
            InitializeComponent();
        }

        public void ShowScripts(
            IEnumerable<InstalledStagehandScript> scripts,
            string? runtimeHealth = null)
        {
            _scripts.Clear();
            _scripts.AddRange(scripts);
            ScriptsItemsControl.ItemsSource = null;
            ScriptsItemsControl.ItemsSource = _scripts;
            EmptyText.Visibility = _scripts.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RuntimeHealthText.Text = string.IsNullOrWhiteSpace(runtimeHealth)
                ? "RUNTIME HEALTH · Not reported yet. Launch the game once, then refresh."
                : runtimeHealth;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) =>
            RefreshRequested?.Invoke();

        private void UpdateRuntime_Click(object sender, RoutedEventArgs e) =>
            UpdateRuntimeRequested?.Invoke();

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.CommandParameter is not string id)
            {
                return;
            }

            InstalledStagehandScript? script = _scripts.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
            if (script is not null)
            {
                SetEnabledRequested?.Invoke(script.Id, !script.IsEnabled);
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is string id)
            {
                RemoveRequested?.Invoke(id);
            }
        }
    }
}
