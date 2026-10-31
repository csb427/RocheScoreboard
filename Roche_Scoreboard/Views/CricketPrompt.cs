using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Panel = System.Windows.Controls.Panel;

namespace Roche_Scoreboard.Views;

/// <summary>An in-app decision sheet. The live match never waits on a native message loop.</summary>
internal static class CricketPrompt
{
    internal static DataTemplate ButtonContent { get; } = CreateButtonContent();
    private static DataTemplate CreateButtonContent()
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        return new DataTemplate { VisualTree = label };
    }
    private static readonly Dictionary<Panel, List<Action>> Active = new();
    internal static void CancelAll(Panel host)
    { if (Active.TryGetValue(host, out var actions)) foreach (var action in actions.ToArray()) action(); }
    internal sealed record Choice(string Key, string Label, bool Primary = false, bool Destructive = false);
    internal static Task<string?> ChooseAsync(Panel host, string title, string message, params Choice[] choices)
        => ShowAsync(host, title, message, null, choices);
    internal static Task<string?> ShowAsync(Panel host, string title, string message, UIElement? body, params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,null,null,null,choices);
    internal static Task<string?> ShowValidatedAsync(Panel host,string title,string message,UIElement body,Func<bool> validate,params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,null,null,validate,choices);
    internal static Task<string?> ShowCorrectionAsync(Panel host,string title,string message,UIElement body,FrameworkElement summary,FrameworkElement? source,Func<bool> validate,params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,summary,source,validate,choices);
    private static Task<string?> ShowCoreAsync(Panel host,string title,string message,UIElement? body,FrameworkElement? summary,FrameworkElement? source,Func<bool>? validate,Choice[] choices)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousFocus = Keyboard.FocusedElement;
        var veil = new Border { Tag = "cricket-decision-sheet", Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Padding = new Thickness(16), Focusable = true };
        OperatorDesign.SetIsEnabled(veil, true);
        var surface = new OperatorGlassSurface { Material = GlassMaterial.Regular, GlassSource = host, MaxWidth = 720, CornerRadius = new CornerRadius(30), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(104, 255, 255, 255)), Background = new SolidColorBrush(Color.FromArgb(140, 33, 35, 43)), RenderTransformOrigin = new System.Windows.Point(.5, .08) };
        surface.SetResourceReference(Border.BackgroundProperty, "ControlCentre.GlassBrush");
        var scale = new ScaleTransform(1, 1); var shift = new TranslateTransform();
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(shift); surface.RenderTransform = transform;
        ExpansionOrigin correctionOrigin=ExpansionOrigin.Default;
        bool correctionStarted=false;
        if(summary is not null){surface.Tag="scorecard-correction-surface";surface.RenderTransformOrigin=new System.Windows.Point(.5,.5);}
        System.Windows.Controls.Panel.SetZIndex(veil, 200); Grid.SetRowSpan(veil, 20); Grid.SetColumnSpan(veil, 20);
        var stack = new StackPanel { Margin = new Thickness(26) };
        ScrollViewer? formScroll = null;
        Grid? formLayout = null;
        var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titleText = new TextBlock { Text = title, FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0), ToolTip = message };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        heading.Children.Add(titleText); stack.Children.Add(heading);
        if(summary is not null)
        {
            heading.ColumnDefinitions.Insert(0,new ColumnDefinition{Width=GridLength.Auto});Grid.SetColumn(titleText,1);
            var symbol=new OperatorSymbol{Kind="scorecard",Width=25,Height=25,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");heading.Children.Add(symbol);
            symbol.Loaded+=(_,_)=>symbol.Activate();
        }
        TextBlock? messageText = null;
        if (!string.IsNullOrWhiteSpace(message)) { messageText = new TextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 201)), FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18) }; messageText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush"); stack.Children.Add(messageText); }
        if(summary is not null){summary.Margin=new Thickness(0,10,0,8);stack.Children.Add(summary);}
        if (body is not null) stack.Children.Add(body);
        var actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; stack.Children.Add(actions);
        FrameworkElement actionsSurface=actions;
        bool finished = false, closing = false;
        string? closingResult=null;
        void Complete(string? key)
        {
            if (finished) return; finished = true; host.SizeChanged -= Resize; host.Unloaded -= HostUnloaded;
            OperatorAppearance.Changed-=PreferencesChanged;
            veil.BeginAnimation(UIElement.OpacityProperty, null); surface.BeginAnimation(UIElement.OpacityProperty, null); scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null);shift.BeginAnimation(TranslateTransform.XProperty,null);
            stack.BeginAnimation(UIElement.OpacityProperty,null);stack.Opacity=1;
            if(formScroll is not null){formScroll.BeginAnimation(UIElement.OpacityProperty,null);formScroll.Opacity=1;}
            if(summary is not null){summary.SizeChanged-=Resize;stack.Children.Remove(summary);}
            if (body is not null) { stack.Children.Remove(body); if (formScroll is not null) formScroll.Content = null; }
            host.Children.Remove(veil); if (Active.TryGetValue(host, out var active)) { active.Remove(Cancel); if (active.Count == 0) Active.Remove(host); }
            completion.TrySetResult(key);
            host.Dispatcher.BeginInvoke(new Action(() =>
            {
                // A following sheet owns focus. A stale completion must never
                // steal it from that sheet or another application window.
                if(!host.IsLoaded || Active.ContainsKey(host) || Window.GetWindow(host) is not { IsActive: true })return;
                if(Keyboard.FocusedElement is UIElement {IsVisible:true,IsEnabled:true,Focusable:true} current && !ReferenceEquals(current,previousFocus) && host.IsAncestorOf(current))return;
                if(previousFocus is UIElement {IsVisible:true,IsEnabled:true,Focusable:true} target && Window.GetWindow(target)==Window.GetWindow(host))
                {FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target),target);Keyboard.Focus(target);}
            }),System.Windows.Threading.DispatcherPriority.Input);
        }
        void Finish(string? key)
        {
            if (finished || closing) return;
            // Invalid input stays in the same owned sheet, so the
            // repaired field keeps focus instead of a close/reopen resetting it.
            if(key=="apply" && validate is not null && !validate())return;
            if (!OperatorMotion.Enabled || !host.IsLoaded || !veil.IsLoaded) { Complete(key); return; }
            closing = true;closingResult=key; surface.IsEnabled = false;
            double currentY = shift.Y; shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = currentY;
            double currentOpacity = surface.Opacity; surface.BeginAnimation(UIElement.OpacityProperty, null); surface.Opacity = currentOpacity;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, summary is null ? .94 : correctionOrigin.ScaleX, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, summary is null ? .94 : correctionOrigin.ScaleY, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(currentY, summary is null ? 12 : correctionOrigin.OffsetY, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            if(summary is not null)
            {
                shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(shift.X,correctionOrigin.OffsetX,OperatorMotion.SurfaceClose){EasingFunction=OperatorMotion.SmoothOut});
                formScroll?.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,OperatorMotion.Press));
            }
            var fade = new DoubleAnimation(0, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) => Complete(key);
            veil.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        void Cancel() => Complete(null);
        void PreferencesChanged()
        {
            if(finished || OperatorMotion.Enabled)return;
            if(closing){Complete(closingResult);return;}
            foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty})scale.BeginAnimation(property,null);
            shift.BeginAnimation(TranslateTransform.XProperty,null);shift.BeginAnimation(TranslateTransform.YProperty,null);shift.X=shift.Y=0;
            stack.BeginAnimation(UIElement.OpacityProperty,null);stack.Opacity=1;
            formScroll?.BeginAnimation(UIElement.OpacityProperty,null);if(formScroll is not null)formScroll.Opacity=1;
            surface.BeginAnimation(UIElement.OpacityProperty,null);surface.Opacity=1;
            veil.BeginAnimation(UIElement.OpacityProperty,null);veil.Opacity=1;
        }
        if (!Active.TryGetValue(host, out var registered)) Active[host] = registered = new(); registered.Add(Cancel);
        var explicitClose = choices.FirstOrDefault(c => c.Key == "close");
        var back = explicitClose is not null ? choices.FirstOrDefault(c => c.Key == "back") : null;
        var dismiss = explicitClose ?? choices.FirstOrDefault(c => c.Key is "back" or "cancel");
        if (back is not null)
        {
            heading.ColumnDefinitions.Insert(0,new ColumnDefinition { Width=GridLength.Auto });
            Grid.SetColumn(titleText,1);
            var backButton = new Button { Tag = "back", Content = "←", ToolTip = back.Label, Style = veil.TryFindResource("ControlCentre.Button") as Style,
                Width=34, Height=34, MinHeight=0, FontSize=22, Padding = new Thickness(0), Margin=new Thickness(0,0,10,0), Background = Brushes.Transparent, Foreground = Brushes.LightSkyBlue, BorderThickness = new Thickness(0) };
            System.Windows.Automation.AutomationProperties.SetName(backButton,back.Label);
            backButton.Click += (_, _) => Finish("back"); heading.Children.Add(backButton);
        }
        var close = new Button { Tag = dismiss?.Key ?? "dismiss-sheet", Content = "×", ToolTip = dismiss?.Label ?? "Close", Width = 36, Height = 36, MinHeight = 0, FontSize = 24, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(51, 51, 57)), BorderBrush = new SolidColorBrush(Color.FromRgb(74, 74, 82)), Padding = new Thickness(0) };
        if (veil.TryFindResource("ControlCentre.Button") is Style closeStyle) close.Style = closeStyle;
        System.Windows.Automation.AutomationProperties.SetName(close,dismiss?.Label??"Close");
        Grid.SetColumn(close, heading.ColumnDefinitions.Count-1); heading.Children.Add(close); close.Click += (_, _) => Finish(dismiss?.Key);
        foreach (var choice in choices.Where(c => !ReferenceEquals(c, dismiss) && !ReferenceEquals(c, back)).OrderByDescending(c => c.Primary))
        {
            var button = new Button
            {
                Tag = choice.Key,
                Content = choice.Label,
                IsDefault = choice.Primary && !choice.Destructive,
                MinHeight = choice.Primary ? 52 : 46,
                Margin = new Thickness(0, choice.Destructive ? 14 : 0, 0, 8),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(12, 8, 12, 8),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(choice.Destructive ? Color.FromRgb(83, 27, 35) : choice.Primary ? Color.FromRgb(26, 64, 98) : Color.FromRgb(26, 35, 47)),
                BorderBrush = new SolidColorBrush(choice.Destructive ? Color.FromRgb(220, 110, 115) : Color.FromRgb(100, 150, 195))
            };
            if (veil.TryFindResource(choice.Primary ? "ControlCentre.PrimaryButton" : "ControlCentre.Button") is Style actionStyle) button.Style=actionStyle;
            ApplyActionColour(button,choice.Key,choice.Primary,choice.Destructive);
            button.ContentTemplate = ButtonContent;
            button.Click += (_, _) => Finish(choice.Key); actions.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (body is null) surface.Child = scroll;
        else
        {
            // Keep the title, close control and commit action visible while a
            // long roster or editor scrolls independently between them.
            stack.Children.Remove(body); stack.Children.Remove(actions); scroll.Content = null;
            if(summary is not null && dismiss is not null)
            {
                var footer=new Grid();footer.ColumnDefinitions.Add(new(){Width=new GridLength(100)});footer.ColumnDefinitions.Add(new(){Width=new GridLength(8)});footer.ColumnDefinitions.Add(new());
                var cancelButton=new Button{Content=dismiss.Label,Tag="scorecard-correction-cancel",MinHeight=48,Padding=new Thickness(10,8,10,8),ContentTemplate=ButtonContent};
                cancelButton.SetResourceReference(FrameworkElement.StyleProperty,"ControlCentre.Button");System.Windows.Automation.AutomationProperties.SetName(cancelButton,dismiss.Label);
                cancelButton.Click+=(_,_)=>Finish(dismiss.Key);footer.Children.Add(cancelButton);
                foreach(var action in actions.Children.OfType<Button>())action.Margin=new Thickness(0);
                actions.Margin=new Thickness(0);Grid.SetColumn(actions,2);footer.Children.Add(actions);actionsSurface=footer;
            }
            formLayout = new Grid { Margin = new Thickness(18) }; stack.Margin = new Thickness(0);
            formLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            // An Auto row contributes the editor's real desired height. A star
            // row inside this auto-height sheet can collapse a lone text field.
            formLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            formLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            formLayout.Children.Add(stack);
            formScroll = new ScrollViewer { Tag = "cricket-prompt-body", Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(formScroll,1); formLayout.Children.Add(formScroll);
            Grid.SetRow(actionsSurface,2); formLayout.Children.Add(actionsSurface);
            // Normally the body alone scrolls. At exceptionally short heights,
            // this outer viewport keeps the title and actions reachable as well.
            scroll.Content = formLayout; surface.Child = scroll;
        }
        veil.Child = surface;
        void Resize(object? sender, SizeChangedEventArgs e)
        {
            surface.MaxHeight = Math.Max(160, host.ActualHeight - 32);
            surface.MaxWidth = Math.Max(1, Math.Min(720, host.ActualWidth - 32));
            if (formLayout is null) return;
            bool compact = host.ActualHeight < 480;
            formLayout.Margin = new Thickness(compact ? 12 : 18);
            titleText.FontSize = compact ? 21 : 25;
            if (messageText is not null) messageText.Visibility = compact || summary is not null ? Visibility.Collapsed : Visibility.Visible;
            actionsSurface.Margin = new Thickness(0, compact ? 8 : 14, 0, 0);
            if (formScroll is not null)
            {
                double width = Math.Max(1, surface.MaxWidth - formLayout.Margin.Left - formLayout.Margin.Right);
                stack.Measure(new System.Windows.Size(width, double.PositiveInfinity));
                actionsSurface.Measure(new System.Windows.Size(width, double.PositiveInfinity));
                double bodySpace = surface.MaxHeight - formLayout.Margin.Top - formLayout.Margin.Bottom - stack.DesiredSize.Height - actionsSurface.DesiredSize.Height;
                bool scrollWholeForm = bodySpace < (summary is null?72:48);
                formScroll.MaxHeight = scrollWholeForm ? double.PositiveInfinity : bodySpace;
                if(summary is not null)formScroll.Height=scrollWholeForm?double.NaN:bodySpace;
                formScroll.VerticalScrollBarVisibility = scrollWholeForm ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            }
        }
        void HostUnloaded(object sender, RoutedEventArgs args) => Complete(null);
        host.SizeChanged += Resize; host.Unloaded += HostUnloaded;if(summary is not null)summary.SizeChanged+=Resize; Resize(null, null!);
        veil.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Finish(null); } };
        KeyboardNavigation.SetTabNavigation(veil, KeyboardNavigationMode.Cycle);
        void StartCorrectionEntry()
        {
            if(correctionStarted || finished || closing)return;
            surface.UpdateLayout();if(surface.ActualWidth<1 || surface.ActualHeight<1)return;
            correctionStarted=true;
            correctionOrigin=ExpansionOrigin.From(surface,source??previousFocus as FrameworkElement).WithTravel(OperatorMotion.TravelScale);
            if(!OperatorMotion.Enabled)return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(correctionOrigin.ScaleX,1,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(correctionOrigin.ScaleY,1,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(correctionOrigin.OffsetX,0,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(correctionOrigin.OffsetY,0,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            stack.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){BeginTime=OperatorMotion.Stagger,FillBehavior=FillBehavior.Stop});
            formScroll?.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){BeginTime=TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks*2),FillBehavior=FillBehavior.Stop});
            veil.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop});
        }
        // Keep the current sheet subscribed through its closing flight. Complete
        // balances this for every result, host unload and explicit cancellation.
        OperatorAppearance.Changed+=PreferencesChanged;
        if(summary is not null)surface.Loaded+=(_,_)=>StartCorrectionEntry();
        host.Children.Add(veil);
        if(summary is not null && surface.IsLoaded)StartCorrectionEntry();
        if(summary is null && previousFocus is FrameworkElement trigger && host.IsAncestorOf(trigger))
        {
            surface.Loaded+=(_,_)=>
            {
                if(finished || closing || surface.ActualWidth<1 || surface.ActualHeight<1)return;
                var origin=trigger.TranslatePoint(new System.Windows.Point(trigger.ActualWidth/2,trigger.ActualHeight/2),surface);
                surface.RenderTransformOrigin=new System.Windows.Point(Math.Clamp(origin.X/surface.ActualWidth,.12,.88),Math.Clamp(origin.Y/surface.ActualHeight,.08,.92));
            };
        }
        if (summary is null && OperatorMotion.Enabled)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.94, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.82, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.65, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
            veil.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        }
        veil.Dispatcher.BeginInvoke(new Action(() =>
        {
            void FocusAction(Button target)
            { FocusManager.SetFocusedElement(FocusManager.GetFocusScope(veil),target);target.Focus(); }
            if(finished || closing) return;
            if(body is FrameworkElement form && form.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) return;
            var primary=choices.FirstOrDefault(c=>c.Primary);
            if((primary is not null && ReferenceEquals(primary,dismiss)) || choices.Where(c=>!ReferenceEquals(c,dismiss)).All(c=>c.Destructive)) {FocusAction(close);return;}
            var first=actions.Children.OfType<Button>().FirstOrDefault(b=>Equals(b.Tag,primary?.Key))??actions.Children.OfType<Button>().FirstOrDefault();
            FocusAction(first??close);
        }));
        return completion.Task;
    }
    internal static void ApplyActionColour(Button button,string key,bool primary=false,bool destructive=false)
    {
        var accent=destructive ? Color.FromRgb(231,113,124) : key switch
        { "break"=>Color.FromRgb(232,183,82),"teams"=>Color.FromRgb(78,208,190),"scorecard" or "figures"=>Color.FromRgb(180,157,247),"resume" or "finish"=>Color.FromRgb(93,218,152),_=>Color.FromRgb(112,181,250) };
        if (primary && !destructive)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(243, 243, 247));
            button.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 22));
        }
        else
        {
            var start = Color.FromRgb((byte)(35 + accent.R * .10), (byte)(35 + accent.G * .10), (byte)(38 + accent.B * .10));
            button.Background = new LinearGradientBrush(start, Color.FromRgb(30, 30, 34), 90);
            button.Foreground = destructive ? new SolidColorBrush(Color.FromRgb(255, 163, 170)) : Brushes.White;
        }
        button.BorderBrush = new SolidColorBrush(primary ? accent : Color.FromArgb(150, accent.R, accent.G, accent.B));
        button.BorderThickness = new Thickness(1);
        button.FontSize = 15;
    }
}
