using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using WinForms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CricketSetupWizard : UserControl
    {
        private int _currentStep = 1;
        private readonly List<CricketPlayer> _teamAPlayers = new();
        private readonly List<CricketPlayer> _teamBPlayers = new();
        private string? _teamALogoPath;
        private string? _teamBLogoPath;
        private List<TeamPreset> _presets = new();

        public CricketSetupResult? Result { get; private set; }
        public event Action<CricketSetupResult>? SetupCompleted;

        public CricketSetupWizard()
        {
            InitializeComponent();
            LoadPresets();

            TeamANameBox.TextChanged += (_, __) => UpdateTeamAPreview();
            TeamAAbbrBox.TextChanged += (_, __) => UpdateTeamAPreview();
            TeamAColorBox.TextChanged += (_, __) => UpdateTeamAPreview();
            TeamASecondaryBox.TextChanged += (_, __) => UpdateTeamAPreview();

            TeamBNameBox.TextChanged += (_, __) => UpdateTeamBPreview();
            TeamBAbbrBox.TextChanged += (_, __) => UpdateTeamBPreview();
            TeamBColorBox.TextChanged += (_, __) => UpdateTeamBPreview();
            TeamBSecondaryBox.TextChanged += (_, __) => UpdateTeamBPreview();
        }

        // ---- Presets ----

        private void LoadPresets()
        {
            _presets = PresetStorage.LoadAllCricket();
            RefreshPresetCombos();
        }

        private void RefreshPresetCombos()
        {
            TeamAPresetCombo.Items.Clear();
            TeamBPresetCombo.Items.Clear();
            foreach (var p in _presets)
            {
                TeamAPresetCombo.Items.Add(p.PresetName);
                TeamBPresetCombo.Items.Add(p.PresetName);
            }
        }

        private void LoadPresetA_Click(object sender, RoutedEventArgs e) => LoadPresetInto(TeamAPresetCombo, true);
        private void LoadPresetB_Click(object sender, RoutedEventArgs e) => LoadPresetInto(TeamBPresetCombo, false);

        private void LoadPresetInto(ComboBox combo, bool isTeamA)
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= _presets.Count) return;
            var preset = _presets[combo.SelectedIndex];

            if (isTeamA)
            {
                TeamANameBox.Text = preset.TeamName;
                TeamAAbbrBox.Text = preset.Abbreviation;
                TeamAColorBox.Text = preset.PrimaryColor;
                TeamASecondaryBox.Text = preset.SecondaryColor;
                TrySetPreview(TeamAColorPreview, preset.PrimaryColor);
                TrySetPreview(TeamASecondaryPreview, preset.SecondaryColor);
                _teamALogoPath = preset.LogoPath;
                TeamALogoText.Text = string.IsNullOrWhiteSpace(preset.LogoPath) ? "(none)" : System.IO.Path.GetFileName(preset.LogoPath);
                ShowLogoPreview(_teamALogoPath, TeamALogoPreview, TeamALogoImage);

                if (preset.CricketPlayers is { Count: > 0 })
                {
                    _teamAPlayers.Clear();
                    TeamAPlayerList.Items.Clear();
                    foreach (var pe in preset.CricketPlayers)
                    {
                        var p = new CricketPlayer { FirstName = pe.FirstName, LastName = pe.LastName };
                        _teamAPlayers.Add(p);
                        TeamAPlayerList.Items.Add($"{_teamAPlayers.Count}. {p.FullName}");
                    }
                }
                UpdateTeamAPreview();
            }
            else
            {
                TeamBNameBox.Text = preset.TeamName;
                TeamBAbbrBox.Text = preset.Abbreviation;
                TeamBColorBox.Text = preset.PrimaryColor;
                TeamBSecondaryBox.Text = preset.SecondaryColor;
                TrySetPreview(TeamBColorPreview, preset.PrimaryColor);
                TrySetPreview(TeamBSecondaryPreview, preset.SecondaryColor);
                _teamBLogoPath = preset.LogoPath;
                TeamBLogoText.Text = string.IsNullOrWhiteSpace(preset.LogoPath) ? "(none)" : System.IO.Path.GetFileName(preset.LogoPath);
                ShowLogoPreview(_teamBLogoPath, TeamBLogoPreview, TeamBLogoImage);

                if (preset.CricketPlayers is { Count: > 0 })
                {
                    _teamBPlayers.Clear();
                    TeamBPlayerList.Items.Clear();
                    foreach (var pe in preset.CricketPlayers)
                    {
                        var p = new CricketPlayer { FirstName = pe.FirstName, LastName = pe.LastName };
                        _teamBPlayers.Add(p);
                        TeamBPlayerList.Items.Add($"{_teamBPlayers.Count}. {p.FullName}");
                    }
                }
                UpdateTeamBPreview();
            }
        }

        private void SavePresetA_Click(object sender, RoutedEventArgs e) => SavePreset(true);
        private void SavePresetB_Click(object sender, RoutedEventArgs e) => SavePreset(false);

        private void SavePreset(bool isTeamA)
        {
            string name = isTeamA ? TeamANameBox.Text.Trim() : TeamBNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var players = isTeamA ? _teamAPlayers : _teamBPlayers;
            var preset = new TeamPreset
            {
                PresetName = name,
                TeamName = name,
                Abbreviation = isTeamA ? TeamAAbbrBox.Text.Trim() : TeamBAbbrBox.Text.Trim(),
                PrimaryColor = isTeamA ? TeamAColorBox.Text.Trim() : TeamBColorBox.Text.Trim(),
                SecondaryColor = isTeamA ? TeamASecondaryBox.Text.Trim() : TeamBSecondaryBox.Text.Trim(),
                LogoPath = isTeamA ? _teamALogoPath : _teamBLogoPath,
                CricketPlayers = players.Select(p => new CricketPlayerEntry
                {
                    FirstName = p.FirstName,
                    LastName = p.LastName
                }).ToList()
            };

            int existing = _presets.FindIndex(p => p.PresetName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                _presets[existing] = preset;
            else
                _presets.Add(preset);

            PresetStorage.SaveAllCricket(_presets);
            RefreshPresetCombos();
        }

        // ---- Reset ----

        public void Reset()
        {
            Result = null;
            _currentStep = 1;
            _teamAPlayers.Clear();
            _teamBPlayers.Clear();
            _teamALogoPath = null;
            _teamBLogoPath = null;

            TeamANameBox.Text = "";
            TeamAAbbrBox.Text = "";
            TeamAColorBox.Text = "#CC0000";
            TeamASecondaryBox.Text = "#FFFFFF";
            TrySetPreview(TeamAColorPreview, "#CC0000");
            TrySetPreview(TeamASecondaryPreview, "#FFFFFF");
            TeamALogoText.Text = "(none)";

            TeamBNameBox.Text = "";
            TeamBAbbrBox.Text = "";
            TeamBColorBox.Text = "#000080";
            TeamBSecondaryBox.Text = "#FFFFFF";
            TrySetPreview(TeamBColorPreview, "#000080");
            TrySetPreview(TeamBSecondaryPreview, "#FFFFFF");
            TeamBLogoText.Text = "(none)";

            TeamAPlayerList.Items.Clear();
            TeamBPlayerList.Items.Clear();

            FormatLimited.IsChecked = true;
            TotalOversBox.Text = "50";
            TossTeamA.IsChecked = true;
            TossElectedBat.IsChecked = true;

            MessageList.Items.Clear();
            NewMessageBox.Text = "";

            LoadPresets();
            ShowStep(1);
        }

        // ---- Navigation ----

        private void ShowStep(int step)
        {
            _currentStep = step;
            Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            BackButton.Visibility = step > 1 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Content = step < 3 ? "Next →" : "Start Match ▶";

            StepSubtitle.Text = step switch
            {
                1 => "Configure both teams",
                2 => "Enter player squads",
                _ => "Match format and toss"
            };

            var accent = FindResource("AccentBrush") as SolidColorBrush;
            var surface = FindResource("Surface2Brush") as SolidColorBrush;
            var muted = FindResource("TextMutedBrush") as SolidColorBrush;

            Step1Dot.Background = step >= 1 ? accent : surface;
            Step2Dot.Background = step >= 2 ? accent : surface;
            Step3Dot.Background = step >= 3 ? accent : surface;
            Step1Label.Foreground = step >= 1 ? Brushes.White : muted;
            Step2Label.Foreground = step >= 2 ? Brushes.White : muted;
            Step3Label.Foreground = step >= 3 ? Brushes.White : muted;

            if (step == 2)
            {
                string aName = TeamANameBox.Text.Trim();
                string bName = TeamBNameBox.Text.Trim();
                TeamAPlayersLabel.Text = string.IsNullOrWhiteSpace(aName) ? "TEAM A PLAYERS" : $"{aName.ToUpper()} PLAYERS";
                TeamBPlayersLabel.Text = string.IsNullOrWhiteSpace(bName) ? "TEAM B PLAYERS" : $"{bName.ToUpper()} PLAYERS";
            }

            if (step == 3)
            {
                string aName = TeamANameBox.Text.Trim();
                string bName = TeamBNameBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(aName)) TossTeamA.Content = aName;
                if (!string.IsNullOrWhiteSpace(bName)) TossTeamB.Content = bName;
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 3)
                ShowStep(_currentStep + 1);
            else
                FinishWizard();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1) ShowStep(_currentStep - 1);
        }

        // ---- Players ----

        private void AddTeamAPlayer_Click(object sender, RoutedEventArgs e)
            => AddPlayer(_teamAPlayers, TeamAPlayerList, TeamAFirstNameBox, TeamALastNameBox);
        private void AddTeamBPlayer_Click(object sender, RoutedEventArgs e)
            => AddPlayer(_teamBPlayers, TeamBPlayerList, TeamBFirstNameBox, TeamBLastNameBox);
        private void RemoveTeamAPlayer_Click(object sender, RoutedEventArgs e)
            => RemovePlayer(_teamAPlayers, TeamAPlayerList);
        private void RemoveTeamBPlayer_Click(object sender, RoutedEventArgs e)
            => RemovePlayer(_teamBPlayers, TeamBPlayerList);

        private void TeamAPlayerBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddPlayer(_teamAPlayers, TeamAPlayerList, TeamAFirstNameBox, TeamALastNameBox);
                e.Handled = true;
            }
        }

        private void TeamBPlayerBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddPlayer(_teamBPlayers, TeamBPlayerList, TeamBFirstNameBox, TeamBLastNameBox);
                e.Handled = true;
            }
        }

        private static void AddPlayer(List<CricketPlayer> list, ListBox listBox, TextBox firstBox, TextBox lastBox)
        {
            string first = firstBox.Text.Trim();
            string last = lastBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(last)) return;

            var player = new CricketPlayer { FirstName = first, LastName = last };
            list.Add(player);
            listBox.Items.Add($"{list.Count}. {player.FullName}");
            firstBox.Text = "";
            lastBox.Text = "";
            firstBox.Focus();
        }

        private static void RemovePlayer(List<CricketPlayer> list, ListBox listBox)
        {
            int idx = listBox.SelectedIndex;
            if (idx < 0 && list.Count > 0) idx = list.Count - 1;
            if (idx < 0 || idx >= list.Count) return;
            list.RemoveAt(idx);
            listBox.Items.Clear();
            for (int i = 0; i < list.Count; i++)
                listBox.Items.Add($"{i + 1}. {list[i].FullName}");
        }

        // ---- Format ----

        private void Format_Changed(object sender, RoutedEventArgs e)
        {
            if (OversPanel != null)
                OversPanel.Visibility = FormatLimited.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Team previews ----

        private void UpdateTeamAPreview()
        {
            string name = TeamANameBox.Text.Trim();
            string abbr = TeamAAbbrBox.Text.Trim();
            TeamAPreviewName.Text = string.IsNullOrWhiteSpace(name) ? "Team A" : name;
            TeamAPreviewAbbr.Text = string.IsNullOrWhiteSpace(abbr) ? "TMA" : abbr.ToUpper();
            try { TeamAPreviewBg.Color = (Color)ColorConverter.ConvertFromString(TeamAColorBox.Text.Trim()); } catch { }
            try { TeamAPreviewFg.Color = (Color)ColorConverter.ConvertFromString(TeamASecondaryBox.Text.Trim()); } catch { }
            try { TeamAPreviewNameFg.Color = (Color)ColorConverter.ConvertFromString(TeamASecondaryBox.Text.Trim()); } catch { }
        }

        private void UpdateTeamBPreview()
        {
            string name = TeamBNameBox.Text.Trim();
            string abbr = TeamBAbbrBox.Text.Trim();
            TeamBPreviewName.Text = string.IsNullOrWhiteSpace(name) ? "Team B" : name;
            TeamBPreviewAbbr.Text = string.IsNullOrWhiteSpace(abbr) ? "TMB" : abbr.ToUpper();
            try { TeamBPreviewBg.Color = (Color)ColorConverter.ConvertFromString(TeamBColorBox.Text.Trim()); } catch { }
            try { TeamBPreviewFg.Color = (Color)ColorConverter.ConvertFromString(TeamBSecondaryBox.Text.Trim()); } catch { }
            try { TeamBPreviewNameFg.Color = (Color)ColorConverter.ConvertFromString(TeamBSecondaryBox.Text.Trim()); } catch { }
        }

        private void ShowLogoPreview(string? path, Border previewBorder, System.Windows.Controls.Image previewImage)
        {
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelHeight = 120;
                    bmp.EndInit();
                    bmp.Freeze();
                    previewImage.Source = bmp;
                    previewBorder.Visibility = Visibility.Visible;
                }
                catch
                {
                    previewBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                previewBorder.Visibility = Visibility.Collapsed;
            }
        }

        // ---- Messages ----

        private void AddMessage_Click(object sender, RoutedEventArgs e)
        {
            string? text = NewMessageBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            MessageList.Items.Add(text);
            NewMessageBox.Text = "";
        }

        private void RemoveMessage_Click(object sender, RoutedEventArgs e)
        {
            if (MessageList.SelectedIndex >= 0)
                MessageList.Items.RemoveAt(MessageList.SelectedIndex);
            else if (MessageList.Items.Count > 0)
                MessageList.Items.RemoveAt(MessageList.Items.Count - 1);
        }

        // ---- Colour pickers ----

        private void PickTeamAColor_Click(object sender, MouseButtonEventArgs e) => PickColor(TeamAColorBox, TeamAColorPreview);
        private void PickTeamASecondary_Click(object sender, MouseButtonEventArgs e) => PickColor(TeamASecondaryBox, TeamASecondaryPreview);
        private void PickTeamBColor_Click(object sender, MouseButtonEventArgs e) => PickColor(TeamBColorBox, TeamBColorPreview);
        private void PickTeamBSecondary_Click(object sender, MouseButtonEventArgs e) => PickColor(TeamBSecondaryBox, TeamBSecondaryPreview);

        // ---- Eye droppers ----

        private void DropperTeamAColor_Click(object sender, MouseButtonEventArgs e) => DropColor(TeamAColorBox, TeamAColorPreview);
        private void DropperTeamASecondary_Click(object sender, MouseButtonEventArgs e) => DropColor(TeamASecondaryBox, TeamASecondaryPreview);
        private void DropperTeamBColor_Click(object sender, MouseButtonEventArgs e) => DropColor(TeamBColorBox, TeamBColorPreview);
        private void DropperTeamBSecondary_Click(object sender, MouseButtonEventArgs e) => DropColor(TeamBSecondaryBox, TeamBSecondaryPreview);

        private static void DropColor(TextBox box, Border preview)
        {
            var c = EyeDropper.Pick();
            if (c == null) return;
            string hex = $"#{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";
            box.Text = hex;
            TrySetPreview(preview, hex);
        }

        private static void PickColor(TextBox box, Border preview)
        {
            var dlg = new WinForms.ColorDialog { FullOpen = true, AnyColor = true };
            try
            {
                var wpf = (Color)ColorConverter.ConvertFromString(box.Text);
                dlg.Color = DrawingColor.FromArgb(wpf.R, wpf.G, wpf.B);
            }
            catch { }
            if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
            string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            box.Text = hex;
            TrySetPreview(preview, hex);
        }

        private static void TrySetPreview(Border preview, string hex)
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { }
        }

        // ---- Logos ----

        private void BrowseLogoA_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseLogo();
            if (path == null) return;
            _teamALogoPath = path;
            TeamALogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(path, TeamALogoPreview, TeamALogoImage);
        }

        private void BrowseLogoB_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseLogo();
            if (path == null) return;
            _teamBLogoPath = path;
            TeamBLogoText.Text = System.IO.Path.GetFileName(path);
            ShowLogoPreview(path, TeamBLogoPreview, TeamBLogoImage);
        }

        private void ClearLogoA_Click(object sender, RoutedEventArgs e)
        {
            _teamALogoPath = null;
            TeamALogoText.Text = "(none)";
            TeamALogoPreview.Visibility = Visibility.Collapsed;
        }

        private void ClearLogoB_Click(object sender, RoutedEventArgs e)
        {
            _teamBLogoPath = null;
            TeamBLogoText.Text = "(none)";
            TeamBLogoPreview.Visibility = Visibility.Collapsed;
        }

        private static string? BrowseLogo()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
                Title = "Select Team Logo"
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ---- Finish ----

        private void FinishWizard()
        {
            if (_teamAPlayers.Count < 2)
            {
                _teamAPlayers.Clear();
                _teamAPlayers.Add(new CricketPlayer { FirstName = "Player", LastName = "1" });
                _teamAPlayers.Add(new CricketPlayer { FirstName = "Player", LastName = "2" });
            }
            if (_teamBPlayers.Count < 2)
            {
                _teamBPlayers.Clear();
                _teamBPlayers.Add(new CricketPlayer { FirstName = "Player", LastName = "1" });
                _teamBPlayers.Add(new CricketPlayer { FirstName = "Player", LastName = "2" });
            }

            int.TryParse(TotalOversBox.Text, out int overs);
            if (overs <= 0) overs = 50;

            var messages = new List<string>();
            foreach (var item in MessageList.Items)
            {
                string? s = item?.ToString();
                if (!string.IsNullOrWhiteSpace(s)) messages.Add(s);
            }

            string aName = TeamANameBox.Text.Trim();
            string bName = TeamBNameBox.Text.Trim();

            // Determine who bats first from toss
            bool teamAWonToss = TossTeamA.IsChecked == true;
            bool electedBat = TossElectedBat.IsChecked == true;
            bool teamABatsFirst = teamAWonToss == electedBat; // won toss + bat = team bats; won toss + bowl = other team bats

            string tossWinner = teamAWonToss
                ? (string.IsNullOrWhiteSpace(aName) ? "Team A" : aName)
                : (string.IsNullOrWhiteSpace(bName) ? "Team B" : bName);

            Result = new CricketSetupResult
            {
                TeamAName = string.IsNullOrWhiteSpace(aName) ? "Team A" : aName,
                TeamAAbbr = string.IsNullOrWhiteSpace(TeamAAbbrBox.Text.Trim()) ? "TMA" : TeamAAbbrBox.Text.Trim(),
                TeamAPrimaryColor = TeamAColorBox.Text.Trim(),
                TeamASecondaryColor = TeamASecondaryBox.Text.Trim(),
                TeamALogoPath = _teamALogoPath,
                TeamAPlayers = _teamAPlayers.Select(p => new CricketPlayer { FirstName = p.FirstName, LastName = p.LastName }).ToList(),

                TeamBName = string.IsNullOrWhiteSpace(bName) ? "Team B" : bName,
                TeamBAbbr = string.IsNullOrWhiteSpace(TeamBAbbrBox.Text.Trim()) ? "TMB" : TeamBAbbrBox.Text.Trim(),
                TeamBPrimaryColor = TeamBColorBox.Text.Trim(),
                TeamBSecondaryColor = TeamBSecondaryBox.Text.Trim(),
                TeamBLogoPath = _teamBLogoPath,
                TeamBPlayers = _teamBPlayers.Select(p => new CricketPlayer { FirstName = p.FirstName, LastName = p.LastName }).ToList(),

                Format = FormatLimited.IsChecked == true ? CricketFormat.LimitedOvers : CricketFormat.MultiDay,
                TotalOvers = overs,
                TossWinner = tossWinner,
                TossWinnerElectedToBat = electedBat,
                TeamABatsFirst = teamABatsFirst,
                Messages = messages
            };

            SetupCompleted?.Invoke(Result);
        }
    }
}
