using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Roche_Scoreboard.Services;
using UserControl = System.Windows.Controls.UserControl;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;

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
            if (wizard.FindName(name) is not UIElement decoration) continue;
            decoration.BeginAnimation(UIElement.OpacityProperty, null);
            decoration.Visibility = Visibility.Collapsed;
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
            var content=new Grid();label.Margin=new Thickness(22,0,22,0);label.HorizontalAlignment=System.Windows.HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;
            label.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=button});content.Children.Add(label);
            var icon=new OperatorSymbol{Kind=next?"forward":"back",Width=18,Height=18,HorizontalAlignment=next?System.Windows.HorizontalAlignment.Right:System.Windows.HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
            icon.SetBinding(OperatorSymbol.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){Source=button});content.Children.Add(icon);
            button.Content=content;button.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch;
            if(!next){button.SetResourceReference(Control.BackgroundProperty,"ControlCentre.GlassBrush");button.SetResourceReference(Control.ForegroundProperty,"ControlCentre.TextBrush");}
        }
    }

    private static void Reflow(UserControl wizard)
    {
        bool compact = wizard.ActualWidth < 620 || wizard.ActualHeight < 500;
        if (wizard.FindName("HeaderPanel") is Border header)
        {
            header.Margin = new Thickness(compact ? 12 : 18, compact ? 8 : 14, compact ? 12 : 18, 8);
            header.Padding = new Thickness(compact ? 10 : 16, compact ? 8 : 12, compact ? 10 : 16, compact ? 6 : 10);
        }
        if (wizard.FindName("HeaderTitle") is TextBlock title) title.FontSize = compact ? 18 : 23;
        if (wizard.FindName("StepIndicator") is FrameworkElement steps) steps.Height = compact ? 53 : 62;
        for (int i = 1; i <= 4; i++)
        {
            if (wizard.FindName($"Step{i}Navigation") is ToggleButton choice) choice.Height = compact ? 51 : 60;
            if (wizard.FindName($"Step{i}Symbol") is OperatorSymbol icon) icon.Width = icon.Height = compact ? 20 : 23;
        }
        if (wizard.FindName("WizardBody") is FrameworkElement body)
            body.Margin = new Thickness(compact ? 12 : 20, 8, compact ? 12 : 20, 20);
        if(wizard.FindName("NavBar") is Grid nav && nav.ColumnDefinitions.Count==3)
        {nav.ColumnDefinitions[0].Width=new GridLength(104);if(nav.Parent is Border shell)shell.MaxWidth=560;}
    }
}
