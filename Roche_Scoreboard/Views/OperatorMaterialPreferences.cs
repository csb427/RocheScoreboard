using System.Windows;
using System.Windows.Controls;
using Roche_Scoreboard.Services;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Roche_Scoreboard.Views;

/// <summary>Shared, immediately applied operator materials. Never changes a public display preference.</summary>
internal sealed class OperatorMaterialPreferences : UserControl
{
    private readonly Dictionary<OperatorGlassIntensity,RadioButton> _strength=new();
    private readonly Dictionary<OperatorBackdropStyle,RadioButton> _backdrops=new();
    private readonly TextBlock _strengthDetail=new(){FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock _backdropDetail=new(){FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private bool _syncing;
    internal OperatorMaterialPreferences()
    {
        var body=new StackPanel();
        body.Children.Add(Label("Glass & backdrop",21,true));
        body.Children.Add(Label("Tune the look of your operator workspace.",13));
        body.Children.Add(Label("Glass intensity",14,true,20));
        var strength=Segments();
        string strengthGroup="glass-"+Guid.NewGuid().ToString("N");
        foreach(var value in Enum.GetValues<OperatorGlassIntensity>())
        {
            var choice=Choice(value.ToString(),strengthGroup);_strength[value]=choice;strength.Children.Add(choice);
            choice.Checked+=(_,_)=>{if(!_syncing)OperatorAppearance.ConfigureMaterial(value,OperatorAppearance.Backdrop);};
        }
        body.Children.Add(Surface(strength));_strengthDetail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");body.Children.Add(_strengthDetail);
        body.Children.Add(Label("Backdrop",14,true,20));
        var backdrops=Segments();string backdropGroup="backdrop-"+Guid.NewGuid().ToString("N");
        foreach(var value in Enum.GetValues<OperatorBackdropStyle>())
        {
            var choice=Choice(value==OperatorBackdropStyle.Black?"Mono":value.ToString(),backdropGroup);_backdrops[value]=choice;backdrops.Children.Add(choice);
            choice.Checked+=(_,_)=>{if(!_syncing)OperatorAppearance.ConfigureMaterial(OperatorAppearance.GlassIntensity,value);};
        }
        body.Children.Add(Surface(backdrops));_backdropDetail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");body.Children.Add(_backdropDetail);
        var reset=new Button{Content="Restore defaults",ToolTip="Use Vivid glass with the Colour backdrop.",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,18,0,0),FontSize=13};reset.SetResourceReference(StyleProperty,"ToolBtn");
        System.Windows.Automation.AutomationProperties.SetName(reset,"Restore material defaults");
        reset.Click+=(_,_)=>OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Vivid,OperatorBackdropStyle.Colour);body.Children.Add(reset);
        Content=body;
        Loaded+=(_,_)=>{OperatorAppearance.Changed-=Sync;OperatorAppearance.Changed+=Sync;Sync();};
        Unloaded+=(_,_)=>OperatorAppearance.Changed-=Sync;
    }
    private static TextBlock Label(string text,double size,bool strong=false,double top=0)
    {
        var label=new TextBlock{Text=text,FontSize=size,FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,top,0,6)};
        label.SetResourceReference(TextBlock.ForegroundProperty,strong?"ControlCentre.TextBrush":"ControlCentre.SecondaryTextBrush");return label;
    }
    private static OperatorSegmentedPanel Segments()=>new(){Rows=1};
    private static OperatorGlassSurface Surface(OperatorSegmentedPanel content)
    {
        var surface=new OperatorGlassSurface{Material=GlassMaterial.Regular,CornerRadius=new CornerRadius(24),Padding=new Thickness(4),Child=content};
        surface.SetResourceReference(BackgroundProperty,"ControlCentre.GlassBrush");return surface;
    }
    private static RadioButton Choice(string label,string group)
    {
        var choice=new RadioButton{Content=label,GroupName=group,FontSize=12,Padding=new Thickness(8,8,8,8),MinHeight=40};choice.SetResourceReference(StyleProperty,"SettingsSegment");
        System.Windows.Automation.AutomationProperties.SetName(choice,label);return choice;
    }
    private void Sync()
    {
        _syncing=true;
        try
        {
            foreach(var (value,button) in _strength)button.IsChecked=value==OperatorAppearance.GlassIntensity;
            foreach(var (value,button) in _backdrops)button.IsChecked=value==OperatorAppearance.Backdrop;
            _strengthDetail.Text=OperatorAppearance.GlassIntensity switch{OperatorGlassIntensity.Soft=>"Gentle lensing and quieter reflections.",OperatorGlassIntensity.Balanced=>"Clear edges with moderate lensing.",_=>"Pronounced lensing, clear rims and deeper reflections."};
            _backdropDetail.Text=OperatorAppearance.Backdrop switch{OperatorBackdropStyle.Black=>"Sculpted monochrome light.",OperatorBackdropStyle.Neutral=>"A quiet, neutral workspace.",_=>"Colourful light ribbons behind the glass."};
        }
        finally{_syncing=false;}
    }
}
