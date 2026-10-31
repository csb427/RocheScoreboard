// Three actual-route supplementary regressions; original-main mount and control-specific pixel/motion checks.
// Root separately owns real sequentialTab420; here1actualMain rawlegacyOFF/mount;2Expressive/Calm
// actual original selection trajectories + isolated production-symbol part pixels.
// NO pending45s behavior case here: active-delay/physical-output acceptance remains separate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using SkiaSharp;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class LiveSettingsClockChoiceTests
{
    private static T QbXField<T>(object owner,string name)=>(T)owner.GetType().GetField(name,HiddenInstance)!.GetValue(owner)!;

    private sealed record QbXInitialFrame(double Milliseconds,bool? Checked,bool Loaded,double Fill,double Pulse,double Turn,
        double ThumbX,double ScaleX,double ScaleY,double Selection,string? Label);

    [AvaloniaFact][Trait("Evidence","QuarterBreakActualLegacyOffMain")]
    public async Task ActualMainWindowReadsRawLegacyOffBeforeFirstNativeCardMountWithoutCelebration()
    {
        // This guard is established BEFORE ANY extra IO. AflFixture.Create
        // preserves the ACTUAL resolved workflow path and all its backups plus
        // self-test files even if SelfTestStorage has already latched elsewhere.
        // Do not invent/force a new latch or read/write an unguarded home path.
        await using var f=await AflFixture.Create(1280,850);
        string firstModel=Scores(f.Match);
        string workflow=(string)typeof(OperatorWorkflowPreferences).GetMethod("GetStoragePath",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[false])!;
        Assert.True(Assert.IsType<App>(Application.Current).IsSelfTestMode);
        string ownedWorkflow=Path.Combine(Path.GetTempPath(),"RocheScoreboard-SelfTests",Environment.ProcessId.ToString(),"workflow","operator_workflow.json");
        Assert.Equal(Path.GetFullPath(ownedWorkflow),Path.GetFullPath(workflow));
        var preserved=Assert.IsType<Dictionary<string,byte[]?>>(typeof(AflFixture).GetField("files",HiddenInstance)!.GetValue(f));
        foreach(string path in new[]{workflow,workflow+".bak",workflow+".tmp"})Assert.True(preserved.ContainsKey(path),"Outer fixture must preserve the exact process-owned file before extra IO: "+path);
        string fixtureHome=Assert.IsType<string>(typeof(AflFixture).GetField("dataHome",HiddenInstance)!.GetValue(f));
        Assert.Equal(Path.GetFullPath(fixtureHome),Path.GetFullPath(Environment.GetEnvironmentVariable("ROCHE_DATA_HOME")!));
        Assert.Equal(Path.GetFullPath(fixtureHome),NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Directory.CreateDirectory(Path.GetDirectoryName(workflow)!);
        File.WriteAllText(workflow,"{\"aflBreakScreenEnabled\":false,\"aflGoalAnimation\":\"clean\",\"goalAnimationPreferenceVersion\":1}");
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        Roche_Scoreboard.MainWindow? second=null;EventHandler? observer=null;
        var samples=new List<QbXInitialFrame>();var watch=Stopwatch.StartNew();
        try
        {
            OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);OperatorMotion.AnimationOverride=null;
            Assert.True(OperatorMotion.Enabled);
            second=new Roche_Scoreboard.MainWindow{Width=1280,Height=850};
            var original=Named<CheckBox>(second,"SettingsBreakScreenToggle");
            Assert.False(original.IsChecked);Assert.False(QbXField<bool>(second,"_breakScreenEnabled"));
            Assert.False(OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
            int changes=0;original.IsCheckedChanged+=(_,_)=>changes++;
            // Observe before Show/public navigation; any real early-mounted
            // original-template frames are retained, not just a settled tail.
            observer=(_,_)=>
            {
                var parts=original.GetVisualDescendants().OfType<Control>().ToArray();
                var symbol=parts.OfType<OperatorSymbol>().SingleOrDefault(x=>x.Name=="QuarterBreakSymbol");
                var thumb=parts.OfType<Avalonia.Controls.Shapes.Ellipse>().SingleOrDefault(x=>x.Name=="QuarterBreakThumb");
                var selection=parts.OfType<OperatorChoiceSurface>().SingleOrDefault(x=>x.Name=="QuarterBreakSelection");
                var label=parts.OfType<TextBlock>().SingleOrDefault(x=>x.Name=="QuarterBreakStateText");
                if(symbol is null||thumb is null||selection is null||label is null||!symbol.IsLoaded||!original.IsLoaded)return;
                var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);
                var shift=Assert.Single(group.Children.OfType<TranslateTransform>());var scale=Assert.Single(group.Children.OfType<ScaleTransform>());
                samples.Add(new(watch.Elapsed.TotalMilliseconds,original.IsChecked,original.IsLoaded,symbol.FillProgress,
                    Mr5FinalsDiagnostic(symbol,"Pulse"),Mr5FinalsDiagnostic(symbol,"Turn"),shift.X,scale.ScaleX,scale.ScaleY,selection.Progress,label.Text));
            };
            NativeRendering.Rendering+=observer;second.Show();
            var home=Named<SportSelectionScreen>(second,"SportSelectionPanel");
            await Until(()=>home.IsEffectivelyVisible&&home.GetVisualDescendants().OfType<Button>().Any(x=>x.Name=="AflLaunchAction"&&x.Bounds.Width>0),"Second actual Main must show real Home.");
            await Tap(second,Assert.Single(home.GetVisualDescendants().OfType<Button>(), x=>x.Name=="AflLaunchAction"));
            var wizard=Named<SetupWizard>(second,"SetupWizardPanel");
            await Until(()=>Named<Control>(wizard,"SportStepContainer").IsEffectivelyVisible,"Real StartGame must show sport choice.");
            await Tap(second,Named<RadioButton>(wizard,"AflSportChoice"));await Tap(second,Named<Button>(wizard,"NextButton"));
            await Until(()=>Named<TextBox>(wizard,"SetupHomeNameBox").IsEffectivelyVisible,"Actual AFL team fields must be current.");
            Named<TextBox>(wizard,"SetupHomeNameBox").Text="Legacy OFF Home";Named<TextBox>(wizard,"SetupAwayNameBox").Text="Legacy OFF Away";
            await Tap(second,Named<Button>(wizard,"NextButton"));await Tap(second,Named<OperatorGlassSurface>(wizard,"CountUpCard"));
            Assert.True(Named<RadioButton>(wizard,"SetupCountUp").IsChecked);
            await Tap(second,Named<Button>(wizard,"NextButton"));Named<ComboBox>(wizard,"SetupWeatherLocation").Text="";
            await Tap(second,Named<Button>(wizard,"NextButton"));
            await Until(()=>Named<Control>(second,"MainContent").IsEffectivelyVisible&&!wizard.IsEffectivelyVisible,"Actual fresh AFL setup must complete.");
            var tools=Named<Button>(second,"GuidedModeButton");
            // The first guarded fixture may already have saved Advanced mode.
            // Only take the real Enter Advanced action when it is needed.
            if(Equals(tools.Content,"ADVANCED TOOLS"))await Tap(second,tools);
            Assert.Equal("RETURN TO ESSENTIALS",tools.Content);
            await Tap(second,Named<TabItem>(second,"MatchSetupTab"));await Settle(480);
            var workspace=Assert.Single(second.GetVisualDescendants().OfType<SettingsWorkspace>(),x=>x.IsEffectivelyVisible);
            Key(second,PhysicalKey.F,RawInputModifiers.Control);
            var search=Assert.Single(workspace.GetVisualDescendants().OfType<OperatorSearchBox>(),x=>x.IsEffectivelyVisible);
            Assert.True(search.IsFocused);second.KeyTextInput("Auto-show summary screen at quarter breaks");await Settle(150);
            Assert.True(original.IsEffectivelyVisible);original.BringIntoView();await Settle(100);
            await Mr5FinalsObserveUntil(()=>samples.Count>=45,"Actual original native card must mount and expose45real rendering samples.");
            Assert.False(original.IsChecked);Assert.False(QbXField<bool>(second,"_breakScreenEnabled"));
            Assert.False(OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);Assert.Equal(0,changes);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));Assert.Equal(ToggleState.Off,provider.ToggleState);
            Assert.Contains("Off",AutomationProperties.GetItemStatus(original)??"");
            Assert.All(samples,x=>{Assert.True(x.Loaded);Assert.False(x.Checked);Assert.Equal(0d,x.Fill);Assert.Equal(0d,x.Pulse);Assert.Equal(0d,x.Turn);Assert.Equal(0d,x.ThumbX);Assert.Equal(1d,x.ScaleX);Assert.Equal(1d,x.ScaleY);Assert.Equal(0d,x.Selection);Assert.Equal("OFF",x.Label);});
            var secondModel=QbXField<MatchManager>(second,"_match");Assert.False(secondModel.ClockRunning);Assert.Empty(secondModel.Events);
            string afterSetup=JsonSerializer.Serialize(secondModel.ToState());await Settle(250);Assert.Equal(afterSetup,JsonSerializer.Serialize(secondModel.ToState()));
            Assert.Equal(firstModel,Scores(f.Match));Capture(second,"quarter-break-original-main-raw-legacy-off");
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            Mr5FinalsWriteTrace("quarter-break-original-main-raw-legacy-off",samples);
            if(second is not null)
            {
                second.Close();var labelHandler=second.GetType().GetMethod("OnQuarterLabelServiceChanged",HiddenInstance);
                if(labelHandler is not null)QuarterLabelService.LabelsChanged-=labelHandler.CreateDelegate<Action>(second);
                await Settle(180);
            }
            OperatorAppearance.ConfigureMotionStyle(previousStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
            // The OUTER AflFixture, still alive, restores its actual guarded
            // workflow/backup/recovery/UX files AFTER this secondary Main closes.
        }
    }

    private sealed record QbXPose(string Stage,string Source,double Milliseconds,double ThumbX,double ScaleX,double ScaleY,
        double Selection,double Fill,double Pulse,double Turn,double BoundFill,bool Checked,
        Rect Input,Rect Title,Rect Description,Matrix ParentTransform,Matrix ToggleTransform,Matrix TitleTransform,Matrix DescriptionTransform);
    private sealed record QbXPixelPatch(Rect Pixels,byte[] Rgb,long Energy,int InkPixels);
    private sealed record QbXPixelFrame(string Stage,string Path,double FillBefore,double FillAfter,Size ClientSize,Rect SymbolBounds,
        QbXPixelPatch[] Frame,QbXPixelPatch[] Rows);

    private static QbXPixelFrame QbXCaptureScreenRows(Window host,OperatorSymbol symbol,string path,string stage)
    {
        double before=symbol.FillProgress;using var bitmap=Mr5PaletteCapture(host,path);double after=symbol.FillProgress;
        double sx=bitmap.Width/host.ClientSize.Width,sy=bitmap.Height/host.ClientSize.Height;Assert.InRange(Math.Abs(sx-sy),0,.01);
        double scale=Math.Min(symbol.Bounds.Width,symbol.Bounds.Height)/28;
        var offset=new Point((symbol.Bounds.Width-24*scale)/2,(symbol.Bounds.Height-24*scale)/2);
        QbXPixelPatch Patch(Rect region)
        {
            Point Map(Point p){var mapped=symbol.TranslatePoint(new Point(offset.X+p.X*scale,offset.Y+p.Y*scale),host);Assert.NotNull(mapped);return mapped.Value;}
            var a=Map(region.TopLeft);var b=Map(region.BottomRight);
            int left=Math.Max(0,(int)Math.Ceiling(a.X*sx)),right=Math.Min(bitmap.Width,(int)Math.Floor(b.X*sx));
            int top=Math.Max(0,(int)Math.Ceiling(a.Y*sy)),bottom=Math.Min(bitmap.Height,(int)Math.Floor(b.Y*sy));Assert.True(right>left&&bottom>top);
            var bytes=new byte[(right-left)*(bottom-top)*3];int index=0,ink=0;long energy=0;
            for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)
            {var p=bitmap.GetPixel(x,y);Assert.Equal((byte)255,p.Alpha);bytes[index++]=p.Red;bytes[index++]=p.Green;bytes[index++]=p.Blue;energy+=p.Red+p.Green+p.Blue;if(Math.Max(p.Red,Math.Max(p.Green,p.Blue))>8)ink++;}
            return new(new Rect(left,top,right-left,bottom-top),bytes,energy,ink);
        }
        // Frame masks exclude ALL internal summary rows. Row masks exclude
        // the frame/foot and one another; actualRGB, not path metadata, is read.
        var frame=new[]{new Rect(.5,1.5,23,3),new Rect(.5,16,23,7.5),new Rect(.5,4.8,3,10.4),new Rect(20.4,4.8,3.2,10.4)}.Select(Patch).ToArray();
        var rows=new[]{new Rect(4.05,5.4,15.4,2.2),new Rect(4.05,8.9,15.4,2.2),new Rect(4.05,12.4,15.4,2.2)}.Select(Patch).ToArray();
        return new(stage,path,before,after,host.ClientSize,FinalsPainted(host,symbol),frame,rows);
    }
    private static void QbXSameFrame(QbXPixelFrame initial,QbXPixelFrame next)
    {
        Assert.Equal(initial.ClientSize,next.ClientSize);Assert.Equal(initial.SymbolBounds,next.SymbolBounds);
        Assert.True(initial.Frame.Sum(x=>x.InkPixels)>=80,"Fixed upright frame masks must really contain painted pixels.");
        for(int i=0;i<initial.Frame.Length;i++){Assert.Equal(initial.Frame[i].Pixels,next.Frame[i].Pixels);Assert.Equal(initial.Frame[i].Rgb,next.Frame[i].Rgb);}
    }

    [AvaloniaTheory][InlineData("Expressive")][InlineData("Calm")]
    [Trait("Evidence","QuarterBreakIndependentSelectionAndScreenRows")]
    public async Task OriginalQuarterBreakSelectionKeepsTextStillWhileScreenRowsReverseNaturallyThenLiveReduceSettles(string policy)
    {
        await using var f=await AflFixture.Create(1280,850);
        var previousStyle=OperatorAppearance.MotionStyle;bool previousReduce=OperatorAppearance.ReduceMotion;
        bool? previousOverride=OperatorMotion.AnimationOverride;
        Window? host=null;IDisposable? binding=null;EventHandler? observer=null;
        var watch=Stopwatch.StartNew();var trace=new List<QbXPose>();var captures=new List<QbXPixelFrame>();
        string stage="arrange";bool dispatching=false;
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-quarter-break-pixels-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string prefix=Path.Combine(directory,"quarter-break-screen-rows-"+policy);
        try
        {
            OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
            await OpenBreakCardSection(f,false);
            var original=Named<CheckBox>(f.Window,"SettingsBreakScreenToggle");var card=Named<Border>(f.Window,"SettingsBreakScreenCard");
            await RevealBreakCardActor(f,original);f.Window.MouseMove(new Point(1,1));await Settle(400);
            var provider=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original));
            if(original.IsChecked==true)provider.Toggle();await Settle(750);
            Assert.False(original.IsChecked);Assert.False(BreakCardValue(f));
            var actualSymbol=Mr5FinalsPart<OperatorSymbol>(original,"QuarterBreakSymbol");
            var thumb=Mr5FinalsPart<Avalonia.Controls.Shapes.Ellipse>(original,"QuarterBreakThumb");
            var group=Assert.IsType<TransformGroup>(thumb.RenderTransform);var shift=Assert.Single(group.Children.OfType<TranslateTransform>());var scale=Assert.Single(group.Children.OfType<ScaleTransform>());
            var surface=Mr5FinalsPart<OperatorChoiceSurface>(original,"QuarterBreakSelection");
            var title=Mr5FinalsPart<TextBlock>(original,"QuarterBreakTitle");var description=Mr5FinalsPart<TextBlock>(original,"QuarterBreakDescription");
            var label=Mr5FinalsPart<TextBlock>(original,"QuarterBreakStateText");
            Assert.Equal("quarter-break",actualSymbol.Kind);Assert.Equal(1d,surface.OriginX);
            Assert.True(OperatorChoiceMotion.GetEnabled(original));Assert.True(OperatorControlMotion.GetEnabled(original));Assert.False(CricketActionMotion.GetEnabled(original));Assert.False(SettingsControlMotion.GetEnabled(original));
            string model=Scores(f.Match),other=BreakCardOtherPreferences();object?[] presentation=BreakCardPresentation(f);int changes=0;original.IsCheckedChanged+=(_,_)=>changes++;
            var symbol=new OperatorSymbol{Kind="quarter-break",Width=84,Height=84,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            binding=symbol.Bind(OperatorSymbol.IsActiveProperty,new Binding(nameof(CheckBox.IsChecked)){Source=original,Mode=BindingMode.OneWay});
            host=new Window{Width=120,Height=120,CanResize=false,Background=Brushes.Black,Content=new Grid{Background=Brushes.Black,Children={symbol}}};
            Matrix MatrixOf(Control c)=>c.RenderTransform?.Value??Matrix.Identity;
            QbXPose Read(string source="snapshot")=>new(stage,source,watch.Elapsed.TotalMilliseconds,shift.X,scale.ScaleX,scale.ScaleY,
                surface.Progress,actualSymbol.FillProgress,Mr5FinalsDiagnostic(actualSymbol,"Pulse"),Mr5FinalsDiagnostic(actualSymbol,"Turn"),symbol.FillProgress,
                original.IsChecked==true,FinalsPainted(f.Window,original),FinalsPainted(f.Window,title),FinalsPainted(f.Window,description),MatrixOf(card),MatrixOf(original),MatrixOf(title),MatrixOf(description));
            observer=(_,_)=>trace.Add(Read(dispatching?"NativeRendering-during-input":"NativeRendering"));NativeRendering.Rendering+=observer;host.Show();
            bool Endpoint(bool on){var p=Read();return Math.Abs(p.ThumbX-(on?20:0))<.00001&&Math.Abs(p.ScaleX-1)<.00001&&Math.Abs(p.ScaleY-1)<.00001&&p.Selection==(on?1:0)&&p.Fill==(on?1:0)&&p.BoundFill==(on?1:0)&&p.Pulse==0&&p.Turn==0;}
            await Mr5FinalsObserveUntil(()=>symbol.IsLoaded&&symbol.Bounds.Width==84&&Endpoint(false)&&trace.Count(x=>x.Source=="NativeRendering")>=3,"Both original native selection and bound production symbol must actually load at OFF.");
            var fixedPose=Read();var off=QbXCaptureScreenRows(host,symbol,prefix+"-01-off.png","off");captures.Add(off);
            Assert.All(off.Rows,row=>{Assert.Equal(0,row.InkPixels);Assert.Equal(0L,row.Energy);});
            void Stable(QbXPose p)
            {Assert.Equal(fixedPose.Input,p.Input);Assert.Equal(fixedPose.Title,p.Title);Assert.Equal(fixedPose.Description,p.Description);Assert.Equal(Matrix.Identity,p.ParentTransform);Assert.Equal(Matrix.Identity,p.ToggleTransform);Assert.Equal(Matrix.Identity,p.TitleTransform);Assert.Equal(Matrix.Identity,p.DescriptionTransform);Assert.Equal(0d,p.Pulse);Assert.Equal(0d,p.Turn);}
            void Toggle(bool expected,int count)
            {
                dispatching=true;try{provider.Toggle();}finally{dispatching=false;}
                Assert.Equal(count,changes);Assert.Equal(expected,original.IsChecked==true);Assert.Equal(expected,BreakCardValue(f));Assert.Equal(expected,OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
                Assert.Equal(expected,actualSymbol.IsActive);Assert.Equal(expected,symbol.IsActive);Assert.Equal(expected?"ON":"OFF",label.Text);
                Assert.Equal(model,Scores(f.Match));Assert.Equal(other,BreakCardOtherPreferences());Assert.Equal(presentation,BreakCardPresentation(f));Stable(Read());
                trace.Add(Read("post-provider-synchronous-authority"));
            }
            stage="first-arrival";Toggle(true,1);
            await Mr5FinalsObserveUntil(()=>symbol.FillProgress>.3&&symbol.FillProgress<.6&&trace.Any(x=>x.Stage==stage&&x.Source=="NativeRendering"&&x.Fill>.01&&x.Fill<.99),"Witness a real CURRENT internal row-stagger pose before capturing pixels.");
            var mid=QbXCaptureScreenRows(host,symbol,prefix+"-02-stagger.png","stagger");captures.Add(mid);
            Assert.InRange(mid.FillBefore,.25,.8);Assert.InRange(mid.FillAfter,.25,.8);QbXSameFrame(off,mid);
            Assert.True(mid.Rows[0].Energy>mid.Rows[1].Energy&&mid.Rows[1].Energy>mid.Rows[2].Energy,"Actual three spatial row masks must show ordered stagger, not one uniformly faded icon.");
            Assert.True(mid.Rows[0].InkPixels>0);Assert.True(mid.Rows[1].InkPixels>0);
            var firstFrames=trace.Where(x=>x.Stage==stage&&x.Source=="NativeRendering").ToArray();
            Assert.Contains(firstFrames,x=>x.ThumbX>.01&&x.ThumbX<19.99);Assert.Contains(firstFrames,x=>x.Selection>.001&&x.Selection<.999);
            // Reverse while CURRENT row progress is still genuinely interior.
            Assert.InRange(symbol.FillProgress,.001,.999);stage="natural-reversal";Toggle(false,2);
            await Mr5FinalsObserveUntil(()=>Endpoint(false)&&trace.Count(x=>x.Stage==stage&&x.Source=="NativeRendering"&&x.Fill==0)>=2,"Natural OFF reversal must finish before any separate liveReduce interruption.");
            var reversed=QbXCaptureScreenRows(host,symbol,prefix+"-03-reversed-off.png","reversed-off");captures.Add(reversed);QbXSameFrame(off,reversed);
            for(int i=0;i<3;i++){Assert.Equal(off.Rows[i].Pixels,reversed.Rows[i].Pixels);Assert.Equal(off.Rows[i].Rgb,reversed.Rows[i].Rgb);}
            stage="full-arrival";Toggle(true,3);await Mr5FinalsObserveUntil(()=>Endpoint(true),"Original ON surface/thumb/rows must finish naturally.");
            var on=QbXCaptureScreenRows(host,symbol,prefix+"-04-all-rows.png","all-rows");captures.Add(on);QbXSameFrame(off,on);
            Assert.Equal(1d,on.FillBefore);Assert.Equal(1d,on.FillAfter);Assert.All(on.Rows,row=>{Assert.True(row.InkPixels>=40);Assert.True(row.Energy>0);});
            stage="separate-live-reduce";Toggle(false,4);
            await Mr5FinalsObserveUntil(()=>symbol.FillProgress>.05&&symbol.FillProgress<.95&&surface.Progress>.001&&surface.Progress<.999&&shift.X>.01&&shift.X<19.99,"All three real channels must be moving before the separate liveReduce change.");
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Assert.False(OperatorMotion.Enabled);Assert.True(Endpoint(false));
            int first=trace.Count(x=>x.Source=="NativeRendering");
            await Mr5FinalsObserveUntil(()=>trace.Count(x=>x.Source=="NativeRendering")-first>=60,"Observe60actual frames after liveReduce to reject delayed replay.");
            Assert.True(Endpoint(false));Assert.Equal(4,changes);Assert.False(BreakCardValue(f));Assert.False(OperatorWorkflowPreferences.Load().AflBreakScreenEnabled);
            Assert.Equal(model,Scores(f.Match));Assert.Equal(other,BreakCardOtherPreferences());Assert.Equal(presentation,BreakCardPresentation(f));
            Assert.All(trace,Stable);
            Capture(f.Window,"quarter-break-native-selection-"+policy);
        }
        finally
        {
            if(observer is not null)NativeRendering.Rendering-=observer;
            File.WriteAllText(prefix+".json",JsonSerializer.Serialize(new{scope="Linux headless original native card trajectories plus STANDALONE84px production symbol pixels bound to original provider; NOT originaltemplate pixel-motion/nativeWPF proof",policy,trace,captures=captures.Select(x=>new{x.Stage,x.Path,x.FillBefore,x.FillAfter,x.ClientSize,x.SymbolBounds,frame=x.Frame.Select(p=>new{p.Pixels,p.Energy,p.InkPixels,sha256=Convert.ToHexString(SHA256.HashData(p.Rgb))}),rows=x.Rows.Select(p=>new{p.Pixels,p.Energy,p.InkPixels,sha256=Convert.ToHexString(SHA256.HashData(p.Rgb))})})},new JsonSerializerOptions{WriteIndented=true}));
            binding?.Dispose();host?.Close();OperatorAppearance.ConfigureMotionStyle(previousStyle);OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,previousReduce);OperatorMotion.AnimationOverride=previousOverride;
        }
    }
}
