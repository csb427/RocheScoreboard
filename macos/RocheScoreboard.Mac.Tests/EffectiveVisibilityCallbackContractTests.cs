// OUTSIDE REVIEW DRAFT ONLY — UNCOMPILED / UNRUN. Root owns integration/build.
// Two adapter contracts. Real property/visual-tree routes; no private Publish,
// manufactured event delivery, reflection or application-model mutation.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class EffectiveVisibilityCallbackContractTests
{
    private static async Task RequireAttached(Control child,Window host)
    {
        for(int i=0;i<80&&(!child.IsLoaded||!NativeEffectiveVisibility.IsVisible(child));i++)await Task.Delay(10);
        Assert.True(child.IsLoaded);Assert.Same(host,TopLevel.GetTopLevel(child));
        Assert.True(NativeEffectiveVisibility.IsVisible(child));
    }

    [AvaloniaFact]
    public async Task RemovingAllSubscribersDuringNotificationSkipsTheCapturedRemainderAndAllowsFreshSubscription()
    {
        var child=new Border{Width=20,Height=20};var parent=new Border{Child=child};
        var host=new Window{Width=160,Height=120,Content=parent};
        var delivered=new List<(string Handler,bool Old,bool Next)>();
        EventHandler<AvaloniaPropertyChangedEventArgs>? first=null,second=null;
        void RetireBoth(string name,object? sender,AvaloniaPropertyChangedEventArgs args)
        {
            Assert.Same(child,sender);Assert.Same(child,args.Sender);
            Assert.Same(Control.IsVisibleProperty,args.Property);
            bool old=Assert.IsType<bool>(args.OldValue),next=Assert.IsType<bool>(args.NewValue);
            Assert.NotEqual(old,next);Assert.Equal(next,NativeEffectiveVisibility.IsVisible(child));
            Assert.True(child.IsVisible); // Never spoof the local property.
            delivered.Add((name,old,next));
            // Distinct handlers are both in the notification snapshot. Whichever
            // runs first removes the final observer, so the other must not run.
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,first!);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,second!);
        }
        first=(sender,args)=>RetireBoth("first",sender,args);
        second=(sender,args)=>RetireBoth("second",sender,args);
        Assert.NotEqual(first,second); // Avoid HashSet dedup masking this test.
        host.Show();await RequireAttached(child,host);
        try
        {
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,first);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,second);
            Assert.Empty(delivered);
            parent.IsVisible=false;
            var hide=Assert.Single(delivered);Assert.True(hide.Old);Assert.False(hide.Next);
            Assert.False(NativeEffectiveVisibility.IsVisible(child));Assert.True(child.IsVisible);
            parent.IsVisible=true;parent.IsVisible=false;
            Assert.Single(delivered);Assert.False(NativeEffectiveVisibility.IsVisible(child));
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,second);
            Assert.Single(delivered); // Re-subscription is not a fake change.
            parent.IsVisible=true;
            Assert.Equal(2,delivered.Count);Assert.False(delivered[1].Old);Assert.True(delivered[1].Next);
            Assert.True(NativeEffectiveVisibility.IsVisible(child));
            parent.IsVisible=false;Assert.Equal(2,delivered.Count);
        }
        finally
        {
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,first);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,second);
            host.Close();
        }
    }

    [AvaloniaFact]
    public async Task NonVisibilityDelegationRetainsActualEnabledPayloadAndCanBeRemovedIndependently()
    {
        var child=new Border{Width=20,Height=20};var parent=new Border{Child=child};
        var host=new Window{Width=160,Height=120,Content=parent};
        var enabled=new List<(bool Old,bool Next)>();var visible=new List<bool>();
        EventHandler<AvaloniaPropertyChangedEventArgs> enabledHandler=(sender,args)=>
        {
            Assert.Same(child,sender);Assert.Same(child,args.Sender);Assert.Same(Control.IsEnabledProperty,args.Property);
            bool old=Assert.IsType<bool>(args.OldValue),next=Assert.IsType<bool>(args.NewValue);
            Assert.Equal(next,child.IsEnabled);enabled.Add((old,next));
        };
        EventHandler<AvaloniaPropertyChangedEventArgs> visibleHandler=(sender,args)=>
        {
            Assert.Same(child,sender);Assert.Same(Control.IsVisibleProperty,args.Property);
            bool next=Assert.IsType<bool>(args.NewValue);
            Assert.Equal(next,NativeEffectiveVisibility.IsVisible(child));visible.Add(next);
        };
        host.Show();await RequireAttached(child,host);
        try
        {
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,visibleHandler);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsEnabledProperty,true,enabledHandler);
            Assert.Empty(enabled);Assert.Empty(visible);
            child.Opacity=.5;Assert.Empty(enabled);Assert.Empty(visible);
            child.IsEnabled=false;child.IsEnabled=true;
            Assert.Equal(new[]{(true,false),(false,true)},enabled);
            Assert.Empty(visible);Assert.True(NativeEffectiveVisibility.IsVisible(child));
            NativeEffectiveVisibility.WatchProperty(child,Control.IsEnabledProperty,false,enabledHandler);
            child.IsEnabled=false;Assert.Equal(2,enabled.Count);
            parent.IsVisible=false;
            Assert.Equal(new[]{false},visible);Assert.Equal(2,enabled.Count);
            Assert.True(child.IsVisible);Assert.False(child.IsEnabled);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,visibleHandler);
            parent.IsVisible=true;Assert.Single(visible);Assert.True(NativeEffectiveVisibility.IsVisible(child));
            NativeEffectiveVisibility.WatchProperty(child,Control.IsEnabledProperty,true,enabledHandler);
            Assert.Equal(2,enabled.Count);child.IsEnabled=true;
            Assert.Equal(3,enabled.Count);Assert.Equal((false,true),enabled[2]);
            Assert.Single(visible);
        }
        finally
        {
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,visibleHandler);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsEnabledProperty,false,enabledHandler);
            host.Close();
        }
    }
}
