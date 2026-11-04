// Generated from Views/MatchHistoryWindow.cs; edit translator/adapters.
// Source SHA-256: abbc54ea69669d59628f63b45d73054df06956bb2f1d63a7bbdd939fbf22c596
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using Roche_Scoreboard.Services;
using Button = Avalonia.Controls.Button;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using UserControl = Avalonia.Controls.UserControl;
using FontFamily = Avalonia.Media.FontFamily;
using Brushes = Avalonia.Media.Brushes;

namespace Roche_Scoreboard.Views;

/// <summary>In-app local checkpoint browser. Review is read-only and never restores state.</summary>
internal sealed class MatchHistoryView : UserControl
{
    internal enum ActionKind { Review, Continue, Restart }
    internal event Func<MatchCheckpointStore.Checkpoint, ActionKind, bool>? ActionChosen;
    internal event Action? CloseRequested;
    private readonly List<Button> _actionButtons = [];
    internal IReadOnlyList<Button> ActionButtons => _actionButtons;
    internal bool HasEmptyState => _emptyState?.IsVisible == true;
    internal bool IsLoading { get; private set; }
    internal bool IsReviewing => _reviewHost.IsVisible == true;
    private readonly StackPanel _list = new();
    private readonly Grid _body = new();
    private readonly Grid _reviewHost = new();
    private TextBlock? _emptyState;
    private readonly bool _canReplaceSession;
    private readonly Button _closeButton;

