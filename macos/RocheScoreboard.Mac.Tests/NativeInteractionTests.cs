using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class NativeInteractionTests
{
    [AvaloniaFact]
    public void ActionSurfacePreservesPointerAndKeyboardSourceHandlers()
    {
        var tile=new OperatorActionBorder{Width=120,Height=80,Background=Brushes.Blue};
        var window=new NativeWindow{Width=420,Height=300,Content=tile};int invoked=0;
        tile.PointerReleased+=(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Left)invoked++;};
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();var p=tile.TranslatePoint(new Point(60,40),window)!.Value;
            window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);Assert.Equal(1,invoked);
            tile.Focus();window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);Assert.Equal(2,invoked);
            tile.IsEnabled=false;tile.Activate();Assert.Equal(2,invoked);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void SvgLogosCanAlsoBeUsedAsMaterialImageBrushes()
    {
        string file=Path.Combine(Path.GetTempPath(),"roche-brush-"+Guid.NewGuid()+".svg");
        File.WriteAllText(file,"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40'><rect width='80' height='40' fill='red'/></svg>");
        try
        {
            var image=Roche_Scoreboard.Services.ImageLoadHelper.Load(file);Assert.NotNull(image);
            var brush=NativeImageBrush.Create(image);
            var raster=Assert.IsType<Avalonia.Media.Imaging.RenderTargetBitmap>(brush.Source);
            Assert.Equal(new PixelSize(80,40),raster.PixelSize);
            raster.Dispose();(image as IDisposable)?.Dispose();
        }
        finally{File.Delete(file);}
    }

    [AvaloniaFact]
    public async Task CommandFInvokesTheSourceSettingsSearchShortcut()
    {
        var workspace=new SettingsWorkspace{Heading="Settings",Subtitle="Command shortcut check"};
        var button=new Button{Content="A setting"};workspace.Sections.Add(new Border{Child=button});
        var window=new NativeWindow{Width=700,Height=450,Content=workspace};window.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("ControlCentre"));window.Show();
        try
        {
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();button.Focus();
            window.KeyPressQwerty(PhysicalKey.F,RawInputModifiers.Meta);
            window.KeyReleaseQwerty(PhysicalKey.F,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();
            var search=Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(workspace).OfType<OperatorSearchBox>().Single();
            Assert.True(search.IsFocused);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public void VisibilityCallbacksIgnoreOtherPropertyChangesAndCanBeDetached()
    {
        var target=new Border();int calls=0;
        EventHandler<AvaloniaPropertyChangedEventArgs> callback=(_,_)=>calls++;
        NativeEvents.WatchProperty(target,Visual.IsVisibleProperty,true,callback);
        target.Opacity=.5;target.Width=100;Assert.Equal(0,calls);
        target.IsVisible=false;target.IsVisible=true;Assert.Equal(2,calls);
        NativeEvents.WatchProperty(target,Visual.IsVisibleProperty,false,callback);target.IsVisible=false;Assert.Equal(2,calls);
    }
    [AvaloniaFact]
    public void KeyboardNavigationCanRevealOffscreenFocusedActions()
    {
        var body=new StackPanel();var first=new Button{Content="First",Height=44};body.Children.Add(first);
        body.Children.Add(new Border{Height=900});var last=new Button{Content="Apply",Height=44};body.Children.Add(last);
        var scroll=new ScrollViewer{Content=body};NativeEvents.BringIntoView(scroll,(_,args)=>args.Handled=true);
        var window=new NativeWindow{Width=420,Height=300,Content=scroll};window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();first.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab,RawInputModifiers.None);last.Focus(NavigationMethod.Tab);last.BringIntoView();
            window.KeyReleaseQwerty(PhysicalKey.Tab,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y>0);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task ManyRepeatingAnimationsYieldToInputPastTheReportedFreezeInterval()
    {
        var button=new Button{Content="Input probe",Width=160,Height=60};var window=new NativeWindow{Width=500,Height=320,Content=button};
        var targets=Enumerable.Range(0,120).Select(_=>new TranslateTransform()).ToArray();int clicks=0;button.Click+=(_,_)=>clicks++;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();int baseline=NativeRendering.SubscriberCount;
            foreach(var target in targets)target.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,10,TimeSpan.FromMilliseconds(350)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
            Assert.Equal(baseline+targets.Length,NativeRendering.SubscriberCount);
            for(int i=0;i<70;i++)
            {
                await Task.Delay(500);Dispatcher.UIThread.RunJobs();
                var p=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window)!.Value;
                window.MouseDown(p,MouseButton.Left);window.MouseUp(p,MouseButton.Left);
                Assert.Equal(2*i+1,clicks);
                button.Focus();window.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);window.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);
                Assert.Equal(2*i+2,clicks);
            }
            foreach(var target in targets)Motion.StopAll(target);
            Assert.Equal(baseline,NativeRendering.SubscriberCount);
        }
        finally{foreach(var target in targets)Motion.StopAll(target);window.Close();}
    }
}
