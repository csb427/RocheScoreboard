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
    internal static void Reveal(FrameworkElement element)
    {
        Attach(element);
        Surfaces.GetValue(element, target => new Surface(target)).Reveal();
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

        internal Surface(FrameworkElement target)
        {
            _target = target;
            _original = target.RenderTransform;
            _originalOrigin=target.RenderTransformOrigin;
            _transforms.Children.Add(_original);
            _transforms.Children.Add(_travel);
            _transforms.Children.Add(_morph);
            target.Loaded += (_, _) => Reveal();
            target.Unloaded += (_, _) => Reset();
            target.IsVisibleChanged += (_, args) => { if (args.NewValue is false) Reset(); else if (target.IsLoaded) Reveal(); };
        }

        private void Reset()
        {
            ++_generation;
            _target.BeginAnimation(UIElement.OpacityProperty, null);
            _travel.BeginAnimation(TranslateTransform.YProperty, null);
            _target.Opacity = 1;
            _travel.Y = 0;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,null);_morph.BeginAnimation(ScaleTransform.ScaleYProperty,null);_morph.ScaleX=_morph.ScaleY=1;_target.RenderTransformOrigin=_originalOrigin;
            _target.RenderTransform = _original;
            if(_revealing)_target.CacheMode=_originalCache;
            _revealing = false;
        }

        internal void Reveal()
        {
            if (!OperatorMotion.Enabled) { Reset(); return; }
            if (!_target.IsLoaded || !_target.IsVisible) return;
            if (!_revealing) { _originalCache=_target.CacheMode;_target.CacheMode=new BitmapCache();_target.Opacity = .82; _travel.Y = 16;_morph.ScaleX=.96;_morph.ScaleY=.90;_target.RenderTransformOrigin=new System.Windows.Point(.5,.08); }
            _revealing = true;
            int generation=++_generation;
            _target.RenderTransform = _transforms;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,OperatorMotion.SurfaceOpen){EasingFunction=OperatorMotion.SmoothOut});
            _morph.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,OperatorMotion.SurfaceOpen){EasingFunction=OperatorMotion.SmoothOut});
            // No reset on re-entry: WPF continues from the current presentation value.
            _target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, OperatorMotion.SurfaceOpen)
                { EasingFunction = OperatorMotion.SmoothOut });
            var travel=new DoubleAnimation(0,OperatorMotion.SurfaceOpen){EasingFunction=OperatorMotion.SmoothOut};
            travel.Completed+=(_,_)=>{if(generation==_generation)Reset();};
            _travel.BeginAnimation(TranslateTransform.YProperty,travel);
        }
    }
}
