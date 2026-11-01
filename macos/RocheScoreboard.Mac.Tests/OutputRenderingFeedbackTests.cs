using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory] [InlineData(1280,850)] [InlineData(420,320)]
    [Trait("Evidence","OutputRenderingFeedback")]
    public async Task ActualOutputShortcutExplainsRequiredOpaquePolicyAndPreservesTheMatch(int width,int height)
    {
        await using var f=await AflFixture.Create(width,height);
        var output=PresPlanRead<Window>(f,"_displayWindow");Assert.True(output.IsVisible);
        string state=ManualWeatherModel(f);bool visible=output.IsVisible;
        // The existing preference is a diagnostic input, not the effective
        // platform compositor state. Exercise BOTH flip directions publicly.
        foreach(bool prior in new[]{true,false})
        {
            DisplaySettings.OpaqueDisplayWindows=prior;
            output.Activate();output.Focusable=true;Assert.True(output.Focus());
            Exception? error=null;Window? acknowledgement=null;
            using var observer=DispatcherTimer.RunOnce(()=>
            {
                try
                {
                    acknowledgement=Assert.Single(output.OwnedWindows,w=>w.Title=="Display rendering mode");
                    Assert.Same(output,acknowledgement.Owner);
                    string text=string.Join("\n",acknowledgement.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text));
                    Assert.Contains("opaque rendering is required",text,StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("embedded video",text,StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("unchanged",text,StringComparison.OrdinalIgnoreCase);
                    Assert.Contains(prior?"TRANSPARENT":"OPAQUE",text,StringComparison.Ordinal);
                    Assert.DoesNotContain("will use TRANSPARENT",text,StringComparison.Ordinal);
                    Assert.Equal(!prior,DisplaySettings.OpaqueDisplayWindows);
                    Assert.Equal(state,ManualWeatherModel(f));Assert.Equal(visible,output.IsVisible);
                    Capture(acknowledgement,"output-rendering-"+width+"-"+prior);
                }
                catch(Exception e){error=e;}
                finally
                {
                    acknowledgement??=output.OwnedWindows.FirstOrDefault(w=>w.Title=="Display rendering mode");
                    if(acknowledgement is not null)
                    {
                        var ok=acknowledgement.GetVisualDescendants().OfType<Button>().Single();
                        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(ok)).Invoke();
                    }
                }
            },TimeSpan.FromMilliseconds(80));
            output.KeyPressQwerty(PhysicalKey.T,RawInputModifiers.Control|RawInputModifiers.Shift);
            output.KeyReleaseQwerty(PhysicalKey.T,RawInputModifiers.Control|RawInputModifiers.Shift);
            if(error is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            Assert.NotNull(acknowledgement);Assert.DoesNotContain(output.OwnedWindows,w=>w.Title=="Display rendering mode");
            Assert.Equal(state,ManualWeatherModel(f));Assert.Equal(visible,output.IsVisible);Assert.Equal(!prior,DisplaySettings.OpaqueDisplayWindows);
        }
    }
}
