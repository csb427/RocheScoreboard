using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Label = System.Windows.Controls.Label;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private StackPanel CreateScorecardFigureEditor(string label, string value, bool overs, out TextBox field)
    {
        var form = new StackPanel { Tag = "scorecard-figure-editor", MinWidth = 0, Margin = new Thickness(0, 4, 0, 8) };
        field = WorkflowInput(value);
        field.Tag = "scorecard-figure-input";
        field.MinWidth = 0; field.Width = double.NaN; field.Height = double.NaN; field.MinHeight = 64;
        field.Padding = new Thickness(16, 10, 16, 10); field.Margin = new Thickness(0, 6, 0, 0);
        field.FontSize = 30; field.FontWeight = FontWeights.SemiBold;
        field.TextAlignment = TextAlignment.Center; field.VerticalContentAlignment = VerticalAlignment.Center;
        field.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        field.InputScope = new InputScope { Names = { new InputScopeName(overs ? InputScopeNameValue.Number : InputScopeNameValue.Digits) } };
        string title = "New " + label.ToLowerInvariant();
        var caption = new Label { Content = "_" + title, Target = field, FontSize = 15, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0) };
        caption.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(field, title);
        System.Windows.Automation.AutomationProperties.SetHelpText(field, overs ? "Enter complete overs, a decimal point, and zero to five legal balls." : "Enter a non-negative whole number. Totals are checked before applying the change.");
        bool selected = false;
        field.GotKeyboardFocus += (sender, _) =>
        {
            if (selected || sender is not TextBox input) return;
            selected = true; input.SelectAll();
        };
        form.Children.Add(caption); form.Children.Add(field);
        var current = WorkflowText("Current: " + value, 13); current.Margin = new Thickness(0, 9, 0, 0);
        current.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        form.Children.Add(current);
        return form;
    }

    private static void ConfigureScorecardMetric(FrameworkElement element, string heading)
    {
        string label = heading switch { "O" => "Overs", "M" => "Maidens", "R" => "Runs", "W" => "Wickets", "B" => "Balls", "WD" => "Wides", "NB" => "No-balls", "ECON" => "Econ", _ => heading };
        if (element is Button button)
        {
            string value = button.Content?.ToString() ?? "";
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = .72, TextAlignment = TextAlignment.Center });
            content.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            button.Content = content; button.ContentTemplate = null; button.Width = double.NaN; button.MinWidth = 0;
            button.MinHeight = 64; button.Padding = new Thickness(3, 6, 3, 6); button.Margin = new Thickness(2);
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            return;
        }
        if (element is TextBlock text)
        {
            string value = text.Text;
            text.Text = ""; text.TextAlignment = TextAlignment.Center; text.Margin = new Thickness(2, 7, 2, 7);
            text.Inlines.Add(new System.Windows.Documents.Run(label) { FontSize = 12 });
            text.Inlines.Add(new System.Windows.Documents.LineBreak());
            text.Inlines.Add(new System.Windows.Documents.Run(value) { FontSize = 20, FontWeight = FontWeights.SemiBold });
        }
    }

    private System.Windows.Media.Brush ScorecardAlertBrush(bool error)
    {
        if (SystemParameters.HighContrast) return System.Windows.SystemColors.WindowTextBrush;
        bool dark = TryFindResource("ControlCentre.TextBrush") is not SolidColorBrush text || text.Color.R + text.Color.G + text.Color.B > 384;
        return new SolidColorBrush(error
            ? dark ? System.Windows.Media.Color.FromRgb(255, 163, 170) : System.Windows.Media.Color.FromRgb(166, 44, 56)
            : dark ? System.Windows.Media.Color.FromRgb(243, 197, 102) : System.Windows.Media.Color.FromRgb(135, 89, 0));
    }

    /// <summary>Opens the existing validated scorecard draft at a current-innings player's figures.</summary>
    internal async Task BeginPlayerFiguresEdit(bool bowling, int playerIndex)
    {
        var match = _match; var innings = match?.CurrentInnings;
        if (match is null || innings is null || playerIndex < 0 || playerIndex >= (bowling ? innings.BowlingAttack.Count : innings.BattingOrder.Count)) return;
        if (_scorecardDirty && _scorecardDraft?.InningsNumber != innings.InningsNumber)
        {
            if (await ChooseAsync("Apply the pending row first?", "Apply the other innings' unfinished inline edit before opening this player's figures.", new("close", "Keep editing"), new("apply", "Apply pending edit", true)) != "apply" || _scorecardDraft is not { } pending || !await CommitScorecardEditAsync(pending)) return;
            if (!ReferenceEquals(match, _match) || !ReferenceEquals(innings, match.CurrentInnings)) return;
            _scorecardDirty = false;
        }
        _scorecardInnings = innings.InningsNumber; _scorecardBowling = bowling;
        MainTabControl.SelectedItem = ScorecardTab;
        RefreshScorecard(_scorecardDraft?.InningsNumber != innings.InningsNumber);
        await Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(match, _match) || !_scorecardPlayerRows.TryGetValue((bowling, playerIndex), out var row)) return;
            _scorecardScroll?.UpdateLayout();
            row.BringIntoView();
            var cell = row.Children.OfType<Button>().FirstOrDefault(button => button.ToolTip is string tip && tip.StartsWith("Edit ", StringComparison.Ordinal));
            cell?.BringIntoView(); if (cell is not null) Keyboard.Focus(cell);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private Button ScorecardSummaryCard(Button button, string label, string value, double valueSize = 29)
    {
        var caption = new StackPanel();
        caption.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Opacity = .78 });
        caption.Children.Add(new TextBlock { Text = value, FontSize = valueSize, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
        button.Content = caption; button.ContentTemplate = null; button.Width = double.NaN; button.MinWidth = 0; button.MinHeight = 78;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left; button.Padding = new Thickness(14, 10, 14, 10); button.Margin = new Thickness(3);
        string? primary = _match is null || _scorecardDraft is null ? null : _match.AllInnings[_scorecardDraft.InningsNumber - 1].BattingTeamName == _match.TeamAName ? _match.TeamAPrimaryColor : _match.TeamBPrimaryColor;
        button.Background = TeamVisuals.Panel(primary, primary, .22); button.BorderBrush = TeamVisuals.Accent(primary); button.BorderThickness = new Thickness(1);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, label + ": " + value + (Equals(button.Tag, "scorecard-view-extras") ? ". View breakdown." : ". Edit figure."));
        return button;
    }

    private void AddScorecardSummary(CricketScorecardDraft draft)
    {
        var summary = new System.Windows.Controls.Primitives.UniformGrid { Columns = ActualWidth < 760 ? 2 : 4, Margin = new Thickness(0, 9, 0, 10), Tag = "scorecard-total-summary" };
        summary.Children.Add(ScorecardSummaryCard(Figure("Runs", () => draft.TotalRuns, v => draft.TotalRuns = v), "Total runs", draft.TotalRuns.ToString()));
        summary.Children.Add(ScorecardSummaryCard(Figure("Wickets", () => draft.TotalWickets, v => draft.TotalWickets = v), "Wickets", draft.TotalWickets.ToString()));
        summary.Children.Add(ScorecardSummaryCard(Figure("Overs", () => draft.LegalBallsBowled, v => draft.LegalBallsBowled = v, true), "Overs", $"{draft.LegalBallsBowled / 6}.{draft.LegalBallsBowled % 6}"));
        var extras = WorkflowButton("", () =>
        {
            if (_scorecardBowling) { _scorecardBowling = false; RenderScorecard(); }
            _scorecardRows.Children.OfType<StackPanel>().FirstOrDefault(region => Equals(region.Tag, "scorecard-extras"))?.BringIntoView();
        }, "scorecard-view-extras");
        extras.ToolTip = "View extras breakdown";
        summary.Children.Add(ScorecardSummaryCard(extras, "Extras", (draft.Wides + draft.NoBalls + draft.Byes + draft.LegByes).ToString()));
        _scorecardRows.Children.Add(summary);
    }

    private void AddInlineFallOfWickets(CricketScorecardDraft draft)
    {
        var region = new StackPanel { Tag = "scorecard-inline-fow", Margin = new Thickness(4, 12, 4, 0) };
        var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(WorkflowText("Fall of wickets", 17));
        var add = WorkflowButton("+ Add", () =>
        {
            draft.FallOfWickets.Add(""); _scorecardDirty = true; RenderScorecard();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (!ReferenceEquals(draft, _scorecardDraft)) return;
                _scorecardScroll?.ScrollToEnd();
                var inlineRegion = _scorecardRows.Children.OfType<StackPanel>().FirstOrDefault(panel => Equals(panel.Tag, "scorecard-inline-fow"));
                var field = inlineRegion?.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<TextBox>()).LastOrDefault(); field?.Focus();
            }));
        }, "scorecard-fow-add");
        add.IsEnabled = draft.FallOfWickets.Count < draft.TotalWickets; add.ToolTip = add.IsEnabled ? "Add a fall-of-wicket entry" : "Mark a wicket in the scorecard before adding another entry";
        Grid.SetColumn(add, 1); heading.Children.Add(add); region.Children.Add(heading);
        if (draft.FallOfWickets.Count == 0) region.Children.Add(WorkflowText(draft.TotalWickets == 0 ? "No wickets recorded." : "Add wicket scores and overs here.", 13));
        for (int index = 0; index < draft.FallOfWickets.Count; index++)
        {
            int i = index; var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(66) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
            var number = WorkflowText((i + 1).ToString(), 15); number.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(number);
            var entry = new TextBox { Tag = "scorecard-fow-entry", Text = draft.FallOfWickets[i], MaxLength = 120, FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent, BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 8, 7, 8), MinHeight = 40, ToolTip = "Wicket score and over, for example 1–24 (4.2)" };
            entry.Uid = "scorecard-fow-entry-" + i;
            entry.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
            entry.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlCentre.BorderBrush");
            System.Windows.Automation.AutomationProperties.SetName(entry, "Fall of wicket " + (i + 1) + ": score and over");
            entry.GotKeyboardFocus += (_, _) => _scorecardFowEditing = true;
            entry.LostKeyboardFocus += (_, _) => { _scorecardFowEditing = false; if (ReferenceEquals(draft, _scorecardDraft)) UpdateScorecardStatus(); };
            entry.TextChanged += (_, _) =>
            {
                if (!ReferenceEquals(draft, _scorecardDraft)) return;
                draft.FallOfWickets[i] = entry.Text.Trim();
                if (string.IsNullOrWhiteSpace(entry.Text)) entry.BorderBrush = ScorecardAlertBrush(true);
                else entry.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlCentre.BorderBrush");
                _scorecardDirty = true; UpdateScorecardStatus();
            };
            Grid.SetColumn(entry, 1); row.Children.Add(entry);
            var apply = WorkflowButton("Apply", async () => { if (ReferenceEquals(draft, _scorecardDraft)) await CommitScorecardEditAsync(draft); }, "scorecard-fow-apply");
            apply.MinWidth = 0; apply.FontSize = 12; apply.Padding = new Thickness(3); Grid.SetColumn(apply, 2); row.Children.Add(apply);
            var remove = WorkflowButton("×", async () =>
            {
                if (!ReferenceEquals(draft, _scorecardDraft) || _match is null) return;
                string previous = draft.FallOfWickets[i]; bool wasPending = _scorecardDirty;
                var savedNotes = _match.CreateScorecardDraft(draft.InningsNumber).FallOfWickets;
                draft.FallOfWickets.RemoveAt(i); _scorecardDirty = true;
                if (i >= savedNotes.Count || string.IsNullOrWhiteSpace(previous) || draft.FallOfWickets.SequenceEqual(savedNotes)) { _scorecardDirty = !draft.FallOfWickets.SequenceEqual(savedNotes); RenderScorecard(); return; }
                if (await CommitScorecardEditAsync(draft)) return;
                if (ReferenceEquals(draft, _scorecardDraft)) { draft.FallOfWickets.Insert(i, previous); _scorecardDirty = wasPending; RenderScorecard(); }
            }, "scorecard-fow-remove");
            remove.ToolTip = "Remove fall-of-wicket entry " + (i + 1); remove.MinWidth = 0; remove.Padding = new Thickness(0); remove.FontSize = 21; remove.Background = Brushes.Transparent; remove.BorderThickness = new Thickness(0);
            remove.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.TextBrush");
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remove fall-of-wicket entry " + (i + 1));
            Grid.SetColumn(remove, 3); row.Children.Add(remove); region.Children.Add(row);
        }
        if (_match is { } match && (draft.FallOfWickets.Count > 0 || match.AllInnings[draft.InningsNumber - 1].ScorecardChanges.Count > 0 || _scorecardDirty))
        {
            var recovery = WorkflowButton("Review manual adjustments", async () => await OpenScorecardRecoveryAsync(), "scorecard-recovery");
            recovery.Background = Brushes.Transparent; recovery.BorderThickness = new Thickness(0); recovery.FontSize = 12; region.Children.Add(recovery);
            recovery.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ControlCentre.AccentBrush");
        }
        _scorecardRows.Children.Add(region);
        _scorecardRows.Children.Add(new Border { Tag = "scorecard-bottom", Height = 1 });
    }
}
