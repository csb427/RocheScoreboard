using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Interactivity;
using Avalonia.Threading;
namespace Roche_Scoreboard.Views;
public class OperatorActionBorder:Border
{
    public static readonly RoutedEvent<RoutedEventArgs> ActivatedEvent=RoutedEvent.Register<OperatorActionBorder,RoutedEventArgs>(nameof(Activated),RoutingStrategies.Bubble);
    public event EventHandler<RoutedEventArgs> Activated {add=>AddHandler(ActivatedEvent,value);remove=>RemoveHandler(ActivatedEvent,value);}
    public OperatorActionBorder(){Focusable=true;this.Bind(RocheScoreboard.Mac.Native.NativeToolTipPolicy.EnabledProperty,new Avalonia.Data.Binding{Source=this,Path=new RocheScoreboard.Mac.Native.PropertyPath(Roche_Scoreboard.Services.OperatorGuidance.ShowHintsProperty)});}
    protected override void OnKeyDown(KeyEventArgs e)
    {if(e.Key is Key.Enter or Key.Space){e.Handled=true;Activate();return;}base.OnKeyDown(e);}
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {base.OnPointerReleased(e);}
    internal void Activate(){if(IsEffectivelyEnabled&&IsEffectivelyVisible){RaiseEvent(RocheScoreboard.Mac.Native.NativeActivation.Release(this));RaiseEvent(new RoutedEventArgs(ActivatedEvent,this));}}
    protected override AutomationPeer OnCreateAutomationPeer()=>new ActionPeer(this);
    private sealed class ActionPeer(OperatorActionBorder owner):ControlAutomationPeer(owner),IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;
        protected override string GetClassNameCore()=>"Action tile";
        protected override string GetNameCore()
        {
            string existing=base.GetNameCore();if(!string.IsNullOrWhiteSpace(existing))return existing;
            string label=string.Join(" ",Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(owner).OfType<TextBlock>().Where(text=>text.IsEffectivelyVisible&&!string.IsNullOrWhiteSpace(text.Text)).Select(text=>text.Text));
            return label.Length>0?label:ToolTip.GetTip(owner)?.ToString()??"Action";
        }
        public void Invoke(){if(owner.IsEffectivelyEnabled)Dispatcher.UIThread.Post(owner.Activate);}
    }
}
