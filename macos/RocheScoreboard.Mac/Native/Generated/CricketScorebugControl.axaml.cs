using RocheScoreboard.Mac.Production;
// Generated from Views/CricketScorebugControl.xaml.cs; edit translator/adapters.
// Source SHA-256: adb9e8a3ae21106905b3fa90b787d8dfcc96f853e379b763c488a7c5d80c9d3b
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Input;
using RocheScoreboard.Mac.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Color = Avalonia.Media.Color;
using ColorConverter = RocheScoreboard.Mac.Native.ColorConverter;
using UserControl = Avalonia.Controls.UserControl;

namespace Roche_Scoreboard.Views;

public partial class CricketScorebugControl : UserControl
{
        private readonly Dictionary<TextBlock,RollingScoreVisual> _numberReels=new();
        private bool _overlayCompact;
        internal bool OverlayMotionEnabled {get;set;}=true;
        private readonly TranslateTransform _railMotion = new(), _topMotion = new(), _bottomMotion = new(), _bowlerMotion = new(), _ballsMotion=new();
        private readonly Avalonia.Controls.Border _railBackplate=new NativeBorder(){IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Top};
        private readonly TranslateTransform _railBackplateOffset=new();
        private readonly Dictionary<Control,TranslateTransform> _railCellMotion=new();
        private readonly Avalonia.Controls.Canvas _headerHandoff=new() { IsHitTestVisible=false };
        private sealed record HeaderLease(Grid Parent,int Index,double Width,double Height);
        private readonly Dictionary<Control,HeaderLease> _headerLeases=new();
        private int _compactMotionGeneration;
        private bool _canvasLayoutPending;
        private int _canvasLayoutGeneration;
        public void SetOverlayCompact(bool compact)
        {
            if (_overlayCompact == compact)
            {
                if(!OverlayMotionEnabled) {ReturnHeaderHandoffs();ApplyOverlayGeometry(false);}
                return;
            }
            if(!compact) ReturnHeaderHandoffs();
            if(compact)
            {
                HideBowlerDetails();
                if(OverlayMotionEnabled)
                {
                    CaptureHeaderHandoff(BattingHeaderPanel); CaptureHeaderHandoff(BowlingHeaderPanel);
                    CaptureHeaderHandoff(ExtrasLabelPanel); CaptureHeaderHandoff(ExtrasValuePanel);
                }
            }
            _overlayCompact = compact; ApplyOverlayGeometry(OverlayMotionEnabled);
        }
        private void ApplyOverlayGeometry(bool animate)
        {
            int generation=++_compactMotionGeneration;
            double railOrigin=LeftScoreRail.NativeTranslatePoint(new Avalonia.Point(),MainScoreboard).Y;
            var railCells=new Control[] { ScoreStack,OversLabelPanel,OversValuePanel,InfoLabelPanel,InfoValuePanel };
            var panels=railCells.Concat(new Control[] { StrikerBand,NonStrikerBand,BowlerBand,OverBallLane }).ToArray();
            var origins=panels.ToDictionary(panel=>panel,panel=>panel.NativeTranslatePoint(new Avalonia.Point(0,panel.NativeHeight()/2),MainScoreboard));
            foreach(var panel in panels) panel.RenderTransform=new MatrixTransform(Matrix.Identity);
            _railMotion.BeginAnimation(TranslateTransform.YProperty,null); _railMotion.Y=0; LeftScoreRail.RenderTransform=_railMotion;
            double height=CricketDesignRoot.Height,offset=height*CricketBroadcastStyle.OverlayFraction;
            if(_railBackplate.Parent is null)
            {
                MainScoreboard.Children.Insert(0,_railBackplate);
                _railBackplate.SetBinding(Avalonia.Controls.Border.BackgroundProperty,new Avalonia.Data.Binding("Background"){Source=LeftScoreRail});
                var transforms=new TransformGroup();transforms.Children.Add(_railMotion);transforms.Children.Add(_railBackplateOffset);_railBackplate.RenderTransform=transforms;
            }
            _railBackplate.Height=height;_railBackplate.Margin=new Thickness(0,0,3,0);_railBackplateOffset.Y=_overlayCompact?offset:0;
            LeftScoreRail.Margin=PlayerBands.Margin=new Thickness(0,_overlayCompact ? offset : 0,0,0);
            double[] railNormal=[1,1,.37,.9,.37,.9,.37,.9];
            double[] playerNormal=[.65,1.1,1.1,.65,1.1,1.05];
            if(_overlayCompact)
            {
                // Keep the two score rows and every glyph at their normal size.
                // Retain one contextual statistic and the six-ball lane instead
                // of replacing the whole lower rail with an oversized overs box.
                double scoreHalf=height/5.81,label=height/18;
                double value=(height-offset-scoreHalf*2-label*2)/2;
                double[] rail=[scoreHalf,scoreHalf,label,value,label,value,0,0];
                for(int i=0;i<rail.Length;i++) ScoreRailGrid.RowDefinitions[i].Height=new GridLength(Math.Max(0,rail[i]));
                double ballHeight=height*1.05/5.65;
                double row=(height-offset-ballHeight)/3;
                double margin=Math.Max(0,(row-(height*1.1/5.65-14))/2);
                double[] players=[0,row,row,0,row,ballHeight];
                for(int i=0;i<players.Length;i++) PlayerBands.RowDefinitions[i].Height=new GridLength(players[i]);
                foreach(var band in new[] {StrikerBand,NonStrikerBand,BowlerBand}) band.Margin=new Thickness(9,margin,9,margin);
            }
            else
            {
                for(int i=0;i<railNormal.Length;i++) ScoreRailGrid.RowDefinitions[i].Height=new GridLength(railNormal[i],GridUnitType.Star);
                for(int i=0;i<playerNormal.Length;i++) PlayerBands.RowDefinitions[i].Height=new GridLength(playerNormal[i],GridUnitType.Star);
                foreach(var band in new[] {StrikerBand,NonStrikerBand,BowlerBand}) band.Margin=new Thickness(9,7,9,7);
            }
            MainScoreboard.UpdateLayout();
            double railFrom=railOrigin-LeftScoreRail.NativeTranslatePoint(new Avalonia.Point(),MainScoreboard).Y;
            _railMotion.Y=railFrom;
            // Each rail cell keeps its own current visual centre. Moving only
            // the outer rail made the overs/context text jump to its new row
            // before the rest of the scoreboard started travelling.
            foreach(var cell in railCells)
            {
                if(!_railCellMotion.TryGetValue(cell,out var motion)) _railCellMotion[cell]=motion=new TranslateTransform();
                Move(cell,motion);
            }
            Move(StrikerBand,_topMotion); Move(NonStrikerBand,_bottomMotion); Move(BowlerBand,_bowlerMotion); Move(OverBallLane,_ballsMotion);
            if(animate && NativeSystemPreferences.ClientAreaAnimation && Math.Abs(railFrom)>.01)
            {
                var railTravel=new DoubleAnimation(railFrom,0,CricketBroadcastStyle.OverlayMotionDuration){EasingFunction=CricketBroadcastStyle.OverlayMotionEase()};
                railTravel.Completed+=(_,_)=>{if(generation==_compactMotionGeneration){_railMotion.BeginAnimation(TranslateTransform.YProperty,null);_railMotion.Y=0;}};
                _railMotion.BeginAnimation(TranslateTransform.YProperty,railTravel);
            }
            else _railMotion.Y=0;
            foreach(var panel in new Control[] {BattingHeaderPanel,BowlingHeaderPanel,ExtrasLabelPanel,ExtrasValuePanel})
            {
                double from=panel.Opacity, to=_overlayCompact ? 0 : 1;
                panel.BeginAnimation(OpacityProperty,null); panel.Opacity=from;
                if(animate && NativeSystemPreferences.ClientAreaAnimation)
                {
                    var fade=new DoubleAnimation(from,to,CricketBroadcastStyle.OverlayMotionDuration) { FillBehavior=FillBehavior.HoldEnd,EasingFunction=CricketBroadcastStyle.OverlayMotionEase() };
                    fade.Completed+=(_,_)=>
                    {
                        if(generation!=_compactMotionGeneration)return;
                        panel.BeginAnimation(OpacityProperty,null);panel.Opacity=to;
                        if(_overlayCompact && panel is Control header)ReturnHeaderHandoff(header);
                    };
                    panel.BeginAnimation(OpacityProperty,fade);
                }
                else {panel.Opacity=to;if(panel is Control header)ReturnHeaderHandoff(header);}
            }
            CompactOversPanel.BeginAnimation(OpacityProperty,null); CompactOversPanel.Opacity=0;
            // RenderTransform replacement invalidates Arrange. Commit that
            // visual transform before reading coordinates for the strike arrow;
            // otherwise TranslatePoint can still describe the identity pose
            // even though the new FLIP offset has already been assigned.
            MainScoreboard.UpdateLayout();
            UpdateStrikeArrow(animate,true);
            void Move(Control panel,TranslateTransform transform)
            {
                var target=panel.NativeTranslatePoint(new Avalonia.Point(0,panel.NativeHeight()/2),MainScoreboard);
                double from=origins[panel].Y-target.Y;
                transform.BeginAnimation(TranslateTransform.YProperty,null); transform.Y=from; panel.RenderTransform=transform;
                if(animate && NativeSystemPreferences.ClientAreaAnimation && Math.Abs(from)>.01)
                {
                    var travel=new DoubleAnimation(from,0,CricketBroadcastStyle.OverlayMotionDuration) { FillBehavior=FillBehavior.HoldEnd,EasingFunction=CricketBroadcastStyle.OverlayMotionEase() };
                    travel.Completed+=(_,_)=>
                    {
                        if(generation!=_compactMotionGeneration)return;
                        transform.BeginAnimation(TranslateTransform.YProperty,null);transform.Y=0;
                    };
                    transform.BeginAnimation(TranslateTransform.YProperty,travel);
                }
                else transform.Y=0;
            }
        }
        private void CaptureHeaderHandoff(Control header)
        {
            if(!IsVisible || !NativeSystemPreferences.ClientAreaAnimation || header.NativeWidth()<1 || header.NativeHeight()<1 || _headerLeases.ContainsKey(header) || header.Parent is not Grid parent) return;
            if(_headerHandoff.Parent is null) { Avalonia.Controls.Grid.SetColumnSpan(_headerHandoff,2); MainScoreboard.Children.Add(_headerHandoff); }
            var point=header.NativeTranslatePoint(new Avalonia.Point(),MainScoreboard);
            double width=header.NativeWidth(),height=header.NativeHeight();
            _headerLeases[header]=new(parent,parent.Children.IndexOf(header),header.Width,header.Height);
            // Keep the original vector surface at its old pose while its grid
            // row collapses. Four synchronous NativeRenderTargetBitmap renders on the
            // first animation frame were a source of visible entry stalls.
            parent.Children.Remove(header);header.Width=width;header.Height=height;
            Avalonia.Controls.Canvas.SetLeft(header,point.X);Avalonia.Controls.Canvas.SetTop(header,point.Y);_headerHandoff.Children.Add(header);
        }
        private void ReturnHeaderHandoff(Control header)
        {
            if(!_headerLeases.Remove(header,out var lease))return;
            double opacity=header.Opacity;header.BeginAnimation(OpacityProperty,null);header.Opacity=opacity;
            _headerHandoff.Children.Remove(header);header.Width=lease.Width;header.Height=lease.Height;
            lease.Parent.Children.Insert(Math.Min(lease.Index,lease.Parent.Children.Count),header);
        }
        private void ReturnHeaderHandoffs()
        {
            foreach(var header in _headerLeases.Keys.ToArray())ReturnHeaderHandoff(header);
        }

