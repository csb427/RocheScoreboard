// Generated from Views/SetupWizard.ClockChoiceDesign.cs; edit translator/adapters.
// Source SHA-256: a9bb8b3d84156b841cea456baaf519809ddd52c853b276090733256f4d4638d8
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Panel=Avalonia.Controls.Panel;
using RadioButton=Avalonia.Controls.RadioButton;
using Brushes=Avalonia.Media.Brushes;

namespace Roche_Scoreboard.Views;

public partial class SetupWizard
{
    private bool _clockChoicesInstalled;
    private void InstallClockChoices()
    {
        if(_clockChoicesInstalled)return;
        _clockChoicesInstalled=true;
        Install(CountUpCard,SetupCountUp,"Count up: elapsed playing time");
        Install(CountdownCard,SetupCountdown,"Count down: time remaining in the quarter");
        void Install(OperatorGlassSurface card,RadioButton choice,string name)
        {
            // Keep the original grouped radio, events and state. Its former
            // collapsed storage panel is not an accessible operator control.
            if(choice.Parent is Panel panel)panel.Children.Remove(choice);
            var content=card.Child;card.Child=null;
            choice.Theme =NativeResources.Require<Avalonia.Styling.ControlTheme>(this, "WizardClockChoice");
            choice.IsVisible=true;choice.Margin=new Thickness(0);
            choice.Background=Brushes.Transparent;
            choice.Content=content;card.Padding=new Thickness(0);card.Child=choice;
            Avalonia.Automation.AutomationProperties.SetName(choice,name);
        }
    }
}
