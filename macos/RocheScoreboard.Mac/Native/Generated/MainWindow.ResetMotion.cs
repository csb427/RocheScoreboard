// Generated from MainWindow.ResetMotion.cs; edit translator/adapters.
// Source SHA-256: 147f4ef2194b57c05b116f8c095ffaa569e40b8011f1b5c195bbdcf23918123f
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private readonly ScaleTransform _resetSurfaceScale = new(1, 1);
    private readonly TranslateTransform _resetSurfaceTravel = new();
    private readonly RotateTransform _resetArrowTurn = new();
    private ExpansionOrigin _resetOrigin = new(1, 1, 0, 0);
    private DispatcherTimer? _resetExitDeadline;

    private void InitializeResetMotion()
    {
        var transform = new TransformGroup();
        transform.Children.Add(_resetSurfaceScale);
        transform.Children.Add(_resetSurfaceTravel);
        ResetSurface.RenderTransform = transform;
        ResetSurface.RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative);
        ResetArrow.RenderTransform = _resetArrowTurn;
        ResetArrow.RenderTransformOrigin = new RelativePoint(.5, .5,RelativeUnit.Relative);
    }

    private ExpansionOrigin CurrentResetSourceOrigin()
    {
        if (_resetSource is { IsLoaded: true, IsVisible: true, Bounds.Width: > 0, Bounds.Height: > 0 } source
            && ReferenceEquals(NativeWindow.GetWindow(source), this))
            return ExpansionOrigin.From(ResetSurface, source).WithTravel(OperatorMotion.TravelScale);
        // A remote/hidden trigger has no physical origin. Stay centered rather
        // than pretending that the last pointer or a hidden control opened it.
        return new ExpansionOrigin(.96, .96, 0, 0).WithTravel(OperatorMotion.TravelScale);
    }

    private void OpenResetMotion()
    {
        ResetSurface.UpdateLayout();
        _resetOrigin = CurrentResetSourceOrigin();
        if (!OperatorMotion.Enabled) { StopResetMotion(); return; }
        AnimateResetSurface(_resetOrigin, new ExpansionOrigin(1, 1, 0, 0), OperatorMotion.SurfaceOpen);
        ResetBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, OperatorMotion.SurfaceOpen)
            { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        // Content never scales or travels with the material. Safe actions are
        // visible and accepted immediately, with a brief independent handoff.
        ResetHeader.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, 1, OperatorMotion.SurfaceOpen)
            { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        // Establish the handoff immediately, then hold through the stagger.
        // BeginTime alone would first paint at base opacity1 and then dim it.
        var detailsArrival = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        detailsArrival.KeyFrames.Add(new DiscreteDoubleKeyFrame(.15, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        detailsArrival.KeyFrames.Add(new DiscreteDoubleKeyFrame(.15, KeyTime.FromTimeSpan(OperatorMotion.Stagger)));
        detailsArrival.KeyFrames.Add(new EasingDoubleKeyFrame(1,
            KeyTime.FromTimeSpan(OperatorMotion.Stagger + OperatorMotion.SurfaceOpen), OperatorMotion.SmoothOut));
        ResetDetails.BeginAnimation(OpacityProperty, detailsArrival);
        ResetActions.BeginAnimation(OpacityProperty, new DoubleAnimation(.75, 1, OperatorMotion.Press)
            { FillBehavior = FillBehavior.Stop });
        _resetArrowTurn.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-42 * OperatorMotion.TravelScale, 0, OperatorMotion.SymbolArrival)
            { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        // The warning stays upright while the outer reset arrow arrives.
        ResetWarningMark.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, OperatorMotion.SymbolAccent)
            { FillBehavior = FillBehavior.Stop });
    }

    private void AnimateResetSurface(ExpansionOrigin from, ExpansionOrigin to, TimeSpan duration)
    {
        _resetSurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(from.ScaleX, to.ScaleX, duration) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        _resetSurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(from.ScaleY, to.ScaleY, duration) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        _resetSurfaceTravel.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(from.OffsetX, to.OffsetX, duration) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        _resetSurfaceTravel.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from.OffsetY, to.OffsetY, duration) { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
    }

    private void CloseResetMotion(int generation)
    {
        if (!OperatorMotion.Enabled) { CompleteResetVisual(generation); return; }
        var current = new ExpansionOrigin(_resetSurfaceScale.ScaleX, _resetSurfaceScale.ScaleY,
            _resetSurfaceTravel.X, _resetSurfaceTravel.Y);
        double backdrop = ResetBackdrop.Opacity, header = ResetHeader.Opacity,
            details = ResetDetails.Opacity, actions = ResetActions.Opacity, arrow = _resetArrowTurn.Angle;
        // Find the source in the untransformed destination coordinate space.
        // It may have moved or disappeared since entry/while the window resized.
        StopResetMotion();
        _resetOrigin = CurrentResetSourceOrigin();
        AnimateResetSurface(current, _resetOrigin, OperatorMotion.SurfaceClose);
        // Stop fill returns to the closed value while the material finishes
        // its longer return, rather than flashing the labels back into view.
        ResetHeader.Opacity = ResetDetails.Opacity = ResetActions.Opacity = ResetBackdrop.Opacity = 0;
        ResetHeader.BeginAnimation(OpacityProperty, new DoubleAnimation(header, 0, OperatorMotion.Press)
            { FillBehavior = FillBehavior.Stop });
        ResetDetails.BeginAnimation(OpacityProperty, new DoubleAnimation(details, 0, OperatorMotion.Press)
            { FillBehavior = FillBehavior.Stop });
        ResetActions.BeginAnimation(OpacityProperty, new DoubleAnimation(actions, 0, OperatorMotion.Press)
            { FillBehavior = FillBehavior.Stop });
        _resetArrowTurn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(arrow, -18 * OperatorMotion.TravelScale, OperatorMotion.SurfaceClose)
            { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop });
        var fade = new DoubleAnimation(backdrop, 0, OperatorMotion.SurfaceClose)
            { EasingFunction = OperatorMotion.SmoothOut, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => CompleteResetVisual(generation);
        ResetBackdrop.BeginAnimation(OpacityProperty, fade);
        // The receipt and input were already released. This bounded fallback
        // only removes an occluded compositor's remaining visual, never focus.
        _resetExitDeadline = new DispatcherTimer { Interval = OperatorMotion.SurfaceClose + TimeSpan.FromMilliseconds(150) };
        _resetExitDeadline.Tick += (_, _) => CompleteResetVisual(generation);
        _resetExitDeadline.Start();
    }

    private void ResetAppearanceChanged()
    {
        if (OperatorMotion.Enabled) return;
        if (_resetClosing) CompleteResetVisual(_resetGeneration);
        else StopResetMotion();
    }

    private void StopResetMotion()
    {
        _resetExitDeadline?.Stop();
        _resetExitDeadline = null;
        _resetSurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _resetSurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _resetSurfaceScale.ScaleX = _resetSurfaceScale.ScaleY = 1;
        _resetSurfaceTravel.BeginAnimation(TranslateTransform.XProperty, null);
        _resetSurfaceTravel.BeginAnimation(TranslateTransform.YProperty, null);
        _resetSurfaceTravel.X = _resetSurfaceTravel.Y = 0;
        _resetArrowTurn.BeginAnimation(RotateTransform.AngleProperty, null);
        _resetArrowTurn.Angle = 0;
        foreach (var element in new Control[] { ResetBackdrop, ResetHeader, ResetDetails, ResetActions, ResetWarningMark })
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1;
        }
        ResetOverlay.BeginAnimation(OpacityProperty, null);
        ResetOverlay.Opacity = 1;
    }

    private void CompleteResetVisual(int generation)
    {
        if (generation != _resetGeneration) return;
        _resetClosing = false;
        OperatorAppearance.Changed -= ResetAppearanceChanged;
        StopResetMotion();
        _resetSource = null;
        _resetReturnFocus = null;
        ResetOverlay.IsHitTestVisible = false;
        ResetOverlay.IsVisible = false;
    }
}