        private readonly DispatcherTimer _timeUpdater = new() { Interval = TimeSpan.FromSeconds(1) };
        private DispatcherTimer? _infoRotationTimer;
        private readonly DispatcherTimer _chaseMiniTimer=new() {Interval=TimeSpan.FromSeconds(8)};
        private bool _chaseMiniShowing;
        private int _infoRotationIndex;
        private CricketMatchManager? _lastMatch;
        private int _topBatterIndex = -1, _bottomBatterIndex = -1, _lastInningsNumber = -1;
        private int _lastDisplayedScore = -1, _lastDisplayedWickets = -1;
        private int _lastTopRuns = -1, _lastBottomRuns = -1, _lastTopBalls = -1, _lastBottomBalls = -1;
        private string _lastBowlerFigures = "", _lastBowlerOvers = "", _lastOversDisplay = "";
        private int _lastBallCount = -1;
        private bool _isDisposed;
        private int _primaryRunCapacity = 4;
        private int _lastBowlerIndex = -1, _infoGeneration, _ballGeneration;
        private bool _clearingBalls;
        private readonly List<string> _displayedBalls = new();
        private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
        private static readonly SolidColorBrush GreyOutBrush = new(Color.FromRgb(0x88, 0x88, 0x88));
        static CricketScorebugControl() { WhiteBrush.Freeze(); GreyOutBrush.Freeze(); }

