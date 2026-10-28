// Generated from Views/CricketPrompt.cs; edit translator/adapters.
// Source SHA-256: dd67aa13f7fec733f0f4de318979beb47da3983cc81eb237dec57b1f7abd2902
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
    internal sealed record Choice(string Key, string Label, bool Primary = false, bool Destructive = false);
    internal static Task<string?> ChooseAsync(Panel host, string title, string message, params Choice[] choices)
        => ShowAsync(host, title, message, null, choices);
    internal static Task<string?> ShowAsync(Panel host, string title, string message, Control? body, params Choice[] choices)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousFocus = NativeKeyboard.FocusedElement;
        var veil = new NativeBorder { Tag = "cricket-decision-sheet", Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Padding = new Thickness(16), Focusable = true };
        OperatorDesign.SetIsEnabled(veil, true);
        var surface = new OperatorGlassSurface { Material = GlassMaterial.Regular, GlassSource = host, MaxWidth = 720, CornerRadius = new CornerRadius(30), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(104, 255, 255, 255)), Background = new SolidColorBrush(Color.FromArgb(140, 33, 35, 43)), RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative) };
        surface.SetResourceReference(Border.BackgroundProperty, "ControlCentre.GlassBrush");
        var scale = new ScaleTransform(1, 1); var shift = new TranslateTransform();
        var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(shift); surface.RenderTransform = transform;
        veil.ZIndex = 200; Grid.SetRowSpan(veil, 20); Grid.SetColumnSpan(veil, 20);
        var stack = new StackPanel { Margin = new Thickness(26) };
        ScrollViewer? formScroll = null;
        Grid? formLayout = null;
        var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titleText = new NativeTextBlock { Text = title, FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0), ToolTip = message };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.TextBrush");
        heading.Children.Add(titleText); stack.Children.Add(heading);
        TextBlock? messageText = null;
        if (!string.IsNullOrWhiteSpace(message)) { messageText = new NativeTextBlock { Text = message, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 201)), FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18) }; messageText.SetResourceReference(TextBlock.ForegroundProperty, "ControlCentre.SecondaryTextBrush"); stack.Children.Add(messageText); }
        if (body is not null) stack.Children.Add(body);
        var actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; stack.Children.Add(actions);
        bool finished = false, closing = false;
        void Complete(string? key)
        {
            if (finished) return; finished = true; host.SizeChanged -= Resize; host.Unloaded -= HostUnloaded;
            veil.BeginAnimation(Control.OpacityProperty, null); surface.BeginAnimation(Control.OpacityProperty, null); scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null);
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
            if (!OperatorMotion.Enabled || !host.IsLoaded || !veil.IsLoaded) { Complete(key); return; }
            closing = true; surface.IsEnabled = false;
            double currentY = shift.Y; shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = currentY;
            double currentOpacity = surface.Opacity; surface.BeginAnimation(Control.OpacityProperty, null); surface.Opacity = currentOpacity;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, .94, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, .94, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(currentY, 12, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd });
            var fade = new DoubleAnimation(0, OperatorMotion.SurfaceClose) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) => Complete(key);
            veil.BeginAnimation(Control.OpacityProperty, fade);
        }
        void Cancel() => Complete(null);
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
        Grid.SetColumn(close, back is null ? 1 : 2); heading.Children.Add(close); close.Click += (_, _) => Finish(dismiss?.Key);
        foreach (var choice in choices.Where(c => !ReferenceEquals(c, dismiss) && !ReferenceEquals(c, back)).OrderByDescending(c => c.Primary))
        {
            var button = new NativeButton
            {
                Tag = choice.Key,
                Content = choice.Label,
                IsDefault = choice.Primary && !choice.Destructive,
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
            button.Click += (_, _) => Finish(choice.Key); actions.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (body is null) surface.Child = scroll;
        else
        {
            // Keep the title, close control and commit action visible while a
            // long roster or editor scrolls independently between them.
            stack.Children.Remove(body); stack.Children.Remove(actions); scroll.Content = null;
            formLayout = new Grid { Margin = new Thickness(18) }; stack.Margin = new Thickness(0);
            formLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            // An Auto row contributes the editor's real desired height. A star
            // row inside this auto-height sheet can collapse a lone text field.
            formLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            formLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
            formLayout.Children.Add(stack);
            formScroll = new ScrollViewer { Tag = "cricket-prompt-body", Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(formScroll,1); formLayout.Children.Add(formScroll);
            Grid.SetRow(actions,2); formLayout.Children.Add(actions);
            // Normally the body alone scrolls. At exceptionally short heights,
            // this outer viewport keeps the title and actions reachable as well.
            scroll.Content = formLayout; surface.Child = scroll;
        }
        veil.Child = surface;
        void Resize(object? sender, SizeChangedEventArgs e)
        {
            surface.MaxHeight = Math.Max(160, host.NativeHeight() - 32);
            surface.MaxWidth = Math.Max(1, Math.Min(720, host.NativeWidth() - 32));
            if (formLayout is null) return;
            bool compact = host.NativeHeight() < 480;
            formLayout.Margin = new Thickness(compact ? 12 : 18);
            titleText.FontSize = compact ? 21 : 25;
            if (messageText is not null) messageText.IsVisible = compact ? false : true;
            actions.Margin = new Thickness(0, compact ? 8 : 14, 0, 0);
            if (formScroll is not null)
            {
                double width = Math.Max(1, surface.MaxWidth - formLayout.Margin.Left - formLayout.Margin.Right);
                stack.Measure(new Avalonia.Size(width, double.PositiveInfinity));
                actions.Measure(new Avalonia.Size(width, double.PositiveInfinity));
                double bodySpace = surface.MaxHeight - formLayout.Margin.Top - formLayout.Margin.Bottom - stack.DesiredSize.Height - actions.DesiredSize.Height;
                bool scrollWholeForm = bodySpace < 72;
                formScroll.MaxHeight = scrollWholeForm ? double.PositiveInfinity : bodySpace;
                formScroll.VerticalScrollBarVisibility = scrollWholeForm ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            }
        }
        void HostUnloaded(object? sender, RoutedEventArgs args) => Complete(null);
        host.SizeChanged += Resize; host.Unloaded += HostUnloaded; Resize(null, null!);
        veil.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Finish(null); } };
        KeyboardNavigation.SetTabNavigation(veil, KeyboardNavigationMode.Cycle);
        host.Children.Add(veil);
        if(previousFocus is Control trigger && host.IsAncestorOf(trigger))
        {
            surface.Loaded+=(_,_)=>
            {
                if(finished || closing || surface.NativeWidth()<1 || surface.NativeHeight()<1)return;
                var origin=trigger.NativeTranslatePoint(new Avalonia.Point(trigger.NativeWidth()/2,trigger.NativeHeight()/2),surface);
                surface.RenderTransformOrigin=new RelativePoint(Math.Clamp(origin.X/surface.NativeWidth(),.12,.88),Math.Clamp(origin.Y/surface.NativeHeight(),.08,.92),RelativeUnit.Relative);
            };
        }
        if (OperatorMotion.Enabled)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.94, 1, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.90, 1, OperatorMotion.SurfaceOpen) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
            surface.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(.65, 1, OperatorMotion.SurfaceOpen) { FillBehavior = FillBehavior.Stop });
            veil.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        }
        Dispatcher.UIThread.InvokeAsync(new Action(() =>
        {
            void FocusAction(Button target)
            { NativeFocusManager.SetFocusedElement(NativeFocusManager.GetFocusScope(veil),target);target.Focus(); }
            if(finished || closing) return;
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
