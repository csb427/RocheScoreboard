using System.Windows;
using System.Windows.Controls;

namespace Roche_Scoreboard.Views;

/// <summary>Fill the operator window; scroll a usable minimum canvas instead of shrinking the entire UI.</summary>
public class OperatorViewport : ScrollViewer
{
    private static int _validationScrollDepth;
    public double MinimumCanvasWidth { get; set; } = 760;
    public double MinimumCanvasHeight { get; set; } = 560;
    private bool _naturalHeight;
    public bool NaturalHeight { get => _naturalHeight; set { _naturalHeight = value; FitContent(); } }

    public OperatorViewport()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        CanContentScroll = false;
        Services.ScrollActivity.SetEnabled(this, true);
        SizeChanged += (_, _) => FitContent();
        ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) FitContent();
        };
        // Focus must not scroll a different command beneath the pointer.
        RequestBringIntoView += (_, e) =>
        {
            if (_validationScrollDepth == 0 && !System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Tab)) e.Handled = true;
        };
    }

    /// <summary>Explicit feedback after a rejected submission must be readable,
    /// even when pointer focus scrolling is otherwise suppressed.</summary>
    public static void RevealValidationError(FrameworkElement error)
    {
        error.UpdateLayout();
        _validationScrollDepth++;
        try { error.BringIntoView(); }
        finally { _validationScrollDepth--; }
    }

    protected void FitContent()
    {
        if (Content is not FrameworkElement content || ActualWidth <= 0 || ActualHeight <= 0) return;
        double width = Math.Max(MinimumCanvasWidth, ViewportWidth > 0 ? ViewportWidth : ActualWidth);
        double height = Math.Max(MinimumCanvasHeight, ViewportHeight > 0 ? ViewportHeight : ActualHeight);
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