        public CricketScorebugControl()
        {
            NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }
            ((Control)ScoreRuns.Parent).SizeChanged += (_, _) => FitPrimaryScore();
            ((Control)ScoreWickets.Parent).SizeChanged += (_, _) => FitPrimaryScore();
            foreach (var number in new[] { OversText, ExtrasValue, StrikerRuns, StrikerBalls, NonStrikerRuns, NonStrikerBalls, BowlerFigures, BowlerOvers })
                ((Control)number.Parent).SizeChanged += (_, _) => FitLongNumericFields();
            InfoValue.SizeChanged+=(_,_)=>FitContextValue((InfoLabel.Text ?? string.Empty),(InfoValue.Text ?? string.Empty));
            SizeChanged += (_, _) =>
            {
                if(_canvasLayoutPending)return;
                _canvasLayoutPending=true;int generation=_canvasLayoutGeneration;
                Dispatcher.UIThread.Post(DispatcherPriority.Background,new Action(()=>
                {
                    if(generation!=_canvasLayoutGeneration)return;
                    _canvasLayoutPending=false;
                    if(_isDisposed || !IsLoaded || Bounds.Width<=0 || Bounds.Height<=0)return;
                    double width=CricketDesignRoot.Width,height=CricketDesignRoot.Height;
                    BroadcastLayout.FitCanvas(CricketDesignRoot,Bounds.Width,Bounds.Height);
                    if(_overlayCompact && (Math.Abs(width-CricketDesignRoot.Width)>.01 || Math.Abs(height-CricketDesignRoot.Height)>.01))
                    {ReturnHeaderHandoffs();ApplyOverlayGeometry(false);}
                }));
            };
            _timeUpdater.Tick += (_, _) => { TopTimeText.Text = BroadcastClock.WallTime(); TickBowlerDetails(); };
            TopTimeText.Text = BroadcastClock.WallTime();
            StartInfoRotation();
            _chaseMiniTimer.Tick+=(_,_)=>
            {
                _chaseMiniTimer.Stop(); _chaseMiniShowing=false;
                if(_lastMatch is not null && !EventShowing && !MomentAwaitingLayout) UpdateInfoTile(_lastMatch);
            };
            Loaded += (_, _) =>
            {
                if (_isDisposed) return;
                _timeUpdater.Start(); _infoRotationTimer?.Start();
                if (_lastMatch is not null) UpdateFromMatch(_lastMatch);
            };
            Unloaded += (_, _) => { CancelBroadcast(false); SuspendTimers(); };
        }
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true; ReleaseBroadcast(); SuspendTimers(); CancelPlayerMotion(); _lastMatch = null;
        }
        private void SuspendTimers()
        {
            ++_compactMotionGeneration;ReturnHeaderHandoffs();
            ++_canvasLayoutGeneration;_canvasLayoutPending=false;
            foreach(var header in new Control[] {BattingHeaderPanel,BowlingHeaderPanel,ExtrasLabelPanel,ExtrasValuePanel})
            {header.BeginAnimation(OpacityProperty,null);header.Opacity=_overlayCompact ? 0 : 1;}
            _headerHandoff.Children.Clear();
            foreach(var move in _railCellMotion.Values.Concat(new[] {_railMotion,_topMotion,_bottomMotion,_bowlerMotion,_ballsMotion}))
            { move.BeginAnimation(TranslateTransform.YProperty,null); move.Y=0; }
            CancelPregameMotion(); HideBowlerDetails(); CancelPlayerMotion();
            ScoreRollVisual.Stop(); foreach(var reel in _numberReels.Values) reel.Stop();
            _timeUpdater.Stop(); _infoRotationTimer?.Stop(); _chaseMiniTimer.Stop(); _chaseMiniShowing=false;
            _infoGeneration++; _ballGeneration++; _clearingBalls = false; _displayedBalls.Clear();
            InfoLabel.BeginAnimation(OpacityProperty, null); InfoValue.BeginAnimation(OpacityProperty, null);
            foreach (var element in new[] { ScoreRuns, ScoreWickets, OversText, StrikerRuns, StrikerBalls, NonStrikerRuns, NonStrikerBalls, BowlerFigures, BowlerOvers })
            {
                element.BeginAnimation(OpacityProperty, null); element.Opacity = 1;
                if (element.RenderTransform is TranslateTransform translate)
                { translate.BeginAnimation(TranslateTransform.YProperty, null); translate.Y = 0; }
            }
            _lastDisplayedScore = _lastDisplayedWickets = -1;
            _lastTopRuns = _lastBottomRuns = _lastTopBalls = _lastBottomBalls = -1;
            _lastBowlerFigures = _lastBowlerOvers = _lastOversDisplay = "";
        }
        public void UpdateWeatherSnapshot(WeatherSnapshot snapshot)
        {
            if (!_isDisposed) TopWeatherText.Text = $"{snapshot.Icon} {Math.Round(snapshot.CurrentTemp)}°";
        }
        public void UpdateFromMatch(CricketMatchManager match)
        {
            if (_isDisposed || DeferForBroadcast(match)) return;
            if (!ReferenceEquals(_lastMatch, match)) { StopReels(InfoValue); CancelInfoFade(); _pregamePending=null; _pregameGeneration++; _lastInningsNumber = -1; _displayedBalls.Clear(); _ballGeneration++; _clearingBalls = false; }
            _lastMatch = match;
            var inn = match.CurrentInnings;
            if (inn == null) return;
            if(match.BreakKind!=CricketBreakKind.None || match.IsMatchFinalized) HideBowlerDetails();
            UpdatePregame(match);

            // Reset batter row tracking on new innings
            if (match.CurrentInningsNumber != _lastInningsNumber)
            {
                _lastInningsNumber = match.CurrentInningsNumber;
                _topBatterIndex = -1;
                _bottomBatterIndex = -1;
                _lastDisplayedScore = -1;
                _lastDisplayedWickets = -1;
                _lastTopRuns = -1;
                _lastBottomRuns = -1;
                _lastTopBalls = -1;
                _lastBottomBalls = -1;
                _lastBowlerFigures = "";
                _lastBowlerOvers = "";
                _lastOversDisplay = "";
                _lastBallCount = -1;
            }

            // Score — independent scroll for wickets and runs
            string wktsStr = inn.TotalWickets.ToString();
            string runsStr = inn.TotalRuns.ToString();
            string oversStr = inn.OversDisplay;

            if (inn.TotalRuns != _lastDisplayedScore && _lastDisplayedScore >= 0)
            {
                AnimateScoreRoll(ScoreRuns, ScoreRunsTranslate, _lastDisplayedScore, inn.TotalRuns, 60);
            }
            else
            {
                ScoreRuns.Text = runsStr;
            }

            if (inn.TotalWickets != _lastDisplayedWickets && _lastDisplayedWickets >= 0)
                ScrollValue(ScoreWickets, ScoreWicketsTranslate, wktsStr, 60);
            else
                ScoreWickets.Text = wktsStr;

            if (oversStr != _lastOversDisplay && !string.IsNullOrEmpty(_lastOversDisplay))
                ScrollValue(OversText, OversTranslate, oversStr, 38);
            else
                OversText.Text = oversStr;

            _lastDisplayedScore = inn.TotalRuns;
            _lastDisplayedWickets = inn.TotalWickets;
            _lastOversDisplay = oversStr;

            // Colours
            TrySetColor(BattingTeamBg, match.BattingTeamPrimaryColor);
            TrySetColor(BattingHeaderBg, match.BattingTeamPrimaryColor);
            TrySetColor(StrikerBorder, match.BattingTeamPrimaryColor);
            TrySetColor(NonStrikerBorder, match.BattingTeamPrimaryColor);
            TrySetColor(BattingIndicatorBrush, match.BattingTeamPrimaryColor);
            TrySetColor(NonStrikerIndicatorBrush, match.BattingTeamPrimaryColor);
            TrySetColor(BowlingHeaderBg, match.BowlingTeamPrimaryColor);
            TrySetColor(BowlerBorder, match.BowlingTeamPrimaryColor);
            TrySetColor(BowlingIndicatorBrush, match.BowlingTeamPrimaryColor);
            TrySetColor(PrevBowlerBorderBrush, match.BowlingTeamPrimaryColor);

            LeftScoreRail.Background = CricketBroadcastStyle.Gradient(match.BattingTeamPrimaryColor, match.BattingTeamSecondaryColor);
            CompactOversPanel.Background=LeftScoreRail.Background;
            BattingHeaderPanel.Background = CricketBroadcastStyle.Gradient(match.BattingTeamPrimaryColor, match.BattingTeamSecondaryColor);
            BowlingHeaderPanel.Background = CricketBroadcastStyle.Gradient(match.BowlingTeamPrimaryColor, match.BowlingTeamSecondaryColor);

            Color arrow = TeamVisuals.Accent(match.BattingTeamPrimaryColor).Color;
            while (ContrastHelper.ContrastRatio(arrow, Color.FromRgb(6, 8, 11)) < 4.5)
                arrow = Color.FromRgb((byte)(arrow.R + (255 - arrow.R) * .2), (byte)(arrow.G + (255 - arrow.G) * .2), (byte)(arrow.B + (255 - arrow.B) * .2));
            BattingIndicatorBrush.Color = NonStrikerIndicatorBrush.Color = arrow;

            // Team text colours — auto-detect from primary background luminance
            var battingColor = SafeColor(match.BattingTeamPrimaryColor, "#FFFFFF");
            var bowlingColor = SafeColor(match.BowlingTeamPrimaryColor, "#FFFFFF");
            var battingTextBrush = WhiteBrush;
            var bowlingTextBrush = WhiteBrush;
            ScoreWickets.Foreground = battingTextBrush;
            ScoreRuns.Foreground = battingTextBrush;
            BattingHeaderText.Foreground = battingTextBrush;
            BowlingHeaderText.Foreground = bowlingTextBrush;

            // Headers
            BattingHeaderText.Text = match.BattingTeamName.ToUpper();
            BowlingHeaderText.Text = match.BowlingTeamName.ToUpper();
            RollField(ExtrasValue,inn.TotalExtras.ToString());
            ScoreSlash.Foreground = battingTextBrush;
            OversText.Foreground = InfoValue.Foreground = ExtrasValue.Foreground = battingTextBrush;
            TopTimeText.Foreground = TopWeatherText.Foreground = battingTextBrush;
            double battingDecoration = 0;
            double bowlingDecoration = 0;
            RailSheen.Opacity = BattingHeaderSheen.Opacity = battingDecoration;
            BowlingHeaderSheen.Opacity = bowlingDecoration;
            BattingLogo.Opacity = .3 * battingDecoration;
            BattingHeaderLogo.Opacity = .22;
            BowlingHeaderLogo.Opacity = .22;

            int previousTop=_topBatterIndex,previousBottom=_bottomBatterIndex,previousBowler=_lastBowlerIndex;
            if(_lastBowlerIndex!=inn.CurrentBowlerIndex) { _lastBowlerFigures=_lastBowlerOvers=""; StopReels(BowlerFigures,BowlerOvers); }
            // Batter display — keep rows stable, only move the indicator
            int si = inn.StrikerIndex;
            int nsi = inn.NonStrikerIndex;

            // Assign rows on first display or when a new batter replaces a dismissed one
            if (_topBatterIndex < 0 || _bottomBatterIndex < 0)
            {
                _topBatterIndex = si;
                _bottomBatterIndex = nsi;
            }
            else
            {
                // If a new batter has replaced one of the tracked positions, update that slot
                bool topStillActive = _topBatterIndex == si || _topBatterIndex == nsi;
                bool bottomStillActive = _bottomBatterIndex == si || _bottomBatterIndex == nsi;

                if (!topStillActive && bottomStillActive)
                    _topBatterIndex = (_bottomBatterIndex == si) ? nsi : si;
                else if (topStillActive && !bottomStillActive)
                    _bottomBatterIndex = (_topBatterIndex == si) ? nsi : si;
                else if (!topStillActive && !bottomStillActive)
                {
                    _topBatterIndex = si;
                    _bottomBatterIndex = nsi;
                }
            }

            var oldTop=previousTop>=0 && previousTop!=_topBatterIndex ? CaptureActor(StrikerContents) : null;
            var oldBottom=previousBottom>=0 && previousBottom!=_bottomBatterIndex ? CaptureActor(NonStrikerContents) : null;
            var oldBowler=previousBowler>=0 && previousBowler!=inn.CurrentBowlerIndex ? CaptureActor(BowlerContents) : null;
            if(previousBowler!=inn.CurrentBowlerIndex) {_lastBowlerFigures=_lastBowlerOvers="";StopReels(BowlerFigures,BowlerOvers);}
            if(previousTop!=_topBatterIndex) { _lastTopRuns=_lastTopBalls=-1; StopReels(StrikerRuns,StrikerBalls); }
            if(previousBottom!=_bottomBatterIndex) { _lastBottomRuns=_lastBottomBalls=-1; StopReels(NonStrikerRuns,NonStrikerBalls); }
            var topPlayer = (_topBatterIndex >= 0 && _topBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_topBatterIndex] : null;
            var bottomPlayer = (_bottomBatterIndex >= 0 && _bottomBatterIndex < inn.BattingOrder.Count)
                ? inn.BattingOrder[_bottomBatterIndex] : null;

            bool topIsStriker = _topBatterIndex == si;
            bool topGreyOut = topPlayer != null && topPlayer.IsOut;
            bool bottomGreyOut = bottomPlayer != null && bottomPlayer.IsOut;

            // Top row
            if (topPlayer != null)
            {
                StrikerName.Text = topPlayer.DisplayName.ToUpper();

                string topRunsStr = topPlayer.Runs.ToString();
                string topBallsStr = topPlayer.BallsFaced.ToString();

                if (topPlayer.Runs != _lastTopRuns && _lastTopRuns >= 0)
                {
                    ScrollValue(StrikerRuns, StrikerRunsTranslate, topRunsStr, 28);
                }
                else
                    StrikerRuns.Text = topRunsStr;

                if (topPlayer.BallsFaced != _lastTopBalls && _lastTopBalls >= 0)
                    ScrollValue(StrikerBalls, StrikerBallsTranslate, topBallsStr, 18);
                else
                    StrikerBalls.Text = topBallsStr;

                StrikerName.Foreground = topGreyOut ? GreyOutBrush : WhiteBrush;
                StrikerRuns.Foreground = StrikerBalls.Foreground = StrikerName.Foreground;
                StrikerIndicator.Text = topGreyOut ? "" : (topIsStriker ? "▶" : "");
                StrikerIndicator.FontSize=38;

                _lastTopRuns = topPlayer.Runs;
                _lastTopBalls = topPlayer.BallsFaced;
            }
            else
            {
                StrikerName.Text = "";
                StrikerRuns.Text = "";
                StrikerBalls.Text = "";
                StrikerIndicator.Text = "  ";
            }

            // Bottom row
            if (bottomPlayer != null)
            {
                NonStrikerName.Text = bottomPlayer.DisplayName.ToUpper();

                string botRunsStr = bottomPlayer.Runs.ToString();
                string botBallsStr = bottomPlayer.BallsFaced.ToString();

                if (bottomPlayer.Runs != _lastBottomRuns && _lastBottomRuns >= 0)
                {
                    ScrollValue(NonStrikerRuns, NonStrikerRunsTranslate, botRunsStr, 28);
                }
                else
                    NonStrikerRuns.Text = botRunsStr;

                if (bottomPlayer.BallsFaced != _lastBottomBalls && _lastBottomBalls >= 0)
                    ScrollValue(NonStrikerBalls, NonStrikerBallsTranslate, botBallsStr, 18);
                else
                    NonStrikerBalls.Text = botBallsStr;

                NonStrikerName.Foreground = bottomGreyOut ? GreyOutBrush : WhiteBrush;
                NonStrikerRuns.Foreground = NonStrikerBalls.Foreground = NonStrikerName.Foreground;
                NonStrikerIndicator.Text = bottomGreyOut ? "" : (!topIsStriker ? "▶" : "");
                NonStrikerIndicator.FontSize=38;

                _lastBottomRuns = bottomPlayer.Runs;
                _lastBottomBalls = bottomPlayer.BallsFaced;
            }
            else
            {
                NonStrikerName.Text = "";
                NonStrikerRuns.Text = "";
                NonStrikerBalls.Text = "";
                NonStrikerIndicator.Text = "  ";
            }

            // Current bowler
            var bowler = inn.CurrentBowler;
            if (bowler != null)
            {
                BowlerName.Text = bowler.DisplayName.ToUpper();

                string figStr = bowler.BowlingFigures;
                string bOvStr = bowler.OversDisplay;

                if (figStr != _lastBowlerFigures && !string.IsNullOrEmpty(_lastBowlerFigures))
                    ScrollValue(BowlerFigures, BowlerFiguresTranslate, figStr, 26);
                else
                    BowlerFigures.Text = figStr;

                if (bOvStr != _lastBowlerOvers && !string.IsNullOrEmpty(_lastBowlerOvers))
                    ScrollValue(BowlerOvers, BowlerOversTranslate, bOvStr, 18);
                else
                    BowlerOvers.Text = bOvStr;

                _lastBowlerFigures = figStr;
                _lastBowlerOvers = bOvStr;
            }
            else
            {
                BowlerName.Text = "";
                BowlerFigures.Text = "";
                BowlerOvers.Text = "";
            }

            // Previous/interchanging bowler
            if (inn.PreviousBowlerIndex >= 0
                && inn.PreviousBowlerIndex < inn.BowlingAttack.Count
                && inn.PreviousBowlerIndex != inn.CurrentBowlerIndex)
            {
                var prev = inn.BowlingAttack[inn.PreviousBowlerIndex];
                PrevBowlerName.Text = prev.DisplayName.ToUpper();
                PrevBowlerFigures.Text = prev.BowlingFigures;
                PrevBowlerOvers.Text = prev.OversDisplay;
            }
            else
            {
                PrevBowlerName.Text = "";
                PrevBowlerFigures.Text = "";
                PrevBowlerOvers.Text = "";
            }


            if (_lastBowlerIndex != inn.CurrentBowlerIndex)
            {
                _lastBowlerIndex=inn.CurrentBowlerIndex; HideBowlerDetails(); _nextBowlerDetailAt=_detailsClock.Elapsed.TotalSeconds+60;
            }
            AnimateActor(StrikerContents,oldTop,previousTop!=_topBatterIndex && _topBatterIndex>=0); AnimateActor(NonStrikerContents,oldBottom,previousBottom!=_bottomBatterIndex && _bottomBatterIndex>=0); AnimateActor(BowlerContents,oldBowler,previousBowler!=inn.CurrentBowlerIndex && inn.CurrentBowlerIndex>=0);
            StrikerBand.Background=topGreyOut ? new SolidColorBrush(Color.FromRgb(0x12,0x12,0x12)) : new SolidColorBrush(Color.FromRgb(6,8,11));
            NonStrikerBand.Background=bottomGreyOut ? new SolidColorBrush(Color.FromRgb(0x12,0x12,0x12)) : new SolidColorBrush(Color.FromRgb(6,8,11));
            if(topGreyOut) StrikerBorder.Color=Color.FromRgb(0x65,0x65,0x65);
            if(bottomGreyOut) NonStrikerBorder.Color=Color.FromRgb(0x65,0x65,0x65);
            UpdateStrikeArrow();
            if(_detailsDeliveryCount!=inn.Deliveries.Count)
            { _detailsDeliveryCount=inn.Deliveries.Count; HideBowlerDetails(); _nextBowlerDetailAt=_detailsClock.Elapsed.TotalSeconds+60; }

            // This over ball tracker
            UpdateOverBallsDisplay(inn);
            int capacity = Math.Max(4, runsStr.Length);
            if (capacity != _primaryRunCapacity) { _primaryRunCapacity = capacity; FitPrimaryScore(); }
            FitLongNumericFields();

            // Logo
            SetLogo(match);

            // Info tile
            UpdateInfoTile(match);


        }

        // ---- Info tile: slow, automatic rotation independent of scoring ----

        private void CancelInfoFade()
        {
            ++_infoGeneration;
            InfoLabel.BeginAnimation(OpacityProperty, null);
            InfoValue.BeginAnimation(OpacityProperty, null);
            InfoLabel.Opacity = 1;
            // An active same-category digit reel owns the value's base opacity.
        }

        private void UpdateInfoTile(CricketMatchManager match)
        {
            var inn = match.CurrentInnings;
            if (inn == null) return;

            bool chase=!match.IsMatchFinalized && match.Format==CricketFormat.LimitedOvers && match.CurrentInningsNumber>1 && match.Target is not null && match.BreakKind==CricketBreakKind.None && !match.FreeHitPending;
            if(!chase) { _chaseMiniTimer.Stop(); _chaseMiniShowing=false; }
            var (newLabel,newValue)=_chaseMiniShowing ? SelectChaseMiniContext(match,_infoRotationIndex-1) : SelectRailContext(match,_infoRotationIndex);

            if ((InfoLabel.Text ?? string.Empty) == newLabel)
            {
                // A pending category change may now be obsolete (for example,
                // Rain was cancelled before its BREAK caption appeared).
                CancelInfoFade();
                FitContextValue(newLabel,newValue);
                RollField(InfoValue,newValue);
                return;
            }
            StopReels(InfoValue);
            int generation = ++_infoGeneration;
            // Crossfade only when the category rotates, not on every ball.
            if ((InfoLabel.Text ?? string.Empty) != newLabel || (InfoValue.Text ?? string.Empty) != newValue)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
                fadeOut.Completed += (_, __) =>
                {
                    if (generation != _infoGeneration || _isDisposed) return;
                    InfoLabel.Text = newLabel;
                    FitContextValue(newLabel,newValue);
                    InfoValue.Text = newValue;
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    InfoLabel.BeginAnimation(OpacityProperty, fadeIn);
                    InfoValue.BeginAnimation(OpacityProperty, fadeIn);
                };
                InfoLabel.BeginAnimation(OpacityProperty, fadeOut);
                InfoValue.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        private void FitContextValue(string label,string value)
        {
            if(InfoValuePanel.NativeWidth()<=0) return;
            string sample=label=="PARTNERSHIP" ? "888(888)" : value.Length>5 ? value : "888.88";
            var measured=new NativeFormattedText(sample,System.Globalization.CultureInfo.InvariantCulture,Avalonia.Media.FlowDirection.LeftToRight,
                new Typeface(CricketBroadcastStyle.Typeface,FontStyle.Normal,FontWeight.Bold,FontStretch.Normal),72,WhiteBrush);
            InfoValue.FontSize=Math.Min(72,Math.Max(24,(InfoValuePanel.NativeWidth()-8)/Math.Max(1,measured.WidthIncludingTrailingWhitespace)*72));
        }

        // Reserve the same digits at every score: layout changes may resize type, scoring never does.
        private void FitPrimaryScore()
        {
            if (WicketRow.NativeWidth() <= 0 || WicketRow.NativeHeight() <= 0 || ScoreRuns.Parent is not Control runCell) return;
            NativeFormattedText Measure(TextBlock text, string sample, double size) => new(sample,
                System.Globalization.CultureInfo.InvariantCulture, Avalonia.Media.FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), size, WhiteBrush);
            // Font metrics scale linearly. Three measurements replace hundreds of
            // trial sizes on every frame of an overlay's height animation.
            var wickets = Measure(ScoreWickets, "88", 100); var word = Measure(ScoreSlash, "FOR", 70);
            var runs = Measure(ScoreRuns, new string('8', _primaryRunCapacity), 100);
            double VerticalFit(NativeFormattedText sample,double height)
            {
                var ink=sample.BuildGeometry(new Avalonia.Point()).Bounds;
                double reach=Math.Max(sample.Height/2-ink.Top,ink.Bottom-sample.Height/2);
                return (height-4)/(2*Math.Max(1,reach))*100;
            }
            // Arial's line box contains large blank ascender/descender margins.
            // Fit the painted digits around the centred baseline, rather than
            // shrinking them to reserve whitespace that a numeral never uses.
            double fit = Math.Min(160, Math.Min(
                    Math.Min((WicketRow.NativeWidth()-18)/(wickets.Width+word.Width)*100, VerticalFit(wickets,WicketRow.NativeHeight())),
                    Math.Min((ScoreStack.NativeWidth()-4)/runs.Width*100, VerticalFit(runs,runCell.NativeHeight()))));
            double size = Math.Clamp(Math.Floor(fit*2)/2,8,160);
            // Reserve both wicket digits so FOR never moves at 9 -> 10.
            ScoreWickets.MinWidth = wickets.Width * size / 100;
            if (Math.Abs(ScoreRuns.FontSize-size) < .1) return;
            ScoreRuns.FontSize = ScoreWickets.FontSize = size; ScoreSlash.FontSize = size*.70;
        }

        // Ordinary cricket figures keep their design size. Exceptionally long
        // manual corrections still have to fit their existing numeric cell.
        // No adjacent label/name is measured or resized by this guard.
        private void FitLongNumericFields()
        {
            Fit(OversText, 5, 72); Fit(ExtrasValue, 4, 72);
            Fit(StrikerRuns, 4, 65); Fit(NonStrikerRuns, 4, 65);
            Fit(StrikerBalls, 4, 40); Fit(NonStrikerBalls, 4, 40);
            Fit(BowlerFigures, 7, 56); Fit(BowlerOvers, 4, 38);
            void Fit(TextBlock number, int reservedCharacters, double designSize)
            {
                if (number.Parent is not Control cell || cell.NativeWidth() <= 0) return;
                double size = designSize;
                string value = (number.Text ?? string.Empty) ?? "";
                if (value.Length > reservedCharacters)
                {
                    var sample = new NativeFormattedText(new string('8', value.Length), System.Globalization.CultureInfo.InvariantCulture,
                        Avalonia.Media.FlowDirection.LeftToRight, new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch),
                        designSize, WhiteBrush);
                    size = Math.Max(8, Math.Min(designSize, (cell.NativeWidth() - 6) / Math.Max(1, sample.WidthIncludingTrailingWhitespace) * designSize));
                }
                if (Math.Abs(number.FontSize - size) > .05) number.FontSize = size;
            }
        }

        private void StartInfoRotation()
        {
            _infoRotationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            _infoRotationTimer.Tick += (_, __) =>
            {
                _infoRotationIndex++;
                if (_lastMatch != null && !EventShowing && !MomentAwaitingLayout)
                {
                    if(!_lastMatch.IsMatchFinalized && !_overlayCompact && _lastMatch.Format==CricketFormat.LimitedOvers && _lastMatch.CurrentInningsNumber>1 && _lastMatch.Target is not null && _lastMatch.BreakKind==CricketBreakKind.None)
                    { _chaseMiniShowing=true; _chaseMiniTimer.Stop(); _chaseMiniTimer.Start(); }
                    UpdateInfoTile(_lastMatch);
                }
            };
            _infoRotationTimer.Start();
        }

        private void UpdateOverBallsDisplay(CricketInnings inn)
        {
            bool added = _lastBallCount >= 0 && inn.Deliveries.Count > _lastBallCount;
            _lastBallCount = inn.Deliveries.Count;
            string[] slots = CricketOverTracker.CreateSlots(inn);
            if (_displayedBalls.SequenceEqual(slots)) return;
            _ballGeneration++;
            if (_clearingBalls)
            {
                _clearingBalls = false; _displayedBalls.Clear(); CurrentOverBallsPanel.Children.Clear();
            }
            bool clearing = _displayedBalls.Any(value => !string.IsNullOrEmpty(value)) && inn.CurrentOverBalls.Count == 0;
            if (clearing && IsVisible && NativeSystemPreferences.ClientAreaAnimation && CurrentOverBallsPanel.Children.Count > 0)
            {
                FoldCompletedOver(slots, _ballGeneration);
                _displayedBalls.Clear(); _displayedBalls.AddRange(slots);
                return;
            }
            while (CurrentOverBallsPanel.Children.Count > slots.Length)
                CurrentOverBallsPanel.Children.RemoveAt(CurrentOverBallsPanel.Children.Count - 1);
            for (int i = 0; i < slots.Length; i++)
            {
                if (i < _displayedBalls.Count && _displayedBalls[i] == slots[i] && i < CurrentOverBallsPanel.Children.Count) continue;
                var pip = CreateBallCircle(slots[i]);
                if (i < CurrentOverBallsPanel.Children.Count) CurrentOverBallsPanel.Children.RemoveAt(i);
                CurrentOverBallsPanel.Children.Insert(i, pip);
                if (clearing && NativeSystemPreferences.ClientAreaAnimation)
                {
                    var scale = (ScaleTransform)pip.RenderTransform;
                    var settle = new DoubleAnimation(.05, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(i * 45), FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
                }
                else if (NativeSystemPreferences.ClientAreaAnimation && added && i == inn.CurrentOverBalls.Count - 1)
                {
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    var scale = (ScaleTransform)pip.RenderTransform;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.7, 1, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
                    pip.BeginAnimation(OpacityProperty, new DoubleAnimation(.15, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
                }
            }
            _displayedBalls.Clear(); _displayedBalls.AddRange(slots);
        }

        private void FoldCompletedOver(string[] slots, int generation)
        {
            _clearingBalls = true;
            var existing = CurrentOverBallsPanel.Children.OfType<Border>().ToArray();
            for (int i = 0; i < existing.Length; i++)
            {
                var close = new DoubleAnimation(1, .03, TimeSpan.FromMilliseconds(240))
                { BeginTime = TimeSpan.FromMilliseconds(i * 18), FillBehavior = FillBehavior.HoldEnd,
                  EasingFunction = BroadcastMotion.CreateOverlayEase() };
                if (i == existing.Length - 1) close.Completed += (_, _) =>
                {
                    if (generation != _ballGeneration || _isDisposed) return;
                    CurrentOverBallsPanel.Children.Clear(); _clearingBalls = false;
                    for (int j = 0; j < slots.Length; j++)
                    {
                        var empty = CreateBallCircle(slots[j]); ((ScaleTransform)empty.RenderTransform).ScaleY=.03; empty.Opacity=0; CurrentOverBallsPanel.Children.Add(empty);
                        empty.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(360)) { BeginTime=TimeSpan.FromMilliseconds(j*18),FillBehavior=FillBehavior.HoldEnd });
                        ((ScaleTransform)empty.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty,
                            new DoubleAnimation(.03, 1, TimeSpan.FromMilliseconds(360))
                            { BeginTime = TimeSpan.FromMilliseconds(j * 18), FillBehavior = FillBehavior.HoldEnd,
                              EasingFunction = BroadcastMotion.CreateOverlayEase() });
                    }
                };
                ((ScaleTransform)existing[i].RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, close);
            }
        }

        private static Border CreateBallCircle(string ball)
        {
            Color fill = ball switch { "W" => Color.FromRgb(220, 38, 38), "4" => Color.FromRgb(21, 128, 61), "6" => Color.FromRgb(29, 78, 216), _ => Color.FromRgb(5, 8, 12) };
            bool extra = ball.Contains("wd", StringComparison.OrdinalIgnoreCase) || ball.Contains("nb", StringComparison.OrdinalIgnoreCase);
            if(ball.StartsWith("W",StringComparison.Ordinal)) fill=Color.FromRgb(180,30,35);
            var content = new Grid();
            if (ball == "•" || ball == "0") content.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 17, Height = 17, Fill = WhiteBrush, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else content.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(5), Child = new NativeTextBlock { Text = ball, Foreground = extra ? new SolidColorBrush(Colors.Orange) : WhiteBrush, FontSize = 42, FontWeight = FontWeight.Bold, FontFamily = CricketBroadcastStyle.Typeface, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            return new NativeBorder { Tag = ball, Width = 84, Height = 84, CornerRadius = new CornerRadius(42), Background = new SolidColorBrush(fill), BorderBrush = WhiteBrush, BorderThickness = new Thickness(3), Margin = new Thickness(4, 0, 4, 0), Child = content, RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative), RenderTransform = new ScaleTransform(1, 1) };
        }
        private string? _battingLogoPath;
        private string? _bowlingLogoPath;

        private void SetLogo(CricketMatchManager match)
        {
            bool teamA = match.BattingTeamName == match.TeamAName;
            string? batting = teamA ? match.TeamALogoPath : match.TeamBLogoPath;
            string? bowling = teamA ? match.TeamBLogoPath : match.TeamALogoPath;
            if (_battingLogoPath != batting)
            {
                _battingLogoPath = batting;
                BattingLogo.Source = TeamVisuals.LoadLogo(batting); CenterLogoBackground(BattingHeaderLogo,BattingLogo.Source,.22);
            }
            if (_bowlingLogoPath != bowling)
            {
                _bowlingLogoPath = bowling;
                CenterLogoBackground(BowlingHeaderLogo,TeamVisuals.LoadLogo(bowling),.22);
            }
        }

        private static void TrySetColor(SolidColorBrush brush, string hex)
        {
            try { brush.Color = (Color)ColorConverter.ConvertFromString(hex); }
            catch { }
        }

        private static Color SafeColor(string hex, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }

        private static SolidColorBrush SafeBrush(string hex, string fallback)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        }

        // Authoritative values are assigned immediately. No delayed text swaps,
        // timer rolls or completion callbacks can survive a correction/undo.
        private void AnimateScoreRoll(TextBlock element, TranslateTransform translate, int fromValue, int toValue, double height = 60)
        { if(RollingFiguresEnabled) ScoreRollVisual.Roll(element,fromValue,toValue); else { ScoreRollVisual.Stop(); element.Text=toValue.ToString(); } }

        private void StopReels(params TextBlock[] fields)
        { foreach(var field in fields) if(_numberReels.TryGetValue(field,out var reel)) reel.Stop(); }
        private void RollField(TextBlock element,string value)
        {
            if((element.Text ?? string.Empty)==value) return;
            if(!RollingFiguresEnabled) { StopReels(element); element.Text=value; return; }
            if(!_numberReels.TryGetValue(element,out var reel) && element.Parent is Grid cell)
            {
                reel=new RollingScoreVisual(); cell.Children.Add(reel); _numberReels[element]=reel;
            }
            if(reel==null) { element.Text=value; return; }
            reel.RollText(element,(element.Text ?? string.Empty),value);
        }
        private void ScrollValue(TextBlock element,TranslateTransform translate,string newValue,double height=28)
        {
            translate.BeginAnimation(TranslateTransform.YProperty,null); translate.Y=0;
            element.BeginAnimation(OpacityProperty,null);
            RollField(element,newValue);
        }
}

