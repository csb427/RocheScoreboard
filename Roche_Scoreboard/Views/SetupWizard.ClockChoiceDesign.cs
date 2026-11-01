using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Panel=System.Windows.Controls.Panel;
using RadioButton=System.Windows.Controls.RadioButton;
using Brushes=System.Windows.Media.Brushes;

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
            choice.Style=(Style)FindResource("WizardClockChoice");
            choice.Visibility=Visibility.Visible;choice.Margin=new Thickness(0);
            choice.Background=Brushes.Transparent;
            choice.Content=content;card.Padding=new Thickness(0);card.Child=choice;
            System.Windows.Automation.AutomationProperties.SetName(choice,name);
        }
    }
}
