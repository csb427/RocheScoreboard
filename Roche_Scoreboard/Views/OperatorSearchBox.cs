using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Services;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace Roche_Scoreboard.Views;

/// <summary>One native text editor with a shared search, clear and focus contract.</summary>
public sealed class OperatorSearchBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(OperatorSearchBox), new PropertyMetadata("Search"));
    private static readonly DependencyPropertyKey HasQueryPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasQuery), typeof(bool), typeof(OperatorSearchBox), new PropertyMetadata(false));
    public static readonly DependencyProperty HasQueryProperty = HasQueryPropertyKey.DependencyProperty;
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public bool HasQuery => (bool)GetValue(HasQueryProperty);
    private Button? _clear;
    private FrameworkElement? _placeholder;
    private OperatorSymbol? _symbol;
    public OperatorSearchBox()
    {
        SetResourceReference(StyleProperty, typeof(OperatorSearchBox));
        Loaded += (_, _) => { OperatorAppearance.Changed -= AppearanceChanged; OperatorAppearance.Changed += AppearanceChanged; };
        Unloaded += (_, _) => { OperatorAppearance.Changed -= AppearanceChanged; StopDecoration(); };
    }
    public override void OnApplyTemplate()
    {
        if (_clear is not null) _clear.Click -= ClearClicked;
        StopDecoration(); base.OnApplyTemplate();
        _clear = GetTemplateChild("PART_Clear") as Button;
        _placeholder = GetTemplateChild("PART_Placeholder") as FrameworkElement;
        _symbol = GetTemplateChild("PART_SearchSymbol") as OperatorSymbol;
        if (_clear is not null) _clear.Click += ClearClicked;
    }
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        SetValue(HasQueryPropertyKey, !string.IsNullOrEmpty(Text));
        if(HasQuery)StopDecoration();
        base.OnTextChanged(e);
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { base.OnGotKeyboardFocus(e); _symbol?.Activate(); }
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && HasQuery && !IsReadOnly)
        { ClearQuery(); e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
    private void ClearClicked(object sender, RoutedEventArgs e) { ClearQuery(); e.Handled=true; }
    private void AppearanceChanged() { if(!OperatorMotion.Enabled)StopDecoration(); }
    internal void ClearQuery()
    {
        if (!IsEnabled || IsReadOnly) return;
        SetCurrentValue(TextProperty, string.Empty); CaretIndex = 0; Focus();
        StopDecoration();
        // Results update immediately. Only the returning hint is animated.
        if (_placeholder is null || !IsLoaded || !OperatorMotion.Enabled) return;
        var scale = new ScaleTransform(.96, .96);
        _placeholder.RenderTransformOrigin = new System.Windows.Point(0, .5);
        _placeholder.RenderTransform = scale;
        var settle = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = OperatorMotion.SmoothOut };
        settle.Completed += (_, _) => StopDecoration();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = OperatorMotion.SmoothOut });
        _placeholder.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
    }
    private void StopDecoration()
    {
        if (_placeholder is null) return;
        if (_placeholder.RenderTransform is ScaleTransform scale)
        { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        _placeholder.BeginAnimation(OpacityProperty, null); _placeholder.RenderTransform = Transform.Identity;
    }
}
