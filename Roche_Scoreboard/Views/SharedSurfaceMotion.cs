using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard.Views;

/// <summary>Quiet native surface entry without changing layout, hit targets, or dialog outcomes.</summary>
internal static class SharedSurfaceMotion
{
    private static readonly ConditionalWeakTable<FrameworkElement, Surface> Surfaces = new();

    internal static void Attach(FrameworkElement element) => Surfaces.GetValue(element, target => new Surface(target));
    internal static void AttachFrom(FrameworkElement element,FrameworkElement? source)
        =>Surfaces.GetValue(element,target=>new Surface(target)).Source=source;
    internal static void Reveal(FrameworkElement element)
    {
        Attach(element);
        Surfaces.GetValue(element, target => new Surface(target)).Reveal();
    }
    internal static void RevealSequence(IEnumerable<FrameworkElement> elements)
    {
        int index=0;
        foreach(var element in elements.Where(item=>item.IsLoaded && item.IsVisible && item.ActualWidth>0).Take(4))
        {
            var surface=Surfaces.GetValue(element,target=>new Surface(target));surface.Sequence=true;
            surface.SequenceDelay=TimeSpan.FromTicks(OperatorMotion.Stagger.Ticks*index++);surface.Reveal();
        }
    }

    private sealed class Surface
    {
        private readonly FrameworkElement _target;
        private readonly TranslateTransform _travel = new();
        private readonly ScaleTransform _morph=new(1,1);
        private readonly System.Windows.Point _originalOrigin;
        private readonly Transform _original;
        private readonly TransformGroup _transforms = new();
        private bool _revealing;
        private CacheMode? _originalCache;
        private int _generation;
        internal FrameworkElement? Source {get;set;}
        internal bool Sequence {get;set;}
        internal TimeSpan SequenceDelay {get;set;}
        private FrameworkElement? _content;
        private double _contentOpacity;

        internal Surface(FrameworkElement target)
        {
            _target = target;
            _original = target.RenderTransform;
            _originalOrigin=target.RenderTransformOrigin;
            _transforms.Children.Add(_original);
            _transforms.Children.Add(_travel);
            _transforms.Children.Add(_morph);
            target.Loaded += (_, _) => { OperatorAppearance.Changed -= PreferenceChanged; OperatorAppearance.Changed += PreferenceChanged; Reveal(); };
            target.Unloaded += (_, _) => { OperatorAppearance.Changed -= PreferenceChanged; Reset(); };
            target.IsVisibleChanged += (_, args) => { if (args.NewValue is false) Reset(); else if (target.IsLoaded) Reveal(); };
            if(target.IsLoaded)OperatorAppearance.Changed += PreferenceChanged;
        }

        private void PreferenceChanged() { if(!OperatorMotion.Enabled)Reset(); }

        private void Reset()
        {
            ++_generation;
            _target.BeginAnimation(UIElement.OpacityProperty, null);
            _travel.BeginAnimation(TranslateTransform.YProperty, null);
            _travel.BeginAnimation(TranslateTransform.XProperty, null);
            _target.Opacity = 1;
            _travel.X=_travel.Y = 0;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,null);_morph.BeginAnimation(ScaleTransform.ScaleYProperty,null);_morph.ScaleX=_morph.ScaleY=1;_target.RenderTransformOrigin=_originalOrigin;
            _target.RenderTransform = _original;
            if(_content is not null){_content.BeginAnimation(UIElement.OpacityProperty,null);_content.Opacity=_contentOpacity;_content=null;}
            if(_revealing)_target.CacheMode=_originalCache;
            _revealing = false;
        }

        internal void Reveal()
        {
            if (!OperatorMotion.Enabled) { Reset(); return; }
            if (!_target.IsLoaded || !_target.IsVisible) return;
            if (!_revealing)
            {
                _target.UpdateLayout();
                var origin=(Sequence?new ExpansionOrigin(1,1,0,8):ExpansionOrigin.From(_target,Source)).WithTravel(OperatorMotion.TravelScale);
                _originalCache=_target.CacheMode;_target.CacheMode=new BitmapCache();_target.Opacity=.72;
                _travel.X=origin.OffsetX;_travel.Y=origin.OffsetY;_morph.ScaleX=origin.ScaleX;_morph.ScaleY=origin.ScaleY;
                _target.RenderTransformOrigin=new System.Windows.Point(.5,.5);
                if(Source is {IsVisible:true} && _target is System.Windows.Controls.Border {Child:FrameworkElement content})
                {
                    _content=content;_contentOpacity=content.Opacity;content.Opacity=0;
                    content.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(_contentOpacity,OperatorMotion.SurfaceOpen)
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
            _target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration)
                { BeginTime=SequenceDelay,EasingFunction = OperatorMotion.SmoothOut });
            var travel=new DoubleAnimation(0,duration){BeginTime=SequenceDelay,EasingFunction=OperatorMotion.SmoothOut};
            travel.Completed+=(_,_)=>{if(generation==_generation)Reset();};
            _travel.BeginAnimation(TranslateTransform.YProperty,travel);
            _travel.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,duration){BeginTime=SequenceDelay,EasingFunction=OperatorMotion.SmoothOut});
        }
    }
}
