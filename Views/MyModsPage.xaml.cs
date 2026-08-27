using Limelight.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class MyModsPage : UserControl
    {
        private readonly Dictionary<string, InstalledMod> _visibleMods =
            new(StringComparer.OrdinalIgnoreCase);

        private string _renamingModId =
            string.Empty;

        private bool _showingCharacters =
            true;

        private int _characterModCount;
        private int _otherModCount;

        public event Action<string>? ToggleModRequested;
        public event Action<string>? RemoveModRequested;
        public event Action<string, string>? RenameModRequested;

        public MyModsPage()
        {
            InitializeComponent();
        }

        public void ShowMods(
            IEnumerable<InstalledMod> mods,
            string activeModId,
            IEnumerable<string> enabledConventionalModIds)
        {
            List<InstalledMod> visibleMods =
                mods.ToList();

            var enabledConventionalIds =
                new HashSet<string>(
                    enabledConventionalModIds,
                    StringComparer.OrdinalIgnoreCase);

            _visibleMods.Clear();

            foreach (InstalledMod mod in visibleMods)
            {
                _visibleMods[mod.Id] =
                    mod;

                mod.IsActive =
                    mod.IsPlayerCharacterMod &&
                    string.Equals(
                        mod.Id,
                        activeModId,
                        StringComparison.OrdinalIgnoreCase);

                mod.IsEnabledForNextLaunch =
                    mod.IsConventionalMod &&
                    enabledConventionalIds.Contains(
                        mod.Id);
            }

            List<InstalledMod> characterMods =
                visibleMods
                    .Where(mod =>
                        mod.IsCharacterReplacement)
                    .ToList();

            List<InstalledMod> otherMods =
                visibleMods
                    .Where(mod =>
                        mod.IsConventionalMod ||
                        mod.IsArenaSlotMod)
                    .ToList();

            CharacterModsList.ItemsSource = null;
            CharacterModsList.ItemsSource = characterMods;

            OtherModsList.ItemsSource = null;
            OtherModsList.ItemsSource = otherMods;

            _characterModCount =
                characterMods.Count;

            _otherModCount =
                otherMods.Count;

            ModCountText.Text =
                visibleMods.Count == 1
                    ? "1 MOD"
                    : $"{visibleMods.Count} MODS";
            ModCountText.Foreground =
    (Brush)FindResource(
        visibleMods.Count == 0
            ? "PinkBrush"
            : "CyanBrush");

            EmptyCharactersText.Visibility =
                characterMods.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            EmptyOtherModsText.Visibility =
                otherMods.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            UpdateCategoryVisibility();
        }

        private void CharactersTab_Click(
            object sender,
            RoutedEventArgs e)
        {
            _showingCharacters =
                true;

            UpdateCategoryVisibility();
        }

        private void OtherModsTab_Click(
            object sender,
            RoutedEventArgs e)
        {
            _showingCharacters =
                false;

            UpdateCategoryVisibility();
        }

        private void UpdateCategoryVisibility()
        {
            CharactersPanel.Visibility =
                _showingCharacters
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            OtherModsPanel.Visibility =
                _showingCharacters
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            CharactersTabButton.Content =
                $"CHARACTERS ({_characterModCount})";

            OtherModsTabButton.Content =
                $"OTHER MODS ({_otherModCount})";

            CharactersTabButton.Background =
                (Brush)FindResource(
                    _showingCharacters
                        ? "CyanBrush"
                        : "PanelBrush");

            CharactersTabButton.Foreground =
                (Brush)FindResource(
                    _showingCharacters
                        ? "BackgroundBrush"
                        : "TextBrush");

            OtherModsTabButton.Background =
                (Brush)FindResource(
                    _showingCharacters
                        ? "PanelBrush"
                        : "CyanBrush");

            OtherModsTabButton.Foreground =
                (Brush)FindResource(
                    _showingCharacters
                        ? "TextBrush"
                        : "BackgroundBrush");
        }

        private void ToggleMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is string modId)
            {
                ToggleModRequested?.Invoke(modId);
            }
        }

        private void RemoveMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is string modId)
            {
                RemoveModRequested?.Invoke(modId);
            }
        }

        private void RenameMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string modId ||
                !_visibleMods.TryGetValue(
                    modId,
                    out InstalledMod? mod))
            {
                return;
            }

            _renamingModId =
                modId;

            RenameTextBox.Text =
                mod.DisplayName;

            RenameValidationText.Text =
                "This changes the name shown inside Limelight. The stored mod files stay untouched.";

            RenameOverlay.Visibility =
                Visibility.Visible;

            RenameTextBox.Focus();
            Keyboard.Focus(
                RenameTextBox);
            RenameTextBox.SelectAll();
        }

        private void SaveRename_Click(
            object sender,
            RoutedEventArgs e)
        {
            string displayName =
                RenameTextBox.Text.Trim();

            if (displayName.Length == 0)
            {
                RenameValidationText.Text =
                    "Enter a name before saving.";

                return;
            }

            if (displayName.Length > 80)
            {
                RenameValidationText.Text =
                    "Keep the display name to 80 characters or fewer.";

                return;
            }

            string modId =
                _renamingModId;

            CloseRenameOverlay();

            RenameModRequested?.Invoke(
                modId,
                displayName);
        }

        private void CancelRename_Click(
            object sender,
            RoutedEventArgs e)
        {
            CloseRenameOverlay();
        }

        private void RenameTextBox_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseRenameOverlay();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SaveRename_Click(
                    sender,
                    e);
            }
        }

        private void CloseRenameOverlay()
        {
            _renamingModId =
                string.Empty;

            RenameOverlay.Visibility =
                Visibility.Collapsed;
        }
    }
}
