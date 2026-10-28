// Generated from Views/SharedSurfaceMotion.cs; edit translator/adapters.
// Source SHA-256: 32ba9aca6e98750639ec63106f349d5e81b266c0c851c6581d7bf2f33452e741
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
    internal static void Reveal(Control element)
    {
        Attach(element);
        Surfaces.GetValue(element, target => new Surface(target)).Reveal();
    }

    private sealed class Surface
    {
        private readonly Control _target;
        private readonly TranslateTransform _travel = new();
        private readonly ScaleTransform _morph=new(1,1);
        private readonly Avalonia.RelativePoint _originalOrigin;
        private readonly ITransform? _original;
        private readonly TransformGroup _transforms = new();
        private bool _revealing;
        private NativeCacheMode? _originalCache;
        private int _generation;

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
            _target.Opacity = 1;
            _travel.Y = 0;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,null);_morph.BeginAnimation(ScaleTransform.ScaleYProperty,null);_morph.ScaleX=_morph.ScaleY=1;_target.RenderTransformOrigin=_originalOrigin;
            _target.RenderTransform = _original;
            if(_revealing)NativeCache.Set(_target, _originalCache);
            _revealing = false;
        }

        internal void Reveal()
        {
            if (!OperatorMotion.Enabled) { Reset(); return; }
            if (!_target.IsLoaded || !_target.IsVisible) return;
            if (!_revealing) { _originalCache=NativeCache.Get(_target);NativeCache.Set(_target, new NativeBitmapCache());_target.Opacity = .72; _travel.Y = 24;_morph.ScaleX=.94;_morph.ScaleY=.82;_target.RenderTransformOrigin=new RelativePoint(.5,.08,RelativeUnit.Relative); }
            _revealing = true;
            int generation=++_generation;
            _target.RenderTransform = _transforms;
            _morph.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,OperatorMotion.MaterialArrival){EasingFunction=OperatorMotion.SmoothOut});
            _morph.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,OperatorMotion.MaterialArrival){EasingFunction=OperatorMotion.SmoothOut});
            // No reset on re-entry: WPF continues from the current presentation value.
            _target.BeginAnimation(Control.OpacityProperty, new DoubleAnimation(1, OperatorMotion.MaterialArrival)
                { EasingFunction = OperatorMotion.SmoothOut });
            var travel=new DoubleAnimation(0,OperatorMotion.MaterialArrival){EasingFunction=OperatorMotion.SmoothOut};
            travel.Completed+=(_,_)=>{if(generation==_generation)Reset();};
            _travel.BeginAnimation(TranslateTransform.YProperty,travel);
        }
    }
}
