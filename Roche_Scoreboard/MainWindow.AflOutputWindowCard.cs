using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Roche_Scoreboard.Views;
using Border=System.Windows.Controls.Border;
using Binding=System.Windows.Data.Binding;

namespace Roche_Scoreboard;

// Window-specific paint and icon parts follow the existing output/thumbnail authority.
public partial class MainWindow
{
    private AflOutputWindowGlyph? _aflOutputWindowGlyph;
    private System.Windows.Window? _outputCardObservedWindow;
    private bool _outputCardOwnerCleanupAttached;
    private bool _outputCardOwnerClosed;

    private System.Windows.Window? OutputCardCurrentWindow() => _sportMode == Roche_Scoreboard.Models.SportMode.AFL
        ? _displayWindow : _cricketDisplayWindow;

    private void ObserveOutputCardWindow()
    {
        if (_outputCardOwnerClosed) return;
        if (!_outputCardOwnerCleanupAttached)
        {
            Closed += OutputCardOwnerClosed;
            _outputCardOwnerCleanupAttached = true;
        }
        var current = OutputCardCurrentWindow();
        if (ReferenceEquals(current, _outputCardObservedWindow)) return;
        DetachOutputCardWindow();
        _outputCardObservedWindow = current;
        if (current is not null) current.IsVisibleChanged += OutputCardWindowVisibilityChanged;
    }

    private void OutputCardWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        // Native compatibility events use PropertyChanged internally. Restrict
        // this handler to the actual current output's visibility, not its size
        // or a stale old output that completed closing after a replacement.
        if (_outputCardOwnerClosed || args.Property.Name != nameof(UIElement.IsVisible)
            || !ReferenceEquals(sender, _outputCardObservedWindow)
            || !ReferenceEquals(sender, OutputCardCurrentWindow())) return;
        UpdateWindowToggleVisual();
    }

    private void DetachOutputCardWindow()
    {
        if (_outputCardObservedWindow is not null)
            _outputCardObservedWindow.IsVisibleChanged -= OutputCardWindowVisibilityChanged;
        _outputCardObservedWindow = null;
    }

    private void OutputCardOwnerClosed(object? sender, EventArgs args)
    {
        _outputCardOwnerClosed = true;
        DetachOutputCardWindow();
        Closed -= OutputCardOwnerClosed;
        _outputCardOwnerCleanupAttached = false;
    }

    private void InstallAflOutputWindowCardVisual()
    {
        if(_aflOutputWindowGlyph is not null)return;
        if(WindowToggleTitle.Parent is not StackPanel body)return;
        _aflOutputWindowGlyph=new AflOutputWindowGlyph
        { HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,0,0,7) };
        _aflOutputWindowGlyph.SetBinding(AflOutputWindowGlyph.ThumbPositionProperty,
            new Binding(nameof(TranslateTransform.X)){Source=WindowToggleThumbTranslate});
        WindowToggleIcon.Visibility=Visibility.Collapsed;
        body.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;body.Margin=new Thickness(12,0,12,0);
        body.Children.Insert(0,_aflOutputWindowGlyph);
        WindowToggleTitle.Text="Output window";WindowToggleTitle.FontSize=16;
        WindowToggleTitle.TextWrapping=TextWrapping.Wrap;WindowToggleTitle.TextAlignment=TextAlignment.Center;
        // Keep the proven verb slot and original track/16px thumb/hit card.
        WindowToggleLabel.MinWidth=40;WindowToggleLabel.TextAlignment=TextAlignment.Center;
        WindowToggleLabel.FontSize=Math.Max(13,WindowToggleLabel.FontSize);
        PresCardWindow.CornerRadius=new CornerRadius(24);
        UpdateAflOutputWindowCardPaint(PresentationOutputIsOpen());
    }
    private void UpdateAflOutputWindowCardPaint(bool isOpen)
    {
        ObserveOutputCardWindow();
        PresCardWindow.SetResourceReference(Border.BackgroundProperty,"ControlCentre.RaisedBrush");
        PresCardWindow.SetResourceReference(Border.BorderBrushProperty,isOpen?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        // Paint pairs are semantic in both themes; action copy stays truthful.
        WindowToggleTrack.SetResourceReference(Border.BackgroundProperty,isOpen?"ControlCentre.SelectionBrush":"ControlCentre.RaisedBrush");
        WindowToggleTrack.SetResourceReference(Border.BorderBrushProperty,isOpen?"ControlCentre.AccentBrush":"ControlCentre.BorderBrush");
        WindowToggleThumb.SetResourceReference(Border.BackgroundProperty,isOpen?"ControlCentre.SelectedTextBrush":"ControlCentre.SecondaryTextBrush");
        WindowToggleThumb.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.TextBrush");
        WindowToggleThumb.BorderThickness=new Thickness(1);
        WindowToggleTitle.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");
        WindowToggleLabel.Text=isOpen?"Close":"Open";
        WindowToggleLabel.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(PresCardWindow,isOpen?"Close output window":"Open output window");
        System.Windows.Automation.AutomationProperties.SetItemStatus(PresCardWindow,isOpen?"Window open":"Window closed");
        System.Windows.Automation.AutomationProperties.SetHelpText(PresCardWindow,
            "Open or close the public output window. This action does not select a presentation or change the match.");
    }
}
