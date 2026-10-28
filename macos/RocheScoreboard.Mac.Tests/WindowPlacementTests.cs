using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RocheScoreboard.Mac.Native;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class WindowPlacementTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void WindowCanCloseBeforeItsFirstShowWithoutAccessingAnUninitialisedBackend(bool owned)
    {
        var owner = new NativeWindow();
        var window = new NativeWindow { Owner = owned ? owner : null };
        int closed = 0; window.Closed += (_, _) => closed++;
        window.Close(); owner.Close();
        Assert.Equal(1, closed);
    }
    [Theory]
    [InlineData(0,0,1280,720,1)]
    [InlineData(0,46,2560,1394,2)]
    [InlineData(-1920,24,1920,1056,1)]
    [InlineData(1920,-900,1440,876,1.5)]
    [InlineData(0,0,640,360,2)]
    public void OversizeOrDisconnectedRestoreFitsPhysicalWorkArea(int x,int y,int width,int height,double scale)
    {
        var work=new PixelRect(x,y,width,height);
        var result=NativeWindowPlacement.Fit(new PixelPoint(99999,-99999),new Size(1550,1050),work,scale,new Size(0,28));
        Assert.InRange(result.Position.X,work.X,work.Right-1);Assert.InRange(result.Position.Y,work.Y,work.Bottom-1);
        Assert.True(result.ClientSize.Width*scale<=work.Width+.01);
        Assert.True((result.ClientSize.Height+28)*scale<=work.Height+.01);
        Assert.True(result.Position.X+Math.Ceiling(result.ClientSize.Width*scale)<=work.Right);
        Assert.True(result.Position.Y+Math.Ceiling((result.ClientSize.Height+28)*scale)<=work.Bottom);
    }
    [Fact]
    public void NormalBoundsAndCompactOutputArePreserved()
    {
        var result=NativeWindowPlacement.Fit(new PixelPoint(100,100),new Size(386,193),new PixelRect(0,24,1440,876),1,new Size(0,28));
        Assert.Equal(new Size(386,193),result.ClientSize);Assert.Equal(new PixelPoint(100,100),result.Position);
    }
    [Fact]
    public void InvalidSavedNumbersHaveFiniteUsableFallbacks()
    {
        var result=NativeWindowPlacement.Fit(default,new Size(double.NaN,double.PositiveInfinity),new PixelRect(0,0,800,500),double.NaN,new Size(0,28));
        Assert.InRange(result.ClientSize.Width,1,800);Assert.InRange(result.ClientSize.Height,1,472);
    }
    [AvaloniaFact]
    public void SynchronousSourceDialogRetainsOwnershipAndEscapeCanCancel()
    {
        var owner=new NativeWindow{Width=700,Height=450,Content=new Button{Content="Owner"}};owner.Show();
        var cancel=new Button{Content="Cancel",IsCancel=true};
        var dialog=new NativeWindow{Owner=owner,Width=380,Height=240,Content=cancel};
        cancel.Click+=(_,_)=>dialog.DialogResult=false;
        try
        {
            Dispatcher.UIThread.Post(()=>
            {
                Assert.True(dialog.IsVisible);Assert.Contains(dialog,owner.OwnedWindows);Assert.Same(owner,dialog.Owner);cancel.Focus();
                Avalonia.Headless.HeadlessWindowExtensions.KeyPressQwerty(dialog,Avalonia.Input.PhysicalKey.Escape,Avalonia.Input.RawInputModifiers.None);
                Avalonia.Headless.HeadlessWindowExtensions.KeyReleaseQwerty(dialog.IsVisible?dialog:owner,Avalonia.Input.PhysicalKey.Escape,Avalonia.Input.RawInputModifiers.None);
            });
            Assert.False(dialog.ShowDialog());Assert.True(owner.IsEnabled);Assert.True(owner.IsVisible);
        }
        finally{if(dialog.IsVisible)dialog.Close();owner.Close();}
    }
    [AvaloniaFact]
    public void RestoringSavedPhysicalCoordinatesAndOversizeBoundsRemainsInsideTheWorkArea()
    {
        var window=new NativeWindow{MinWidth=900,MinHeight=700};
        Roche_Scoreboard.Services.WindowLayoutStorage.Apply(window,new(99999,-99999,9999,9999,99999,-99999));
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();var work=window.Screens.ScreenFromWindow(window)!.WorkingArea;
            Assert.InRange(window.Position.X,work.X,work.Right);Assert.InRange(window.Position.Y,work.Y,work.Bottom);
            Assert.True(window.Width*window.RenderScaling<=work.Width);
            Assert.True((window.Height+28)*window.RenderScaling<=work.Height);
            var saved=Roche_Scoreboard.Services.WindowLayoutStorage.Capture(window);Assert.Equal(window.Position.X,saved.PixelLeft);Assert.Equal(window.Position.Y,saved.PixelTop);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void LongDialogRemainsScrollableToItsLastAction()
    {
        var form=new StackPanel{Spacing=8};
        for(int i=0;i<35;i++)form.Children.Add(new TextBox{Text="Long player name "+i,MinHeight=40});
        var apply=new Button{Content="Apply changes",MinHeight=44};form.Children.Add(apply);
        var window=new NativeWindow{Width=420,Height=300,Content=form,Title="Long form"};
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scroll=Assert.IsType<NativeAccessibleWindowContent>(window.Content);
            Assert.True(scroll.Extent.Height>scroll.Viewport.Height);
            scroll.ScrollToEnd();Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y>0);
            var position=apply.TranslatePoint(default,scroll);
            Assert.NotNull(position);Assert.InRange(position.Value.Y,-1,scroll.Bounds.Height);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public void OutputUsesIndependentContentWithoutOperatorScrollChrome()
    {
        var content=new Grid();var window=new NativeWindow{Width=386,Height=193,Content=content,Title="Cricket Display"};
        window.Show();
        try{Dispatcher.UIThread.RunJobs();Assert.Same(content,window.Content);Assert.True(window.IsOutputWindow);Assert.Equal(386,window.Width);Assert.Equal(193,window.Height);}
        finally{window.Close();}
    }
    [AvaloniaFact]
    public void ModelessOwnedWindowRetainsOwnerAndCanCloseIndependently()
    {
        var owner=new NativeWindow{Width=600,Height=400};owner.Show();
        var child=new NativeWindow{Owner=owner,Width=400,Height=300};child.Show();
        try{Dispatcher.UIThread.RunJobs();Assert.Same(owner,child.Owner);child.Close();Assert.True(owner.IsVisible);}
        finally{if(child.IsVisible)child.Close();owner.Close();}
    }
}
