using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using ScrollViewer = System.Windows.Controls.ScrollViewer;

namespace Roche_Scoreboard.Views;

public partial class CricketControlPanel
{
    private PresentationChoiceMotion? _liveScorebugChoice, _battingScorecardChoice, _bowlingFiguresChoice, _matchSummaryChoice, _scorewormChoice, _manhattanChoice;
    // Component diagnostics; production decisions never depend on these values.
    private int _liveCardGeneration => _liveScorebugChoice?.Generation ?? 0;
    private bool _liveCardEpochActive => _liveScorebugChoice?.EpochActive ?? false;

    private void InitializeLiveScorebugCard()
    {
        if (_liveScorebugChoice is not null) return;
        _liveScorebugChoice = new(this, Presentationscorebug, CricketLiveScorebugGlyphView,
            CricketLiveScorebugGlyph.ProgressProperty, () => CricketLiveScorebugGlyphView.Progress,
            value => CricketLiveScorebugGlyphView.Progress = value,
            CricketLiveScorebugGlyphView.SetSelectedInk, CricketLiveScorebugCheck, "scorebug");
        _battingScorecardChoice = new(this, Presentationbatting, CricketBattingGlyphView,
            CricketBattingScorecardGlyph.ProgressProperty, () => CricketBattingGlyphView.Progress,
            value => CricketBattingGlyphView.Progress = value,
            CricketBattingGlyphView.SetSelectedInk, CricketBattingCheck, "batting");
        _bowlingFiguresChoice = new(this, Presentationbowling, CricketBowlingGlyphView,
            CricketBowlingFiguresGlyph.ProgressProperty, () => CricketBowlingGlyphView.Progress,
            value => CricketBowlingGlyphView.Progress = value,
            CricketBowlingGlyphView.SetSelectedInk, CricketBowlingCheck, "bowling");
        _matchSummaryChoice = new(this, Presentationsummary, CricketMatchSummaryGlyphView,
            CricketMatchSummaryGlyph.ProgressProperty, () => CricketMatchSummaryGlyphView.Progress,
            value => CricketMatchSummaryGlyphView.Progress = value,
            CricketMatchSummaryGlyphView.SetSelectedInk, CricketMatchSummaryCheck, "summary");
        _scorewormChoice = new(this, Presentationworm, CricketScorewormGlyphView,
            CricketScorewormGlyph.ProgressProperty, () => CricketScorewormGlyphView.Progress,
            value => CricketScorewormGlyphView.Progress = value,
            CricketScorewormGlyphView.SetSelectedInk, CricketScorewormCheck, "worm");
        _manhattanChoice = new(this, Presentationmanhattan, CricketManhattanGlyphView,
            CricketManhattanGlyph.ProgressProperty, () => CricketManhattanGlyphView.Progress,
            value => CricketManhattanGlyphView.Progress = value,
            CricketManhattanGlyphView.SetSelectedInk, CricketManhattanCheck, "manhattan");
        _liveScorebugChoice.Install();
        _battingScorecardChoice.Install();
        _bowlingFiguresChoice.Install();
        _matchSummaryChoice.Install();
        _scorewormChoice.Install();
        _manhattanChoice.Install();
    }

    private void RefreshLiveScorebugCard(bool animateTransition)
    {
        _liveScorebugChoice?.Refresh(animateTransition);
        _battingScorecardChoice?.Refresh(animateTransition);
        _bowlingFiguresChoice?.Refresh(animateTransition);
        _matchSummaryChoice?.Refresh(animateTransition);
        _scorewormChoice?.Refresh(animateTransition);
        _manhattanChoice?.Refresh(animateTransition);
    }

    private void SettleLiveScorebugCard()
    {
        _liveScorebugChoice?.Settle();
        _battingScorecardChoice?.Settle();
        _bowlingFiguresChoice?.Settle();
        _matchSummaryChoice?.Settle();
        _scorewormChoice?.Settle();
        _manhattanChoice?.Settle();
    }

