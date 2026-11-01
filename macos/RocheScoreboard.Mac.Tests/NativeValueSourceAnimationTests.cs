using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class NativeValueSourceAnimationTests
{
    [AvaloniaFact]
    public void AnimationSourceTracksOnlyItsPropertyAndClearingRestoresTheLocalValue()
    {
        var target = new Border { Opacity = .6, Width = 200 };
        Assert.False(NativeValueSource.GetValueSource(target, Control.OpacityProperty).IsAnimated);
        try
        {
            target.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(.1, .9, TimeSpan.FromSeconds(1)));
            Motion.SampleAt(target, TimeSpan.FromMilliseconds(500));
            Assert.True(NativeValueSource.GetValueSource(target, Control.OpacityProperty).IsAnimated);
            Assert.False(NativeValueSource.GetValueSource(target, Control.WidthProperty).IsAnimated);
            Assert.InRange(target.Opacity, .49, .51);
        }
        finally { target.BeginAnimation(Control.OpacityProperty, null); }
        Assert.False(NativeValueSource.GetValueSource(target, Control.OpacityProperty).IsAnimated);
        Assert.Equal(.6, target.Opacity, 6);
        Assert.Equal(200, target.Width);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletedClockReportsWhetherItsFillStillSuppliesAValue(bool holdEnd)
    {
        var target = new Border { Opacity = .6 };
        try
        {
            target.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(.1, .9, TimeSpan.FromSeconds(1))
            { FillBehavior = holdEnd ? FillBehavior.HoldEnd : FillBehavior.Stop });
            Motion.SampleAt(target, TimeSpan.FromSeconds(2));
            Assert.False(Motion.IsAnimating(target));
            Assert.Equal(holdEnd, NativeValueSource.GetValueSource(target, Control.OpacityProperty).IsAnimated);
            Assert.Equal(holdEnd ? .9 : .6, target.Opacity, 6);
        }
        finally { target.BeginAnimation(Control.OpacityProperty, null); }
        Assert.False(NativeValueSource.GetValueSource(target, Control.OpacityProperty).IsAnimated);
        Assert.Equal(.6, target.Opacity, 6);
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public void ReentrantCancellationDisposesTheInFlightAnimationValue(bool replace)
    {
        var target=new Border{Opacity=.6};int interruptions=0,completed=0;
        var animation=new DoubleAnimation(.1,.9,TimeSpan.FromSeconds(1));animation.Completed+=(_,_)=>completed++;
        void Observe(object? sender,Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if(args.Property!=Control.OpacityProperty || target.Opacity>=.5 || interruptions!=0)return;
            interruptions++;
            target.BeginAnimation(Control.OpacityProperty,null);
            if(replace)target.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(.7,.8,TimeSpan.FromSeconds(1)));
        }
        target.PropertyChanged+=Observe;
        try
        {
            target.BeginAnimation(Control.OpacityProperty,animation);
            Assert.Equal(1,interruptions);
            Assert.Equal(replace?.7:.6,target.Opacity,3);
            Motion.SampleAt(target,TimeSpan.FromSeconds(2));
            Assert.Equal(replace?.8:.6,target.Opacity,3);Assert.Equal(0,completed);
            target.BeginAnimation(Control.OpacityProperty,null);
            Assert.Equal(.6,target.Opacity,6);
            Assert.False(NativeValueSource.GetValueSource(target,Control.OpacityProperty).IsAnimated);
            Assert.False(Motion.IsAnimating(target));
        }
        finally{target.PropertyChanged-=Observe;target.BeginAnimation(Control.OpacityProperty,null);}
    }

}
