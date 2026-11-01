// Current production player-list input and policy/lifetime regressions.
// App policy Configure is an explicit fixture stimulus, not native OS delivery.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace RocheScoreboard.Mac.Tests;

[CollectionDefinition("Selection check lifetime", DisableParallelization = true)]
public sealed class SelectionCheckLifetimeCollection { }
[Collection("Selection check lifetime")]
public sealed class SelectionCheckLifetimeTests
{
    private static T Named<T>(Control root,string name) where T:Control => Assert.Single(root.GetVisualDescendants().OfType<T>(),x=>x.Name==name);
    private static T Part<T>(Control item,string name) where T:class => Assert.IsAssignableFrom<T>(NativeTemplates.FindObject(item,name));
    private static string State(CricketMatchManager model)=>JsonSerializer.Serialize(model.ToState());
    private static void KeyNow(Window window,PhysicalKey key){window.KeyPressQwerty(key,RawInputModifiers.None);window.KeyReleaseQwerty(key,RawInputModifiers.None);}
    private static async Task Pump(int ms=12){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static async Task Until(Func<bool> ready,string reason)
    {for(int i=0;i<500&&!ready();i++)await Pump();Assert.True(ready(),reason);}
    private static ListBoxItem Selected(ListBox list)=>Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),x=>x.IsSelected);
    private static object ReadParts(ListBoxItem item,string stage,double time)=>new {stage,time,item.IsSelected,item.IsLoaded,item.IsVisible,offset=Part<ShapePath>(item,"SelectionStroke").StrokeDashOffset,
        scaleX=Part<ScaleTransform>(item,"SelectionPop").ScaleX,scaleY=Part<ScaleTransform>(item,"SelectionPop").ScaleY,
        drawClock=Motion.IsAnimating(Part<ShapePath>(item,"SelectionStroke")),popClock=Motion.IsAnimating(Part<ScaleTransform>(item,"SelectionPop")),item.Bounds};
    private static bool Moving(ListBoxItem item)
    {var check=Part<ShapePath>(item,"SelectionStroke");var pop=Part<ScaleTransform>(item,"SelectionPop");return Motion.IsAnimating(check)&&Motion.IsAnimating(pop)&&check.StrokeDashOffset>.02&&check.StrokeDashOffset<6.98&&Math.Abs(pop.ScaleX-1)>.002;}
    private static void Settled(ListBoxItem item)
    {
        var check=Part<ShapePath>(item,"SelectionStroke");var pop=Part<ScaleTransform>(item,"SelectionPop");
        Assert.False(Motion.IsAnimating(check));Assert.False(Motion.IsAnimating(pop));
        Assert.Equal(item.IsSelected?0d:7d,check.StrokeDashOffset,6);Assert.Equal(1d,pop.ScaleX,6);Assert.Equal(1d,pop.ScaleY,6);
    }
    private sealed class Fixture:IDisposable
    {
        internal readonly CricketControlPanel Panel=new();internal readonly NativeWindow Window;
        internal readonly Grid Host=new();internal readonly CricketMatchManager Model;
        internal readonly string DirectoryPath;
        private readonly App app=Assert.IsType<App>(Application.Current);private readonly bool oldSelf=((App)Application.Current!).IsSelfTestMode;
        private readonly bool? oldOverride=OperatorMotion.AnimationOverride;private readonly bool oldGlass=OperatorAppearance.GlassEnabled,oldReduce=OperatorAppearance.ReduceMotion;
        private readonly OperatorMotionStyle oldStyle=OperatorAppearance.MotionStyle;
        private readonly Dictionary<string,byte[]> files=new();private readonly HashSet<string> dirs=new();
        internal ListBox List=null!;internal bool Closed;
        internal Fixture(bool bowling,int width=700,int height=450)
        {
            string temp=Path.GetFullPath(Path.GetTempPath());SelfTestStorage.Initialize(true);Assert.True(SelfTestStorage.IsEnabled);
            Assert.StartsWith(temp,Path.GetFullPath(SelfTestStorage.RootPath),StringComparison.Ordinal);
            if(System.IO.Directory.Exists(SelfTestStorage.RootPath))
            {
                foreach(var p in System.IO.Directory.EnumerateFiles(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories))files[p]=File.ReadAllBytes(p);
                foreach(var p in System.IO.Directory.EnumerateDirectories(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories).Append(SelfTestStorage.RootPath))dirs.Add(p);
            }
            app.IsSelfTestMode=true;OperatorMotion.AnimationOverride=null;
            OperatorAppearance.Configure(oldGlass,true);OperatorAppearance.ConfigureMotionStyle(OperatorMotionStyle.Expressive);
            Model=CricketReliabilityChecks.CreateMatch(false);Assert.False(Model.TossPending);
            if(bowling){Model.SetStriker(0);Model.SetNonStriker(1);}
            Panel.SetMatch(Model);Host.Children.Add(Panel);
            Window=new NativeWindow{Width=width,Height=height,Content=Host};Window.Closed+=(_,_)=>Closed=true;
            DirectoryPath=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??throw new InvalidOperationException("Explicit private captures required."),"selection-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(DirectoryPath);
            Window.Show();
        }
        internal async Task Open(bool bowling)
        {
            await Pump(550);var action=Named<Button>(Panel,bowling?"SelectOpeningBowler":"SelectOpeningBatters");
            Assert.True(action.IsEffectivelyVisible&&action.IsEffectivelyEnabled);Assert.True(action.Focus(NavigationMethod.Tab));action.BringIntoView();await Pump(100);
            KeyNow(Window,PhysicalKey.Enter);
            List=Named<ListBox>(Panel,bowling?"BowlerSelectionList":"BatterSelectionList");
            await Until(()=>List.IsEffectivelyVisible&&List.IsLoaded&&List.GetVisualDescendants().OfType<ListBoxItem>().Count()>1,"Original public SELECT action must open actual player list.");
            await Pump(550);Assert.True(List.Focus(NavigationMethod.Tab));Assert.True(List.Items.Count>1);
            Assert.True(SelectionCheckMotion.GetEnabled(Selected(List)));using var rendered=Window.CaptureRenderedFrame();Assert.NotNull(rendered);
        }
        internal void Save(string name,object value)=>File.WriteAllText(Path.Combine(DirectoryPath,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        public void Dispose()
        {
            Panel.ClearMatch();if(!Closed)Window.Close();Dispatcher.UIThread.RunJobs();
            OperatorAppearance.ConfigureMotionStyle(oldStyle);OperatorAppearance.Configure(oldGlass,oldReduce);OperatorMotion.AnimationOverride=oldOverride;app.IsSelfTestMode=oldSelf;
            if(System.IO.Directory.Exists(SelfTestStorage.RootPath))
            {
                foreach(var p in System.IO.Directory.EnumerateFiles(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories))if(!files.ContainsKey(p))File.Delete(p);
                foreach(var p in System.IO.Directory.EnumerateDirectories(SelfTestStorage.RootPath,"*",SearchOption.AllDirectories).OrderByDescending(x=>x.Length))if(!dirs.Contains(p)&&!System.IO.Directory.EnumerateFileSystemEntries(p).Any())System.IO.Directory.Delete(p);
            }
            foreach(var(p,data) in files){System.IO.Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,data);}
        }
    }

    // Natural managed rendering only. Never SampleAt, private Paint, forced
    // progress, artificial duration or synthetic OS preference notification.
    private static async Task<ListBoxItem> DuringRealSelection(Fixture f,Action<ListBoxItem> interrupt,string name,PhysicalKey input=PhysicalKey.ArrowDown)
    {
        string? error=null;bool witnessed=false,completed=false,busy=false,inputFinished=false;ListBoxItem? selected=null;WriteableBitmap? partial=null;
        var trace=new List<object>();var timer=Stopwatch.StartNew();EventHandler? observe=null;
        observe=(_,_)=>
        {
            if(!inputFinished||busy||witnessed||error is not null)return;
            try
            {
                selected=Selected(f.List);trace.Add(ReadParts(selected,"observe",timer.Elapsed.TotalMilliseconds));
                if(!Moving(selected))return;
                busy=true;partial=f.Window.CaptureRenderedFrame();Assert.NotNull(partial);
                trace.Add(ReadParts(selected,"coherent-partial",timer.Elapsed.TotalMilliseconds));
                if(!Moving(selected)){partial.Dispose();partial=null;busy=false;return;}
                witnessed=true;interrupt(selected);trace.Add(ReadParts(selected,"after-interrupt",timer.Elapsed.TotalMilliseconds));busy=false;completed=true;
            }
            catch(Exception failure){error=failure.ToString();busy=false;}
        };
        NativeRendering.Rendering+=observe;
        try
        {
            Assert.True(OperatorMotion.Enabled);int before=f.List.SelectedIndex;KeyNow(f.Window,input);inputFinished=true;
            Assert.Equal(Math.Clamp(before+(input==PhysicalKey.ArrowDown?1:-1),0,f.List.Items.Count-1),f.List.SelectedIndex);
            await Until(()=>error is not null||completed,"Fixture timing: require actual naturally moving production draw AND pop after original list ArrowDown.");
            Assert.True(witnessed,"No product failure without natural partial witness.");Assert.Null(error);return Assert.IsType<ListBoxItem>(selected);
        }
        finally
        {
            NativeRendering.Rendering-=observe;partial?.Save(Path.Combine(f.DirectoryPath,name+"-natural-partial.png"));partial?.Dispose();
            f.Save(name,new{error,witnessed,frames=trace});
            if(!f.Closed && f.Window.IsVisible){using var frame=f.Window.CaptureRenderedFrame();frame?.Save(Path.Combine(f.DirectoryPath,name+"-after.png"));}
        }
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task PublicPlayerArrowSelectionLiveAppReduceStopsItsActualDrawAndPopWithoutCommitting(bool bowling)
    {
        using var f=new Fixture(bowling);await f.Open(bowling);
        string before=State(f.Model);int changes=0;f.Model.MatchChanged+=()=>changes++;
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
        var item=await DuringRealSelection(f,current=>
        {
            var bounds=current.Bounds;int selected=f.List.SelectedIndex;
            Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
            // Explicit actual app-policy API fixture stimulus. Root commands
            // are intentionally blocked while this selector owns input.
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);
            Assert.False(OperatorMotion.Enabled);Assert.True(current.IsSelected);Assert.Equal(selected,f.List.SelectedIndex);
            Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);Assert.Equal(bounds,current.Bounds);
            Settled(current);
        },"live-app-reduce-"+bowling);
        await Pump(480);Settled(item);Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
        KeyNow(f.Window,PhysicalKey.ArrowUp);await Pump(30);Settled(Selected(f.List));Settled(item);
        Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
    }
    [AvaloniaFact]
    public async Task ActualListInputPreservesEachDirectSelectionCallbackOwnerAndBalancedRemoval()
    {
        var first=new ListBoxItem{Content="First",Name="FirstOwner"};var second=new ListBoxItem{Content="Second",Name="SecondOwner"};
        var list=new ListBox{Focusable=true,Width=280,Height=180,ItemsSource=new[]{first,second},SelectedIndex=0};
        var window=new NativeWindow{Width=400,Height=300,Content=list};
        var callbacks=new List<(object? Sender,object? Source,bool Selected)>();
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> selected=(sender,args)=>callbacks.Add((sender,args.Source,true));
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> unselected=(sender,args)=>callbacks.Add((sender,args.Source,false));
        string directory=Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")!,"event-owner-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(directory);
        void Watch(bool adding)
        {
            NativeEvents.WatchSelection(second,adding,true,selected);NativeEvents.WatchSelection(first,adding,false,unselected);
        }
        try
        {
            window.Show();await Pump(200);Assert.True(list.Focus(NavigationMethod.Tab));using(var image=window.CaptureRenderedFrame())Assert.NotNull(image);
            Watch(true);Watch(true); // duplicate subscriptions must remain one callback per actual change
            KeyNow(window,PhysicalKey.ArrowDown);await Pump(40);
            Assert.Equal(1,list.SelectedIndex);Assert.Equal(2,callbacks.Count);
            Watch(false);Watch(false);KeyNow(window,PhysicalKey.ArrowUp);await Pump(40);
            Assert.Equal(0,list.SelectedIndex);Assert.Equal(2,callbacks.Count);
            Watch(true);KeyNow(window,PhysicalKey.ArrowDown);await Pump(40);
            Assert.Equal(1,list.SelectedIndex);Assert.Equal(4,callbacks.Count);
            Watch(false);KeyNow(window,PhysicalKey.ArrowUp);await Pump(40);Assert.Equal(4,callbacks.Count);
            Assert.Equal(2,callbacks.Count(x=>x.Selected));Assert.Equal(2,callbacks.Count(x=>!x.Selected));
            Assert.All(callbacks.Where(x=>x.Selected),x=>{Assert.Same(second,x.Sender);Assert.Same(second,x.Source);});
            Assert.All(callbacks.Where(x=>!x.Selected),x=>{Assert.Same(first,x.Sender);Assert.Same(first,x.Source);});
        }
        finally
        {
            Watch(false);window.Close();
            File.WriteAllText(Path.Combine(directory,"actual-callback-owners.json"),JsonSerializer.Serialize(callbacks.Select(x=>new{sender=(x.Sender as Control)?.Name,source=(x.Source as Control)?.Name,x.Selected,sameOwner=ReferenceEquals(x.Sender,x.Source)}),new JsonSerializerOptions{WriteIndented=true}));
        }
    }

    [AvaloniaTheory]
    [InlineData(false,"hide")][InlineData(true,"hide")]
    [InlineData(false,"unload")][InlineData(true,"unload")]
    [InlineData(false,"disable")][InlineData(true,"disable")]
    [InlineData(false,"rebind")][InlineData(true,"rebind")]
    [InlineData(false,"close")][InlineData(true,"close")]
    public async Task PublicPlayerSelectionRetirementSettlesOnlyCurrentPartsAndReentryDoesNotReplay(bool bowling,string retirement)
    {
        using var f=new Fixture(bowling);await f.Open(bowling);string before=State(f.Model);int changes=0;f.Model.MatchChanged+=()=>changes++;
        var replacement=CricketReliabilityChecks.CreateMatch(false);if(bowling){replacement.SetStriker(0);replacement.SetNonStriker(1);}
        string replacementBefore=State(replacement);int replacementChanges=0;replacement.MatchChanged+=()=>replacementChanges++;
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
        var item=await DuringRealSelection(f,current=>
        {
            if(retirement=="hide")f.Window.Hide();
            else if(retirement=="unload")f.Host.Children.Remove(f.Panel);
            else if(retirement=="disable")current.IsEnabled=false;
            else if(retirement=="rebind")f.Panel.SetMatch(replacement);
            else f.Window.Close();
            Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
            Assert.Equal(replacementBefore,State(replacement));Assert.Equal(0,replacementChanges);
            Settled(current);
        },"lifetime-"+bowling+"-"+retirement);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);await Pump(450);Settled(item);
        if(retirement=="close")return;
        if(retirement=="hide")f.Window.Show();
        else if(retirement=="unload")f.Host.Children.Add(f.Panel);
        else if(retirement=="disable")item.IsEnabled=true;
        else await f.Open(bowling);
        await Pump(150);Assert.True(f.List.IsEffectivelyVisible);Assert.True(f.List.Focus(NavigationMethod.Tab));
        KeyNow(f.Window,PhysicalKey.ArrowUp);await Pump(25);Settled(Selected(f.List));Settled(item);
        Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);Assert.Equal(replacementBefore,State(replacement));Assert.Equal(0,replacementChanges);
    }

    [AvaloniaTheory]
    [InlineData(false,700,450,"Expressive")][InlineData(true,700,450,"Expressive")]
    [InlineData(false,420,320,"Calm")][InlineData(true,420,320,"Calm")]
    public async Task RealPlayerReversalRepeatAndFutureReducedInputRetainCurrentNativeSelection(bool bowling,int width,int height,string policy)
    {
        using var f=new Fixture(bowling,width,height);await f.Open(bowling);
        OperatorAppearance.ConfigureMotionStyle(policy=="Calm"?OperatorMotionStyle.Calm:OperatorMotionStyle.Expressive);
        string before=State(f.Model);int changes=0;f.Model.MatchChanged+=()=>changes++;
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
        ListBoxItem? reverseTo=null;
        var first=await DuringRealSelection(f,current=>
        {
            Assert.True(current.IsSelected);KeyNow(f.Window,PhysicalKey.ArrowUp);Assert.Equal(0,f.List.SelectedIndex);
            Assert.False(current.IsSelected);Settled(current);reverseTo=Selected(f.List);
            Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
        },"actual-reverse-"+policy);
        var final=await DuringRealSelection(f,current=>
        {
            Assert.True(current.IsSelected);Assert.NotNull(reverseTo);Assert.False(reverseTo.IsSelected);Settled(reverseTo);
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,true);Settled(current);Settled(reverseTo);
            Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
        },"rapid-current-reduce-"+policy);
        await Pump(480);Settled(final);Assert.Same(first,final);
        // Same current item through actual pointer input must not restart a draw.
        Assert.True(final.Focus(NavigationMethod.Pointer));final.BringIntoView();await Pump(100);
        var point=final.TranslatePoint(new Point(final.Bounds.Width/2,final.Bounds.Height/2),f.Window)!.Value;
        Assert.Contains(final,(f.Window.InputHitTest(point) as Visual)!.GetVisualAncestors().OfType<ListBoxItem>().Append(f.Window.InputHitTest(point) as ListBoxItem).Where(x=>x is not null));
        f.Window.MouseMove(point);f.Window.MouseDown(point,MouseButton.Left);f.Window.MouseUp(point,MouseButton.Left);
        await Pump(30);Settled(final);Assert.Equal(1,f.List.SelectedIndex);
        Assert.True(f.List.Focus(NavigationMethod.Tab));KeyNow(f.Window,PhysicalKey.ArrowUp);await Pump(30);Settled(Selected(f.List));Settled(final);
        OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);
        var future=await DuringRealSelection(f,current=>{Assert.True(current.IsSelected);Assert.Equal(before,State(f.Model));},"future-allowed-"+policy);
        await Pump(450);Settled(future);Assert.Equal(before,State(f.Model));Assert.Equal(0,changes);
    }

}