    internal MatchHistoryView(bool canReplaceSession, IReadOnlyList<MatchCheckpointStore.Checkpoint>? checkpoints = null)
    {
        _canReplaceSession = canReplaceSession;
        Focusable = true;
        FontFamily = NativeFonts.Family("Segoe UI Variable, Segoe UI");
        Background = Brushes.Transparent;
        this.SetResourceReference(ForegroundProperty, "ControlCentre.TextBrush");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(4, 0, 4, 16) };
        heading.Children.Add(new NativeTextBlock { Text = "Match history", FontSize = 26, FontWeight = FontWeight.SemiBold });
        var subtitle = new NativeTextBlock
        {
            Text = canReplaceSession ? "Review a checkpoint, continue it, or restart with the same teams and a fresh score." : "Review saved checkpoints. Return to the current session before continuing or restarting one.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        heading.Children.Add(subtitle);
        root.Children.Add(heading);

        _body.Children.Add(new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        _reviewHost.IsVisible = false;
        Grid.SetRow(_body, 1); Grid.SetRow(_reviewHost, 1);
        root.Children.Add(_body); root.Children.Add(_reviewHost);
        _closeButton = MakeButton("Close", () => CloseRequested?.Invoke());
        _closeButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; _closeButton.Margin = new Thickness(0, 14, 0, 0);
        _closeButton.IsDefault = true;
        Grid.SetRow(_closeButton, 2); root.Children.Add(_closeButton);
        Content = root;
        if (checkpoints is null) SetLoading(); else SetCheckpoints(checkpoints);
    }

    private void SetLoading()
    {
        IsLoading = true;
        _list.Children.Clear();
        var loading = new NativeTextBlock { Text = "Loading saved matches…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 8) };
        loading.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
        _list.Children.Add(loading);
    }

    internal void SetCheckpoints(IReadOnlyList<MatchCheckpointStore.Checkpoint> checkpoints)
    {
        IsLoading = false;
        _actionButtons.Clear();
        _list.Children.Clear();
        _emptyState = null;
        foreach (var item in checkpoints)
        {
            var card = new NativeBorder
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10)
            };
            card.SetResourceReference(Border.BackgroundProperty, "ControlCentre.SurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "ControlCentre.BorderBrush");
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new NativeTextBlock { Text = item.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            var detail = new NativeTextBlock { Text = item.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 8, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
            info.Children.Add(detail); body.Children.Add(info);
            var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            actions.Children.Add(MakeActionButton("Review", item, ActionKind.Review)); _actionButtons.Add((Button)actions.Children[^1]);
            bool completed = item.State.CricketMatchState?.IsMatchFinalized == true
                || item.State.MatchState is { CompletedQuarterCount: >= 4, QuarterSnapshots: { Length: >= 4 } snapshots } && snapshots[3] is not null;
            var continueButton = MakeActionButton("Continue", item, ActionKind.Continue);
            continueButton.IsEnabled = !completed;
            Avalonia.Controls.ToolTip.SetTip(continueButton, completed ? "Completed matches can be reviewed or restarted with a fresh score"
                : _canReplaceSession ? "Continue from this saved checkpoint" : "Save the current session before continuing from this checkpoint");
            actions.Children.Add(continueButton); _actionButtons.Add(continueButton);
            var restartButton = MakeActionButton("Restart", item, ActionKind.Restart);
            Avalonia.Controls.ToolTip.SetTip(restartButton, _canReplaceSession ? "Start a fresh score with these teams" : "Save the current session before starting with these teams");
            actions.Children.Add(restartButton); _actionButtons.Add(restartButton);
            Grid.SetColumn(actions, 1); body.Children.Add(actions);
            body.SizeChanged += (_, args) =>
            {
                bool narrow = args.NewSize.Width < 680;
                Grid.SetRow(actions, narrow ? 1 : 0);
                Grid.SetColumn(actions, narrow ? 0 : 1);
                Grid.SetColumnSpan(info, narrow ? 2 : 1);
                actions.HorizontalAlignment = narrow ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
                actions.Margin = narrow ? new Thickness(-4, 10, 0, 0) : new Thickness(0);
            };
            card.Child = body; _list.Children.Add(card);
        }

        if (_list.Children.Count == 0)
        {
            _emptyState = new NativeTextBlock { Text = "Saved matches will appear here after a match has been recorded.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 2, 8) };
            _emptyState.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush");
            _list.Children.Add(_emptyState);
        }
    }

    private Button MakeActionButton(string label, MatchCheckpointStore.Checkpoint item, ActionKind action)
        => MakeButton(label, () => Choose(item, action));

    private static Button MakeButton(string label, Action action)
    {
        var button = new NativeButton
        {
            Content = label, MinHeight = 36, MinWidth = 68, Margin = new Thickness(4), Padding = new Thickness(10, 5, 10, 5),
            BorderThickness = new Thickness(1), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        button.SetResourceReference(Control.ThemeProperty, "ControlCentre.Button");
        button.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        button.SetResourceReference(TemplatedControl.BackgroundProperty, "ControlCentre.GlassBrush");
        button.SetResourceReference(TemplatedControl.BorderBrushProperty, "ControlCentre.BorderBrush");
        button.Click += (_, _) => action();
        return button;
    }

    private void Choose(MatchCheckpointStore.Checkpoint item, ActionKind action)
    {
        if (action == ActionKind.Review) { ShowReview(item); return; }
        if (ActionChosen?.Invoke(item, action) == true) CloseRequested?.Invoke();
    }

    private void ShowReview(MatchCheckpointStore.Checkpoint item)
    {
        _body.IsVisible = false;
        _reviewHost.IsVisible = true;
        _reviewHost.Children.Clear();
        var content = new StackPanel { Margin = new Thickness(4, 0, 4, 4) };
        content.Children.Add(new NativeTextBlock { Text = item.Title, FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var details = new NativeTextBlock { Text = item.Detail, Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
        details.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush"); content.Children.Add(details);
        var timestamp = new NativeTextBlock { Text = "Read-only checkpoint · saved " + item.SavedAtUtc.ToLocalTime().ToString("g"), TextWrapping = TextWrapping.Wrap };
        timestamp.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush"); content.Children.Add(timestamp);
        var back = MakeButton("Back to history", () => { _reviewHost.IsVisible = false; _body.IsVisible = true; _closeButton.Focus(); });
        back.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; back.Margin = new Thickness(0, 18, 0, 0); content.Children.Add(back);
        _reviewHost.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        back.Focus();
    }
}
