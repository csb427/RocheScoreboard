using RocheScoreboard.Mac.Production;
// Generated from Views/CloudRemoteLauncher.xaml.cs; edit translator/adapters.
// Source SHA-256: 37f50952b81efe79d24629fb369a3de0dd17ea1374016278d290734b6087ecad
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
// ─────────────────────────────────────────────────────────────────────────────
// CloudRemoteLauncher.xaml.cs
//
// Reusable compact widget for surfacing the cloud remote (start session +
// QR + join code) on tabs other than the Settings → Cloud Remote card.
// It owns no transport — the host (MainWindow) wires the launcher to the
// existing cloud session lifecycle by handling StartRequested /
// EndRequested and calling ApplyState/ApplyConnection to refresh visuals.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Services;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Color = Avalonia.Media.Color;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views
{
    public partial class CloudRemoteLauncher : UserControl
    {
        private static readonly SolidColorBrush BadgeOnline =
            new(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly SolidColorBrush BadgeOffline =
            new(Color.FromRgb(0x6E, 0x76, 0x81));

        /// <summary>Raised when the operator taps "Start cloud session".</summary>
        public event EventHandler? StartRequested;

        /// <summary>Raised when the operator taps "End session".</summary>
        public event EventHandler? EndRequested;

        /// <summary>Raised when the operator taps the join code (clipboard copy).</summary>
        public event EventHandler? CopyJoinCodeRequested;

        /// <summary>Raised when the operator taps "Copy URL".</summary>
        public event EventHandler? CopyJoinUrlRequested;

        /// <summary>Captured displayed value and host result; legacy event handlers remain supported.</summary>
        public sealed class CopyRequestEventArgs : EventArgs
        {
            public string Value { get; }
            public bool? Succeeded { get; set; }
            internal CopyRequestEventArgs(string value) => Value = value;
        }

        private bool _active, _configReady, _closing, _focusAfterClose, _focusOnOpen, _suppressHeaderClick;
        private bool _ownerGeometryReady, _ownerGeometryPending;
        private int _geometryGeneration;
        private string _lastCloseReason = "none";
        private string _joinCode = "", _joinUrl = "";
        private int _stateGeneration, _feedbackGeneration, _motionGeneration;
        private DispatcherTimer? _feedbackTimer;
        private Window? _owner;
        private readonly List<Control> _interactionOwners = [];
        private bool _motionActive;
        private ExpansionOrigin _origin = ExpansionOrigin.Default;

        public CloudRemoteLauncher()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
            KeyDown += DetailsPreviewKeyDown;
            Loaded += (_, _) => { if (IsExpanded) OpenDetails(); };
            Unloaded += (_, _) => CompleteClose(false, "launcher-unloaded");
            NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_, args) => { if (args.NewValue is false) CompleteClose(false, "launcher-hidden"); });
            NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsEnabledProperty,true,(_, args) => { if (args.NewValue is false) CompleteClose(false, "launcher-disabled"); });
        }

        /// <summary>True as soon as the operator opens the detail scope.</summary>
        public bool IsExpanded { get; private set; }
        internal bool IsDetailsAnimating => _motionActive;
        // The disclosure keeps Escape until its closing scope is retired.
        internal bool OwnsEscapeKey => IsExpanded || _closing;
        // A new owned decision must not leave a popup or late focus return above it.
        internal void DismissForDecision() => CompleteClose(false, "owned-decision");
        internal ExpansionOrigin CurrentDetailsPose => ReadDetailsPose();
        internal string DisclosureDiagnostics => $"expanded={IsExpanded}; popup={DetailsPopup.IsOpen}; closing={_closing}; loaded={IsLoaded}; visible={IsVisible}; available={LauncherAvailable()}; ownerReady={_ownerGeometryReady}; owner={_owner?.NativeWidth():0.0}x{_owner?.NativeHeight():0.0}; lastClose={_lastCloseReason}";

        /// <summary>Refreshes the launcher visuals from a single state snapshot.</summary>
        public void ApplyState(bool configReady, bool active, string? joinCode, string? joinUrl)
        {
            string code = joinCode ?? "", url = joinUrl ?? "";
            if (_active != active || _configReady != configReady || _joinCode != code || _joinUrl != url)
            {
                ++_stateGeneration;
                ClearCopyFeedback();
            }
            _active = active;
            _configReady = configReady;
            _joinCode = code;
            _joinUrl = url;
            CopyCodeButton.IsEnabled = active && !string.IsNullOrWhiteSpace(code);
            IdleState.IsVisible     = (!active && configReady) ? true : false;
            LiveState.IsVisible     = active                    ? true : false;
            DisabledState.IsVisible = (!active && !configReady) ? true : false;

            StartButton.IsEnabled = configReady;
            EndButton.IsEnabled = active;

            if (active)
            {
                StatusText.Text = "Session live · waiting for connection…";

                CollapsedStatusText.Text = string.IsNullOrWhiteSpace(joinCode)
                    ? "Session live"
                    : $"Live · code {joinCode}";
            }
            else if (configReady)
            {
                StatusText.Text = "Ready. Tap Start to open a session.";

                CollapsedStatusText.Text = "Tap to start";
            }
            else
            {
                StatusText.Text = "supabase.config.json missing or incomplete.";

                CollapsedStatusText.Text = "Setup required";
            }

            if (active)
            {
                JoinCodeText.Text = joinCode ?? "—";
                JoinUrlText.Text = joinUrl ?? "(set remoteAppBaseUrl in supabase.config.json)";
                JoinUrlPanel.IsVisible = string.IsNullOrWhiteSpace(joinUrl) ? false : true;
                CopyUrlButton.IsEnabled = !string.IsNullOrWhiteSpace(joinUrl);

                try
                {
                    string qrPayload = joinUrl ?? joinCode ?? "";
                    if (!string.IsNullOrWhiteSpace(qrPayload))
                    {
                        NativeBitmap bmp = NativeQr.GenerateQrCode(qrPayload);
                        QrImage.Source = bmp;
                        QrImage.IsVisible = true;
                    }
                    else
                    {
                        QrImage.Source = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CloudRemoteLauncher] QR build failed: {ex.Message}");
                    QrImage.Source = null;
                }
            }
            else
            {
                QrImage.Source = null;
            }
        }

        /// <summary>Updates the small "connected" pip on the live state.</summary>
        public void ApplyConnection(bool connected, bool active)
        {
            ConnectionDot.Fill = connected ? BadgeOnline : BadgeOffline;
            ConnectionLabel.Text = connected ? "Connected" : "Connecting…";


            if (active)
            {
                StatusText.Text = connected ? "Session live · cloud connected" : "Session live · reconnecting…";

            }
        }

        /// <summary>Open anchored details without changing the live console's layout.</summary>
        public void Expand() => ExpandDetails(false);

        private void ExpandDetails(bool keyboardEntry)
        {
            if (!LauncherAvailable() || IsExpanded && !_closing) return;
            _focusOnOpen = keyboardEntry;
            IsExpanded = true;
            _suppressHeaderClick = false;
            if (IsLoaded) OpenDetails();
            else UpdateDisclosureState();
        }

        public void Collapse() => CloseDetails(DetailsSurface.IsKeyboardFocusWithin);

        private void CollapsedButton_Click(object? sender, RoutedEventArgs e)
        {
            // A light-dismiss click on the source closes the popup before the
            // same click reaches the header. It must not reopen that scope.
            if (_suppressHeaderClick) { _suppressHeaderClick = false; return; }
            bool keyboard = CollapsedButton.IsFocused && OperatorInputFocus.GetIsKeyboardMode(CollapsedButton);
            if (IsExpanded) CloseDetails(keyboard); else ExpandDetails(keyboard);
        }

        private void UpdateDisclosureState()
        {
            Avalonia.Automation.AutomationProperties.SetName(CollapsedButton,
                IsExpanded ? "Collapse cloud remote" : "Expand cloud remote");
            Avalonia.Automation.AutomationProperties.SetItemStatus(CollapsedButton, IsExpanded ? "Expanded" : "Collapsed");
        }

        private void AttachOwner()
        {
            var owner = NativeWindow.GetWindow(this);
            if (ReferenceEquals(owner, _owner)) return;
            DetachOwner();
            _owner = owner;
            if (_owner is null) return;
            _owner.KeyDown += OwnerPreviewKeyDown;
            _owner.SizeChanged += OwnerSizeChanged;
            _owner.PositionChanged += OwnerLocationChanged;
            _owner.Closed += OwnerClosed;
            // WPF inherits enabled/visible state; the generated native tree may
            // report the local ancestor change instead. Observe this owned path
            // while open so a command/modal scope can disable the whole console.
            AvaloniaObject? parent = VisualTreeHelper.GetParent(this);
            while (parent is not null)
            {
                if (parent is Control element)
                {
                    _interactionOwners.Add(element);
                    NativeEvents.WatchProperty(element,Avalonia.Controls.Control.IsEnabledProperty,true,InteractionOwnerChanged);
                    NativeEvents.WatchProperty(element,Avalonia.Controls.Control.IsVisibleProperty,true,InteractionOwnerChanged);
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        private bool LauncherAvailable()
        {
            AvaloniaObject? current = this;
            while (current is not null)
            {
                if (current is Control element && (!element.IsEnabled || !element.IsVisible)) return false;
                current = VisualTreeHelper.GetParent(current);
            }
            return true;
        }

        private void InteractionOwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!LauncherAvailable()) CompleteClose(false, "interaction-owner-unavailable");
        }

        private void DetachOwner()
        {
            foreach (var element in _interactionOwners)
            {
                NativeEvents.WatchProperty(element,Avalonia.Controls.Control.IsEnabledProperty,false,InteractionOwnerChanged);
                NativeEvents.WatchProperty(element,Avalonia.Controls.Control.IsVisibleProperty,false,InteractionOwnerChanged);
            }
            _interactionOwners.Clear();
            if (_owner is null) return;
            _owner.KeyDown -= OwnerPreviewKeyDown;
            _owner.SizeChanged -= OwnerSizeChanged;
            _owner.PositionChanged -= OwnerLocationChanged;
            _owner.Closed -= OwnerClosed;
            _owner = null;
        }

        private void OwnerSizeChanged(object? sender, SizeChangedEventArgs e) => OwnerGeometryChanged("owner-size-change");
        private void OwnerLocationChanged(object? sender, EventArgs e) => OwnerGeometryChanged("owner-location-change");
        private void OwnerClosed(object? sender, EventArgs e) => CompleteClose(false, "owner-closed");

        private void OwnerGeometryChanged(string reason)
        {
            // Show/first measure and native work-area fitting may publish the
            // owner's initial geometry after Expand. They establish placement;
            // they are not a user dismissal of the newly requested scope.
            if (!_ownerGeometryReady)
            {
                FitDetails();
                QueueOwnerGeometryReady();
                return;
            }
            CompleteClose(false, reason);
        }

        private void QueueOwnerGeometryReady()
        {
            if (_ownerGeometryPending || _owner is null) return;
            _ownerGeometryPending = true;
            int generation = _geometryGeneration;
            var owner = _owner;
            Dispatcher.UIThread.Post(DispatcherPriority.Background, new Action(() =>
            {
                if (generation != _geometryGeneration) return;
                _ownerGeometryPending = false;
                if (!ReferenceEquals(owner, _owner) || !IsExpanded || _closing || !DetailsPopup.IsOpen) return;
                var content = owner.Content as Control;
                if (owner.NativeWidth() <= 0 || owner.NativeHeight() <= 0 || (content is not null && (content.NativeWidth() <= 0 || content.NativeHeight() <= 0))) return;
                FitDetails();
                _ownerGeometryReady = true;
            }));
        }
        private void OwnerPreviewKeyDown(object? sender, KeyEventArgs e) => HandleDetailsEscape(e);
        private void DetailsPreviewKeyDown(object? sender, KeyEventArgs e) => HandleDetailsEscape(e);

        private void HandleDetailsEscape(KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !OwnsEscapeKey) return;
            e.Handled = true;
            CloseDetails(true);
        }

        private void DetailsClose_Click(object? sender, RoutedEventArgs e) => CloseDetails(true);

        private void FitDetails()
        {
            var content = _owner?.Content as Control;
            double width = content?.NativeWidth() > 0 ? content.NativeWidth() : _owner?.NativeWidth() ?? 680;
            double height = content?.NativeHeight() > 0 ? content.NativeHeight() : _owner?.NativeHeight() ?? 680;
            DetailsSurface.Width = Math.Clamp(CollapsedButton.NativeWidth() > 0 ? CollapsedButton.NativeWidth() : 300,
                180, Math.Max(180, Math.Min(340, width - 24)));
            DetailsSurface.MaxHeight = Math.Max(140, Math.Min(560, height - 24));
        }

        private void OpenDetails()
        {
            if (!IsLoaded || !LauncherAvailable() || !IsExpanded) return;
            bool reversing = DetailsPopup.IsOpen;
            if (!reversing)
            {
                ++_geometryGeneration;
                _ownerGeometryReady = false;
                _ownerGeometryPending = false;
            }
            _lastCloseReason = "none";
            var from = ReadDetailsPose();
            double opacity = DetailsSurface.Opacity, angle = ChevronRotate.Angle;
            int generation = ++_motionGeneration;
            StopDetailsClocks();
            _closing = false;
            _focusAfterClose = false;
            AttachOwner();
            FitDetails();
            // Geometry follows the trigger, but material samples only decorative
            // scenery. Capturing HeaderSurface promotes it to the interactive
            // owner tree and can ghost live labels through the popup's text.
            var backdrop = _owner is not null ? OperatorDesign.GetBackdrop(_owner) : null;
            DetailsSurface.GlassSource = backdrop;
            DetailsSurface.Material = backdrop is null ? GlassMaterial.Opaque : GlassMaterial.Regular;
            DetailsSurface.SetResourceReference(BackgroundProperty, backdrop is null
                ? "ControlCentre.SurfaceBrush" : "ControlCentre.GlassBrush");
            DetailsSurface.IsHitTestVisible = true;
            ExpandedPanel.IsVisible = true;
            UpdateDisclosureState();
            OperatorAppearance.Changed -= DisclosurePreferenceChanged;
            OperatorAppearance.Changed += DisclosurePreferenceChanged;
            DetailsPopup.IsOpen = true;
            DetailsSurface.UpdateLayout();
            QueueOwnerGeometryReady();
            FocusDetailsAction(generation);
            if (!OperatorMotion.Enabled)
            {
                SetDetailsPose(new(1, 1, 0, 0));
                DetailsSurface.Opacity = 1;
                ChevronRotate.Angle = 180;
                _origin = MeasureDetailsOrigin();
                if (_focusOnOpen)
                    Dispatcher.UIThread.Post(DispatcherPriority.Loaded, new Action(() => FocusDetailsAction(generation)));
                return;
            }
            // Keep content readable immediately. Native popup placement settles
            // on the next layout beat; only its bounded presentation pose moves.
            if (!reversing) { SetDetailsPose(new ExpansionOrigin(.98, .96, 0, -8).WithTravel(OperatorMotion.TravelScale)); opacity = .94; }
            _motionActive = true;
            DetailsSurface.Opacity = opacity;
            Dispatcher.UIThread.Post(DispatcherPriority.Loaded, new Action(() =>
            {
                if (generation != _motionGeneration || !DetailsPopup.IsOpen || !IsExpanded || _closing) return;
                DetailsSurface.UpdateLayout();
                _origin = MeasureDetailsOrigin();
                AnimateDetails(reversing ? from : _origin, new(1, 1, 0, 0), opacity, angle,
                    OperatorMotion.PanelOpen, generation, false);
                FocusDetailsAction(generation);
            }));
        }

        private void FocusDetailsAction(int generation)
        {
            if (!_focusOnOpen || generation != _motionGeneration || !IsExpanded || _closing
                || !DetailsPopup.IsOpen || !IsLoaded || !LauncherAvailable()) return;
            OperatorInputFocus.SetIsKeyboardMode(DetailsSurface, true);
            var first = _active && CopyCodeButton.IsEnabled ? CopyCodeButton
                : _active && CopyUrlButton.IsEnabled && JoinUrlPanel.IsVisible ? CopyUrlButton
                : !_active && _configReady && StartButton.IsEnabled ? StartButton : DetailsCloseButton;
            first.Focus();
            NativeKeyboard.Focus(first);
            first.BringIntoView();
            if (first.IsFocused) _focusOnOpen = false;
        }

        private ExpansionOrigin MeasureDetailsOrigin()
        {
            var size = new Size(Math.Max(1, DetailsSurface.NativeWidth()), Math.Max(1, DetailsSurface.NativeHeight()));
            Rect source;
            try
            {
                var first = DetailsSurface.PointFromScreen(CollapsedButton.PointToScreen(new Point()));
                var last = DetailsSurface.PointFromScreen(CollapsedButton.PointToScreen(new Point(CollapsedButton.NativeWidth(), CollapsedButton.NativeHeight())));
                source = new Rect(first, last);
            }
            catch (InvalidOperationException)
            {
                source = new Rect(0, 0, Math.Max(1, CollapsedButton.NativeWidth()), Math.Max(1, CollapsedButton.NativeHeight()));
            }
            var origin = ExpansionOrigin.FromRect(source, size);
            // The source establishes direction; shared values remain readable
            // instead of being compressed down to the header's full envelope.
            return new ExpansionOrigin(Math.Max(.96, origin.ScaleX), Math.Max(.94, origin.ScaleY),
                Math.Clamp(origin.OffsetX, -12, 12), Math.Clamp(origin.OffsetY, -16, 16)).WithTravel(OperatorMotion.TravelScale);
        }

        private ExpansionOrigin ReadDetailsPose() => new(DetailsScale.ScaleX, DetailsScale.ScaleY, DetailsTravel.X, DetailsTravel.Y);
        private void SetDetailsPose(ExpansionOrigin pose)
        {
            DetailsScale.ScaleX = pose.ScaleX;
            DetailsScale.ScaleY = pose.ScaleY;
            DetailsTravel.X = pose.OffsetX;
            DetailsTravel.Y = pose.OffsetY;
        }

        private void StopDetailsClocks()
        {
            _motionActive = false;
            DetailsSurface.BeginAnimation(OpacityProperty, null);
            DetailsScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DetailsScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DetailsTravel.BeginAnimation(TranslateTransform.XProperty, null);
            DetailsTravel.BeginAnimation(TranslateTransform.YProperty, null);
            ChevronRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        private void AnimateDetails(ExpansionOrigin from, ExpansionOrigin to, double fromOpacity, double fromAngle,
            TimeSpan duration, int generation, bool closing)
        {
            SetDetailsPose(to);
            DetailsSurface.Opacity = closing ? .94 : 1;
            ChevronRotate.Angle = closing ? 0 : 180;
            _motionActive = true;
            DoubleAnimation Part(double start, double end, TimeSpan timing) => new(start, end, timing)
                { FillBehavior = FillBehavior.Stop, EasingFunction = OperatorMotion.SmoothOut };
            DetailsScale.BeginAnimation(ScaleTransform.ScaleXProperty, Part(from.ScaleX, to.ScaleX, duration));
            DetailsScale.BeginAnimation(ScaleTransform.ScaleYProperty, Part(from.ScaleY, to.ScaleY, duration));
            DetailsTravel.BeginAnimation(TranslateTransform.XProperty, Part(from.OffsetX, to.OffsetX, duration));
            DetailsTravel.BeginAnimation(TranslateTransform.YProperty, Part(from.OffsetY, to.OffsetY, duration));
            ChevronRotate.BeginAnimation(RotateTransform.AngleProperty, Part(fromAngle, closing ? 0 : 180, closing ? duration : OperatorMotion.Toggle));
            var fade = Part(fromOpacity, closing ? .94 : 1, duration);
            fade.Completed += (_, _) =>
            {
                if (generation != _motionGeneration) return;
                if (closing) CompleteClose(_focusAfterClose);
                else
                {
                    StopDetailsClocks();
                    SetDetailsPose(new(1, 1, 0, 0));
                    DetailsSurface.Opacity = 1;
                    ChevronRotate.Angle = 180;
                }
            };
            DetailsSurface.BeginAnimation(OpacityProperty, fade);
        }

        private void CloseDetails(bool returnFocus)
        {
            if (!IsExpanded && !_closing && !DetailsPopup.IsOpen) return;
            var from = ReadDetailsPose();
            double opacity = DetailsSurface.Opacity, angle = ChevronRotate.Angle;
            int generation = ++_motionGeneration;
            StopDetailsClocks();
            IsExpanded = false;
            _focusOnOpen = false;
            _closing = DetailsPopup.IsOpen;
            _focusAfterClose = returnFocus;
            ClearCopyFeedback();
            UpdateDisclosureState();
            DetailsSurface.IsHitTestVisible = false;
            if (returnFocus && FocusHeader()) _focusAfterClose = false;
            if (!DetailsPopup.IsOpen) return;
            if (!OperatorMotion.Enabled) { CompleteClose(_focusAfterClose); return; }
            _origin = MeasureDetailsOrigin();
            AnimateDetails(from, _origin, opacity, angle, OperatorMotion.SurfaceClose, generation, true);
        }

        private bool FocusHeader()
        {
            if (!IsLoaded || !LauncherAvailable()) return false;
            CollapsedButton.Focus();
            NativeKeyboard.Focus(CollapsedButton);
            return CollapsedButton.IsFocused;
        }

        private void CompleteClose(bool returnFocus, string? reason = null)
        {
            if (!DetailsPopup.IsOpen && !IsExpanded && !_closing && !_motionActive) return;
            if (reason is not null) _lastCloseReason = reason;
            _focusAfterClose = returnFocus;
            _closing = true; // Programmatic closure must not consume a later source click.
            if (DetailsPopup.IsOpen) DetailsPopup.IsOpen = false;
            else ResetClosedDetails();
        }

        private void DetailsPopup_Closed(object? sender, EventArgs e)
        {
            _suppressHeaderClick = !_closing && CollapsedButton.IsPointerOver;
            if (!_closing) _lastCloseReason = "light-dismiss";
            ResetClosedDetails();
        }

        private void ResetClosedDetails()
        {
            bool returnFocus = _focusAfterClose;
            _focusAfterClose = false;
            _focusOnOpen = false;
            ++_motionGeneration;
            ++_geometryGeneration;
            _ownerGeometryReady = false;
            _ownerGeometryPending = false;
            IsExpanded = false;
            _closing = false;
            StopDetailsClocks();
            SetDetailsPose(new(1, 1, 0, 0));
            DetailsSurface.Opacity = 1;
            ChevronRotate.Angle = 0;
            ExpandedPanel.IsVisible = false;
            DetailsSurface.GlassSource = null;
            ClearCopyFeedback();
            UpdateDisclosureState();
            OperatorAppearance.Changed -= DisclosurePreferenceChanged;
            DetachOwner();
            if (returnFocus) FocusHeader();
        }

        private void DisclosurePreferenceChanged()
        {
            if (OperatorMotion.Enabled || !DetailsPopup.IsOpen) return;
            if (_closing) { CompleteClose(_focusAfterClose); return; }
            ++_motionGeneration;
            StopDetailsClocks();
            SetDetailsPose(new(1, 1, 0, 0));
            DetailsSurface.Opacity = 1;
            ChevronRotate.Angle = 180;
            int generation = _motionGeneration;
            FocusDetailsAction(generation);
            if (_focusOnOpen)
                Dispatcher.UIThread.Post(DispatcherPriority.Loaded, new Action(() => FocusDetailsAction(generation)));
        }

        private void StartButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsExpanded || _closing || !IsLoaded || !LauncherAvailable() || !_configReady || _active || !StartButton.IsEnabled) return;
            StartButton.IsEnabled = false;
            StatusText.Text = "Creating session…";

            CompleteClose(false, "start-request");
            StartRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EndButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!IsExpanded || _closing || !IsLoaded || !LauncherAvailable() || !_active || !EndButton.IsEnabled) return;
            EndButton.IsEnabled = false;
            CompleteClose(false, "end-request");
            EndRequested?.Invoke(this, EventArgs.Empty);
        }

        private void JoinCodeBox_Click(object? sender, RoutedEventArgs e) => RequestCopy(true);
        private void CopyUrlButton_Click(object? sender, RoutedEventArgs e) => RequestCopy(false);

        private void RequestCopy(bool code)
        {
            if (!_active || !IsExpanded || _closing || !IsLoaded || !LauncherAvailable()) return;
            string value = code ? _joinCode : _joinUrl;
            var handler = code ? CopyJoinCodeRequested : CopyJoinUrlRequested;
            ClearCopyFeedback();
            int feedback = _feedbackGeneration, state = _stateGeneration;
            bool success = false;
            if (handler is not null && !string.IsNullOrWhiteSpace(value))
            {
                var request = new CopyRequestEventArgs(value);
                try
                {
                    handler.Invoke(this, request);
                    // Existing subscribers communicate rejection by throwing;
                    // the production host reports an explicit result instead.
                    success = request.Succeeded ?? true;
                }
                catch (System.Runtime.InteropServices.ExternalException) { success = false; }
            }
            if (state != _stateGeneration || feedback != _feedbackGeneration || !IsLoaded || !LauncherAvailable() || !IsExpanded || _closing) return;
            string item = code ? "Join code" : "URL";
            CopyFeedbackText.Text = success ? $"{item} copied." : $"Could not copy the {item.ToLowerInvariant()}. Please try again.";

            if (!success) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _feedbackTimer = timer;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!ReferenceEquals(_feedbackTimer, timer) || feedback != _feedbackGeneration || state != _stateGeneration) return;
                _feedbackTimer = null;
                CopyFeedbackText.Text = "";
            };
            timer.Start();
        }

        private void ClearCopyFeedback()
        {
            ++_feedbackGeneration;
            _feedbackTimer?.Stop();
            _feedbackTimer = null;
            CopyFeedbackText.Text = "";
        }
    }
}

namespace Roche_Scoreboard.Views
{
public partial class CloudRemoteLauncher
{
    private RotateTransform ChevronRotate => (RotateTransform)ProductionNames.RegistryFor(this)["ChevronRotate"];
    private ScaleTransform DetailsScale => (ScaleTransform)ProductionNames.RegistryFor(this)["DetailsScale"];
    private TranslateTransform DetailsTravel => (TranslateTransform)ProductionNames.RegistryFor(this)["DetailsTravel"];
    private void Native_DetailsPreviewKeyDown(object? sender, KeyEventArgs e) { DetailsPreviewKeyDown(sender!, e); }
}
}