    // Private ownership mechanics are shared. Each passive glyph defines its
    // own anatomy, phases and content handoff; none own broadcast state.
    private sealed class PresentationChoiceMotion
    {
        private readonly CricketControlPanel _panel;
        private readonly Button _card;
        private readonly FrameworkElement _glyph, _check;
        private readonly DependencyProperty _progressProperty;
        private readonly Func<double> _readProgress;
        private readonly Action<double> _writeProgress;
        private readonly Action<bool> _setInk;
        private readonly string _presentation;
        private readonly List<FrameworkElement> _ancestors = new();
        private Window? _owner;
        private bool? _paintedSelection;
        private bool _animating;
        private int _epoch;
        internal int Generation { get; private set; }
        internal bool EpochActive { get; private set; }
        private bool Selected => _panel._currentPresentation == _presentation;

        internal PresentationChoiceMotion(CricketControlPanel panel, Button card, FrameworkElement glyph,
            DependencyProperty progressProperty, Func<double> readProgress, Action<double> writeProgress,
            Action<bool> setInk, FrameworkElement check, string presentation)
        {
            _panel = panel; _card = card; _glyph = glyph; _progressProperty = progressProperty;
            _readProgress = readProgress; _writeProgress = writeProgress; _setInk = setInk;
            _check = check; _presentation = presentation;
        }

        internal void Install()
        {
            _card.Loaded += Loaded;
            _card.Unloaded += Unloaded;
            _panel.Unloaded += Unloaded;
            Refresh(false);
            if (_card.IsLoaded) Loaded(_card, new RoutedEventArgs());
        }

        private void Loaded(object? sender, RoutedEventArgs args)
        {
            if (EpochActive) return;
            EpochActive = true; ++_epoch;
            OperatorAppearance.Changed += AppearanceChanged;
            SystemParameters.StaticPropertyChanged += SystemPolicyChanged;
            _panel.MainTabControl.SelectionChanged += TabChanged;
            _panel.PreviewKeyDown += KeyboardInput;
            _panel.LayoutUpdated += LayoutUpdated;
            AttachAncestors(); Refresh(false);
        }

        private void Unloaded(object? sender, RoutedEventArgs args) => Retire();
        private void Retire()
        {
            Settle(); ++_epoch;
            if (!EpochActive) return;
            EpochActive = false;
            OperatorAppearance.Changed -= AppearanceChanged;
            SystemParameters.StaticPropertyChanged -= SystemPolicyChanged;
            _panel.MainTabControl.SelectionChanged -= TabChanged;
            _panel.PreviewKeyDown -= KeyboardInput;
            _panel.LayoutUpdated -= LayoutUpdated;
            DetachAncestors();
        }

        private void DetachAncestors()
        {
            foreach (var element in _ancestors)
            {
                element.IsVisibleChanged -= AvailabilityChanged;
                element.IsEnabledChanged -= AvailabilityChanged;
                if (element is ScrollViewer scroll) scroll.ScrollChanged -= Scrolled;
            }
            _ancestors.Clear();
            if (_owner is not null) _owner.Closed -= OwnerClosed;
            _owner = null;
        }

        private void AttachAncestors()
        {
            var owner = Window.GetWindow(_card);
            int index = 0;
            bool same = ReferenceEquals(owner, _owner);
            for (DependencyObject? current = _card; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is FrameworkElement element)
                {
                    if (index >= _ancestors.Count || !ReferenceEquals(element, _ancestors[index])) same = false;
                    ++index;
                }
            if (same && index == _ancestors.Count) return;
            Settle(); ++_epoch; DetachAncestors();
            _owner = owner;
            if (owner is not null) owner.Closed += OwnerClosed;
            for (DependencyObject? current = _card; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is FrameworkElement element)
                {
                    _ancestors.Add(element);
                    element.IsVisibleChanged += AvailabilityChanged;
                    element.IsEnabledChanged += AvailabilityChanged;
                    if (element is ScrollViewer scroll) scroll.ScrollChanged += Scrolled;
                }
        }