public partial class CricketScorebugControl
{
    private SolidColorBrush BattingTeamBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["BattingTeamBg"];
    private TranslateTransform ScoreWicketsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ScoreWicketsTranslate"];
    private TranslateTransform ScoreRunsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["ScoreRunsTranslate"];
    private TranslateTransform OversTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["OversTranslate"];
    private SolidColorBrush BattingHeaderBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["BattingHeaderBg"];
    private SolidColorBrush StrikerBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["StrikerBorder"];
    private SolidColorBrush BattingIndicatorBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["BattingIndicatorBrush"];
    private TranslateTransform StrikerRunsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["StrikerRunsTranslate"];
    private TranslateTransform StrikerBallsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["StrikerBallsTranslate"];
    private SolidColorBrush NonStrikerBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["NonStrikerBorder"];
    private SolidColorBrush NonStrikerIndicatorBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["NonStrikerIndicatorBrush"];
    private TranslateTransform NonStrikerRunsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["NonStrikerRunsTranslate"];
    private TranslateTransform NonStrikerBallsTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["NonStrikerBallsTranslate"];
    private SolidColorBrush BowlingHeaderBg => (SolidColorBrush)ProductionNames.RegistryFor(this)["BowlingHeaderBg"];
    private SolidColorBrush BowlerBorder => (SolidColorBrush)ProductionNames.RegistryFor(this)["BowlerBorder"];
    private TranslateTransform BowlerFiguresTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["BowlerFiguresTranslate"];
    private TranslateTransform BowlerOversTranslate => (TranslateTransform)ProductionNames.RegistryFor(this)["BowlerOversTranslate"];
    private SolidColorBrush BowlingIndicatorBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["BowlingIndicatorBrush"];
    private SolidColorBrush PrevBowlerBorderBrush => (SolidColorBrush)ProductionNames.RegistryFor(this)["PrevBowlerBorderBrush"];
}
