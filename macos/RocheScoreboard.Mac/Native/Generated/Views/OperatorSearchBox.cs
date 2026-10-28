// Generated from Views/OperatorSearchBox.cs; edit translator/adapters.
// Source SHA-256: d8da631e7d9710f0e4b8a75722de492ef5f21aa4732b87e17db2a4b28bcee838
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Roche_Scoreboard.Services;
using TextBox = Avalonia.Controls.TextBox;
using Button = Avalonia.Controls.Button;

namespace Roche_Scoreboard.Views;

/// <summary>One native text editor with a shared search, clear and focus contract.</summary>
public sealed class OperatorSearchBox : NativeSearchTextBox
{
    public static readonly AttachedProperty<string> PlaceholderProperty = (AttachedProperty<string>)NativeProperty.Register(nameof(Placeholder), typeof(string), typeof(OperatorSearchBox), new PropertyMetadata("Search"));
    private static readonly NativePropertyKey HasQueryPropertyKey = NativeProperty.RegisterReadOnly(nameof(HasQuery), typeof(bool), typeof(OperatorSearchBox), new PropertyMetadata(false));
    public static readonly AttachedProperty<bool> HasQueryProperty = (AttachedProperty<bool>)HasQueryPropertyKey.DependencyProperty;
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public bool HasQuery => (bool)GetValue(HasQueryProperty);
    private Button? _clear;
    private Control? _placeholder;
    private OperatorSymbol? _symbol;
    public OperatorSearchBox()
    {
        this.SetResourceReference(ThemeProperty, typeof(OperatorSearchBox));
        Loaded += (_, _) => { OperatorAppearance.Changed -= AppearanceChanged; OperatorAppearance.Changed += AppearanceChanged; };
        Unloaded += (_, _) => { OperatorAppearance.Changed -= AppearanceChanged; StopDecoration(); };
    }
    public override void OnNativeApplyTemplate()
    {
        if (_clear is not null) _clear.Click -= ClearClicked;
        StopDecoration(); base.OnNativeApplyTemplate();
        _clear = GetTemplateChild("PART_Clear") as Button;
        _placeholder = GetTemplateChild("PART_Placeholder") as Control;
        _symbol = GetTemplateChild("PART_SearchSymbol") as OperatorSymbol;
        if (_clear is not null) _clear.Click += ClearClicked;
    }
    protected override void OnNativeTextChanged(TextChangedEventArgs e)
    {
        this.SetValue(HasQueryPropertyKey, !string.IsNullOrEmpty(Text));
        if(HasQuery)StopDecoration();
        base.OnNativeTextChanged(e);
    }
    protected override void OnGotFocus(Avalonia.Input.GotFocusEventArgs e)
    { base.OnGotFocus(e); _symbol?.Activate(); }
    protected override void OnNativePreviewKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && HasQuery && !IsReadOnly)
        { ClearQuery(); e.Handled = true; return; }
        base.OnNativePreviewKeyDown(e);
    }
    private void ClearClicked(object? sender, RoutedEventArgs e) { ClearQuery(); e.Handled=true; }
    private void AppearanceChanged() { if(!OperatorMotion.Enabled)StopDecoration(); }
    internal void ClearQuery()
    {
        if (!IsEnabled || IsReadOnly) return;
        SetCurrentValue(TextProperty, string.Empty); CaretIndex = 0; Focus();
        StopDecoration();
        // Results update immediately. Only the returning hint is animated.
        if (_placeholder is null || !IsLoaded || !OperatorMotion.Enabled) return;
        var scale = new ScaleTransform(.96, .96);
        _placeholder.RenderTransformOrigin = new RelativePoint(0, .5,RelativeUnit.Relative);
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
        _placeholder.BeginAnimation(OpacityProperty, null); _placeholder.RenderTransform = new MatrixTransform(Matrix.Identity);
    }
}
