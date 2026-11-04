using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Views;

/// <summary>Operator-only accent colour selection with an immediate, persistent preview.</summary>
internal sealed class OperatorAccentPreferences : UserControl
{
    private static readonly SolidColorBrush SharedAccentBrush = new(Color.FromRgb(10,87,255));
    private static readonly SolidColorBrush SharedSelectionBrush = new(Color.FromRgb(230,241,255));
    private readonly Dictionary<OperatorAccentPalette,RadioButton> _choices=new();
    private readonly Border _preview=new(){CornerRadius=new CornerRadius(18),Padding=new Thickness(16),Margin=new Thickness(0,12,0,0)};
    private readonly TextBlock _sampleText=new(){Text="Selected controls and highlights",FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button _sampleButton=new(){Content="Accent preview",Padding=new Thickness(16,8,16,8),MinHeight=42,Margin=new Thickness(0,12,0,0)};
    private bool _syncing;

    internal OperatorAccentPreferences()
    {
        var body=new StackPanel();
        body.Children.Add(Label("Accent colour",21,true));
        body.Children.Add(Label("Choose a highlight for selected controls, focus accents and scorecard details. Team colours stay unchanged.",13));
        var palette=new OperatorSegmentedPanel{Rows=1,AllowScrubbing=true};
        string group="accent-"+Guid.NewGuid().ToString("N");
        foreach(var value in Enum.GetValues<OperatorAccentPalette>())
        {
            var choice=new RadioButton{Content=PaletteLabel(value),GroupName=group,FontSize=12,Padding=new Thickness(8,8,8,8),MinHeight=44};
            choice.SetResourceReference(StyleProperty,"SettingsSegment");
            System.Windows.Automation.AutomationProperties.SetName(choice,PaletteLabel(value)+" accent colour");
            choice.Checked+=(_,_)=>{if(!_syncing)OperatorAppearance.ConfigureAccentPalette(value);};
            _choices[value]=choice;palette.Children.Add(choice);
        }
        var surface=new OperatorGlassSurface{Material=GlassMaterial.Regular,CornerRadius=new CornerRadius(24),Padding=new Thickness(4),Child=palette};
        surface.SetResourceReference(BackgroundProperty,"ControlCentre.GlassBrush");body.Children.Add(surface);
        _preview.Child=_sampleText;_preview.SetResourceReference(BackgroundProperty,"ControlCentre.SelectionBrush");_preview.SetResourceReference(BorderBrushProperty,"ControlCentre.AccentBrush");_preview.BorderThickness=new Thickness(2);body.Children.Add(_preview);
        _sampleButton.SetResourceReference(StyleProperty,"ControlCentre.Button");
        _sampleButton.SetResourceReference(BackgroundProperty,"ControlCentre.SelectionBrush");_sampleButton.SetResourceReference(ForegroundProperty,"ControlCentre.AccentBrush");
        body.Children.Add(_sampleButton);
        var reset=new Button{Content="Restore Sky accent",HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,16,0,0),FontSize=13};
        reset.SetResourceReference(StyleProperty,"ToolBtn");reset.Click+=(_,_)=>OperatorAppearance.ConfigureAccentPalette(OperatorAccentPalette.Sky);body.Children.Add(reset);
        Content=body;
        Loaded+=(_,_)=>{OperatorAppearance.Changed-=Sync;OperatorAppearance.Changed+=Sync;Sync();};
        Unloaded+=(_,_)=>OperatorAppearance.Changed-=Sync;
    }

