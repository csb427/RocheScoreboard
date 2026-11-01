// Generated from Views/SetupWizard.DurationDesign.cs; edit translator/adapters.
// Source SHA-256: 5438e722b89bc816e3677312617d92fd46bbec65f5931ccd0e8b5d8a731f0fed
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Panel=Avalonia.Controls.Panel;
using TextBox=Avalonia.Controls.TextBox;
using TextBlock=Avalonia.Controls.TextBlock;

namespace Roche_Scoreboard.Views;

public partial class SetupWizard
{
    private bool _durationEditorInstalled;
    private void InstallDurationEditor()
    {
        if(_durationEditorInstalled || DurationMinutesDisplay.Parent is not StackPanel host)return;
        _durationEditorInstalled=true;
        foreach(var old in host.Children.OfType<Control>())old.IsVisible=false;
        host.Margin=new Thickness(4,0,4,0);
        var editor=new Grid();editor.ColumnDefinitions.Add(new(){Width=new GridLength(60)});
        editor.ColumnDefinitions.Add(new(){Width=new GridLength(14)});editor.ColumnDefinitions.Add(new(){Width=new GridLength(48)});
        Add(SetupQuarterMinutes,"minutes",0,28);Add(SetupQuarterSeconds,"seconds",2,24);
        var colon=new NativeTextBlock{Text=":",FontSize=24,Margin=new Thickness(0,7,0,0),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center};
        colon.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");Grid.SetColumn(colon,1);editor.Children.Add(colon);
        host.Children.Add(editor);UpdateSetupClockSummary();
        void Add(TextBox field,string unit,int column,double size)
        {
            // Preserve the original input, TextChanged handler and validation
            // focus target instead of introducing a second duration value.
            if(field.Parent is Panel parent)parent.Children.Remove(field);
            field.Theme =NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "SettingsInput");field.IsVisible=true;
            field.FontSize=size;field.Height=46;field.MinHeight=46;field.Padding=new Thickness(4,6,4,6);
            field.TextAlignment=TextAlignment.Center;field.VerticalContentAlignment=VerticalAlignment.Center;
            field.FontFeatures=new FontFeatureCollection { FontFeature.Parse("tnum"), FontFeature.Parse("lnum"), FontFeature.Parse("-kern") };
            Avalonia.Automation.AutomationProperties.SetName(field,"Quarter "+unit);
            var part=new StackPanel();part.Children.Add(field);
            var label=new NativeTextBlock{Text=unit,FontSize=11,Margin=new Thickness(0,4,0,0),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center};
            label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");part.Children.Add(label);
            Grid.SetColumn(part,column);editor.Children.Add(part);
        }
    }
}
