using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)]
    [InlineData(420,320)]
    public async Task CancelOverlayFiveProbePointerCancelsActualForecastAndKeepsWeatherCardsReachable(int width,int height)
    {
        await ManualWeatherControlledScope.Run(async controlled =>
        {
            string directory = Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "roche-cancel-overlay-geometry"), width + "x" + height);
            Directory.CreateDirectory(directory);
            var f = controlled.Fixture;
            f.Window.Width=width;f.Window.Height=height;await Settle(400);
            var bug=await ManualWeatherPrepare(f, controlled, offAir: false);
            var pending=await ManualWeatherInvoke(f, controlled, "FORECAST");
            var target = Named<Button>(f.Window, "CancelOverlayButton");
            var history = new List<object>();
            string Identity(object? item) => item is Control c ? c.Name + ":" + c.GetType().Name : item?.GetType().Name ?? "null";
            object Point(Point value) => new { x = value.X, y = value.Y };
            void Record(string stage)
            {
                if (history.Count >= 200) return;
                var box = FinalsPainted(f.Window,target);
                var probes = FinalsProbes(box);
                var scrollers = target.GetVisualAncestors().OfType<ScrollViewer>().ToArray();
                history.Add(new {
                    stage, client = f.Window.ClientSize.ToString(), bounds = target.Bounds.ToString(), painted = box.ToString(),
                    target.IsEffectivelyVisible,target.IsEffectivelyEnabled,target.IsFocused,
                    target.MinHeight,target.Height,padding = target.Padding.ToString(),margin = target.Margin.ToString(),
                    probes = probes.Select((point,index) => new { index, point = Point(point),
                        reachesTarget = Within(target,f.Window.InputHitTest(point)),
                        hit = Identity(f.Window.InputHitTest(point)),
                        ancestors = (f.Window.InputHitTest(point) as Visual)?.GetVisualAncestors().Take(10).Select(Identity).ToArray() }).ToArray(),
                    scrollers = scrollers.Select(s => new { name=Identity(s),bounds=s.Bounds.ToString(),
                        origin=s.TranslatePoint(default,f.Window)?.ToString(),offset=s.Offset.ToString(),viewport=s.Viewport.ToString(),extent=s.Extent.ToString(),
                        s.VerticalScrollBarVisibility,s.HorizontalScrollBarVisibility,
                        content=(s.Content as Control)?.Bounds.ToString(),
                        bars=s.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                            .Where(b=>ReferenceEquals(b.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(),s))
                            .Select(b=>new{name=Identity(b),bounds=b.Bounds.ToString(),origin=b.TranslatePoint(default,f.Window)?.ToString(),b.IsEffectivelyVisible,b.Orientation}).ToArray() }).ToArray()
                });
            }
            void Frame(string name)
            {
                using var frame=f.Window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(directory,name+".png"));
            }
            EventHandler<ScrollChangedEventArgs> observer=(_,_)=>Record("scroll-changed");
            var ancestors=target.GetVisualAncestors().OfType<ScrollViewer>().ToArray();
            foreach(var scroller in ancestors)scroller.ScrollChanged+=observer;
            try
            {
                Record("before-reveal");Frame("before-reveal");
                await PresPlanReveal(f,target);
                Record("reveal-returned");Frame("reveal-returned");
                string model=ManualWeatherModel(f);int clicks=0;
                target.Click+=(_,_)=>clicks++;
                await ManualWeatherComplete(pending,ManualWeatherSnapshot(17));
                var group=Assert.Single(f.Window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>(),
                    item=>item.Children.OfType<Roche_Scoreboard.Views.OperatorActionBorder>().Any(c=>c.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="FORECAST"))
                        &&item.Children.OfType<Roche_Scoreboard.Views.OperatorActionBorder>().Any(c=>c.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="RAIN")));
                var forecast=Assert.Single(group.Children.OfType<Roche_Scoreboard.Views.OperatorActionBorder>(),c=>c.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="FORECAST"));
                for(int index=0;index<5;index++)
                {
                    if(index>0)PresPlanProvider(forecast).Invoke();
                    await Until(()=>ManualWeatherOverlay(bug)=="Forecast","The real Forecast overlay must be active before physical Cancel.");
                    await PresPlanReveal(f,target);
                    var point=FinalsProbes(FinalsPainted(f.Window,target))[index];
                    Assert.True(Within(target,f.Window.InputHitTest(point)));
                    f.Window.MouseMove(point);f.Window.MouseDown(point,MouseButton.Left);f.Window.MouseUp(point,MouseButton.Left);
                    Assert.Equal(index+1,clicks);
                    await Until(()=>ManualWeatherOverlay(bug) is null,"Physical Cancel must finish the actual active overlay lifecycle.");
                }
                foreach(string caption in new[]{"FORECAST","WEATHER","RAIN"})
                {
                    var card=Assert.Single(group.Children.OfType<Roche_Scoreboard.Views.OperatorActionBorder>(),c=>c.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==caption));
                    await PresPlanReveal(f,card);
                    Assert.All(FinalsProbes(FinalsPainted(f.Window,card)),point=>Assert.True(Within(card,f.Window.InputHitTest(point))));
                }
                Assert.Equal(model,ManualWeatherModel(f));Assert.True(PresWindowOpen(f));Assert.Empty(controlled.Feedback);
                await PresPlanReveal(f,target);Record("after-five-real-cancels-and-three-card-reveals");Frame("after-five-real-cancels-and-three-card-reveals");
            }
            finally
            {
                foreach(var scroller in ancestors)scroller.ScrollChanged-=observer;
                Record("final-before-scope-cleanup");Frame("final-before-scope-cleanup");
                File.WriteAllText(Path.Combine(directory,"geometry.json"),JsonSerializer.Serialize(history,new JsonSerializerOptions{WriteIndented=true}));
            }
        });
    }
}
