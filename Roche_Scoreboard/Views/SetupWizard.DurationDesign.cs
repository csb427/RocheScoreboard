using System.Windows;
using System.Windows.Controls;
using Panel=System.Windows.Controls.Panel;
using TextBox=System.Windows.Controls.TextBox;
using TextBlock=System.Windows.Controls.TextBlock;

namespace Roche_Scoreboard.Views;

public partial class SetupWizard
{
    private bool _durationEditorInstalled;
    private void InstallDurationEditor()
    {
        if(_durationEditorInstalled || DurationMinutesDisplay.Parent is not StackPanel host)return;
        _durationEditorInstalled=true;
        foreach(var old in host.Children.OfType<UIElement>())old.Visibility=Visibility.Collapsed;
        host.Margin=new Thickness(4,0,4,0);
        var editor=new Grid();editor.ColumnDefinitions.Add(new(){Width=new GridLength(60)});
        editor.ColumnDefinitions.Add(new(){Width=new GridLength(14)});editor.ColumnDefinitions.Add(new(){Width=new GridLength(48)});
        Add(SetupQuarterMinutes,"minutes",0,28);Add(SetupQuarterSeconds,"seconds",2,24);
        var colon=new TextBlock{Text=":",FontSize=24,Margin=new Thickness(0,7,0,0),HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
        colon.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");Grid.SetColumn(colon,1);editor.Children.Add(colon);
        host.Children.Add(editor);UpdateSetupClockSummary();
        void Add(TextBox field,string unit,int column,double size)
        {
            // Preserve the original input, TextChanged handler and validation
            // focus target instead of introducing a second duration value.
            if(field.Parent is Panel parent)parent.Children.Remove(field);
            field.Style=(Style)FindResource("SettingsInput");field.Visibility=Visibility.Visible;
            field.FontSize=size;field.Height=46;field.MinHeight=46;field.Padding=new Thickness(4,6,4,6);
            field.TextAlignment=TextAlignment.Center;field.VerticalContentAlignment=VerticalAlignment.Center;
            System.Windows.Documents.Typography.SetNumeralAlignment(field,FontNumeralAlignment.Tabular);
            System.Windows.Automation.AutomationProperties.SetName(field,"Quarter "+unit);
            var part=new StackPanel();part.Children.Add(field);
            var label=new TextBlock{Text=unit,FontSize=11,Margin=new Thickness(0,4,0,0),HorizontalAlignment=System.Windows.HorizontalAlignment.Center};
            label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");part.Children.Add(label);
            Grid.SetColumn(part,column);editor.Children.Add(part);
        }
    }
}
