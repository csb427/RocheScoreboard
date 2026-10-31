// Generated from Views/WizardTeamEditor.cs; edit translator/adapters.
// Source SHA-256: 0b8db2d502d924bb40d1b0c11e6f653f0729dc7b3ad70d42ea928874f1647d6f
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using Border = Avalonia.Controls.Border;
using Button = Avalonia.Controls.Button;
using TextBox = Avalonia.Controls.TextBox;
using UserControl = Avalonia.Controls.UserControl;
using Orientation = Avalonia.Layout.Orientation;
using Brushes = Avalonia.Media.Brushes;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;
using ComboBox = Avalonia.Controls.ComboBox;
using Panel = Avalonia.Controls.Panel;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace Roche_Scoreboard.Views;

/// <summary>One operator-only team form for both sports. Existing input instances keep their bindings and handlers.</summary>
internal static class WizardTeamEditor
{
    private static readonly AttachedProperty<bool> InstalledProperty=(AttachedProperty<bool>)NativeProperty.RegisterAttached("Installed",typeof(bool),typeof(WizardTeamEditor),new PropertyMetadata(false));
    internal static void Install(UserControl wizard)
    {
        if((bool)wizard.GetValue(InstalledProperty) || wizard.FindName("TeamsStepContainer") is not Grid stage)return;
        wizard.SetValue(InstalledProperty,true);
        // Gather the existing actions before removing their legacy containers.
        var buttons=Descendants(stage).OfType<Button>().ToArray();
        var pair=new Grid();pair.ColumnDefinitions.Add(new());pair.ColumnDefinitions.Add(new(){Width=new GridLength(18)});pair.ColumnDefinitions.Add(new());
        WizardPair.SetMinimumColumnWidth(pair,330);
        foreach(string side in new[]{"Home","Away"})
        {
            bool home=side=="Home";
            var ownButtons=buttons.Where(b=>IsWithinSide(b,side)).ToArray();
            var name=(TextBox)wizard.FindName($"Setup{side}NameBox");
            var abbr=(TextBox)wizard.FindName($"Setup{side}AbbrBox");
            var primary=(TextBox)wizard.FindName($"Setup{side}ColorBox");
            var secondary=(TextBox)wizard.FindName($"Setup{side}SecondaryBox");
            var colours=new Grid();colours.ColumnDefinitions.Add(new());colours.ColumnDefinitions.Add(new(){Width=new GridLength(16)});colours.ColumnDefinitions.Add(new());
            WizardPair.SetMinimumColumnWidth(colours,168);
            for(int i=0;i<2;i++)
            {
                var input=i==0?primary:secondary;var row=(Control)input.Parent;Detach(row);
                input.SetResourceReference(TemplatedControl.ThemeProperty,"SettingsInput");input.Background=Brushes.Transparent;input.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");input.MinHeight=36;input.FontSize=13;
                foreach(var old in Descendants(row).OfType<Border>().Where(b=>b is OperatorActionBorder || b.Cursor==new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)))
                {old.CornerRadius=new CornerRadius(10);old.Height=40;old.Width=40;if(old.Child is TextBlock)old.Child=new OperatorSymbol{Kind="eyedropper",Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};}
                var field=Field(i==0?"Primary colour":"Secondary colour",row);Grid.SetColumn(field,i*2);colours.Children.Add(field);
            }
            var preset=(WrapPanel)wizard.FindName($"{side}PresetPanel");Detach(preset);
            var empty=(TextBlock)wizard.FindName($"{side}PresetEmpty");Detach(empty);
            var oldPreset=(Border)wizard.FindName($"{side}PresetCard");oldPreset.Child=null;oldPreset.RenderTransform=new MatrixTransform(Matrix.Identity);oldPreset.Padding=new Thickness(0);oldPreset.BorderThickness=new Thickness(0);oldPreset.Background=Brushes.Transparent;
            var saved=new StackPanel();saved.Children.Add(Label("Saved clubs",13));
            saved.Children.Add(new ScrollViewer{Content=preset,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,10,0,0)});
            saved.Children.Add(empty);oldPreset.Child=saved;Detach(oldPreset);
            var oldCard=(Border)wizard.FindName($"{side}TeamCard");oldCard.Child=null;oldCard.RenderTransform=new MatrixTransform(Matrix.Identity);oldCard.Padding=new Thickness(0);oldCard.Background=Brushes.Transparent;oldCard.BorderThickness=new Thickness(0);Detach(oldCard);
            var body=new StackPanel();
            var header=new Grid();header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var identity=new StackPanel();identity.Children.Add(Label(home?"Home team":"Away team",24,true));identity.Children.Add(Label("Choose a club, or enter your own team.",13));header.Children.Add(identity);
            var emblem=new OperatorSymbol{Kind="teams",Width=27,Height=27,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};emblem.SetResourceReference(OperatorSymbol.ForegroundProperty,"ControlCentre.AccentBrush");Grid.SetColumn(emblem,1);header.Children.Add(emblem);body.Children.Add(header);
            var search=ownButtons.First(b=>(b.Content as string)=="Find a club");Detach(search);search.Content="Find a club";search.MinHeight=46;search.Height=double.NaN;search.FontSize=15;search.Margin=new Thickness(0,18,0,14);search.HorizontalAlignment=HorizontalAlignment.Stretch;body.Children.Add(search);
            body.Children.Add(oldPreset);
            var form=new StackPanel{Margin=new Thickness(0,16,0,0)};
            Detach(name);name.Margin=new Thickness(0);name.MinWidth=0;name.HorizontalAlignment=HorizontalAlignment.Stretch;
            Detach(abbr);abbr.Margin=new Thickness(0);abbr.MinWidth=0;abbr.HorizontalAlignment=HorizontalAlignment.Stretch;abbr.MaxWidth=double.PositiveInfinity;
            var names=new Grid();names.ColumnDefinitions.Add(new());names.ColumnDefinitions.Add(new(){Width=new GridLength(12)});names.ColumnDefinitions.Add(new(){Width=new GridLength(96)});
            names.Children.Add(Field("Team name",name));var shortName=Field("Abbreviation",abbr);Grid.SetColumn(shortName,2);names.Children.Add(shortName);form.Children.Add(names);form.Children.Add(colours);
            var logoText=(TextBlock)wizard.FindName($"Setup{side}LogoText");var logoActions=(Control)logoText.Parent;Detach(logoActions);logoText.FontSize=12;logoText.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
            foreach(var action in Descendants(logoActions).OfType<Button>()){action.Height=double.NaN;action.MinHeight=36;action.FontSize=14;}
            var preview=(Border)wizard.FindName($"Setup{side}LogoPreview");Detach(preview);preview.Background=Brushes.Transparent;preview.Padding=new Thickness(0);preview.Margin=new Thickness(0,10,0,0);
            var crest=new Grid{Height=72};crest.ColumnDefinitions.Add(new());crest.ColumnDefinitions.Add(new(){Width=new GridLength(76)});
            logoActions.VerticalAlignment=VerticalAlignment.Center;crest.Children.Add(logoActions);
            preview.MaxHeight=64;preview.MaxWidth=64;preview.Margin=new Thickness(6);Grid.SetColumn(preview,1);crest.Children.Add(preview);
            form.Children.Add(Field("Club crest",crest));
            if(wizard.FindName($"Setup{side}VideoText") is TextBlock video)
            {var videoActions=(Control)video.Parent;Detach(videoActions);form.Children.Add(Field("Custom goal video",videoActions));}
            var actions=new WrapPanel{Margin=new Thickness(0,18,0,0)};
            foreach(var button in ownButtons.Where(b=>b.Content is string caption && (caption=="Save Current" || caption=="Delete")))
            {Detach(button);button.Content=(string)button.Content=="Delete"?"Delete preset":"Save as preset";button.Height=double.NaN;button.MinHeight=38;button.Margin=new Thickness(0,0,8,6);actions.Children.Add(button);}
            form.Children.Add(actions);
            var presetStatus=new NativeTextBlock{Tag="team-preset-status:"+side,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,6),IsVisible=false};
            presetStatus.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");
            Avalonia.Automation.AutomationProperties.SetLiveSetting(presetStatus,Avalonia.Automation.AutomationLiveSetting.Polite);
            form.Children.Add(presetStatus);oldCard.Child=form;body.Children.Add(oldCard);
            var surface=new OperatorGlassSurface{Material=GlassMaterial.Regular,CornerRadius=new CornerRadius(30),Padding=new Thickness(24),BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Top,Child=body};surface.SetResourceReference(Border.BackgroundProperty,"ControlCentre.GlassBrush");surface.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");Grid.SetColumn(surface,home?0:2);pair.Children.Add(surface);
        }
        stage.Children.Clear();stage.Children.Add(pair);
        RestyleWeather(wizard);
    }
    internal static void SetPresetStatus(UserControl wizard,string side,string message,bool pending=false)
    {
        if(wizard.FindName(side+"TeamCard") is not Border card)return;
        var status=Descendants(card).OfType<TextBlock>().FirstOrDefault(text=>Equals(text.Tag,"team-preset-status:"+side));
        if(status is not null){status.Text=message;status.IsVisible=string.IsNullOrWhiteSpace(message)?false:true;}
        foreach(var action in Descendants(card).OfType<Button>().Where(button=>button.Content is string text && (text=="Save as preset"||text=="Delete preset")))
            action.IsEnabled=!pending;
    }
    internal static void SetPresetActionsPending(UserControl wizard,bool pending)
    {
        foreach(string side in new[]{"Home","Away"})
            if(wizard.FindName(side+"TeamCard") is Border card)
                foreach(var action in Descendants(card).OfType<Button>().Where(button=>button.Content is string text && (text=="Save as preset"||text=="Delete preset")))
                    action.IsEnabled=!pending;
    }
    private static bool IsWithinSide(Control element,string side)
    {
        for(AvaloniaObject? parent=element;parent is Control current;parent=current.Parent)
            if((current.Name ?? string.Empty).StartsWith(side,StringComparison.Ordinal))return true;
        // Actions already detached while gathering must retain their side identity.
        return Equals(element.Tag,"team-editor:"+side);
    }
    private static StackPanel Field(string title,Control input)
    {var field=new StackPanel{Margin=new Thickness(0,0,0,14)};field.Children.Add(Label(title,13));input.Margin=new Thickness(input.Margin.Left,6,input.Margin.Right,input.Margin.Bottom);field.Children.Add(input);return field;}
    private static TextBlock Label(string text,double size,bool strong=false)
    {var label=new NativeTextBlock{Text=text,FontSize=size,FontWeight=strong?FontWeight.SemiBold:FontWeight.Normal,TextWrapping=TextWrapping.Wrap};label.SetResourceReference(TextBlock.ForegroundProperty,strong?"ControlCentre.TextBrush":"ControlCentre.SecondaryTextBrush");return label;}
    private static void RestyleWeather(UserControl wizard)
    {
        if(wizard.FindName("SetupWeatherLocation") is not ComboBox location)return;
        Border? surface=location.Parent as Border;
        for(AvaloniaObject? parent=location.Parent;parent is Control current;parent=current.Parent)
            if(current is OperatorGlassSurface glass){surface=glass;break;}
        if(surface is null)return;Detach(location);surface.Child=null;
        surface.CornerRadius=new CornerRadius(28);surface.Padding=new Thickness(24);surface.BorderThickness=new Thickness(1);surface.SetResourceReference(Border.BackgroundProperty,"ControlCentre.GlassBrush");surface.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");
        var body=new StackPanel();body.Children.Add(new OperatorSymbol{Kind="weather",Width=28,Height=28,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,10)});body.Children.Add(Label("Weather at your venue",23,true));body.Children.Add(Label("Search for the town or suburb nearest the ground.",14));location.Height=46;location.Margin=new Thickness(0,16,0,0);body.Children.Add(location);surface.Child=body;
    }
    private static IEnumerable<AvaloniaObject> Descendants(AvaloniaObject root)
    {foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<AvaloniaObject>()){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private static void Detach(Control element)
    {switch(element.Parent){case Panel panel:panel.Children.Remove(element);break;case Decorator decorator:decorator.Child=null;break;case ContentControl content:content.Content=null;break;}}
}
