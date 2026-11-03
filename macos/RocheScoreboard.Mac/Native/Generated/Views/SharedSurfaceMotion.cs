// Generated from Views/SharedSurfaceMotion.cs; edit translator/adapters.
// Source SHA-256: 5a9e5bd33c87a2f873f4c750f1575a407dc96d3e688a41f86cbc2635ed7eac06
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

/// <summary>Quiet native surface entry without changing layout, hit targets, or dialog outcomes.</summary>
internal static class SharedSurfaceMotion
{
    private static readonly ConditionalWeakTable<Control, Surface> Surfaces = new();

    internal static void Attach(Control element) => Surfaces.GetValue(element, target => new Surface(target));
    internal static void AttachFrom(Control element,Control? source)
        =>Surfaces.GetValue(element,target=>new Surface(target)).Source=source;
    internal static void Reveal(Control element, bool animate = true)
    {
        Attach(element);
        Surfaces.GetValue(element, target => new Surface(target)).Reveal(animate);
    }
    internal static void RevealSequence(IEnumerable<Control> elements)
    {
        int index=0;
        foreach(var element in elements.Where(item=>item.IsLoaded && item.IsVisible && item.NativeWidth()>0).Take(4))
        {
            var surface=Surfaces.GetValue(element,target=>new Surface(target));surface.Sequence=true;
            surface.SequenceDelay=TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks*index++);surface.Reveal();
        }
    }

    private sealed class Surface
    {
        private readonly Control _target;
        private TranslateTransform _travel = new();
        private ScaleTransform _morph=new(1,1);
        private Avalonia.RelativePoint _originalOrigin;
        private ITransform? _original;
        private TransformGroup _transforms = new();
        private bool _revealing;
        private NativeCacheMode? _originalCache;
        private int _generation;
        internal Control? Source {get;set;}
        internal bool Sequence {get;set;}
        internal TimeSpan SequenceDelay {get;set;}
        private Control? _content;
        private double _contentOpacity;

        internal Surface(Control target)
        {
            _target = target;
            _original = target.RenderTransform;
            _originalOrigin=target.RenderTransformOrigin;
            _transforms.Children.Add(_original as Transform??new MatrixTransform(_original?.Value??Matrix.Identity));
            _transforms.Children.Add(_travel);
            _transforms.Children.Add(_morph);
            target.Loaded += (_, _) => { OperatorAppearance.Changed -= PreferenceChanged; OperatorAppearance.Changed += PreferenceChanged; Reveal(); };
            target.Unloaded += (_, _) => { OperatorAppearance.Changed -= PreferenceChanged; Reset(); };
            NativeEvents.WatchProperty(target,Avalonia.Controls.Control.IsVisibleProperty,true,(_, args) => { if (args.NewValue is false) Reset(); else if (target.IsLoaded) Reveal(); });
            if(target.IsLoaded)OperatorAppearance.Changed += PreferenceChanged;
        }

        private void PreferenceChanged() { if(!OperatorMotion.Enabled)Reset(); }

        private void Reset()
        {
            ++_generation;
            _target.BeginAnimation(Control.OpacityProperty, null);
            _travel.BeginAnimation(TranslateTransform.YProperty, null);
            _travel.BeginAnimation(TranslateTransform.XProperty, null);
            _target.Opacity = 1;
            _travel.X=_travel.Y = 0;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,null);_morph.BeginAnimation(ScaleTransform.ScaleYProperty,null);_morph.ScaleX=_morph.ScaleY=1;
            // Another controller may have replaced the root transform while
            // this arrival was running. Its newer state must survive completion,
            // reduced motion and unload rather than being reset by an old owner.
            if(ReferenceEquals(_target.RenderTransform,_transforms))
            { _target.RenderTransform = _original;_target.RenderTransformOrigin=_originalOrigin; }
            if(_content is not null){_content.BeginAnimation(Control.OpacityProperty,null);_content.Opacity=_contentOpacity;_content=null;}
            if(_revealing)NativeCache.Set(_target, _originalCache);
            _revealing = false;
        }

        internal void Reveal(bool animate = true)
        {
            if (!animate || !OperatorMotion.Enabled) { Reset(); return; }
            if (!_target.IsLoaded || !_target.IsVisible) return;
            if(_revealing && !ReferenceEquals(_target.RenderTransform,_transforms))Reset();
            if (!_revealing)
            {
                if(!ReferenceEquals(_target.RenderTransform,_transforms))
                {
                    _original=_target.RenderTransform;_originalOrigin=_target.RenderTransformOrigin;
                    // The replacement may compose our old, now settled group.
                    // A fresh lease avoids mutating that borrowed group or making
                    // it a child of itself during interrupted re-entry.
                    _travel=new TranslateTransform();_morph=new ScaleTransform(1,1);_transforms=new TransformGroup();
                    _transforms.Children.Add(_original as Transform??new MatrixTransform(_original?.Value??Matrix.Identity));_transforms.Children.Add(_travel);_transforms.Children.Add(_morph);
                }
                _target.UpdateLayout();
                var origin=(Sequence?new ExpansionOrigin(1,1,0,8):ExpansionOrigin.From(_target,Source)).WithTravel(OperatorMotion.TravelScale);
                _originalCache=NativeCache.Get(_target);NativeCache.Set(_target, new NativeBitmapCache());_target.Opacity=.72;
                _travel.X=origin.OffsetX;_travel.Y=origin.OffsetY;_morph.ScaleX=origin.ScaleX;_morph.ScaleY=origin.ScaleY;
                _target.RenderTransformOrigin=new RelativePoint(.5,.5,RelativeUnit.Relative);
                if(Source is {IsVisible:true} && _target is Avalonia.Controls.Border {Child:Control content})
                {
                    _content=content;_contentOpacity=content.Opacity;content.Opacity=0;
                    content.BeginAnimation(Control.OpacityProperty,new DoubleAnimation(_contentOpacity,OperatorMotion.SurfaceOpen)
                        {BeginTime=TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks*2),EasingFunction=OperatorMotion.SmoothOut});
                }
            }
            _revealing = true;
            int generation=++_generation;
            var duration=Sequence?OperatorMotion.SurfaceOpen:OperatorMotion.MaterialArrival;
            _target.RenderTransform = _transforms;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,duration){BeginTime=SequenceDelay,EasingFunction=OperatorMotion.SmoothOut});
            _morph.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,duration){BeginTime=SequenceDelay,EasingFunction=OperatorMotion.SmoothOut});
            // No reset on re-entry: WPF continues from the current presentation value.
            _target.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(1, duration)
                { BeginTime=SequenceDelay,EasingFunction = OperatorMotion.SmoothOut });
            var travel=new DoubleAnimation(0,duration){BeginTime=SequenceDelay,EasingFunction=OperatorMotion.SmoothOut};
            travel.Completed+=(_,_)=>{if(generation==_generation)Reset();};
            _travel.BeginAnimation(TranslateTransform.YProperty,travel);
            _travel.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,duration){BeginTime=SequenceDelay,EasingFunction=OperatorMotion.SmoothOut});
        }
    }
}
