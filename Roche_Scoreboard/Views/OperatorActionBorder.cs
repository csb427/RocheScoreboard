using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Roche_Scoreboard.Views;

/// <summary>Retains existing tile layout/events while adding native keyboard and Invoke semantics.</summary>
public class OperatorActionBorder:Border
{
    public OperatorActionBorder(){Focusable=true;SetBinding(ToolTipService.IsEnabledProperty,new System.Windows.Data.Binding{Source=this,Path=new PropertyPath(Roche_Scoreboard.Services.OperatorGuidance.ShowHintsProperty)});}
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs args)
    {
        if(args.Key is Key.Enter or Key.Space){args.Handled=true;Activate();return;}base.OnKeyDown(args);
    }
    internal void Activate()
    {
        if(!IsEnabled||!IsVisible)return;
        RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=MouseLeftButtonUpEvent,Source=this});
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new ActionPeer(this);
    private sealed class ActionPeer(OperatorActionBorder owner):FrameworkElementAutomationPeer(owner),IInvokeProvider
    {
        public override object? GetPattern(PatternInterface pattern)=>pattern==PatternInterface.Invoke?this:base.GetPattern(pattern);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;
        protected override string GetClassNameCore()=>"Action tile";
        protected override string GetNameCore()
        {
            string name=base.GetNameCore();if(!string.IsNullOrWhiteSpace(name))return name;
            var labels=Labels(owner).Where(text=>text.IsVisible&&!string.IsNullOrWhiteSpace(text.Text)).Select(text=>text.Text);
            return string.Join(" ",labels) is {Length:>0} label?label:owner.ToolTip?.ToString()??"Action";
        }
        private static IEnumerable<TextBlock> Labels(DependencyObject root)
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is TextBlock text)yield return text;foreach(var label in Labels(child))yield return label;}}
        public void Invoke()
        {if(!owner.IsEnabled)throw new System.Windows.Automation.ElementNotEnabledException();owner.Dispatcher.BeginInvoke(new Action(owner.Activate));}
    }
}
