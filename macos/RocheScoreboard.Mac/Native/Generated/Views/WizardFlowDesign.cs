// Generated from Views/WizardFlowDesign.cs; edit translator/adapters.
// Source SHA-256: fed8abb716ca6b1166854d637c8861fcef16da3d6d3908af0520150081189445
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
    internal static void Enable(UserControl wizard, Action settleMotion)
    {
        void PreferenceChanged(){if(!OperatorMotion.Enabled)settleMotion();}
        wizard.SizeChanged += (_, _) => Reflow(wizard);
        wizard.Loaded += (_, _) => { OperatorAppearance.Changed-=PreferenceChanged;OperatorAppearance.Changed+=PreferenceChanged;WizardTeamEditor.Install(wizard); InstallNavigation(wizard); Reflow(wizard); };
        wizard.Unloaded += (_, _) => OperatorAppearance.Changed-=PreferenceChanged;
    }

    internal static void SelectStep(UserControl wizard, int selected, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            if (wizard.FindName($"Step{i}Navigation") is ToggleButton choice) choice.IsChecked = selected == i;
            if (wizard.FindName($"Step{i}Symbol") is OperatorSymbol symbol) symbol.IsActive = selected == i;
        }
        bool unified = wizard.FindName("SportStepNavigation") is ToggleButton sport && sport.IsVisible == true;
        if (wizard.FindName("SportStepNavigation") is ToggleButton sportChoice) sportChoice.IsChecked = selected == 0;
        if (wizard.FindName("WizardStepCounter") is TextBlock counter) counter.Text = unified ? $"{selected + 1} of {count + 1}" : $"{selected} of {count}";
        Reflow(wizard);
    }

    internal static void TryCancelOnEscape(UserControl wizard, Avalonia.Input.KeyEventArgs args, Action cancel)
    {
        if (args.Handled || args.Key != Avalonia.Input.Key.Escape || NativeKeyboard.FocusedElement is Avalonia.Controls.MenuItem) return;
        for (AvaloniaObject? node = args.Source as AvaloniaObject; node is not null;)
        {
            if (node is Avalonia.Controls.ComboBox { IsDropDownOpen: true }) return;
            if (node is not Visual) break;
            node = VisualTreeHelper.GetParent(node);
        }
        args.Handled = true; cancel();
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
            // Reserve space only on the arrow's side. Reserving both sides
            // clipped "Back" in the compact 84px navigation column.
            var content=new Grid();label.Margin=next?new Thickness(0,0,22,0):new Thickness(22,0,0,0);label.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center;label.VerticalAlignment=VerticalAlignment.Center;
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
        for (int i = 0; i <= 4; i++)
        {
            if (wizard.FindName(i == 0 ? "SportStepNavigation" : $"Step{i}Navigation") is ToggleButton choice) choice.Height = compact ? 51 : 60;
            if (wizard.FindName($"Step{i}Symbol") is OperatorSymbol icon) icon.Width = icon.Height = compact ? 20 : 23;
        }
        if (wizard.FindName("SportOptionsPreview") is Control preview) preview.Height = compact ? 51 : 60;
        if (wizard.FindName("SportQuestion") is TextBlock question) question.FontSize = compact ? 22 : 28;
        if (wizard.FindName("SportDescription") is TextBlock description) description.IsVisible = wizard.NativeHeight() < 420 ? false : true;
        foreach (string side in new[] { "Afl", "Cricket" })
        {
            if (wizard.FindName(side + "SportChoice") is TemplatedControl sportChoice) sportChoice.Padding = new Thickness(compact ? 12 : 22);
            if (wizard.FindName(side + "SportSymbol") is OperatorSymbol sportSymbol)
            { sportSymbol.Width = sportSymbol.Height = compact ? 32 : 52; sportSymbol.Margin = new Thickness(0, 0, 0, compact ? 8 : 20); }
            if (wizard.FindName(side + "SportLabel") is TextBlock sportLabel) sportLabel.FontSize = compact ? 20 : 24;
            if (wizard.FindName(side + "SportDescription") is TextBlock sportDescription) sportDescription.IsVisible = wizard.NativeHeight() < 420 ? false : true;
        }
        if (wizard.FindName("WizardBody") is Control body)
            body.Margin = new Thickness(compact ? 12 : 20, 8, compact ? 12 : 20, 20);
        if(wizard.FindName("NavBar") is Grid nav && nav.ColumnDefinitions.Count==3)
        {nav.ColumnDefinitions[0].Width=new GridLength(compact ? 84 : 104);if(nav.Parent is Border shell)shell.MaxWidth=560;}
        if(wizard.FindName("NextButton") is Button {Content:Grid forward})
        {
            // At the narrowest supported width the action's complete name is
            // more useful than a redundant directional glyph.
            bool showArrow=wizard.NativeWidth()>=380;
            foreach(var icon in forward.Children.OfType<OperatorSymbol>())icon.IsVisible=showArrow?true:false;
            foreach(var label in forward.Children.OfType<TextBlock>())label.Margin=new Thickness(0,0,showArrow?22:0,0);
        }
    }
}