        private void OwnerClosed(object? sender, EventArgs args) => Retire();
        private void AvailabilityChanged(object? sender, DependencyPropertyChangedEventArgs args) => Settle();
        private void Scrolled(object? sender, ScrollChangedEventArgs args)
        { if (_animating && !CanAnimate()) Settle(); }
        private void LayoutUpdated(object? sender, EventArgs args)
        { AttachAncestors(); if (_animating && !CanAnimate()) Settle(); }
        private void TabChanged(object? sender, SelectionChangedEventArgs args)
        { if (ReferenceEquals(args.Source, _panel.MainTabControl)) Settle(); }
        private void KeyboardInput(object? sender, System.Windows.Input.KeyEventArgs args) => Settle();
        private void AppearanceChanged() => QueuePolicySettlement();
        private void SystemPolicyChanged(object? sender, PropertyChangedEventArgs args)
        { if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) QueuePolicySettlement(); }
        private void QueuePolicySettlement()
        {
            int epoch = _epoch; var owner = _owner;
            void SettleCurrent()
            { if (EpochActive && epoch == _epoch && ReferenceEquals(owner, _owner)) Settle(); }
            if (_panel.Dispatcher.CheckAccess()) SettleCurrent();
            else _panel.Dispatcher.BeginInvoke(new Action(SettleCurrent));
        }

        private bool CanAnimate()
        {
            if (!EpochActive || !OperatorMotion.Enabled || _owner is null || !_card.IsLoaded
                || !ReferenceEquals(_panel.MainTabControl.SelectedItem, _panel.OutputTab)
                || !ReferenceEquals(Window.GetWindow(_card), _owner)
                || OperatorInputFocus.GetIsKeyboardMode(_card)) return false;
            foreach (var element in _ancestors)
            {
                if (!element.IsVisible || !element.IsEnabled) return false;
                if (element is ScrollViewer scroll && !FitsViewport(scroll)) return false;
            }
            return _card.ActualWidth > 0 && _card.ActualHeight > 0;
        }

        private bool FitsViewport(ScrollViewer viewport)
        {
            if (viewport.ViewportWidth <= 0 || viewport.ViewportHeight <= 0) return false;
            var origin = _card.TranslatePoint(new System.Windows.Point(), viewport);
            return origin.X >= -.5 && origin.Y >= -.5
                && origin.X + _card.ActualWidth <= viewport.ViewportWidth + .5
                && origin.Y + _card.ActualHeight <= viewport.ViewportHeight + .5;
        }

        internal void Refresh(bool animateTransition)
        {
            bool selected = Selected;
            bool changed = _paintedSelection.HasValue && _paintedSelection != selected;
            _paintedSelection = selected;
            _card.SetResourceReference(Button.BackgroundProperty, selected ? "ControlCentre.SelectionBrush" : "ControlCentre.RaisedBrush");
            _card.SetResourceReference(Button.BorderBrushProperty, selected ? "ControlCentre.AccentBrush" : "ControlCentre.BorderBrush");
            _card.SetResourceReference(Button.ForegroundProperty, selected ? "ControlCentre.SelectedTextBrush" : "ControlCentre.TextBrush");
            _setInk(selected); _check.Opacity = selected ? 1 : 0;
            System.Windows.Automation.AutomationProperties.SetItemStatus(_card, selected ? "Selected" : "Not selected");
            if (!animateTransition || !changed || !CanAnimate()) { Settle(); return; }
            double from = _readProgress();
            int generation = ++Generation, epoch = _epoch;
            _glyph.BeginAnimation(_progressProperty, null); _writeProgress(from); _animating = true;
            var arrival = new DoubleAnimation(from, selected ? 1d : 0d, OperatorMotion.ChoiceArrival) { EasingFunction = OperatorMotion.SmoothOut };
            arrival.Completed += (_, _) =>
            { if (generation == Generation && epoch == _epoch) Settle(); };
            _glyph.BeginAnimation(_progressProperty, arrival);
        }

        internal void Settle()
        {
            ++Generation; _animating = false;
            _glyph.BeginAnimation(_progressProperty, null); _writeProgress(Selected ? 1 : 0);
        }
    }
}
