// Real Home -> unified setup -> chosen preset badge lifetime. Supporting copy only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
using Shape=Avalonia.Controls.Shapes.Shape;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private sealed class ClubBadgePreferences:IDisposable
    {
        private readonly bool glass=OperatorAppearance.GlassEnabled,reduce=OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle style=OperatorAppearance.MotionStyle;
        private readonly bool? priorOverride=OperatorMotion.AnimationOverride;
        private readonly string path=OperatorAppearance.StoragePath;
        private readonly byte[]? bytes=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        internal ClubBadgePreferences(){OperatorMotion.AnimationOverride=null;OperatorAppearance.Configure(true,false);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);}
        public void Dispose(){OperatorAppearance.Configure(glass,reduce);OperatorAppearance.ConfigureMotionStyle(style);OperatorMotion.AnimationOverride=priorOverride;if(bytes is null)File.Delete(path);else File.WriteAllBytes(path,bytes);}
    }
    private static (Shape Check,Border Mark,ScaleTransform Pop) ClubBadgeParts(Border tile)
    {
        var check=Assert.Single(tile.GetVisualDescendants().OfType<Shape>(),x=>Equals(x.Tag,"preset-selection-marker"));
        var mark=Assert.IsAssignableFrom<Border>(check.GetVisualParent());var pop=Assert.IsType<ScaleTransform>(mark.RenderTransform);return(check,mark,pop);
    }
    private static object ClubBadgePose(Border tile)
    {
        var(check,mark,pop)=ClubBadgeParts(tile);return new{status=AutomationProperties.GetItemStatus(tile),tile.IsLoaded,tile.IsVisible,tile.IsEnabled,opacity=mark.Opacity,dash=check.StrokeDashOffset,popX=pop.ScaleX,popY=pop.ScaleY,checkActive=Motion.IsAnimating(check),popActive=Motion.IsAnimating(pop)};
    }
    private static bool ClubBadgeMoving(Border tile)
    {
        var(check,_,pop)=ClubBadgeParts(tile);return check.StrokeDashOffset is >.001 and <9.999&&Math.Abs(pop.ScaleX-1)>.001&&Motion.IsAnimating(check)&&Motion.IsAnimating(pop);
    }
    private static void ClubBadgeSettled(Border tile)
    {
        var(check,mark,pop)=ClubBadgeParts(tile);Assert.Equal("Selected",AutomationProperties.GetItemStatus(tile));Assert.Equal(1,mark.Opacity);Assert.Equal(0,check.StrokeDashOffset);Assert.Equal(1,pop.ScaleX);Assert.Equal(1,pop.ScaleY);Assert.False(Motion.IsAnimating(check));Assert.False(Motion.IsAnimating(pop));
    }
    private static async Task<(Control Wizard,Border Tile)> OpenClubBadge(KbRoot f,bool cricket)
    {
        if(!cricket)return(f.Wizard,f.Tile("Home","First keyboard club"));
        await PresetStorage.SaveAllCricketAsync(KbPresetDirectory.Teams());await PresetStorage.ReadAsync(TeamLibrarySport.Cricket);
        await KbClick(f.Main,Named<Button>(f.Wizard,"BackButton"));
        await Until(()=>Named<Control>(f.Wizard,"SportStepContainer").IsEffectivelyVisible,"Actual Back must return to sport choice.");
        await KbClick(f.Main,Named<RadioButton>(f.Wizard,"CricketSportChoice"));await KbClick(f.Main,Named<Button>(f.Wizard,"NextButton"));
        var wizard=Named<CricketSetupWizard>(f.Main,"CricketSetupWizardPanel");
        await Until(()=>wizard.IsEffectivelyVisible&&Named<WrapPanel>(wizard,"HomePresetPanel").Children.OfType<Border>().Count()==3,"Current cricket team presets must be mounted.");
        return(wizard,Assert.Single(Named<WrapPanel>(wizard,"HomePresetPanel").Children.OfType<Border>(),b=>b.Tag is TeamPreset p&&p.PresetName=="First keyboard club"));
    }
    [AvaloniaTheory]
    [InlineData(false,"reduce")][InlineData(true,"reduce")]
    [InlineData(false,"tile-hide")][InlineData(true,"tile-hide")]
    [InlineData(false,"tile-disable")][InlineData(true,"tile-disable")]
    [InlineData(false,"window-hide")][InlineData(true,"window-hide")]
    [InlineData(false,"window-disable")][InlineData(true,"window-disable")]
    [InlineData(false,"unload")][InlineData(true,"unload")]
    public async Task PublicChosenClubInterruptionSettlesItsOwnedBadgeWithoutChangingTheSetup(bool cricket,string interruption)
    {
        await using var f=await KbRoot.Create();var(wizard,tile)=await OpenClubBadge(f,cricket);using var preferences=new ClubBadgePreferences();await Pump(650);
        var recovery=f.Recovery();var files=f.Files.Bytes();
        string Draft()=>string.Join("|",new[]{"Home","Away"}.SelectMany(side=>new[]{"Name","Abbr","Color","Secondary"}.Select(part=>Named<TextBox>(wizard,"Setup"+side+part+"Box").Text)));
        string? expectedDraft=null,error=null;bool inputFinished=false,witnessed=false,busy=false,completed=false,queued=false,stopped=false;DispatcherOperation? pendingInterruption=null;var trace=new List<object>();
        var point=await KbPoint(f.Main,tile);
        // Establish an actual operator-visible rendered starting state before
        // the single input. All capture/IO finishes before observers or gesture.
        KbScreenshot(f.Main,"badge-before-"+cricket+"-"+interruption);
        point=await KbPoint(f.Main,tile);EventHandler? observe=null;
        var(observedCheck,_,observedPop)=ClubBadgeParts(tile);EventHandler<AvaloniaPropertyChangedEventArgs>? partsChanged=null;
        void StopObserving(){NativeRendering.Rendering-=observe;observedCheck.PropertyChanged-=partsChanged;observedPop.PropertyChanged-=partsChanged;}
        var panel=Named<WrapPanel>(wizard,"HomePresetPanel");int index=panel.Children.IndexOf(tile);
        void Restore(){tile.IsEnabled=true;tile.IsVisible=true;if(!panel.Children.Contains(tile))panel.Children.Insert(Math.Min(index,panel.Children.Count),tile);f.Main.IsEnabled=true;if(!f.Main.IsVisible)f.Main.Show();}
        observe=(_,_)=>
        {
            if(stopped||!inputFinished||busy||witnessed||queued||error is not null)return;
            if(!tile.IsLoaded||!tile.IsEffectivelyVisible||!tile.IsEffectivelyEnabled||!ClubBadgeMoving(tile))return;
            queued=true;StopObserving();
            // Exactly one queued interruption outside the rendering invocation and
            // after the initiating input has returned. Recheck strict live phase;
            // never repeat gestures/extend duration or accept an endpoint witness.
            pendingInterruption=Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(stopped)return;
                try
                {
                    Assert.True(inputFinished);Assert.True(tile.IsLoaded&&tile.IsEffectivelyVisible&&tile.IsEffectivelyEnabled);
                    Assert.True(ClubBadgeMoving(tile),"Fixture prerequisite: queued interruption must still see a naturally partial check AND pop.");
                    busy=true;witnessed=true;trace.Add(new{stage="before-interrupt",pose=ClubBadgePose(tile),draft=Draft(),width=f.Main.Bounds.Width,height=f.Main.Bounds.Height});
                    Assert.Equal(expectedDraft,Draft());Assert.Equal("First keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);
                    switch(interruption)
                    {
                        case "reduce":OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Assert.False(OperatorMotion.Enabled);break;
                        case "tile-hide":tile.IsVisible=false;break;
                        case "tile-disable":tile.IsEnabled=false;break;
                        case "window-hide":f.Main.Hide();break;
                        case "window-disable":f.Main.IsEnabled=false;break;
                        case "unload":Assert.True(panel.Children.Remove(tile));Assert.False(tile.IsLoaded);break;
                        default:throw new InvalidOperationException(interruption);
                    }
                    trace.Add(new{stage="immediate-after-interrupt",pose=ClubBadgePose(tile),draft=Draft()});
                    Assert.Equal(expectedDraft,Draft());ClubBadgeSettled(tile);completed=true;
                }
                catch(Exception failure){error=failure.ToString();}
                finally{busy=false;}
            },DispatcherPriority.Normal);
        };
        partsChanged=(_,args)=>
        {
            if(args.Property==Shape.StrokeDashOffsetProperty||args.Property==ScaleTransform.ScaleXProperty||args.Property==ScaleTransform.ScaleYProperty)observe(null,EventArgs.Empty);
        };
        observedCheck.PropertyChanged+=partsChanged;observedPop.PropertyChanged+=partsChanged;
        NativeRendering.Rendering+=observe;
        try
        {
            Assert.True(OperatorMotion.Enabled);f.Main.MouseMove(point);f.Main.MouseDown(point,MouseButton.Left);f.Main.MouseUp(point,MouseButton.Left);expectedDraft=Draft();inputFinished=true;observe(null,EventArgs.Empty);
            await Until(()=>completed||error is not null,"Fixture prerequisite: require actual partial check AND pop after the single real preset pointer gesture returned.");
            Assert.True(witnessed,"No product failure without a naturally moving selected badge.");Assert.Null(error);
            await Pump(480);ClubBadgeSettled(tile);Assert.Equal(expectedDraft,Draft());f.AssertRecovery(recovery);f.Files.AssertBytes(files);
            if(cricket)Assert.Null(((CricketSetupWizard)wizard).Result);else Assert.Null(f.Wizard.Result);
        }
        finally
        {
            stopped=true;StopObserving();pendingInterruption?.Abort();Restore();
            string dir=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!,"badge-"+cricket+"-"+interruption);Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"trace.json"),JsonSerializer.Serialize(new{witnessed,completed,error,trace},new JsonSerializerOptions{WriteIndented=true}));
            using var frame=f.Main.CaptureRenderedFrame();frame?.Save(Path.Combine(dir,"after-interruption.png"));
        }
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task PublicChosenClubRapidReversalKeepsOnlyLatestBadgeAndSetup(bool cricket)
    {
        await using var f=await KbRoot.Create();var(wizard,first)=await OpenClubBadge(f,cricket);using var preferences=new ClubBadgePreferences();await Pump(650);
        var panel=Named<WrapPanel>(wizard,"HomePresetPanel");var second=Assert.Single(panel.Children.OfType<Border>(),b=>b.Tag is TeamPreset p&&p.PresetName=="Second keyboard club");
        Point a=await KbPoint(f.Main,first),b=await KbPoint(f.Main,second);var recovery=f.Recovery();var saved=f.Files.Bytes();
        void ClickNow(Point point){f.Main.MouseMove(point);f.Main.MouseDown(point,MouseButton.Left);f.Main.MouseUp(point,MouseButton.Left);}
        ClickNow(a);Assert.Equal("First keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);
        ClickNow(b);Assert.Equal("Second keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);Assert.Equal("Not selected",AutomationProperties.GetItemStatus(first));
        ClickNow(a);Assert.Equal("First keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);
        Assert.Equal("Selected",AutomationProperties.GetItemStatus(first));Assert.Equal("Not selected",AutomationProperties.GetItemStatus(second));
        OperatorAppearance.Configure(true,true);ClubBadgeSettled(first);
        var(check,mark,pop)=ClubBadgeParts(second);Assert.Equal(0,mark.Opacity);Assert.Equal(0,check.StrokeDashOffset);Assert.Equal(1,pop.ScaleX);Assert.Equal(1,pop.ScaleY);Assert.False(Motion.IsAnimating(check));Assert.False(Motion.IsAnimating(pop));
        await Pump(480);ClubBadgeSettled(first);Assert.Equal("First keyboard club",Named<TextBox>(wizard,"SetupHomeNameBox").Text);Assert.Equal("Not selected",AutomationProperties.GetItemStatus(second));
        f.AssertRecovery(recovery);f.Files.AssertBytes(saved);if(cricket)Assert.Null(((CricketSetupWizard)wizard).Result);else Assert.Null(f.Wizard.Result);
    }

}
