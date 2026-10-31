using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed class EffectiveMotionVisibilityTests
{
    [AvaloniaFact]
    public async Task EffectiveObserverTracksHiddenAncestorsWithoutChangingLocalVisibility()
    {
        var child=new Border{Width=20,Height=20};var parent=new Border{Child=child};
        var host=new Window{Width=160,Height=120,Content=parent};
        var events=new List<(bool Old,bool Next,bool Read)>();
        EventHandler<AvaloniaPropertyChangedEventArgs> handler=(sender,args)=>
        {Assert.Same(child,sender);events.Add(((bool)args.OldValue!,(bool)args.NewValue!,NativeEffectiveVisibility.IsVisible(child)));};
        host.Show();await Task.Delay(50);
        NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,handler);
        try
        {
            Assert.True(NativeEffectiveVisibility.IsVisible(child));Assert.True(child.IsVisible);
            parent.IsVisible=false;
            Assert.False(NativeEffectiveVisibility.IsVisible(child));Assert.True(child.IsVisible);
            Assert.Equal((true,false,false),Assert.Single(events));
            parent.IsVisible=true;Assert.True(NativeEffectiveVisibility.IsVisible(child));
            Assert.Equal((false,true,true),events[1]);Assert.Equal(2,events.Count);
        }
        finally{NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,handler);host.Close();}
    }

    [AvaloniaFact]
    public async Task ReparentDetachesOldAncestorsAndRemovingWatcherRetiresCallbacks()
    {
        var child=new Border{Width=20,Height=20};var first=new Border{Child=child};var second=new Border{IsVisible=false};
        var host=new Window{Width=160,Height=120,Content=new StackPanel{Children={first,second}}};
        var events=new List<bool>();
        EventHandler<AvaloniaPropertyChangedEventArgs> handler=(_,args)=>
        {Assert.Equal((bool)args.NewValue!,NativeEffectiveVisibility.IsVisible(child));events.Add((bool)args.NewValue!);};
        host.Show();await Task.Delay(50);NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,handler);
        try
        {
            first.Child=null;Assert.False(NativeEffectiveVisibility.IsVisible(child));Assert.Equal(new[]{false},events);
            second.Child=child;Assert.False(NativeEffectiveVisibility.IsVisible(child));Assert.Single(events);
            second.IsVisible=true;Assert.Equal(new[]{false,true},events);
            first.IsVisible=false;Assert.True(NativeEffectiveVisibility.IsVisible(child));Assert.Equal(2,events.Count);
            NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,handler);
            second.IsVisible=false;Assert.Equal(2,events.Count);Assert.False(NativeEffectiveVisibility.IsVisible(child));
        }
        finally{NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,handler);host.Close();}
    }

    [AvaloniaFact]
    public async Task ReentrantVisibilityChangesCannotDeliverObsoleteOuterState()
    {
        var child=new Border{Width=20,Height=20};var parent=new Border{Child=child};var host=new Window{Width=160,Height=120,Content=parent};
        var reads=new List<bool>();
        EventHandler<AvaloniaPropertyChangedEventArgs> first=(_,args)=>{if(args.NewValue is false)parent.IsVisible=true;};
        EventHandler<AvaloniaPropertyChangedEventArgs> second=(_,args)=>
        {Assert.Equal((bool)args.NewValue!,NativeEffectiveVisibility.IsVisible(child));reads.Add((bool)args.NewValue!);};
        host.Show();await Task.Delay(50);NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,first);NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,true,second);
        try{parent.IsVisible=false;Assert.True(parent.IsVisible);Assert.True(NativeEffectiveVisibility.IsVisible(child));Assert.NotEmpty(reads);Assert.True(reads.Last());}
        finally{NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,first);NativeEffectiveVisibility.WatchProperty(child,Control.IsVisibleProperty,false,second);host.Close();}
    }

    [AvaloniaTheory][InlineData("selection")][InlineData("audio")]
    public async Task StateChangesWhileAncestorIsHiddenSettleSymbolWithoutStartingPaintTimelines(string part)
    {
        bool oldReduce=OperatorAppearance.ReduceMotion;bool? oldOverride=OperatorMotion.AnimationOverride;
        var symbol=new OperatorSymbol{Kind="hide-team-names",Width=40,Height=40};
        var parent=new Border{Child=symbol};var host=new Window{Width=160,Height=120,Content=parent};
        try
        {
            OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,false);OperatorMotion.AnimationOverride=null;
            Assert.True(OperatorMotion.Enabled);host.Show();await Task.Delay(80);Assert.True(symbol.IsLoaded);
            parent.IsVisible=false;Assert.True(symbol.IsLoaded);Assert.False(NativeEffectiveVisibility.IsVisible(symbol));
            if(part=="selection"){symbol.IsActive=true;Assert.Equal(1d,symbol.FillProgress);}
            else{symbol.AudioLevel=.35;Assert.Equal(.35,symbol.RenderedAudioLevel,5);}
            Assert.False(symbol.IsAnimating);await Task.Delay(80);Assert.False(symbol.IsAnimating);
        }
        finally{host.Close();OperatorAppearance.Configure(OperatorAppearance.GlassEnabled,oldReduce);OperatorMotion.AnimationOverride=oldOverride;}
    }
}
