// Generated from Views/WizardFlowDesign.cs; edit translator/adapters.
// Source SHA-256: d3ac46c276c5a817c6efd5b2ef769d3a55be2bcd4d1a7ee41c2443453ee11799
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using UserControl = Avalonia.Controls.UserControl;
using Border = Avalonia.Controls.Border;
using Button = Avalonia.Controls.Button;
using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;

namespace Roche_Scoreboard.Views;

/// <summary>Shared operator-only wizard structure and semantic step feedback.</summary>
internal static class WizardFlowDesign
{
    internal static void Enable(UserControl wizard)
    {
        wizard.SizeChanged += (_, _) => Reflow(wizard);
        wizard.Loaded += (_, _) => { WizardTeamEditor.Install(wizard); InstallNavigation(wizard); Reflow(wizard); };
    }

    internal static void SelectStep(UserControl wizard, int selected, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            if (wizard.FindName($"Step{i}Navigation") is ToggleButton choice) choice.IsChecked = selected == i;
            if (wizard.FindName($"Step{i}Symbol") is OperatorSymbol symbol) symbol.IsActive = selected == i;
        }
        if (wizard.FindName("WizardStepCounter") is TextBlock counter) counter.Text = $"{selected} of {count}";
        Reflow(wizard);
    }

    internal static void StopDecoration(UserControl wizard)
    {
        foreach (string name in new[] { "HomeGlow", "AwayGlow", "BgGlow", "ClockGlow", "PlayersGlow", "FormatGlow", "MsgGlow", "WeatherGlowBg", "MsgSectionGlow", "FlashOverlay", "BottomAccent", "DividerGlowLine" })
        {
            if (wizard.FindName(name) is not Control decoration) continue;
            decoration.BeginAnimation(Control.OpacityProperty, null);
            decoration.IsVisible = false;
        }
        if (wizard.FindName("BeamTranslate") is TranslateTransform beam)
        { beam.BeginAnimation(TranslateTransform.XProperty, null); beam.X = 0; }
    }

    internal static void StyleChoice(Border card, bool selected)
    {
        card.SetResourceReference(Border.BackgroundProperty, selected ? "ControlCentre.GlassBrush" : "ControlCentre.SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, selected ? "ControlCentre.AccentBrush" : "ControlCentre.BorderBrush");
        card.BorderThickness = new Thickness(selected ? 2 : 1);
        card.CornerRadius = new CornerRadius(22);
        card.Effect = null;
    }

    private static void InstallNavigation(UserControl wizard)
    {
        foreach(string name in new[]{"BackButton","NextButton"})
        {
            if(wizard.FindName(name) is not Button button || button.Content is not TextBlock label)continue;
            bool next=name=="NextButton";button.Content=null;
            var content=new Grid();label.Margin=new Thickness(22,0,22,0);label.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;
            label.SetBinding(TextBlock.ForegroundProperty,new Avalonia.Data.Binding("Foreground"){Source=button});content.Children.Add(label);
            var icon=new OperatorSymbol{Kind=next?"forward":"back",Width=18,Height=18,HorizontalAlignment=next?Avalonia.Layout.HorizontalAlignment.Right:Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            icon.SetBinding(OperatorSymbol.ForegroundProperty,new Avalonia.Data.Binding("Foreground"){Source=button});content.Children.Add(icon);
            button.Content=content;button.HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;
            if(!next){button.SetResourceReference(TemplatedControl.BackgroundProperty,"ControlCentre.GlassBrush");button.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");}
        }
    }

    private static void Reflow(UserControl wizard)
    {
        bool compact = wizard.NativeWidth() < 620 || wizard.NativeHeight() < 500;
        if (wizard.FindName("HeaderPanel") is Border header)
        {
            header.Margin = new Thickness(compact ? 12 : 18, compact ? 8 : 14, compact ? 12 : 18, 8);
            header.Padding = new Thickness(compact ? 10 : 16, compact ? 8 : 12, compact ? 10 : 16, compact ? 6 : 10);
        }
        if (wizard.FindName("HeaderTitle") is TextBlock title) title.FontSize = compact ? 18 : 23;
        if (wizard.FindName("StepIndicator") is Control steps) steps.Height = compact ? 53 : 62;
        for (int i = 1; i <= 4; i++)
        {
            if (wizard.FindName($"Step{i}Navigation") is ToggleButton choice) choice.Height = compact ? 51 : 60;
            if (wizard.FindName($"Step{i}Symbol") is OperatorSymbol icon) icon.Width = icon.Height = compact ? 20 : 23;
        }
        if (wizard.FindName("WizardBody") is Control body)
            body.Margin = new Thickness(compact ? 12 : 20, 8, compact ? 12 : 20, 20);
        if(wizard.FindName("NavBar") is Grid nav && nav.ColumnDefinitions.Count==3)
        {nav.ColumnDefinitions[0].Width=new GridLength(104);if(nav.Parent is Border shell)shell.MaxWidth=560;}
    }
}
