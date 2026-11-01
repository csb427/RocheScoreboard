// Generated from Views/CricketPrompt.cs; edit translator/adapters.
// Source SHA-256: 8cb2b031a926d753ceccbeedbecdc42ea8256e64525dc2bb7c583eac67919fa3
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Roche_Scoreboard.Services;
using Button = Avalonia.Controls.Button;
using TextBlock = Avalonia.Controls.TextBlock;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;
using Panel = Avalonia.Controls.Panel;

namespace Roche_Scoreboard.Views;

/// <summary>An in-app decision sheet. The live match never waits on a native message loop.</summary>
internal static class CricketPrompt
{
    internal static DataTemplate ButtonContent { get; } = CreateButtonContent();
    private static DataTemplate CreateButtonContent()
    {
        var label = new NativeElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Avalonia.Data.Binding());
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        return new DataTemplate { VisualTree = label };
    }
    private static readonly Dictionary<Panel, List<Action>> Active = new();
    internal static void CancelAll(Panel host)
    { if (Active.TryGetValue(host, out var actions)) foreach (var action in actions.ToArray()) action(); }
    internal sealed record Choice(string Key, string Label, bool Primary = false, bool Destructive = false, string? Detail = null);
    internal static Task<string?> ChooseAsync(Panel host, string title, string message, params Choice[] choices)
        => ShowAsync(host, title, message, null, choices);
    internal static Task<string?> ShowAsync(Panel host, string title, string message, Control? body, params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,null,null,null,choices);
    internal static Task<string?> ShowSourceFormAsync(Panel host,string title,string message,Control body,Control source,Func<bool> validate,params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,null,source,validate,choices);
    internal static Task<string?> ShowValidatedAsync(Panel host,string title,string message,Control body,Func<bool> validate,params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,null,null,validate,choices);
    internal static Task<string?> ShowCorrectionAsync(Panel host,string title,string message,Control body,Control summary,Control? source,Func<bool> validate,params Choice[] choices)
        =>ShowCoreAsync(host,title,message,body,summary,source,validate,choices);
    internal static Task<string?> ChooseWithContextAsync(Panel host,string title,string message,Control context,Control? source,params Choice[] choices)
        =>ShowCoreAsync(host,title,message,null,null,source,null,choices,context);
    private static Task<string?> ShowCoreAsync(Panel host,string title,string message,Control? body,Control? summary,Control? source,Func<bool>? validate,Choice[] choices,Control? decisionContext=null)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousFocus = NativeKeyboard.FocusedElement;
        var openingOwner = NativeWindow.GetWindow(host);
        var veil = new NativeBorder { Tag = "cricket-decision-sheet", Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Padding = new Thickness(16), Focusable = true };
        OperatorDesign.SetIsEnabled(veil, true);
        var surface = new OperatorGlassSurface { Material = GlassMaterial.Regular, GlassSource = host, MaxWidth = 720, CornerRadius = new CornerRadius(30), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(104, 255, 255, 255)), Background = new SolidColorBrush(Color.FromArgb(140, 33, 35, 43)), RenderTransformOrigin = new RelativePoint(.5, .08,RelativeUnit.Relative) };
        surface.SetResourceReference(Border.BackgroundProperty, "ControlCentre.GlassBrush");
        var scale = new ScaleTransform(1, 1); var shift = new TranslateTransform();
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(shift); surface.RenderTransform = transform;
        ExpansionOrigin correctionOrigin=ExpansionOrigin.Default;
        ExpansionOrigin measuredCorrectionOrigin=ExpansionOrigin.Default;
        var observedMotionStyle=OperatorAppearance.MotionStyle;
        bool correctionStarted=false;
        bool sourceForm = source is not null && body is not null && summary is null && decisionContext is null;
        bool expandsFromSource = sourceForm || summary is not null || decisionContext is not null;
        bool animateDecision = decisionContext is null && !sourceForm || !OperatorInputFocus.GetIsKeyboardMode(host);
        if(sourceForm){surface.Tag="source-form-surface";surface.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);}
        if(summary is not null){surface.Tag="scorecard-correction-surface";surface.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);}
        veil.ZIndex = 200; Grid.SetRowSpan(veil, 20); Grid.SetColumnSpan(veil, 20);
        if(decisionContext is not null){surface.Tag="innings-decision-surface";surface.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);}
        var stack = new StackPanel { Margin = decisionContext is null ? new Thickness(26) : new Thickness(26,26,38,26) };
        ScrollViewer? formScroll = null;
        Grid? formLayout = null;
        var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titleText = new NativeTextBlock { Text = title, FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0), ToolTip = string.IsNullOrWhiteSpace(message) ? null : message };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        heading.Children.Add(titleText); stack.Children.Add(heading);
        if(summary is not null)
        {
            heading.ColumnDefinitions.Insert(0,new ColumnDefinition{Width=GridLength.Auto});Grid.SetColumn(titleText,1);
            var symbol=new OperatorSymbol{Kind="scorecard",Width=25,Height=25,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            symbol.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.TextBrush");heading.Children.Add(symbol);
            symbol.Loaded+=(_,_)=>symbol.Activate();
        }
        if(decisionContext is not null){decisionContext.Margin=new Thickness(0,12,0,4);stack.Children.Add(decisionContext);}
        TextBlock? messageText = null;
        if (!string.IsNullOrWhiteSpace(message)) { messageText = new NativeTextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 201)), FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18) }; messageText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush"); stack.Children.Add(messageText); }
        if(summary is not null){summary.Margin=new Thickness(0,10,0,8);stack.Children.Add(summary);}
        if (body is not null) stack.Children.Add(body);
        var actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; stack.Children.Add(actions);
        Control actionsSurface=actions;
        bool finished = false, closing = false;
        string? closingResult=null;
        void Complete(string? key)
        {
            if (finished) return; finished = true; host.SizeChanged -= Resize; host.Unloaded -= HostUnloaded;
            OperatorAppearance.Changed-=PreferencesChanged;
            veil.BeginAnimation(Control.OpacityProperty, null); surface.BeginAnimation(Control.OpacityProperty, null); scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null);shift.BeginAnimation(TranslateTransform.XProperty,null);
            stack.BeginAnimation(Control.OpacityProperty,null);stack.Opacity=1;
            if(formScroll is not null){formScroll.BeginAnimation(Control.OpacityProperty,null);formScroll.Opacity=1;}
            if(summary is not null){summary.SizeChanged-=Resize;stack.Children.Remove(summary);}
            if(decisionContext is not null)stack.Children.Remove(decisionContext);
            if (body is not null) { stack.Children.Remove(body); if (formScroll is not null) formScroll.Content = null; }
            host.Children.Remove(veil); if (Active.TryGetValue(host, out var active)) { active.Remove(Cancel); if (active.Count == 0) Active.Remove(host); }
            completion.TrySetResult(key);
            Dispatcher.UIThread.InvokeAsync(new Action(() =>
            {
                // A following sheet owns focus. A stale completion must never
                // steal it from that sheet or another application window.
                if(!host.IsLoaded || Active.ContainsKey(host) || NativeWindow.GetWindow(host) is not { IsActive: true })return;
                if(NativeKeyboard.FocusedElement is Control {IsVisible:true,IsEnabled:true,Focusable:true} current && !ReferenceEquals(current,previousFocus) && host.IsAncestorOf(current))return;
                if(previousFocus is Control {IsVisible:true,IsEnabled:true,Focusable:true} target && NativeWindow.GetWindow(target)==NativeWindow.GetWindow(host))
                {NativeFocusManager.SetFocusedElement(NativeFocusManager.GetFocusScope(target),target);NativeKeyboard.Focus(target);}
            }),Avalonia.Threading.DispatcherPriority.Input);
        }
        void Finish(string? key)
        {
            if (finished || closing) return;
            // Invalid input stays in the same owned sheet, so the
            // repaired field keeps focus instead of a close/reopen resetting it.
            if(key is "apply" or "save" && validate is not null && !validate())return;
            if (!OperatorMotion.Enabled || !host.IsLoaded || !veil.IsLoaded || ((decisionContext is not null || sourceForm) && OperatorInputFocus.GetIsKeyboardMode(host))) { Complete(key); return; }
            closing = true;closingResult=key; surface.IsEnabled = false;
            double currentY = shift.Y; shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = currentY;
            double currentOpacity = surface.Opacity; surface.BeginAnimation(Control.OpacityProperty, null); surface.Opacity = currentOpacity;
            double closeScale=1-.06*OperatorMotion.TravelScale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, !expandsFromSource ? closeScale : correctionOrigin.ScaleX, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, !expandsFromSource ? closeScale : correctionOrigin.ScaleY, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(currentY, !expandsFromSource ? 12*OperatorMotion.TravelScale : correctionOrigin.OffsetY, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            if(expandsFromSource)
            {
                shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(shift.X,correctionOrigin.OffsetX,OperatorMotion.SurfaceClose){EasingFunction=OperatorMotion.SmoothOut});
                formScroll?.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(0,OperatorMotion.Press));
            }
            var fade = new DoubleAnimation(0, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) => Complete(key);
            veil.BeginAnimation(Control.OpacityProperty, fade);
        }
        void Cancel() => Complete(null);
        void PreferencesChanged()
        {
            if(finished)return;
            bool styleChanged=observedMotionStyle!=OperatorAppearance.MotionStyle;
            observedMotionStyle=OperatorAppearance.MotionStyle;
            if(OperatorMotion.Enabled && !styleChanged)return;
            if(closing){Complete(closingResult);return;}
            // A live style change settles the current flight without replaying
            // it. A later source-return uses the newly selected travel scale.
            correctionOrigin=measuredCorrectionOrigin.WithTravel(OperatorMotion.TravelScale);
            foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty})scale.BeginAnimation(property,null);
            shift.BeginAnimation(TranslateTransform.XProperty,null);shift.BeginAnimation(TranslateTransform.YProperty,null);shift.X=shift.Y=0;
            stack.BeginAnimation(Control.OpacityProperty,null);stack.Opacity=1;
            formScroll?.BeginAnimation(Control.OpacityProperty,null);if(formScroll is not null)formScroll.Opacity=1;
            surface.BeginAnimation(Control.OpacityProperty,null);surface.Opacity=1;
            veil.BeginAnimation(Control.OpacityProperty,null);veil.Opacity=1;
        }
        if (!Active.TryGetValue(host, out var registered)) Active[host] = registered = new(); registered.Add(Cancel);
        var explicitClose = choices.FirstOrDefault(c => c.Key == "close");
        var back = explicitClose is not null ? choices.FirstOrDefault(c => c.Key == "back") : null;
        var dismiss = explicitClose ?? choices.FirstOrDefault(c => c.Key is "back" or "cancel");
        if (back is not null)
        {
            heading.ColumnDefinitions.Insert(0,new ColumnDefinition { Width=GridLength.Auto });
            Grid.SetColumn(titleText,1);
            var backButton = new NativeButton { Tag = "back", Content = "←", ToolTip = back.Label, Theme = veil.TryFindResource("ControlCentre.Button") as Style,
                Width=34, Height=34, MinHeight=0, FontSize=22, Padding = new Thickness(0), Margin=new Thickness(0,0,10,0), Background = Brushes.Transparent, Foreground = Brushes.LightSkyBlue, BorderThickness = new Thickness(0) };
            Avalonia.Automation.AutomationProperties.SetName(backButton,back.Label);
            backButton.Click += (_, _) => Finish("back"); heading.Children.Add(backButton);
        }
        var close = new NativeButton { Tag = dismiss?.Key ?? "dismiss-sheet", Content = "×", ToolTip = dismiss?.Label ?? "Close", Width = 36, Height = 36, MinHeight = 0, FontSize = 24, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(51, 51, 57)), BorderBrush = new SolidColorBrush(Color.FromRgb(74, 74, 82)), Padding = new Thickness(0) };
        if (veil.TryFindResource("ControlCentre.Button") is Style closeStyle) close.Theme = closeStyle;
        Avalonia.Automation.AutomationProperties.SetName(close,dismiss?.Label??"Close");
        Grid.SetColumn(close, heading.ColumnDefinitions.Count-1); heading.Children.Add(close); close.Click += (_, _) => Finish(dismiss?.Key);
        foreach (var choice in choices.Where(c => !ReferenceEquals(c, dismiss) && !ReferenceEquals(c, back)).OrderByDescending(c => c.Primary))
        {
            var button = new NativeButton
            {
                Tag = choice.Key,
                Content = choice.Label,
                IsDefault = decisionContext is null && choice.Primary && !choice.Destructive,
                MinHeight = choice.Primary ? 52 : 46,
                Margin = new Thickness(0, choice.Destructive ? 14 : 0, 0, 8),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(12, 8, 12, 8),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(choice.Destructive ? Color.FromRgb(83, 27, 35) : choice.Primary ? Color.FromRgb(26, 64, 98) : Color.FromRgb(26, 35, 47)),
                BorderBrush = new SolidColorBrush(choice.Destructive ? Color.FromRgb(220, 110, 115) : Color.FromRgb(100, 150, 195))
            };
            if (veil.TryFindResource(choice.Primary ? "ControlCentre.PrimaryButton" : "ControlCentre.Button") is Style actionStyle) button.Theme =actionStyle;
            ApplyActionColour(button,choice.Key,choice.Primary,choice.Destructive);
            button.ContentTemplate = ButtonContent;
            if(!string.IsNullOrWhiteSpace(choice.Detail))
            {
                var content=new StackPanel();
                content.Children.Add(new NativeTextBlock{Text=choice.Label,FontSize=14,FontWeight=FontWeight.Bold,TextWrapping=TextWrapping.Wrap});
                content.Children.Add(new NativeTextBlock{Text=choice.Detail,FontSize=12,TextWrapping=TextWrapping.Wrap,Opacity=.85,Margin=new Thickness(0,4,0,0)});
                button.Content=content;button.ContentTemplate=null;button.HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;
                Avalonia.Automation.AutomationProperties.SetName(button,choice.Label);
                Avalonia.Automation.AutomationProperties.SetHelpText(button,choice.Detail);
            }
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
                var cancelButton=new NativeButton{Content=dismiss.Label,Tag="scorecard-correction-cancel",MinHeight=48,Padding=new Thickness(10,8,10,8),ContentTemplate=ButtonContent};
                cancelButton.SetResourceReference(Control.ThemeProperty,"ControlCentre.Button");Avalonia.Automation.AutomationProperties.SetName(cancelButton,dismiss.Label);
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
            surface.MaxHeight = Math.Max(160, host.NativeHeight() - 32);
            surface.MaxWidth = Math.Max(1, Math.Min(720, host.NativeWidth() - 32));
            if(decisionContext is not null)
            {
                bool compactDecision=host.NativeHeight()<480;
                stack.Margin=compactDecision?new Thickness(12,12,24,12):new Thickness(26,26,38,26);
                titleText.FontSize=compactDecision?21:25;
                if(messageText is not null)messageText.IsVisible=compactDecision && choices.Any(choice=>!string.IsNullOrWhiteSpace(choice.Detail))?false:true;
            }
            if (formLayout is null) return;
            bool compact = host.NativeHeight() < 480;
            formLayout.Margin = new Thickness(compact ? 12 : 18);
            titleText.FontSize = compact ? 21 : 25;
            if (messageText is not null) messageText.IsVisible = compact || summary is not null ? false : true;
            actionsSurface.Margin = new Thickness(0, compact ? 8 : 14, 0, 0);
            if (formScroll is not null)
            {
                double width = Math.Max(1, surface.MaxWidth - formLayout.Margin.Left - formLayout.Margin.Right);
                stack.Measure(new Avalonia.Size(width, double.PositiveInfinity));
                actionsSurface.Measure(new Avalonia.Size(width, double.PositiveInfinity));
                double bodySpace = surface.MaxHeight - formLayout.Margin.Top - formLayout.Margin.Bottom - stack.DesiredSize.Height - actionsSurface.DesiredSize.Height;
                bool scrollWholeForm = bodySpace < (summary is null?72:48);
                formScroll.MaxHeight = scrollWholeForm ? double.PositiveInfinity : bodySpace;
                if(summary is not null)formScroll.Height=scrollWholeForm?double.NaN:bodySpace;
                formScroll.VerticalScrollBarVisibility = scrollWholeForm ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            }
        }
        void HostUnloaded(object? sender, RoutedEventArgs args) => Complete(null);
        host.SizeChanged += Resize; host.Unloaded += HostUnloaded;if(summary is not null)summary.SizeChanged+=Resize; Resize(null, null!);
        veil.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if(decisionContext is not null)Complete(null);else Finish(null); } };
        KeyboardNavigation.SetTabNavigation(veil, KeyboardNavigationMode.Cycle);
        void StartCorrectionEntry()
        {
            if(correctionStarted || finished || closing)return;
            surface.UpdateLayout();if(surface.NativeWidth()<1 || surface.NativeHeight()<1)return;
            correctionStarted=true;
            measuredCorrectionOrigin=ExpansionOrigin.From(surface,source??previousFocus as Control);
            correctionOrigin=measuredCorrectionOrigin.WithTravel(OperatorMotion.TravelScale);
            if(!OperatorMotion.Enabled || !animateDecision)return;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(correctionOrigin.ScaleX,1,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(correctionOrigin.ScaleY,1,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(correctionOrigin.OffsetX,0,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(correctionOrigin.OffsetY,0,OperatorMotion.PanelOpen){EasingFunction=OperatorMotion.SmoothOut,FillBehavior=FillBehavior.Stop});
            stack.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){BeginTime=OperatorMotion.Stagger,FillBehavior=FillBehavior.Stop});
            formScroll?.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){BeginTime=TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks*2),FillBehavior=FillBehavior.Stop});
            veil.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(0,1,OperatorMotion.SurfaceOpen){FillBehavior=FillBehavior.Stop});
        }
        // Keep the current sheet subscribed through its closing flight. Complete
        // balances this for every result, host unload and explicit cancellation.
        OperatorAppearance.Changed+=PreferencesChanged;
        if(expandsFromSource)surface.Loaded+=(_,_)=>StartCorrectionEntry();
        host.Children.Add(veil);
        if(expandsFromSource && surface.IsLoaded)StartCorrectionEntry();
        if(!expandsFromSource && previousFocus is Control trigger && host.IsAncestorOf(trigger))
        {
            surface.Loaded+=(_,_)=>
            {
                if(finished || closing || surface.NativeWidth()<1 || surface.NativeHeight()<1)return;
                var origin=trigger.NativeTranslatePoint(new Avalonia.Point(trigger.NativeWidth()/2,trigger.NativeHeight()/2),surface);
                surface.RenderTransformOrigin=new RelativePoint(Math.Clamp(origin.X/surface.NativeWidth(),.12,.88),Math.Clamp(origin.Y/surface.NativeHeight(),.08,.92),RelativeUnit.Relative);
            };
        }
        if (!expandsFromSource && OperatorMotion.Enabled)
        {
            double travel=OperatorMotion.TravelScale;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24*travel, 0, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1-.06*travel, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1-.18*travel, 1, OperatorMotion.MaterialArrival) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            surface.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(.65, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
            veil.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        }
        Dispatcher.UIThread.InvokeAsync(new Action(() =>
        {
            void FocusAction(Button target)
            { NativeFocusManager.SetFocusedElement(NativeFocusManager.GetFocusScope(veil),target);target.Focus(); }
            if(finished || closing || !host.IsLoaded || !host.IsVisible || !veil.IsLoaded || !veil.IsVisible
                || !host.Children.Contains(veil) || NativeWindow.GetWindow(host) is not { IsActive:true } owner
                || !ReferenceEquals(owner,openingOwner) || !Active.TryGetValue(host,out var active)
                || active.Count==0 || active[^1]!=(Action)Cancel) return;
            // Opening focus is only a fallback. A newer keyboard/pointer choice
            // inside this sheet already owns focus; never reset its field/caret
            // or move it back from the safe close action.
            if(NativeKeyboard.FocusedElement is Control {IsVisible:true,IsEnabled:true,Focusable:true} current
                && !ReferenceEquals(current,previousFocus) && veil.IsAncestorOf(current))return;
            if(decisionContext is not null){FocusAction(close);return;}
            if(body is Control form && NativeNavigation.FocusFirst(form)) return;
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
            button.Background = NativeBrush.Linear(start, Color.FromRgb(30, 30, 34), 90);
            button.Foreground = destructive ? new SolidColorBrush(Color.FromRgb(255, 163, 170)) : Brushes.White;
        }
        button.BorderBrush = new SolidColorBrush(primary ? accent : Color.FromArgb(150, accent.R, accent.G, accent.B));
        button.BorderThickness = new Thickness(1);
        button.FontSize = 15;
    }
}
