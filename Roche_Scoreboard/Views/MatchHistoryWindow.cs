#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;

namespace Roche_Scoreboard.Views;

/// <summary>Local checkpoint browser. Review never restores or mutates match state.</summary>
internal sealed class MatchHistoryWindow : Window
{
    internal enum ActionKind { Review, Continue, Restart }
    internal event Action<MatchCheckpointStore.Checkpoint, ActionKind>? ActionChosen;
    private readonly List<Button> _actionButtons = [];
    internal IReadOnlyList<Button> ActionButtons => _actionButtons;

    internal MatchHistoryWindow(bool canReplaceSession, IReadOnlyList<MatchCheckpointStore.Checkpoint>? checkpoints = null)
    {
        Title = "Match history";
        Width = 760; Height = 640; MinWidth = 360; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ControlCentre.GlassBrush");
        SetResourceReference(ForegroundProperty, "ControlCentre.TextBrush");
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            args.Handled = true;
            Close();
        };
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = "Match history", FontSize = 26, FontWeight = FontWeights.SemiBold });
        var subtitle = new TextBlock
        {
            Text = canReplaceSession ? "Review a checkpoint, continue it, or restart with the same teams and a fresh score." : "Review saved checkpoints. Return to the current session before continuing or restarting one.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        heading.Children.Add(subtitle);
        root.Children.Add(heading);

        var list = new StackPanel();
        foreach (var item in checkpoints ?? MatchCheckpointStore.Load())
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10)
            };
            card.SetResourceReference(Border.BackgroundProperty, "ControlCentre.SurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "ControlCentre.BorderBrush");
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = item.Title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var detail = new TextBlock { Text = item.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 8, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
            info.Children.Add(detail);
            body.Children.Add(info);
            var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            actions.Children.Add(MakeButton("Review", () => Choose(item, ActionKind.Review)));
            _actionButtons.Add((Button)actions.Children[^1]);
            var continueButton = MakeButton("Continue", () => Choose(item, ActionKind.Continue));
            bool completed = item.State.CricketMatchState?.IsMatchFinalized == true
                || item.State.MatchState is { CompletedQuarterCount: >= 4, QuarterSnapshots: { Length: >= 4 } snapshots } && snapshots[3] is not null;
            continueButton.IsEnabled = !completed;
            continueButton.ToolTip = completed ? "Completed matches can be reviewed or restarted with a fresh score"
                : canReplaceSession ? "Continue from this saved checkpoint" : "Save the current session before continuing from this checkpoint";
            actions.Children.Add(continueButton);
            _actionButtons.Add(continueButton);
            var restartButton = MakeButton("Restart", () => Choose(item, ActionKind.Restart));
            restartButton.IsEnabled = true;
            restartButton.ToolTip = canReplaceSession ? "Start a fresh score with these teams" : "Save the current session before starting with these teams";
            actions.Children.Add(restartButton);
            _actionButtons.Add(restartButton);
            Grid.SetColumn(actions, 1); body.Children.Add(actions); card.Child = body; list.Children.Add(card);
        }

        if (list.Children.Count == 0)
        {
            var empty = new TextBlock { Text = "Saved matches will appear here after a match has been recorded.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 8) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
            list.Children.Add(empty);
        }
        var scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1); root.Children.Add(scroller);
        var close = MakeButton("Close", Close);
        close.HorizontalAlignment = System.Windows.HorizontalAlignment.Right; close.Margin = new Thickness(0, 14, 0, 0);
        close.IsDefault = true;
        Grid.SetRow(close, 2); root.Children.Add(close);
        Content = root;
    }

    private static Button MakeButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label, MinHeight = 36, MinWidth = 68, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ControlCentre.Button");
        button.SetResourceReference(Control.ForegroundProperty, "ControlCentre.TextBrush");
        button.SetResourceReference(Control.BackgroundProperty, "ControlCentre.GlassBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "ControlCentre.BorderBrush");
        button.Click += (_, _) => action();
        return button;
    }

    private void Choose(MatchCheckpointStore.Checkpoint item, ActionKind action)
    {
        if (action != ActionKind.Review)
        {
            ActionChosen?.Invoke(item, action);
            Close();
            return;
        }
        var dialog = new Window
        {
            Owner = this, Title = "Checkpoint review", Width = 460, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            FontFamily = FontFamily
        };
        foreach (ResourceDictionary dictionary in Resources.MergedDictionaries)
            dialog.Resources.MergedDictionaries.Add(dictionary);
        dialog.SetResourceReference(BackgroundProperty, "ControlCentre.GlassBrush");
        dialog.SetResourceReference(ForegroundProperty, "ControlCentre.TextBrush");
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            args.Handled = true;
            dialog.Close();
        };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = item.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var details = new TextBlock { Text = item.Detail, Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
        details.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        content.Children.Add(details);
        var timestamp = new TextBlock { Text = "Read-only checkpoint · saved " + item.SavedAtUtc.ToLocalTime().ToString("g"), TextWrapping = TextWrapping.Wrap };
        timestamp.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        content.Children.Add(timestamp);
        var done = MakeButton("Done", dialog.Close); done.HorizontalAlignment = System.Windows.HorizontalAlignment.Right; done.Margin = new Thickness(0, 18, 0, 0);
        content.Children.Add(done); dialog.Content = content; dialog.ShowDialog();
    }
}
#endif
