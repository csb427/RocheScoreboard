using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.IO;
using System.Reflection;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;
using Button = System.Windows.Controls.Button;

namespace Roche_Scoreboard.Services;

/// <summary>Runs on the WPF dispatcher; deliberately separate from synchronous domain checks.</summary>
internal static class CricketWorkflowUiChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync(string? outputDirectory = null)
    {
        var checks = new Dictionary<string, bool>();
        var match = CricketReliabilityChecks.CreateMatch(true);
        var panel = new CricketControlPanel(); panel.SetMatch(match);
        var window = new Window { Content = panel, Width = 1100, Height = 800, ShowInTaskbar = false };
        async Task Pump()
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Background);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 1500 && panel.CricketRoot.Children.OfType<Border>().Any(sheet => Equals(sheet.Tag, "cricket-decision-sheet") && sheet.Child is UIElement { IsEnabled: false }))
            {
                await Task.Delay(12);
                await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Background);
            }
            await Task.Yield();
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Background);
        }
        IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        Button? Tagged(string key) => Descendants<Button>(panel).FirstOrDefault(b => Equals(b.Tag, key));
        Border? Sheet() => Descendants<Border>(panel).FirstOrDefault(b => Equals(b.Tag, "cricket-decision-sheet"));
        CricketScorecardDraft? Draft() => typeof(CricketControlPanel).GetField("_scorecardDraft", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(panel) as CricketScorecardDraft;
        void Click(Button? button) { if (button?.IsEnabled == true) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        async Task<bool> WaitFor(Func<bool> predicate)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 3500)
            {
                await Pump();
                if (predicate()) return true;
                await Task.Delay(12);
            }
            return predicate();
        }
        bool HasSheetTitle(string title) => Sheet() is { } sheet && Descendants<System.Windows.Controls.TextBlock>(sheet).Any(text => text.Text == title);
        async Task Capture(string name)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) return;
            await Task.Delay(280); await Pump();
            Directory.CreateDirectory(outputDirectory);
            int width = Math.Max(1, (int)Math.Ceiling(panel.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(panel.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outputDirectory, name + ".png")); encoder.Save(stream);
        }
        try
        {
            window.Show(); await Pump();
            checks["cricket-tabs-include-editable-scorecard-before-output"] = panel.MainTabControl.Items.IndexOf(panel.ScorecardTab) == 1 && panel.MainTabControl.Items.IndexOf(panel.OutputTab) == 2;
            panel.MainTabControl.SelectedItem = panel.ScorecardTab; await Pump();
            checks["scorecard-uses-immediate-edits-without-top-save-or-more"] = Tagged("save-scorecard") is null && Tagged("scorecard-more") is null && !Descendants<System.Windows.Controls.TextBox>(panel.ScorecardHost).Any();
            await Capture("cricket-scorecard-wide");
            var rowsScroll = Descendants<ScrollViewer>(panel.ScorecardHost).FirstOrDefault(s => Equals(s.Tag, "scorecard-rows-scroll"));
            double navigationY = Tagged("scorecard-innings-1")?.TranslatePoint(new System.Windows.Point(), panel).Y ?? -1;
            rowsScroll?.ScrollToEnd(); await Pump();
            checks["scorecard-wide-keeps-innings-navigation-fixed-above-table"] = rowsScroll is { ScrollableHeight: > 0 } && navigationY >= 0 && Math.Abs((Tagged("scorecard-innings-1")?.TranslatePoint(new System.Windows.Point(), panel).Y ?? -5) - navigationY) < .5;
            rowsScroll?.ScrollToHome(); await Pump();
            await panel.BeginPlayerFiguresEdit(false, 3); await Pump();
            checks["player-edit-figures-focuses-the-requested-row"] = panel.MainTabControl.SelectedItem == panel.ScorecardTab && Descendants<Grid>(panel.ScorecardHost).Any(row => Equals(row.Tag, "scorecard-player-batting-3") && row.IsKeyboardFocusWithin);
            Click(Descendants<Button>(panel.ScorecardHost).FirstOrDefault(b => Equals(b.ToolTip, "Edit Runs"))); await Pump();
            var input = Sheet() is { } numericSheet ? Descendants<System.Windows.Controls.TextBox>(numericSheet).FirstOrDefault() : null;
            if (input is not null) input.Text = "99";
            Click(Tagged("apply")); await WaitFor(() => Tagged("go-back") is not null && Tagged("continue-anyway") is not null);
            checks["immediate-numeric-edit-warns-before-applying-inconsistent-total"] = match.TotalRuns == 0 && Tagged("go-back") is not null && Tagged("continue-anyway") is not null;
            checks["scorecard-reconciliation-warning-prioritises-go-back"] = Tagged("go-back") is { } goBack && Tagged("continue-anyway") is { } continueAnyway && goBack.MinHeight > continueAnyway.MinHeight;
            await Capture("cricket-scorecard-reconciliation-warning"); Click(Tagged("go-back"));
            await WaitFor(() => HasSheetTitle("Runs") && Sheet() is { } returned && Descendants<System.Windows.Controls.TextBox>(returned).Any(field => field.Text == "99"));
            input = Sheet() is { } returnedNumeric ? Descendants<System.Windows.Controls.TextBox>(returnedNumeric).FirstOrDefault() : null;
            checks["reconciliation-go-back-keeps-entered-value-for-correction"] = input?.Text == "99" && match.TotalRuns == 0;
            Click(Tagged("apply")); await WaitFor(() => Tagged("continue-anyway") is not null);
            Click(Tagged("continue-anyway")); await WaitFor(() => match.TotalRuns == 99 && Sheet() is null);
            checks["explicit-continue-applies-exact-value-without-invented-player-runs"] = match.TotalRuns == 99 && match.CurrentInnings!.BattingOrder.Sum(player => player.Runs) == 0 && !string.IsNullOrWhiteSpace(match.CurrentInnings.ManualScorecardWarning) && Sheet() is null;
            var recovered = new CricketMatchManager(); recovered.LoadState(match.ToState());
            checks["accepted-reconciliation-warning-persists-with-manual-figures"] = recovered.TotalRuns == 99 && !string.IsNullOrWhiteSpace(recovered.CurrentInnings!.ManualScorecardWarning);
            Click(Tagged("scorecard-recovery")); await WaitFor(() => Tagged("revert") is not null);
            Click(Tagged("revert")); await WaitFor(() => Tagged("yes") is not null);
            Click(Tagged("yes")); await WaitFor(() => match.TotalRuns == 0 && Sheet() is null);
            checks["contextual-recovery-reverts-accepted-mismatch-without-deleting-history"] = match.TotalRuns == 0 && string.IsNullOrWhiteSpace(match.CurrentInnings!.ManualScorecardWarning);
            var structural = match.CreateScorecardDraft(1); structural.TotalWickets = 1;
            checks["continue-anyway-cannot-bypass-wicket-invariants"] = !match.TryApplyScorecard(structural, true, out _) && match.TotalWickets == 0;
            structural = match.CreateScorecardDraft(1); structural.TotalRuns = -1;
            checks["continue-anyway-cannot-bypass-negative-values"] = !match.TryApplyScorecard(structural, true, out _) && match.TotalRuns == 0;
            structural = match.CreateScorecardDraft(1); structural.Revision--;
            checks["continue-anyway-cannot-bypass-stale-context"] = !match.TryApplyScorecard(structural, true, out _) && match.TotalRuns == 0;
            match.RecordDelivery(0, CricketDeliveryType.Dot); await WaitFor(() => Draft()?.DeliveryCount == 1);
            checks["clean-scorecard-refreshes-after-normal-delivery-without-revision-change"] = Draft() is { } freshDraft && freshDraft.DeliveryCount == 1 && freshDraft.MatchDeliveryCount == 1 && freshDraft.LegalBallsBowled == 1 && freshDraft.Batters.Sum(p => p.BallsFaced) == 1 && match.ValidateScorecard(freshDraft).Count == 0;
            Click(Tagged("scorecard-dismissal")); await WaitFor(() => Tagged("Caught") is not null); Click(Tagged("Caught")); await Pump();
            var fielders = Sheet() is { } dismissalSheet ? Descendants<System.Windows.Controls.ComboBox>(dismissalSheet).ToArray() : [];
            if (fielders.Length == 2) { fielders[0].SelectedIndex = 1; fielders[1].SelectedIndex = 2; }
            await Capture("cricket-scorecard-structured-dismissal"); Click(Tagged("apply")); await WaitFor(() => match.TotalWickets == 1 && Sheet() is null);
            checks["dismissal-apply-immediately-commits-reconciled-wicket-and-credit"] = match.TotalWickets == 1 && match.CurrentInnings!.BattingOrder[0].IsOut && match.CurrentInnings.BowlingAttack[0].Wickets == 1 && match.CurrentInnings.BattingOrder[0].HowOut == DismissalType.Caught && match.CurrentInnings.BattingOrder[0].DismissalFielder == match.CurrentInnings.BowlingAttack[1].DisplayName && Sheet() is null;
            checks["balanced-immediate-apply-announces-success-without-a-modal"] = Sheet() is null && Descendants<System.Windows.Controls.TextBlock>(panel.ScorecardHost).Any(text => text.Text == "Change applied. Click another figure to edit." && System.Windows.Automation.AutomationProperties.GetLiveSetting(text) == System.Windows.Automation.AutomationLiveSetting.Polite);
            Click(Tagged("scorecard-fow-add")); await WaitFor(() => Descendants<System.Windows.Controls.TextBox>(panel.ScorecardHost).Any(field => Equals(field.Tag, "scorecard-fow-entry")));
            var inlineWicket = Descendants<System.Windows.Controls.TextBox>(panel.ScorecardHost).FirstOrDefault(field => Equals(field.Tag, "scorecard-fow-entry"));
            if (inlineWicket is not null) inlineWicket.Text = "1–0 (0.1)";
            checks["fall-of-wickets-edit-stays-inline-before-row-apply"] = Sheet() is null && inlineWicket is not null && Draft()?.FallOfWickets.SequenceEqual(new[] { "1–0 (0.1)" }) == true;
            Click(Tagged("scorecard-fow-apply")); await WaitFor(() => match.CurrentInnings!.ManualFallOfWickets?.SequenceEqual(new[] { "1–0 (0.1)" }) == true && Sheet() is null);
            checks["fall-of-wickets-row-apply-immediately-persists-notes"] = match.CurrentInnings!.ManualFallOfWickets?.SequenceEqual(new[] { "1–0 (0.1)" }) == true && Sheet() is null;
            rowsScroll?.ScrollToEnd(); await Pump();
            var scorecardBottom = Descendants<Border>(panel.ScorecardHost).FirstOrDefault(item => Equals(item.Tag, "scorecard-bottom"));
            double bottomEdge = scorecardBottom is null || rowsScroll is null ? -1 : scorecardBottom.TranslatePoint(new System.Windows.Point(), rowsScroll).Y + scorecardBottom.ActualHeight;
            checks["scorecard-inline-bottom-is-reachable-without-dead-scroll-gap"] = rowsScroll is not null && bottomEdge >= 0 && bottomEdge <= rowsScroll.ActualHeight + 1 && rowsScroll.ActualHeight - bottomEdge < 16;
            await Capture("cricket-scorecard-inline-fow");
            await Capture("cricket-scorecard-saved-dismissal"); Click(Tagged("scorecard-team-bowling")); await Pump(); await Capture("cricket-scorecard-bowling-wide"); Click(Tagged("scorecard-team-batting")); await Pump();
            for (int adjustment = 0; adjustment < 2; adjustment++)
            {
                int savedAdjustments = match.CurrentInnings!.ScorecardChanges.Count;
                Click(Tagged("scorecard-recovery")); await WaitFor(() => Tagged("revert") is not null);
                Click(Tagged("revert")); await WaitFor(() => Tagged("yes") is not null);
                Click(Tagged("yes")); await WaitFor(() => match.CurrentInnings!.ScorecardChanges.Count == savedAdjustments - 1 && Sheet() is null);
            }
            checks["scorecard-contextual-recovery-restores-original-wicket-state"] = match.TotalWickets == 0 && !match.CurrentInnings!.BattingOrder[0].IsOut && match.CurrentInnings.BowlingAttack[0].Wickets == 0;
            panel.MainTabControl.SelectedItem = panel.MatchTab;
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); await Capture("cricket-match-options"); Click(Tagged("break")); await WaitFor(() => HasSheetTitle("Break in play")); await Capture("cricket-break-options"); Click(Tagged("Rain")); await WaitFor(() => match.BreakKind == CricketBreakKind.Rain && Sheet() is null);
            checks["match-options-rain-break-locks-scoring-with-clear-status"] = match.BreakKind == CricketBreakKind.Rain && !panel.ScoringCommands.IsEnabled && panel.WorkflowStatus.Text == "RAIN";
            await Capture("cricket-rain-break");
            Click(panel.ScoringLockBanner); await Pump();
            checks["match-options-resume-restores-scoring"] = match.BreakKind == CricketBreakKind.None && panel.ScoringCommands.IsEnabled;
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options"));
            checks["match-options-keeps-scorecard-navigation-out-of-actions"] = Tagged("scorecard") is null && Tagged("conditions") is not null && Tagged("rain") is not null;
            Click(Tagged("break")); await WaitFor(() => HasSheetTitle("Break in play")); Click(Tagged("back")); await Pump();
            checks["child-break-back-arrow-returns-to-match-options"] = Sheet() is not null && Tagged("teams") is not null && Tagged("close") is not null;
            Click(Tagged("conditions")); await WaitFor(() => HasSheetTitle("Format and over limit"));
            var conditionsOvers = Sheet() is { } conditionsSheet ? Descendants<System.Windows.Controls.TextBox>(conditionsSheet).FirstOrDefault(field => Equals(field.Tag, "conditions-overs")) : null;
            if (conditionsOvers is not null) conditionsOvers.Text = "25";
            await Capture("cricket-match-conditions"); Click(Tagged("apply")); await Pump();
            checks["match-conditions-applies-over-limit-without-changing-score"] = match.TotalOvers == 25 && match.TotalRuns == 0 && match.TotalWickets == 0 && Sheet() is null;
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); Click(Tagged("conditions")); await WaitFor(() => HasSheetTitle("Format and over limit"));
            conditionsOvers = Sheet() is { } staleConditions ? Descendants<System.Windows.Controls.TextBox>(staleConditions).FirstOrDefault(field => Equals(field.Tag, "conditions-overs")) : null;
            if (conditionsOvers is not null) conditionsOvers.Text = "40";
            match.RecordDelivery(0, CricketDeliveryType.Dot); await Pump(); Click(Tagged("apply")); await Pump();
            checks["match-conditions-rejects-stale-open-editor"] = match.TotalOvers == 25 && Sheet() is not null && Descendants<System.Windows.Controls.TextBlock>(Sheet()!).Any(text => text.Text.Contains("match changed", StringComparison.OrdinalIgnoreCase));
            Click(Tagged("close")); await Pump();
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); Click(Tagged("rain")); await WaitFor(() => HasSheetTitle("Revised overs / target"));
            var rainFields = Sheet() is { } rainSheet ? Descendants<System.Windows.Controls.TextBox>(rainSheet).ToArray() : [];
            checks["first-innings-rain-editor-shows-overs-without-chase-target"] = rainFields.Any(field => Equals(field.Tag, "conditions-overs")) && !rainFields.Any(field => Equals(field.Tag, "conditions-target"));
            var revisedOvers = rainFields.FirstOrDefault(field => Equals(field.Tag, "conditions-overs")); if (revisedOvers is not null) revisedOvers.Text = "20";
            await Capture("cricket-first-innings-rain-revision"); Click(Tagged("apply")); await Pump();
            checks["rain-revision-applies-first-innings-limit-only"] = match.HasRainAdjustment && match.CurrentOverLimit == 20 && match.TotalOvers == 25 && match.CurrentInnings!.TargetOverride is null;
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); Click(Tagged("rain")); await WaitFor(() => HasSheetTitle("Revised overs / target")); Click(Tagged("clear")); await WaitFor(() => Tagged("yes") is not null); Click(Tagged("yes")); await WaitFor(() => !match.HasRainAdjustment && Sheet() is null);
            checks["rain-revision-restore-keeps-score-and-original-limit"] = !match.HasRainAdjustment && match.CurrentOverLimit == 25 && match.TotalRuns == 0;
            Click(panel.EndInningsButton); await Pump();
            checks["early-limited-innings-uses-custom-decision-sheet"] = Sheet() is not null && Tagged("finish") is null;
            bool teamABattedFirst = match.CurrentInnings!.BattingTeamName == match.TeamAName;
            checks["first-innings-menu-only-offers-opponents-first-innings"] = Tagged(teamABattedFirst ? "a" : "b") is null && Tagged(teamABattedFirst ? "b" : "a") is not null;
            checks["innings-options-keeps-scorecard-navigation-out-of-actions"] = Tagged("scorecard") is null;
            checks["first-innings-menu-prioritises-opponents-innings-action"] = Sheet() is { } inningsSheet && Descendants<Button>(inningsSheet).FirstOrDefault(b => !Equals(b.Tag, "back"))?.Tag is string primaryAction && primaryAction == (teamABattedFirst ? "b" : "a");
            await Capture("cricket-early-innings-warning");
            Click(Tagged("back")); await Pump();
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); Click(Tagged("teams")); await WaitFor(() => HasSheetTitle("Edit teams"));
            var teamFields = Sheet() is { } homeSheet ? Descendants<System.Windows.Controls.TextBox>(homeSheet).ToArray() : [];
            checks["team-editor-uses-wk-and-drag-preview-without-manual-arrow-buttons"] = Tagged("lineup-keeper")?.Content is string keeperCaption && keeperCaption == "WK"
                && Sheet() is { } teamEditorSheet && Descendants<Border>(teamEditorSheet).Any(border => Equals(border.Tag, "lineup-insertion-preview"))
                && !Descendants<Button>(teamEditorSheet).Any(button => button.Content is string caption && caption is "↑" or "↓");
            Click(Tagged("lineup-keeper")); await Pump();
            checks["team-editor-wk-selection-has-clear-accessible-state"] = Tagged("lineup-keeper") is { } keeperButton && System.Windows.Automation.AutomationProperties.GetItemStatus(keeperButton) == "Selected";
            teamFields = Sheet() is { } changedRoles ? Descendants<System.Windows.Controls.TextBox>(changedRoles).ToArray() : [];
            string originalHomeName = match.TeamAName, originalAwayName = match.TeamBName;
            if (teamFields.Length >= 2) { teamFields[0].Text = "QA Home Club"; teamFields[1].Text = "QAH"; }
            Click(Tagged("edit-team-away")); await Pump();
            teamFields = Sheet() is { } awaySheet ? Descendants<System.Windows.Controls.TextBox>(awaySheet).ToArray() : [];
            if (teamFields.Length >= 2) { teamFields[0].Text = "QA Away Club"; teamFields[1].Text = "QAA"; }
            Click(Tagged("edit-team-home")); await Pump();
            teamFields = Sheet() is { } retainedHome ? Descendants<System.Windows.Controls.TextBox>(retainedHome).ToArray() : [];
            checks["combined-team-editor-retains-both-tab-drafts-before-save"] = teamFields.Length >= 2 && teamFields[0].Text == "QA Home Club" && teamFields[1].Text == "QAH" && match.TeamAName == originalHomeName && match.TeamBName == originalAwayName;
            await Capture("cricket-lineup-editor"); Click(Tagged("edit-team-away")); await Pump();
            var rosterScroll = Sheet() is {} lineupSheet ? Descendants<ScrollViewer>(lineupSheet).FirstOrDefault(viewport => Equals(viewport.Tag, "cricket-prompt-body")) : null;
            var saveTeams = Tagged("save"); double pinnedSaveY = saveTeams?.TranslatePoint(new System.Windows.Point(),panel).Y ?? -1;
            rosterScroll?.ScrollToEnd(); await Pump();
            checks["team-editor-keeps-save-visible-while-roster-scrolls"] = rosterScroll is {ScrollableHeight:>0} && saveTeams is not null && pinnedSaveY>=0 && pinnedSaveY+saveTeams.ActualHeight<=panel.ActualHeight
                && Math.Abs(saveTeams.TranslatePoint(new System.Windows.Point(),panel).Y-pinnedSaveY)<.5;
            window.Width=420;window.Height=320;await Pump();
            double compactSaveY=saveTeams?.TranslatePoint(new System.Windows.Point(),panel).Y??-1;
            checks["small-team-editor-keeps-useful-form-viewport-and-save-reachable"] = rosterScroll is {ActualHeight:>=70} && saveTeams is not null && compactSaveY>=0 && compactSaveY+saveTeams.ActualHeight<=panel.ActualHeight;
            await Capture("cricket-lineup-editor-420x320");window.Width=1100;window.Height=800;await Pump();
            rosterScroll?.ScrollToHome(); await Pump();
            teamFields = Sheet() is { } retainedAway ? Descendants<System.Windows.Controls.TextBox>(retainedAway).ToArray() : [];
            checks["combined-team-editor-retains-away-draft-after-tab-roundtrip"] = teamFields.Length >= 2 && teamFields[0].Text == "QA Away Club" && teamFields[1].Text == "QAA";
            await Capture("cricket-lineup-editor-away");
            int teamNotifications = 0; void TeamChanged() => teamNotifications++;
            match.MatchChanged += TeamChanged; Click(Tagged("save")); await WaitFor(() => match.TeamAName == "QA Home Club" && match.TeamBName == "QA Away Club" && Sheet() is null); match.MatchChanged -= TeamChanged;
            checks["combined-team-editor-saves-both-teams-atomically"] = match.TeamAName == "QA Home Club" && match.TeamAAbbr == "QAH" && match.TeamBName == "QA Away Club" && match.TeamBAbbr == "QAA" && teamNotifications == 1 && Sheet() is null;
            Click(Descendants<Button>(panel.StrikerBar).FirstOrDefault(b => Equals(b.Content, "⋯"))); await Pump();
            await Capture("cricket-batter-actions");
            var actorMenu = typeof(CricketControlPanel).GetField("_playerMenu",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(panel) as System.Windows.Controls.Primitives.Popup;
            Click(actorMenu?.Child is {} menu ? Descendants<Button>(menu).FirstOrDefault(b=>Equals(b.Tag,"replace-batter")) : null); await Pump();
            checks["context-replacement-opens-directly-in-player-selection"] = Sheet() is not null && Tagged("RetiredHurt") is null && Descendants<System.Windows.Controls.TextBlock>(panel).Any(t=>t.Text=="Select replacement");
            await Capture("cricket-replacement-selection"); Click(Tagged("back")); await Pump();
            match.RecordDelivery(1, CricketDeliveryType.Runs); match.RecordDelivery(2, CricketDeliveryType.Runs); await Pump();
            var ballTexts = Descendants<System.Windows.Controls.TextBlock>(panel.EventLog).Select(t => t.Text).ToArray();
            checks["delivery-log-shows-newest-ball-before-first-ball"] = Array.IndexOf(ballTexts, "0.2") < Array.IndexOf(ballTexts, "0.1") && Array.IndexOf(ballTexts, "0.2") >= 0;
            window.Width = 420; window.Height = 520; await Pump();
            // The stacked log follows the commands. Scroll to the command's own
            // position rather than past it to the very end of the entire log.
            panel.CricketMatchPageViewport.ScrollToVerticalOffset(panel.CricketMatchPageViewport.VerticalOffset
                + panel.MatchOptionsButton.TranslatePoint(new System.Windows.Point(),panel.CricketMatchPageViewport).Y - 8); await Pump();
            var dock = (Border)panel.MainTabControl.Template.FindName("PART_OperatorDock", panel.MainTabControl);
            var dockBounds = dock.TransformToAncestor(panel).TransformBounds(new Rect(dock.RenderSize));
            var optionsBounds = panel.MatchOptionsButton.TransformToAncestor(panel).TransformBounds(new Rect(panel.MatchOptionsButton.RenderSize));
            checks["cricket-console-scrolls-match-with-visible-navigation"] = panel.ConsoleViewport.ScrollableWidth < 1
                && panel.CricketMatchPageViewport.ScrollableHeight > 0 && dockBounds.Bottom <= panel.ActualHeight + 1
                && optionsBounds.Top >= 0 && optionsBounds.Bottom <= dockBounds.Top + 1;
            if(!checks["cricket-console-scrolls-match-with-visible-navigation"] && outputDirectory is not null)
                File.WriteAllText(Path.Combine(outputDirectory,"compact-console-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(new {outerWidth=panel.ConsoleViewport.ScrollableWidth,scrollHeight=panel.CricketMatchPageViewport.ScrollableHeight,offset=panel.CricketMatchPageViewport.VerticalOffset,dockTop=dockBounds.Top,dockBottom=dockBounds.Bottom,optionsTop=optionsBounds.Top,optionsBottom=optionsBounds.Bottom,panelHeight=panel.ActualHeight}));
            panel.MainTabControl.SelectedItem = panel.ScorecardTab; await Pump();
            rowsScroll = Descendants<ScrollViewer>(panel.ScorecardHost).FirstOrDefault(s => Equals(s.Tag, "scorecard-rows-scroll"));
            navigationY = Tagged("scorecard-innings-1")?.TranslatePoint(new System.Windows.Point(), panel).Y ?? -1;
            rowsScroll?.ScrollToEnd(); await Pump();
            double navigationAfter = Tagged("scorecard-innings-1")?.TranslatePoint(new System.Windows.Point(), panel).Y ?? -5;
            checks["scorecard-narrow-keeps-innings-navigation-fixed-and-visible"] = rowsScroll is { ScrollableHeight: > 0 } && Math.Abs(navigationAfter - navigationY) < .5 && navigationAfter >= 0 && navigationAfter < panel.ActualHeight;
            rowsScroll?.ScrollToHome(); await Pump(); await Capture("cricket-scorecard-narrow");
            panel.MainTabControl.SelectedItem = panel.MatchTab; await Pump();
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); match.EndCurrentInnings(); await Pump();
            checks["remote-innings-completion-waits-for-current-workflow"] = Descendants<System.Windows.Controls.TextBlock>(panel).Any(t => t.Text == "Match options") && Tagged("continue") is null;
            Click(Tagged("close")); await WaitFor(() => HasSheetTitle(match.CompletionReason));
            checks["remote-innings-completion-is-presented-after-workflow-closes"] = Tagged(teamABattedFirst ? "b" : "a") is not null && Tagged("continue") is null && Tagged("finish") is null && Descendants<System.Windows.Controls.TextBlock>(panel).Any(t => t.Text == match.CompletionReason);
            await Capture("cricket-queued-innings-completion"); Click(Tagged("back")); await Pump();
            bool startedChase = match.TryStartAdditionalInnings(!teamABattedFirst, out _); await Pump();
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); Click(Tagged("rain")); await WaitFor(() => HasSheetTitle("Revised overs / target"));
            var chaseFields = Sheet() is { } chaseSheet ? Descendants<System.Windows.Controls.TextBox>(chaseSheet).ToArray() : [];
            var chaseOvers = chaseFields.FirstOrDefault(field => Equals(field.Tag, "conditions-overs")); var chaseTarget = chaseFields.FirstOrDefault(field => Equals(field.Tag, "conditions-target"));
            checks["chase-rain-editor-shows-explicit-revised-target"] = startedChase && chaseOvers is not null && chaseTarget is not null;
            if (chaseOvers is not null) chaseOvers.Text = "15"; if (chaseTarget is not null) chaseTarget.Text = "12";
            await Capture("cricket-chase-rain-revision"); Click(Tagged("apply")); await Pump();
            checks["chase-rain-revision-applies-external-target-without-fabricated-runs"] = match.HasRainAdjustment && match.CurrentOverLimit == 15 && match.Target == 12 && match.TotalRuns == 0;
            Click(panel.MatchOptionsButton); await WaitFor(() => HasSheetTitle("Match options")); panel.ClearMatch(); await Pump();
            checks["reset-closes-custom-prompts-and-releases-operator-surface"] = !Descendants<Border>(panel).Any(b => Equals(b.Tag, "cricket-decision-sheet")) && panel.ConsoleViewport.IsEnabled;
        }
        finally { panel.ClearMatch(); window.Close(); }
        return checks;
    }
}
