using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CricketControlPanel : UserControl
    {
        private CricketMatchManager? _match;
        private CricketScorebugControl? _scorebug;
        private Window? _displayWindow;

        // Extras popup state
        private CricketDeliveryType _pendingExtraType;
        // Batter selection mode: "wicket", "striker", "nonstriker"
        private string _batterSelectionMode = "wicket";

        // Batter display rows: track which player index occupies top vs bottom
        private int _cpTopBatterIndex = -1;
        private int _cpBottomBatterIndex = -1;

        // Delivery log: current over group tracking
        private int _logCurrentOver = -1;
        private StackPanel? _logCurrentOverPanel;

        public event Action? ResetRequested;

        public CricketControlPanel()
        {
            InitializeComponent();
        }

        public void SetMatch(CricketMatchManager match)
        {
            _match = match;
            _match.MatchChanged += UpdateUI;
            _match.DeliveryAdded += OnDeliveryAdded;
            _match.BatterSelectionNeeded += OnBatterSelectionNeeded;
            _match.BowlerSelectionNeeded += OnBowlerSelectionNeeded;
            UpdateUI();
            RefreshBowlerList();
        }

        public void SetScorebug(CricketScorebugControl scorebug)
        {
            _scorebug = scorebug;
            _scorebug.OverlayQueueChanged += OnOverlayQueueChanged;
        }

        private void OnOverlayQueueChanged(int count)
        {
            Dispatcher.Invoke(() =>
            {
                if (count > 0)
                {
                    OverlayQueueBadge.Visibility = Visibility.Visible;
                    OverlayQueueText.Text = count == 1 ? "1 overlay queued" : $"{count} overlays queued";
                }
                else
                {
                    OverlayQueueBadge.Visibility = Visibility.Collapsed;
                }
            });
        }

        public void SetDisplayWindow(Window window)
        {
            _displayWindow = window;
        }

        public void SetMessages(List<string> messages)
        {
            MessageList.Items.Clear();
            foreach (var msg in messages)
                MessageList.Items.Add(msg);
            _scorebug?.SetMarqueeMessages(messages);
        }

        /// <summary>Trigger the initial batter + bowler selection at match start.</summary>
        public void TriggerInitialSelection()
        {
            _cpTopBatterIndex = -1;
            _cpBottomBatterIndex = -1;
            _batterSelectionMode = "striker";
            ShowBatterSelection("SELECT OPENING STRIKER");
        }

        // ---- Scoring ----

        private void Dot_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(0, CricketDeliveryType.Dot);
        private void Run1_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(1, CricketDeliveryType.Runs);
        private void Run2_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(2, CricketDeliveryType.Runs);
        private void Run3_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(3, CricketDeliveryType.Runs);
        private void Four_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(4, CricketDeliveryType.Four);
        private void Run5_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(5, CricketDeliveryType.Runs);
        private void Six_Click(object sender, RoutedEventArgs e) =>
            _match?.RecordDelivery(6, CricketDeliveryType.Six);
        private void Wicket_Click(object sender, RoutedEventArgs e)
        {
            if (_match?.CurrentInnings == null) return;
            var inn = _match.CurrentInnings;
            var striker = inn.Striker;
            if (striker == null) return;

            // Record the wicket immediately so scores update and animation fires
            _match.RecordDelivery(0, CricketDeliveryType.Wicket, "out");

            // Now show details popup to fill in how out (updates the already-dismissed player)
            WicketBatsmanLabel.Text = $"Batter: {striker.DisplayName}";
            _pendingDismissalType = DismissalType.Other;
            _pendingWicketBatter = striker;
            WicketFielderPanel.Visibility = Visibility.Collapsed;
            WicketBowlerPanel.Visibility = Visibility.Collapsed;
            WicketDetailsOverlay.Visibility = Visibility.Visible;
        }

        // Wicket details state
        private DismissalType _pendingDismissalType = DismissalType.Other;
        private CricketPlayer? _pendingWicketBatter;

        private void WicketType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            if (!Enum.TryParse<DismissalType>(tag, out var dtype)) return;
            _pendingDismissalType = dtype;

            var inn = _match?.CurrentInnings;
            if (inn == null) return;

            bool needsFielder = dtype == DismissalType.Caught || dtype == DismissalType.Stumped || dtype == DismissalType.RunOut;
            if (needsFielder)
            {
                WicketFielderPanel.Visibility = Visibility.Visible;
                WicketFielderList.Items.Clear();
                foreach (var p in inn.BowlingAttack)
                    WicketFielderList.Items.Add(p.DisplayName);
                if (WicketFielderList.Items.Count > 0)
                    WicketFielderList.SelectedIndex = 0;
            }
            else
            {
                WicketFielderPanel.Visibility = Visibility.Collapsed;
            }

            // Auto-select bowler for non-runout dismissals
            bool bowlerCredit = dtype != DismissalType.RunOut && dtype != DismissalType.RetiredHurt && dtype != DismissalType.Other;
            if (bowlerCredit && inn.CurrentBowler != null)
            {
                WicketBowlerPanel.Visibility = Visibility.Visible;
                WicketBowlerAutoLabel.Text = $"▸ {inn.CurrentBowler.DisplayName} (auto)";
            }
            else
            {
                WicketBowlerPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ConfirmWicket_Click(object sender, RoutedEventArgs e)
        {
            if (_match?.CurrentInnings == null || _pendingWicketBatter == null) return;
            var inn = _match.CurrentInnings;

            string? fielder = null;
            if (WicketFielderPanel.Visibility == Visibility.Visible && WicketFielderList.SelectedIndex >= 0)
                fielder = WicketFielderList.SelectedItem?.ToString();

            string? bowlerName = null;
            bool bowlerCredit = _pendingDismissalType != DismissalType.RunOut && _pendingDismissalType != DismissalType.RetiredHurt && _pendingDismissalType != DismissalType.Other;
            if (bowlerCredit && inn.CurrentBowler != null)
                bowlerName = inn.CurrentBowler.DisplayName;

            // Build dismissal text
            string dismissalText = _pendingDismissalType switch
            {
                DismissalType.Bowled => $"b {bowlerName}",
                DismissalType.Caught => $"c {fielder} b {bowlerName}",
                DismissalType.CaughtAndBowled => $"c&b {bowlerName}",
                DismissalType.LBW => $"lbw b {bowlerName}",
                DismissalType.Stumped => $"st {fielder} b {bowlerName}",
                DismissalType.RunOut => $"run out ({fielder})",
                DismissalType.HitWicket => $"hit wicket b {bowlerName}",
                _ => "out"
            };

            // Update the already-dismissed batter's details
            _pendingWicketBatter.DismissalText = dismissalText;
            _pendingWicketBatter.HowOut = _pendingDismissalType;
            _pendingWicketBatter.DismissalBowler = bowlerName;
            _pendingWicketBatter.DismissalFielder = fielder;

            _pendingWicketBatter = null;
            WicketDetailsOverlay.Visibility = Visibility.Collapsed;
        }

        // ---- Extras with popup ----

        private void Wide_Click(object sender, RoutedEventArgs e) => ShowExtrasPopup(CricketDeliveryType.Wide, "WIDE");
        private void NoBall_Click(object sender, RoutedEventArgs e) => ShowExtrasPopup(CricketDeliveryType.NoBall, "NO BALL");
        private void Bye_Click(object sender, RoutedEventArgs e) => ShowExtrasPopup(CricketDeliveryType.Bye, "BYE");
        private void LegBye_Click(object sender, RoutedEventArgs e) => ShowExtrasPopup(CricketDeliveryType.LegBye, "LEG BYE");

        private void ShowExtrasPopup(CricketDeliveryType type, string title)
        {
            _pendingExtraType = type;
            _pendingNoBallSubType = null;
            ExtrasPopupTitle.Text = title;
            NoBallSubExtrasPanel.Visibility = type == CricketDeliveryType.NoBall
                ? Visibility.Visible : Visibility.Collapsed;
            NoBallSubRunsPanel.Visibility = Visibility.Collapsed;
            ExtrasHintText.Text = type == CricketDeliveryType.NoBall
                ? "Total: 1 no-ball + additional runs"
                : "Total: 1 + additional runs";
            ExtrasPopup.Visibility = Visibility.Visible;
        }

        private void ExtrasPopupClose_Click(object sender, RoutedEventArgs e)
        {
            ExtrasPopup.Visibility = Visibility.Collapsed;
        }

        private void ExtrasRun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagStr) return;
            if (!int.TryParse(tagStr, out int additionalRuns)) return;

            int totalRuns = 1 + additionalRuns;
            _match?.RecordDelivery(totalRuns, _pendingExtraType);
            ExtrasPopup.Visibility = Visibility.Collapsed;
        }

        // No-ball sub-type state
        private string? _pendingNoBallSubType;

        private void NoBallSubType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;
            _pendingNoBallSubType = tag;

            string label = tag switch
            {
                "nbwide" => "NB + Wide",
                "nbbye" => "NB + Bye",
                "nblb" => "NB + Leg Bye",
                _ => "NB"
            };
            NoBallSubRunsLabel.Text = $"{label} — additional runs:";
            NoBallSubRunsPanel.Visibility = Visibility.Visible;
        }

        private void NoBallSubRun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tagStr) return;
            if (!int.TryParse(tagStr, out int additionalRuns)) return;

            // No-ball base = 1, sub-extra base = 1 (for wide), + additional
            int totalRuns = _pendingNoBallSubType switch
            {
                "nbwide" => 2 + additionalRuns,  // 1 NB + 1 Wide + extra
                "nbbye" => 1 + additionalRuns,   // 1 NB + extra byes
                "nblb" => 1 + additionalRuns,     // 1 NB + extra leg byes
                _ => 1 + additionalRuns
            };
            _match?.RecordDelivery(totalRuns, CricketDeliveryType.NoBall);
            ExtrasPopup.Visibility = Visibility.Collapsed;
        }

        // ---- Batter/bowler bars ----

        private void StrikerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void NonStrikerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BowlerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void ChangeStriker_Click(object sender, RoutedEventArgs e)
        {
            _batterSelectionMode = "striker";
            ShowBatterSelection("SELECT STRIKER");
        }

        private void ChangeNonStriker_Click(object sender, RoutedEventArgs e)
        {
            _batterSelectionMode = "nonstriker";
            ShowBatterSelection("SELECT NON-STRIKER");
        }

        private void ChangeBowlerMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowBowlerSelection(false);
        }

        private void SwapStrike_Click(object sender, RoutedEventArgs e)
        {
            _match?.RotateStrike();
            _match?.RaiseMatchChanged();
        }

        // ---- Batter selection ----

        private void OnBatterSelectionNeeded()
        {
            _batterSelectionMode = "wicket";
            ShowBatterSelection("SELECT NEW BATTER");
        }

        private void ShowBatterSelection(string title)
        {
            if (_match?.CurrentInnings == null) return;
            var inn = _match.CurrentInnings;

            BatterSelectionTitle.Text = title;
            BatterSelectionList.Items.Clear();
            BatterSelectionList.Tag = new List<int>();

            var indexList = (List<int>)BatterSelectionList.Tag;
            for (int i = 0; i < inn.BattingOrder.Count; i++)
            {
                var p = inn.BattingOrder[i];
                if (p.IsOut) continue;

                // Exclude the OTHER active batter to prevent selecting same player for both slots
                if (_batterSelectionMode == "striker" && i == inn.NonStrikerIndex) continue;
                if (_batterSelectionMode == "nonstriker" && i == inn.StrikerIndex) continue;
                // For wicket mode, exclude both current batsmen at crease
                if (_batterSelectionMode == "wicket" &&
                    (i == inn.StrikerIndex || i == inn.NonStrikerIndex)) continue;

                string suffix = (i == inn.StrikerIndex) ? "  (striker)"
                              : (i == inn.NonStrikerIndex) ? "  (non-striker)"
                              : "";
                indexList.Add(i);
                BatterSelectionList.Items.Add($"{p.FullName}{suffix}");
            }

            if (BatterSelectionList.Items.Count > 0)
                BatterSelectionList.SelectedIndex = 0;

            BatterSelectionOverlay.Visibility = Visibility.Visible;
        }

        private void ConfirmBatterSelection_Click(object sender, RoutedEventArgs e)
        {
            if (BatterSelectionList.SelectedIndex < 0 || _match == null) return;
            var indexList = BatterSelectionList.Tag as List<int>;
            if (indexList == null || BatterSelectionList.SelectedIndex >= indexList.Count) return;

            int playerIndex = indexList[BatterSelectionList.SelectedIndex];

            switch (_batterSelectionMode)
            {
                case "wicket":
                    _match.SelectNewBatter(playerIndex);
                    break;
                case "striker":
                    _match.SetStriker(playerIndex);
                    // After selecting striker, prompt for non-striker if both are default (index 0,1)
                    if (_match.CurrentInnings != null && _match.CurrentInnings.LegalBallsBowled == 0)
                    {
                        BatterSelectionOverlay.Visibility = Visibility.Collapsed;
                        _batterSelectionMode = "nonstriker";
                        ShowBatterSelection("SELECT OPENING NON-STRIKER");
                        return;
                    }
                    break;
                case "nonstriker":
                    _match.SetNonStriker(playerIndex);
                    // After selecting non-striker at start, prompt for bowler
                    if (_match.CurrentInnings != null && _match.CurrentInnings.LegalBallsBowled == 0)
                    {
                        BatterSelectionOverlay.Visibility = Visibility.Collapsed;
                        ShowBowlerSelection(true);
                        return;
                    }
                    break;
            }

            BatterSelectionOverlay.Visibility = Visibility.Collapsed;
        }

        // ---- Bowler selection ----

        private void OnBowlerSelectionNeeded()
        {
            ShowBowlerSelection(true);
        }

        private void ShowBowlerSelection(bool isOverEnd)
        {
            if (_match?.CurrentInnings == null) return;
            var inn = _match.CurrentInnings;

            BowlerSelectionHint.Text = isOverEnd
                ? "End of over — choose the next bowler"
                : "Select a bowler";

            BowlerSelectionList.Items.Clear();
            BowlerSelectionList.Tag = new List<int>();
            var indexList = (List<int>)BowlerSelectionList.Tag;
            int currentCompletedOvers = inn.CompletedOvers;
            int suggestedIndex = _match.SuggestedNextBowler();

            for (int i = 0; i < inn.BowlingAttack.Count; i++)
            {
                var p = inn.BowlingAttack[i];

                // Don't allow the bowler who just finished to bowl back-to-back
                if (isOverEnd && i == inn.CurrentBowlerIndex) continue;

                string hint;
                if (p.LastOverBowledAt >= 0)
                {
                    int oversAgo = currentCompletedOvers - p.LastOverBowledAt;
                    hint = oversAgo == 1 ? "  bowled 1 over ago" : $"  bowled {oversAgo} overs ago";
                }
                else
                    hint = "  not yet bowled";

                indexList.Add(i);
                BowlerSelectionList.Items.Add($"{p.DisplayName}  {p.BowlingFigures} ({p.OversDisplay}){hint}");
            }

            // Pre-select the suggested bowler if available in filtered list
            int sugIdx = indexList.IndexOf(suggestedIndex);
            if (sugIdx >= 0)
                BowlerSelectionList.SelectedIndex = sugIdx;
            else if (BowlerSelectionList.Items.Count > 0)
                BowlerSelectionList.SelectedIndex = 0;

            BowlerSelectionOverlay.Visibility = Visibility.Visible;
        }

        private void ConfirmBowlerSelection_Click(object sender, RoutedEventArgs e)
        {
            if (BowlerSelectionList.SelectedIndex < 0 || _match == null) return;
            var indexList = BowlerSelectionList.Tag as List<int>;
            if (indexList == null || BowlerSelectionList.SelectedIndex >= indexList.Count) return;

            int bowlerIndex = indexList[BowlerSelectionList.SelectedIndex];
            _match.ConfirmBowler(bowlerIndex);
            BowlerSelectionOverlay.Visibility = Visibility.Collapsed;
            RefreshBowlerList();
        }

        // ---- Actions ----

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            _match?.UndoLastDelivery();
            // Remove last delivery from grouped log
            if (_logCurrentOverPanel != null && _logCurrentOverPanel.Children.Count > 0)
            {
                _logCurrentOverPanel.Children.RemoveAt(_logCurrentOverPanel.Children.Count - 1);
                // If over group is now empty, remove the whole group
                if (_logCurrentOverPanel.Children.Count == 0 && _logCurrentOverPanel.Parent is StackPanel group)
                {
                    EventLog.Children.Remove(group);
                    _logCurrentOverPanel = null;
                    _logCurrentOver = -1;
                    // Find the previous over group if any
                    if (EventLog.Children.Count > 0
                        && EventLog.Children[0] is StackPanel prevGroup
                        && prevGroup.Children.Count >= 2
                        && prevGroup.Children[1] is StackPanel prevPanel)
                    {
                        _logCurrentOverPanel = prevPanel;
                        // Recover over number from the match state
                        if (_match?.CurrentInnings != null)
                            _logCurrentOver = _match.CurrentInnings.CompletedOvers;
                    }
                }
            }
        }

        private void EndInnings_Click(object sender, RoutedEventArgs e)
        {
            if (_match == null) return;
            var inn = _match.CurrentInnings;
            if (inn == null) return;

            if (!inn.IsComplete)
            {
                var res = MessageBox.Show("End the current innings?", "End Innings",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
                inn.IsComplete = true;
            }

            if (_match.CanStartNextInnings())
            {
                _match.StartNextInnings();
                _cpTopBatterIndex = -1;
                _cpBottomBatterIndex = -1;
                _logCurrentOver = -1;
                _logCurrentOverPanel = null;
                RefreshBowlerList();
                EventLog.Children.Clear();
                TriggerInitialSelection();
            }
            else
            {
                MessageBox.Show(_match.MatchResult ?? "Match complete.",
                    "Match Result", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ---- Overlay buttons ----

        private void OverlayStats_Click(object sender, RoutedEventArgs e)
        {
            _scorebug?.TriggerStatsBar();
        }

        private void OverlayPartnership_Click(object sender, RoutedEventArgs e)
        {
            if (_match?.CurrentInnings == null) return;
            var inn = _match.CurrentInnings;
            _scorebug?.ShowPartnershipOverlay(inn.PartnershipRuns, inn.PartnershipBalls);
        }

        private void OverlayExtras_Click(object sender, RoutedEventArgs e)
        {
            if (_match?.CurrentInnings == null) return;
            var inn = _match.CurrentInnings;
            _scorebug?.ShowExtrasOverlay(inn.Wides, inn.NoBalls, inn.Byes, inn.LegByes, inn.TotalExtras);
        }

        private void OverlayThisOver_Click(object sender, RoutedEventArgs e)
        {
            _scorebug?.ShowOverTracker();
        }

        private CricketSummaryControl? _summaryControl;
        private string _currentPresentation = "scorebug"; // "scorebug", "batting", "bowling"

        /// <summary>Fires when the user switches presentation screen. Arg = "scorebug", "batting", "bowling".</summary>
        public event Action<string>? PresentationChanged;

        public void SetSummaryControl(CricketSummaryControl summary)
        {
            _summaryControl = summary;
        }

        private void ShowBattingSummary_Click(object sender, RoutedEventArgs e)
        {
            if (_match == null) return;
            SwitchPresentation("batting");
        }

        private void ShowBowlingSummary_Click(object sender, RoutedEventArgs e)
        {
            if (_match == null) return;
            SwitchPresentation("bowling");
        }

        // ---- Bowler settings list ----

        private void RefreshBowlerList()
        {
            BowlerList.Items.Clear();
            var inn = _match?.CurrentInnings;
            if (inn == null) return;
            for (int i = 0; i < inn.BowlingAttack.Count; i++)
            {
                var p = inn.BowlingAttack[i];
                BowlerList.Items.Add($"{p.DisplayName}  {p.BowlingFigures} ({p.OversDisplay})");
            }
            if (inn.CurrentBowlerIndex < BowlerList.Items.Count)
                BowlerList.SelectedIndex = inn.CurrentBowlerIndex;
        }

        private void BowlerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BowlerList.SelectedIndex >= 0 && _match != null)
                _match.SetCurrentBowler(BowlerList.SelectedIndex);
        }

        // ---- Display ----

        private void ShowScorebug_Click(object sender, RoutedEventArgs e)
        {
            SwitchPresentation("scorebug");
        }

        private void ShowWindow_Click(object sender, RoutedEventArgs e)
        {
            PresentationChanged?.Invoke("window");
        }

        /// <summary>Switches the display window between scorebug, batting card, and bowling card permanently.</summary>
        private void SwitchPresentation(string target)
        {
            if (_match == null) return;

            if (target == "scorebug")
            {
                _summaryControl?.HideSummary();
                _currentPresentation = "scorebug";
            }
            else if (target == "batting")
            {
                _summaryControl?.ShowBattingSummary(_match);
                _currentPresentation = "batting";
            }
            else if (target == "bowling")
            {
                _summaryControl?.ShowBowlingSummary(_match);
                _currentPresentation = "bowling";
            }

            PresentationChanged?.Invoke(target);
        }

        /// <summary>Refreshes the current summary card if one is showing (called after score updates).</summary>
        public void RefreshSummaryIfShowing()
        {
            if (_match == null || _summaryControl == null) return;
            if (_currentPresentation == "batting")
                _summaryControl.RefreshBattingSummary(_match);
            else if (_currentPresentation == "bowling")
                _summaryControl.RefreshBowlingSummary(_match);
        }

        // ---- Messages ----

        private void AddMessage_Click(object sender, RoutedEventArgs e)
        {
            string? text = NewMessageBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            MessageList.Items.Add(text);
            NewMessageBox.Text = "";
            PushMessages();
        }

        private void RemoveMessage_Click(object sender, RoutedEventArgs e)
        {
            if (MessageList.SelectedIndex >= 0)
                MessageList.Items.RemoveAt(MessageList.SelectedIndex);
            else if (MessageList.Items.Count > 0)
                MessageList.Items.RemoveAt(MessageList.Items.Count - 1);
            PushMessages();
        }

        private void PushMessages()
        {
            var messages = new List<string>();
            foreach (var item in MessageList.Items)
            {
                string? s = item?.ToString();
                if (!string.IsNullOrWhiteSpace(s)) messages.Add(s);
            }
            _scorebug?.SetMarqueeMessages(messages);
        }

        // ---- Reset ----

        private void ResetMatch_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Reset the cricket match and set up a new game?", "Reset",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            ResetRequested?.Invoke();
        }

        // ---- Delivery log grouped by over ----

        private void OnDeliveryAdded(CricketDelivery delivery)
        {
            // Trigger event banner animations for boundaries and wickets
            if (_match != null && (delivery.Type == CricketDeliveryType.Four
                || delivery.Type == CricketDeliveryType.Six
                || delivery.Type == CricketDeliveryType.Wicket))
            {
                // 4/6 = batting team color, Wicket = bowling team color
                string color = delivery.Type == CricketDeliveryType.Wicket
                    ? _match.BowlingTeamPrimaryColor
                    : _match.BattingTeamPrimaryColor;
                _scorebug?.ShowEventBanner(delivery.Type, color);
            }

            // Start a new over group if the over number changed
            if (delivery.OverNumber != _logCurrentOver)
            {
                _logCurrentOver = delivery.OverNumber;
                _logCurrentOverPanel = new StackPanel();

                // Over header
                var header = new Border
                {
                    CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var overLabel = new TextBlock
                {
                    Text = $"OVER {delivery.OverNumber + 1}",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Black,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(overLabel, 0);
                headerGrid.Children.Add(overLabel);

                var bowlerLabel = new TextBlock
                {
                    Text = delivery.BowlerName ?? "",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(bowlerLabel, 1);
                headerGrid.Children.Add(bowlerLabel);

                var scoreLabel = new TextBlock
                {
                    Tag = "overScore",
                    Text = $"{delivery.WicketsAfter}/{delivery.TotalAfter}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Bahnschrift"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(scoreLabel, 2);
                headerGrid.Children.Add(scoreLabel);

                header.Child = headerGrid;

                var overGroup = new StackPanel();
                overGroup.Children.Add(header);
                overGroup.Children.Add(_logCurrentOverPanel);

                EventLog.Children.Insert(0, overGroup);
            }

            // Update the over score in the header to latest
            if (_logCurrentOverPanel?.Parent is StackPanel group
                && group.Children[0] is Border hdr
                && hdr.Child is Grid hdrGrid)
            {
                foreach (UIElement child in hdrGrid.Children)
                {
                    if (child is TextBlock tb && tb.Tag is string tag && tag == "overScore")
                    {
                        tb.Text = $"{delivery.WicketsAfter}/{delivery.TotalAfter}";
                        break;
                    }
                }
            }

            // Add delivery bar into the current over group
            var bar = CreateDeliveryBar(delivery);
            _logCurrentOverPanel?.Children.Add(bar);
        }

        private static Border CreateDeliveryBar(CricketDelivery delivery)
        {
            // Colour-coded bar based on delivery type
            Color barColor = delivery.Type switch
            {
                CricketDeliveryType.Wicket => Color.FromRgb(0xDC, 0x26, 0x26),
                CricketDeliveryType.Four => Color.FromRgb(0x15, 0x80, 0x3D),
                CricketDeliveryType.Six => Color.FromRgb(0x7C, 0x3A, 0xED),
                CricketDeliveryType.Wide => Color.FromRgb(0xD9, 0x77, 0x06),
                CricketDeliveryType.NoBall => Color.FromRgb(0xD9, 0x77, 0x06),
                CricketDeliveryType.Bye => Color.FromRgb(0x06, 0x59, 0x9D),
                CricketDeliveryType.LegBye => Color.FromRgb(0x06, 0x59, 0x9D),
                CricketDeliveryType.Dot => Color.FromRgb(0x33, 0x41, 0x55),
                _ => Color.FromRgb(0x1E, 0x40, 0xAF),
            };

            string overStr = $"{delivery.OverNumber}.{delivery.BallInOver}";

            // Symbol
            string symbol = delivery.Type switch
            {
                CricketDeliveryType.Wicket => "W",
                CricketDeliveryType.Four => "4",
                CricketDeliveryType.Six => "6",
                CricketDeliveryType.Dot => "•",
                CricketDeliveryType.Wide => "Wd",
                CricketDeliveryType.NoBall => "Nb",
                CricketDeliveryType.Bye => "B",
                CricketDeliveryType.LegBye => "Lb",
                _ => delivery.Runs.ToString()
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(8, 5, 8, 5),
                Background = new SolidColorBrush(Color.FromArgb(0x33, barColor.R, barColor.G, barColor.B)),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Over number
            var overText = new TextBlock
            {
                Text = overStr,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(overText, 0);
            grid.Children.Add(overText);

            // Symbol pill
            var pill = new Border
            {
                Background = new SolidColorBrush(barColor),
                CornerRadius = new CornerRadius(4),
                Width = 24,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = symbol,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(pill, 1);
            grid.Children.Add(pill);

            // Description
            var desc = new TextBlock
            {
                Text = delivery.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(desc, 2);
            grid.Children.Add(desc);

            // Running score
            var score = new TextBlock
            {
                Text = $"{delivery.WicketsAfter}/{delivery.TotalAfter}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Bahnschrift"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(score, 3);
            grid.Children.Add(score);

            border.Child = grid;
            return border;
        }

        // ---- UI updates ----

        private void UpdateUI()
        {
            if (_match == null) return;
            var inn = _match.CurrentInnings;
            if (inn == null) return;

            // Header
            InningsLabel.Text = _match.CurrentInningsNumber switch
            {
                1 => "1st Innings",
                2 => "2nd Innings",
                3 => "3rd Innings",
                _ => $"{_match.CurrentInningsNumber}th Innings"
            };
            HeaderScore.Text = $"{inn.TotalWickets}/{inn.TotalRuns} ({inn.OversDisplay})";

            // Batter display — keep rows stable, only move the indicator
            int si = inn.StrikerIndex;
            int nsi = inn.NonStrikerIndex;

            if (_cpTopBatterIndex < 0 || _cpBottomBatterIndex < 0)
            {
                _cpTopBatterIndex = si;
                _cpBottomBatterIndex = nsi;
            }
            else
            {
                bool topStillActive = _cpTopBatterIndex == si || _cpTopBatterIndex == nsi;
                bool bottomStillActive = _cpBottomBatterIndex == si || _cpBottomBatterIndex == nsi;

                if (!topStillActive && bottomStillActive)
                    _cpTopBatterIndex = (_cpBottomBatterIndex == si) ? nsi : si;
                else if (topStillActive && !bottomStillActive)
                    _cpBottomBatterIndex = (_cpTopBatterIndex == si) ? nsi : si;
                else if (!topStillActive && !bottomStillActive)
                {
                    _cpTopBatterIndex = si;
                    _cpBottomBatterIndex = nsi;
                }
            }

            var topPlayer = (_cpTopBatterIndex >= 0 && _cpTopBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_cpTopBatterIndex] : null;
            var bottomPlayer = (_cpBottomBatterIndex >= 0 && _cpBottomBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_cpBottomBatterIndex] : null;

            bool topIsStriker = _cpTopBatterIndex == si;
            bool topGreyOut = topPlayer != null && topPlayer.IsOut && inn.NeedsBatterSelection;
            bool bottomGreyOut = bottomPlayer != null && bottomPlayer.IsOut && inn.NeedsBatterSelection;

            // Top batter bar (uses Striker* controls)
            if (topPlayer != null)
            {
                StrikerName.Text = topPlayer.DisplayName;
                StrikerName.Foreground = Brushes.White;
                StrikerStats.Text = $"{topPlayer.Runs} ({topPlayer.BallsFaced})  SR {topPlayer.StrikeRate:F1}";
                StrikerRuns.Text = topPlayer.Runs.ToString();
                StrikerRuns.Foreground = Brushes.White;
                TopBatterIndicator.Text = topIsStriker ? "▶" : "  ";
            }
            else
            {
                StrikerName.Text = "—";
                StrikerStats.Text = "";
                StrikerRuns.Text = "0";
                TopBatterIndicator.Text = "  ";
            }

            // Bottom batter bar (uses NonStriker* controls)
            if (bottomPlayer != null)
            {
                NonStrikerName.Text = bottomPlayer.DisplayName;
                NonStrikerName.Foreground = Brushes.White;
                NonStrikerStats.Text = $"{bottomPlayer.Runs} ({bottomPlayer.BallsFaced})  SR {bottomPlayer.StrikeRate:F1}";
                NonStrikerRunsText.Text = bottomPlayer.Runs.ToString();
                NonStrikerRunsText.Foreground = Brushes.White;
                BottomBatterIndicator.Text = !topIsStriker ? "▶" : "  ";
            }
            else
            {
                NonStrikerName.Text = "—";
                NonStrikerStats.Text = "";
                NonStrikerRunsText.Text = "0";
                BottomBatterIndicator.Text = "  ";
            }

            // Bowler bar
            var bowler = inn.CurrentBowler;
            if (bowler != null)
            {
                BowlerName.Text = bowler.DisplayName;
                BowlerStats.Text = $"{bowler.BowlingFigures} ({bowler.OversDisplay})  Econ {bowler.Economy:F2}";
                BowlerFigures.Text = bowler.BowlingFigures;
            }
            else
            {
                BowlerName.Text = "—";
                BowlerStats.Text = "";
                BowlerFigures.Text = "0-0";
            }

            OverDisplay.Text = $"This over: {string.Join("  ", inn.CurrentOverBalls)}";

            // Update bowler list figures
            for (int i = 0; i < inn.BowlingAttack.Count && i < BowlerList.Items.Count; i++)
            {
                var p = inn.BowlingAttack[i];
                BowlerList.Items[i] = $"{p.DisplayName}  {p.BowlingFigures} ({p.OversDisplay})";
            }

            _scorebug?.UpdateFromMatch(_match);
            RefreshSummaryIfShowing();
        }
    }
}
