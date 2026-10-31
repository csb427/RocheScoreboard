// Generated from Views/OperatorViewport.cs; edit translator/adapters.
// Source SHA-256: 99fed30d738255786b83e7f75253fbbf7135794271fe89ec2e30e6d421544e82
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Roche_Scoreboard.Views;

/// <summary>Fill the operator window; scroll a usable minimum canvas instead of shrinking the entire UI.</summary>
public class OperatorViewport : ScrollViewer
{
    protected override Type StyleKeyOverride=>typeof(ScrollViewer);
    private static int _validationScrollDepth;
    public double MinimumCanvasWidth { get; set; } = 760;
    public double MinimumCanvasHeight { get; set; } = 560;
    private bool _naturalHeight;
    public bool NaturalHeight { get => _naturalHeight; set { _naturalHeight = value; FitContent(); } }

    public OperatorViewport()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        
        Services.ScrollActivity.SetEnabled(this, true);
        SizeChanged += (_, _) => FitContent();
        ScrollChanged += (_, e) =>
        {
            if (e.ViewportDelta.X != 0 || e.ViewportDelta.Y != 0) FitContent();
        };
        // Focus must not scroll a different command beneath the pointer.
        NativeEvents.BringIntoView(this,(_, e) =>
        {
            if (_validationScrollDepth == 0 && !NativeKeyboard.IsKeyDown(Avalonia.Input.Key.Tab)) e.Handled = true;
        });
    }

    /// <summary>Explicit feedback after a rejected submission must be readable,
    /// even when pointer focus scrolling is otherwise suppressed.</summary>
    public static void RevealValidationError(Control error) => RevealOperatorTarget(error);

    /// <summary>Explicit navigation, such as returning from a picker, may
    /// reveal its destination without enabling scroll-on-focus for scoring clicks.</summary>
    public static void RevealOperatorTarget(Control target, Func<bool>? isCurrent = null)
    {
        target.UpdateLayout();
        if (isCurrent?.Invoke() == false || !target.IsLoaded || !target.IsVisible) return;
        _validationScrollDepth++;
        try { NativeFocusReveal.Reveal(target); }
        finally { _validationScrollDepth--; }
    }

    protected void FitContent()
    {
        if (Content is not Control content || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double width = Math.Max(MinimumCanvasWidth, Viewport.Width > 0 ? Viewport.Width : Bounds.Width);
        double height = Math.Max(MinimumCanvasHeight, Viewport.Height > 0 ? Viewport.Height : Bounds.Height);
        if (!double.IsFinite(content.Width) || Math.Abs(content.Width - width) > .5) content.Width = width;
        if (NaturalHeight)
        {
            if (!double.IsNaN(content.Height)) content.Height = double.NaN;
            if (Math.Abs(content.MinHeight - height) > .5) content.MinHeight = height;
        }
        else
        {
            if (content.MinHeight > height) content.MinHeight = 0;
            if (!double.IsFinite(content.Height) || Math.Abs(content.Height - height) > .5) content.Height = height;
        }
    }
    public void RefreshViewport() => FitContent();
}

/// <summary>Reflow paired setup cards vertically while keeping setup navigation outside the scroller.</summary>
public sealed class WizardViewport : OperatorViewport
{
    public WizardViewport()
    {
        MinimumCanvasWidth = 320; MinimumCanvasHeight = 0; NaturalHeight = true;
    }
}
