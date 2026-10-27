using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Roche_Scoreboard.Views;

public partial class SportSelectionScreen
{
    private readonly Dictionary<Border,Button> _launchActions=new();
    private readonly ScaleTransform _settingsContentScale=new(1,1);
    private int _selectionGeneration, _landingMotionGeneration, _changelogMotionGeneration;
    private IInputElement? _changelogPreviousFocus;
    private bool _entryPlayed;
    private readonly Dictionary<Border, HomeTileMotion> _homeTiles = new();
    private HomeLensMotion? _homeLens;
    private HomeSheetMotion? _releaseSheetMotion;
    private int _lastPrimarySport;

    private void InitializeLaunchInteractions()
    {
        void Install(Border card,SportMode mode,string name,string label,int tab)
        {
            if(card.Child is not UIElement content) return;
            card.Child=null;
            if(mode==SportMode.Settings && content is FrameworkElement settings)
            {settings.RenderTransformOrigin=new System.Windows.Point(.5,.5);settings.RenderTransform=_settingsContentScale;}
            var layer=new Grid();layer.Children.Add(content);
            var action=new Button {Name=name,Style=(Style)FindResource("SportsLaunchSurface"),ToolTip=label+" · Enter or Space"};
            OperatorFeedback.SetManaged(action,true);
            action.SetBinding(ToolTipService.IsEnabledProperty,new System.Windows.Data.Binding{Source=action,Path=new PropertyPath(OperatorGuidance.ShowHintsProperty)});
            AutomationProperties.SetName(action,label);KeyboardNavigation.SetTabIndex(action,tab);
            action.Click+=(_,_)=>ActivateSport(mode);
            action.PreviewKeyDown+=(_,args)=>
            {
                if(args.Key==Key.Enter) {if(!args.IsRepeat)ActivateSport(mode);args.Handled=true;return;}
                if(mode==SportMode.Settings || args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
                var cards=new[] {AflCard,CricketCard,TrainingCard};int index=Array.IndexOf(cards,card);
                int offset=args.Key is Key.Left or Key.Up ? -1 : 1;
                int next=_sportCardsStacked==true || _sportCardsAcross==true?(index+offset+cards.Length)%cards.Length:args.Key switch
                {Key.Down=>2,Key.Up=>index==2?_lastPrimarySport:2,Key.Left=>index==2?0:1-index,_=>index==2?1:1-index};
                _launchActions[cards[next]].Focus();args.Handled=true;
            };
            action.GotKeyboardFocus+=(_,_)=>FocusLaunch(card,true);
            action.LostKeyboardFocus+=(_,_)=> {if(!card.IsMouseOver)FocusLaunch(card,false);};
            _launchActions[card]=action;RegisterName(name,action);layer.Children.Add(action);card.Child=layer;
        }
        Install(AflCard,SportMode.AFL,"AflLaunchAction","Select AFL",10);
        Install(CricketCard,SportMode.Cricket,"CricketLaunchAction","Select cricket",11);
        Install(TrainingCard,SportMode.Training,"TrainingLaunchAction","Select training",12);
        Install(SettingsTile,SportMode.Settings,"SettingsLaunchAction","Open app settings",0);
        _homeTiles[AflCard] = new HomeTileMotion(this, AflCard, AflIconScale, AflIconHover, AflGlow, AflHover);
        _homeTiles[CricketCard] = new HomeTileMotion(this, CricketCard, CricketIconScale, CricketIconHover, CricketGlow, CricketHover);
        _homeTiles[TrainingCard] = new HomeTileMotion(this, TrainingCard, TrainingIconMotionScale, TrainingIconMotionShift, TrainingGlassEdge, TrainingHover);
        _homeLens = new HomeLensMotion(this);
        _releaseSheetMotion = new HomeSheetMotion(this);
        KeyboardNavigation.SetTabIndex(ResumeSavedMatchButton,1);KeyboardNavigation.SetTabIndex(ChangelogToggle,20);
        Unloaded+=(_,_)=>SuspendLandingInteractions();
        IsVisibleChanged+=(_,_) =>
        {
            if(!IsVisible) SuspendLandingInteractions();
            else if(_entryPlayed) PrepareForDisplay();
        };
    }

    private void FocusLaunch(Border card,bool active)
    {
        if(active && card==AflCard)_lastPrimarySport=0;else if(active && card==CricketCard)_lastPrimarySport=1;
        var args=new MouseEventArgs(Mouse.PrimaryDevice,0);
        if(card==SettingsTile) {if(active)SettingsTile_MouseEnter(card,args);else SettingsTile_MouseLeave(card,args);}
        else if(card==TrainingCard) {if(active)TrainingTile_MouseEnter(card,args);else TrainingTile_MouseLeave(card,args);}
        else {if(active)Card_MouseEnter(card,args);else Card_MouseLeave(card,args);}
    }

    private void ActivateSport(SportMode mode)
    {
        if(!IsVisible || _changelogOpen || !TryBeginSelection()) return;
        int generation=_selectionGeneration;
        // Navigation is a command, not the completion of a decorative flash.
        // The destination owns its entrance and remains interactive immediately.
        CompleteSelection(mode,generation);
    }

    private void SuspendLandingInteractions()
    {
        ++_selectionGeneration;++_landingMotionGeneration;++_changelogMotionGeneration;
        _selectionTimer?.Stop();_selectionTimer=null;_selectionPending=false;IsHitTestVisible=true;
        foreach (var tile in _homeTiles.Values) tile.Reset();
        _homeLens?.Reset();
        _releaseSheetMotion?.Reset();
        RestoreLandingVisuals();
        ChangelogOverlay.BeginAnimation(OpacityProperty,null);ChangelogOverlay.Opacity=0;ChangelogOverlay.Visibility=Visibility.Collapsed;
        _changelogOpen=false;
        _changelogPreviousFocus=null;
    }

    private void RestoreLandingVisuals()
    {
        void Value(Animatable target,DependencyProperty property,double value) {target.BeginAnimation(property,null);target.SetValue(property,value);}
        void Element(UIElement target,double opacity) {target.BeginAnimation(OpacityProperty,null);target.Opacity=opacity;}
        foreach(var element in new UIElement[] {HeaderPanel,AflCard,CricketCard,TrainingCard,AflIconGroup,CricketIconGroup,AflTextPanel,CricketTextPanel,SettingsTile,ChangelogToggle,BottomAccent}) Element(element,1);
        foreach(var element in new UIElement[] {AflHover,CricketHover,AflFlash,CricketFlash,TrainingFlash})Element(element,0);
        foreach(var scale in new[] {AflCardScale,CricketCardScale,TrainingTileScale,SettingsTileScale,AflIconScale,CricketIconScale,TrainingContentScale,_settingsContentScale})
        {Value(scale,ScaleTransform.ScaleXProperty,1);Value(scale,ScaleTransform.ScaleYProperty,1);}
        foreach(var move in new[] {HeaderTranslate,AflCardTranslate,CricketCardTranslate,TrainingTileTranslate,SettingsTileTranslate,AflIconFloat,CricketIconFloat,AflIconHover,CricketIconHover,AflTextTranslate,CricketTextTranslate})
        {Value(move,TranslateTransform.XProperty,0);Value(move,TranslateTransform.YProperty,0);}
        Value(AflBeamTranslate,TranslateTransform.XProperty,-250);Value(CricketBeamTranslate,TranslateTransform.XProperty,-250);
        Value(SettingsTileGlow,System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,.12);Value(TrainingTileGlow,System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,.4);
        Element(AflGlow,.12);Element(CricketGlow,.12);
        foreach (var tile in _homeTiles.Values) tile.Reset();
        _homeLens?.Reset();
    }

    private void ReacquireLandingPointer()
    {
        int generation=_landingMotionGeneration;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>
        {
            if(!IsLoaded || !IsVisible || generation!=_landingMotionGeneration || _changelogOpen)return;
            foreach(var card in _homeTiles.Keys)if(card.IsMouseOver || _launchActions[card].IsKeyboardFocusWithin)EngageHomeTile(card,true);
        }));
    }

    private void EngageHomeTile(Border card, bool active)
    {
        if (_homeTiles.TryGetValue(card, out var tile)) tile.Engage(active);
        // Each card owns its edge. A second travelling full-card glass slab
        // painted square corners over the illustrated card while crossing rows.
    }

    private sealed class HomeSpring(double value)
    {
        public double Value = value, Target = value, Velocity;
        public void Set(double value) { Value = Target = value; Velocity = 0; }
        public bool Step(double delta, double response = 25)
        {
            double offset = Value - Target, coefficient = Velocity + response * offset;
            // This is the analytic spring solution: use the same elapsed time
            // in both terms, including when Windows delays a composition frame.
            delta=Math.Max(0,delta);
            double decay = Math.Exp(-response * delta);
            Value = Target + (offset + coefficient * delta) * decay;
            Velocity = (Velocity - response * coefficient * delta) * decay;
            if (Math.Abs(Value - Target) < .001 && Math.Abs(Velocity) < .008) { Set(Target); return false; }
            return true;
        }
    }

    private sealed class HomeTileMotion
    {
        private readonly SportSelectionScreen _owner;
        private readonly Border _card, _edge, _wash;
        private readonly ScaleTransform _scale;
        private readonly TranslateTransform _shift;
        private readonly HomeSpring _size = new(1), _x = new(0), _y = new(0), _light = new(.12), _white = new(0);
        private bool _pressed, _running;
        private double _last;
        public HomeTileMotion(SportSelectionScreen owner, Border card, ScaleTransform scale, TranslateTransform shift, Border edge, Border wash)
        {
            _owner = owner; _card = card; _scale = scale; _shift = shift; _edge = edge; _wash = wash;
            var action = owner._launchActions[card];
            action.PreviewMouseLeftButtonDown += (_, _) => { _pressed = true; Engage(true); };
            action.PreviewMouseLeftButtonUp += (_, _) => { _pressed = false; Engage(card.IsMouseOver || action.IsKeyboardFocusWithin); };
            action.LostMouseCapture += (_, _) => { _pressed = false; Engage(card.IsMouseOver || action.IsKeyboardFocusWithin); };
            action.PreviewKeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { _pressed = true; Engage(true); } };
            action.PreviewKeyUp += (_, _) => { _pressed = false; Engage(action.IsKeyboardFocusWithin); };
            action.LostKeyboardFocus += (_, _) => { _pressed = false; Engage(card.IsMouseOver); };
            action.MouseMove += (_, e) =>
            {
                if (!OperatorMotion.Enabled || !card.IsMouseOver || card.ActualWidth < 1 || card.ActualHeight < 1) return;
                var point = e.GetPosition(card);
                _x.Target = (point.X / card.ActualWidth - .5) * 5;
                _y.Target = -4 + (point.Y / card.ActualHeight - .5) * 4;
                Start();
            };
        }
        public void Engage(bool active)
        {
            _size.Target = _pressed ? .975 : active ? 1.035 : 1;
            _x.Target = 0; _y.Target = active ? -4 : 0;
            _light.Target = active ? .68 : .12; _white.Target = active ? .035 : 0;
            Start();
        }
        public void Enter()
        {
            if (!OperatorMotion.Enabled) return;
            _size.Value = .9; _size.Target = 1; _y.Value = 10; _y.Target = 0; Start();
        }
        private void Start()
        {
            if (!_owner.IsVisible || !_owner.IsLoaded) return;
            if (!OperatorMotion.Enabled)
            {
                _size.Set(1); _x.Set(0); _y.Set(0); _light.Set(_light.Target); _white.Set(_white.Target); Paint(); Stop(); return;
            }
            if (_running) return;
            _running = true; _last = Time(); CompositionTarget.Rendering += Frame;
        }
        private static double Time() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        private void Frame(object? sender, EventArgs e)
        {
            if (!_owner.IsVisible || !_owner.IsLoaded) { Reset(); return; }
            double now = Time(), delta = now - _last; _last = now;
            bool moving = _size.Step(delta) | _x.Step(delta) | _y.Step(delta) | _light.Step(delta) | _white.Step(delta);
            Paint(); if (!moving) Stop();
        }
        private void Paint() { _scale.ScaleX = _scale.ScaleY = _size.Value; _shift.X = _x.Value; _shift.Y = _y.Value; _edge.Opacity = Math.Clamp(_light.Value, 0, 1); _wash.Opacity = Math.Clamp(_white.Value, 0, 1); }
        private void Stop() { if (!_running) return; CompositionTarget.Rendering -= Frame; _running = false; }
        public void Reset() { Stop(); _pressed = false; _size.Set(1); _x.Set(0); _y.Set(0); _light.Set(.12); _white.Set(0); Paint(); }
    }

    private sealed class HomeLensMotion(SportSelectionScreen owner)
    {
        private readonly HomeSpring _x = new(0), _y = new(0), _width = new(0), _height = new(0), _opacity = new(0);
        private Border? _target;
        private bool _running, _shown;
        private double _last;
        public void Follow(Border card)
        {
            if (card.ActualWidth < 1 || card.ActualHeight < 1) return;
            _target = card;
            var origin = card.TranslatePoint(new System.Windows.Point(), owner.SportCardsGrid);
            _x.Target = origin.X; _y.Target = origin.Y; _width.Target = card.ActualWidth; _height.Target = card.ActualHeight; _opacity.Target = .35;
            if (!_shown) { _shown = true; _x.Set(_x.Target); _y.Set(_y.Target); _width.Set(_width.Target); _height.Set(_height.Target); }
            Start();
        }
        public void Hide() { _target = null; _opacity.Target = 0; Start(); }
        public void Reflow() { if (_target is { IsVisible: true } target) { Reset(); Follow(target); } }
        private void Start()
        {
            if (!OperatorMotion.Enabled) { _x.Set(_x.Target); _y.Set(_y.Target); _width.Set(_width.Target); _height.Set(_height.Target); _opacity.Set(_opacity.Target); Paint(); Stop(); return; }
            if (_running || !owner.IsLoaded || !owner.IsVisible) return;
            _running = true; _last = Time(); CompositionTarget.Rendering += Frame;
        }
        private static double Time() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        private void Frame(object? sender, EventArgs e)
        {
            if (!owner.IsVisible || !owner.IsLoaded) { Reset(); return; }
            double now = Time(), delta = now - _last; _last = now;
            bool moving = _x.Step(delta, 22) | _y.Step(delta, 22) | _width.Step(delta, 22) | _height.Step(delta, 22) | _opacity.Step(delta, 27);
            Paint(); if (!moving) { if (_opacity.Value == 0) _shown = false; Stop(); }
        }
        private void Paint() { owner.HomeLensTranslate.X = _x.Value; owner.HomeLensTranslate.Y = _y.Value; owner.HomeInteractionLens.Width = Math.Max(0, _width.Value); owner.HomeInteractionLens.Height = Math.Max(0, _height.Value); owner.HomeInteractionLens.Opacity = Math.Clamp(_opacity.Value, 0, 1); }
        private void Stop() { if (!_running) return; CompositionTarget.Rendering -= Frame; _running = false; }
        public void Reset() { Stop(); _target = null; _shown = false; _opacity.Set(0); Paint(); }
    }

    private sealed class HomeSheetMotion(SportSelectionScreen owner)
    {
        private readonly HomeSpring _progress = new(0);
        private bool _running;
        private double _last;
        private Action? _closed;
        private System.Windows.Threading.DispatcherTimer? _closeDeadline;
        public void Move(bool open, Action? closed = null)
        {
            _closeDeadline?.Stop();_closeDeadline=null;
            _closed = closed;
            _progress.Target = open ? 1 : 0;
            if (!OperatorMotion.Enabled)
            {
                _progress.Set(_progress.Target); Paint(); Stop();
                if (!open) { var complete = _closed; _closed = null; complete?.Invoke(); }
                return;
            }
            if(!open)
            {
                // Occluded Windows can stop receiving composition frames. A
                // retired sheet must still relinquish its input/focus scope.
                _closeDeadline=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(700)};
                _closeDeadline.Tick+=(_,_)=>{if(_progress.Target!=0)return;_progress.Set(0);Paint();Stop();var complete=_closed;_closed=null;complete?.Invoke();};
                _closeDeadline.Start();
            }
            if (_running) return;
            _running = true; _last = Time(); CompositionTarget.Rendering += Frame;
        }
        private static double Time() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        private void Frame(object? sender, EventArgs e)
        {
            if (!owner.IsVisible || !owner.IsLoaded) { Reset(); return; }
            double now = Time(), delta = now - _last; _last = now;
            bool moving = _progress.Step(delta, _progress.Target == 0 ? 48 : 30);
            Paint();
            if (!moving || (_progress.Target == 0 && _progress.Value < .003 && Math.Abs(_progress.Velocity) < .1))
            {
                _progress.Set(_progress.Target); Paint(); Stop();
                if (_progress.Target == 0) { var complete = _closed; _closed = null; complete?.Invoke(); }
            }
        }
        private void Paint()
        {
            double progress = Math.Clamp(_progress.Value, 0, 1);
            owner.ChangelogOverlay.Opacity = progress;
            owner.ChangelogPanelScale.ScaleX = owner.ChangelogPanelScale.ScaleY = .96 + progress * .04;
            owner.ChangelogPanelTranslate.Y = (1 - progress) * 24;
        }
        private void Stop() { _closeDeadline?.Stop();_closeDeadline=null;if (!_running) return; CompositionTarget.Rendering -= Frame; _running = false; }
        public void Reset() { Stop(); _closed = null; _progress.Set(0); Paint(); }
    }
}