    internal static void ApplyAccentResources()
    {
        if(WpfApplication.Current is not { } app)return;
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var themeService=app.GetType().GetProperty("ThemeService",flags)?.GetValue(app);
        var currentTheme=themeService?.GetType().GetProperty("CurrentTheme",flags)?.GetValue(themeService);
        bool dark=Equals(currentTheme,AppTheme.Dark);
        var (accent,selection)=ColorsFor(OperatorAppearance.AccentPalette,dark);
        app.Resources["Operator.CustomAccentColor"]=accent;
        app.Resources["Operator.CustomSelectionColor"]=selection;
        SharedAccentBrush.Color=accent;
        SharedSelectionBrush.Color=selection;
        UpdateMergedAccentBrushes(app.Resources,SharedAccentBrush,SharedSelectionBrush);
        foreach (Window window in app.Windows)
        {
            UpdateMergedAccentBrushes(window.Resources,SharedAccentBrush,SharedSelectionBrush);
            if(window.Content is DependencyObject content)UpdateVisualTreeAccentBrushes(content,SharedAccentBrush,SharedSelectionBrush);
        }
    }

    private static void UpdateVisualTreeAccentBrushes(DependencyObject element,SolidColorBrush accent,SolidColorBrush selection)
    {
        if(element is FrameworkElement frameworkElement)
            UpdateMergedAccentBrushes(frameworkElement.Resources,accent,selection);
        int children=System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
        for(int index=0;index<children;index++)
            UpdateVisualTreeAccentBrushes(System.Windows.Media.VisualTreeHelper.GetChild(element,index),accent,selection);
    }

    private static void UpdateMergedAccentBrushes(ResourceDictionary resources,SolidColorBrush accent,SolidColorBrush selection)
    {
        Update("ControlCentre.AccentBrush",accent);
        Update("ControlCentre.SelectionBrush",selection);
        foreach(ResourceDictionary merged in resources.MergedDictionaries)UpdateMergedAccentBrushes(merged,accent,selection);
        void Update(string key,SolidColorBrush source)
        {
            if(!resources.Contains(key))return;
            if(resources[key] is not SolidColorBrush target || ReferenceEquals(target,source) || target.IsFrozen)
            { resources[key]=source; return; }
            target.BeginAnimation(SolidColorBrush.ColorProperty,null);
            System.Windows.Data.BindingOperations.SetBinding(target,SolidColorBrush.ColorProperty,new System.Windows.Data.Binding(nameof(SolidColorBrush.Color)){Source=source,Mode=System.Windows.Data.BindingMode.OneWay});
        }
    }

    internal static SolidColorBrush AccentBrushResource => SharedAccentBrush;
    internal static SolidColorBrush SelectionBrushResource => SharedSelectionBrush;

    private static (Color Accent,Color Selection) ColorsFor(OperatorAccentPalette palette,bool dark)=>palette switch
    {
        OperatorAccentPalette.Teal=>dark?(Color.FromRgb(93,224,199),Color.FromRgb(27,67,61)):(Color.FromRgb(0,112,101),Color.FromRgb(216,245,239)),
        OperatorAccentPalette.Violet=>dark?(Color.FromRgb(190,161,255),Color.FromRgb(61,47,88)):(Color.FromRgb(100,65,173),Color.FromRgb(236,228,252)),
        OperatorAccentPalette.Amber=>dark?(Color.FromRgb(255,203,106),Color.FromRgb(76,59,31)):(Color.FromRgb(139,89,0),Color.FromRgb(250,238,210)),
        _=>dark?(Color.FromRgb(123,184,255),Color.FromRgb(37,61,85)):(Color.FromRgb(10,87,255),Color.FromRgb(230,241,255))
    };
    private static string PaletteLabel(OperatorAccentPalette palette)=>palette.ToString();
    private static TextBlock Label(string text,double size,bool strong=false)
    {
        var label=new TextBlock{Text=text,FontSize=size,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
        label.SetResourceReference(TextBlock.ForegroundProperty,strong?"ControlCentre.TextBrush":"ControlCentre.SecondaryTextBrush");return label;
    }
    private void Sync()
    {
        _syncing=true;
        try{foreach(var (value,button) in _choices)button.IsChecked=value==OperatorAppearance.AccentPalette;ApplyAccentResources();}
        finally{_syncing=false;}
    }
}
