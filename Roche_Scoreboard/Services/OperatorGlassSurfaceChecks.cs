using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roche_Scoreboard.Views;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;

namespace Roche_Scoreboard.Services;

/// <summary>Bounded real-WPF checks for live backdrop resource ownership and invalidation.</summary>
internal static class OperatorGlassSurfaceChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync()
    {
        var checks = new Dictionary<string, bool>();
        var observations = new Dictionary<string, object?>();
        if (!SelfTestStorage.IsEnabled)
        {
            checks["glass-resource-qa-requires-process-isolated-storage"] = false;
            return checks;
        }

        bool oldGlass = OperatorAppearance.GlassEnabled, oldReduction = OperatorAppearance.ReduceMotion;
        var oldIntensity = OperatorAppearance.GlassIntensity;
        var oldBackdrop = OperatorAppearance.Backdrop;
        string appearancePath = OperatorAppearance.StoragePath;
        var files = new[] { "", ".bak", ".tmp" }.ToDictionary(suffix => appearancePath + suffix,
            path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var backdrop = new Grid { IsHitTestVisible = false, Background = System.Windows.Media.Brushes.SteelBlue };
        var other = new Border { Width = 100, Height = 20, HorizontalAlignment = HorizontalAlignment.Right };
        var surface = new OperatorGlassSurface
        {
            Width = 140, Height = 80, Margin = new Thickness(20, 30, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Material = GlassMaterial.Regular, GlassSource = backdrop,
            Background = new SolidColorBrush(Color.FromArgb(100, 20, 22, 28))
        };
        var root = new Grid(); root.Children.Add(backdrop);
        // Hide the source behind an opaque sibling. A changed captured pixel must
        // therefore come through the glass sample, not the background behind it.
        root.Children.Add(new Border { Background = System.Windows.Media.Brushes.Black, IsHitTestVisible = false });
        root.Children.Add(other); root.Children.Add(surface);
        // OperatorDesign installs inherited backdrops for operator windows or
        // UserControl roots, deliberately not for an arbitrary System.Window.
        var host = new UserControl { Content = root };
        var window = new Window { Title = "Live glass resource checks", Width = 500, Height = 360, ShowInTaskbar = false, Content = host };
        async Task Pump()
        {
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        }
        static T? Field<T>(OperatorGlassSurface item, string name) where T : class =>
            typeof(OperatorGlassSurface).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item) as T;
        VisualBrush? Live() => Field<VisualBrush>(surface, "_liveBrush");
        Brush? Sample() => Field<Brush>(surface, "_sample");
        Border? Frost() => Field<Border>(surface, "_frostVisual");
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .05;
        bool Bounds(double x, double y, double width, double height) => Live() is { } brush
            && Near(brush.Viewbox.X, x) && Near(brush.Viewbox.Y, y)
            && Near(brush.Viewbox.Width, width) && Near(brush.Viewbox.Height, height);
        bool Released(VisualBrush? live, VisualBrush? sample, Border? frost) => Live() is null
            && Sample() is not VisualBrush && Frost() is null
            && (live is null || live.Visual is null) && (sample is null || sample.Visual is null)
            && (frost is null || (frost.Background is null && frost.Effect is null && frost.CacheMode is null));
        (byte Blue, byte Red, byte Alpha) SamplePixel()
        {
            // An already arranged element retains its layout offset when rendered
            // alone. Capture its root and address the actual center in root space.
            int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var center = surface.TranslatePoint(new System.Windows.Point(surface.ActualWidth / 2, surface.ActualHeight / 2), root);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)center.X, (int)center.Y, 1, 1), pixel, 4, 0);
            observations["captureGeometry"] = new { width, height, center.X, center.Y };
            return (pixel[0], pixel[2], pixel[3]);
        }
        try
        {
            OperatorAppearance.Configure(true, true);
            OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Balanced, oldBackdrop);
            OperatorDesign.Apply(host);
            window.Show(); await Pump(); await Pump();
            bool supportsGlass = !SystemParameters.HighContrast;
            // High Contrast cannot be changed by a test. Mark the required live path
            // unverified instead of turning a missing live sample into a passing test.
            checks["glass-live-path-available-for-resource-checks"] = supportsGlass && Live() is not null;
            if (!supportsGlass) return checks;

            var live = Live(); var sample = Sample(); var frost = Frost();
            int builds = surface.LiveSampleBuildCount, updates = surface.LiveSampleUpdateCount;
            checks["glass-initial-sample-maps-sibling-backdrop"] = live is not null
                && ReferenceEquals(live.Visual, backdrop) && Bounds(20, 30, 140, 80) && builds == 1;
            // Reintroducing unconditional SetLiveSample allocation fails both identity
            // and work-count assertions, even if GC happens to keep RSS temporarily flat.
            for (int i = 0; i < 24; i++) { other.Width = 101 + i; await Pump(); }
            checks["glass-unrelated-layout-does-not-rebuild-or-invalidate-sample"] = live is not null
                && ReferenceEquals(live, Live()) && ReferenceEquals(sample, Sample())
                && ReferenceEquals(frost, Frost()) && surface.LiveSampleBuildCount == builds
                && surface.LiveSampleUpdateCount == updates;

            surface.Margin = new Thickness(45, 55, 0, 0); await Pump();
            checks["glass-movement-updates-viewbox-without-rebuilding"] = Bounds(45, 55, 140, 80)
                && ReferenceEquals(live, Live()) && ReferenceEquals(sample, Sample())
                && surface.LiveSampleBuildCount == builds && surface.LiveSampleUpdateCount > updates;
            surface.Width = 180; surface.Height = 95; await Pump();
            checks["glass-resize-updates-retained-sample"] = Bounds(45, 55, 180, 95)
                && ReferenceEquals(live, Live()) && ReferenceEquals(sample, Sample())
                && surface.LiveSampleBuildCount == builds
                && (frost is null || (Near(frost.ActualWidth, 180) && Near(frost.ActualHeight, 95)));

            OperatorAppearance.ConfigureMaterial(OperatorGlassIntensity.Soft, oldBackdrop); await Pump();
            checks["glass-appearance-updates-retained-blur"] = ReferenceEquals(live, Live())
                && ReferenceEquals(sample, Sample()) && surface.LiveSampleBuildCount == builds
                && ((RenderCapability.Tier >> 16) == 0 || Frost()?.Effect is BlurEffect { Radius: 10 });
            updates = surface.LiveSampleUpdateCount;
            var bluePixel = SamplePixel();
            backdrop.Background = System.Windows.Media.Brushes.Coral; await Pump();
            var coralPixel = SamplePixel();
            observations["bluePixel"] = new { bluePixel.Blue, bluePixel.Red, bluePixel.Alpha };
            observations["coralPixel"] = new { coralPixel.Blue, coralPixel.Red, coralPixel.Alpha };
            checks["glass-source-content-remains-live-without-resampling"] = ReferenceEquals(live, Live())
                && ReferenceEquals(Live()?.Visual, backdrop) && ReferenceEquals(sample, Sample())
                && surface.LiveSampleUpdateCount == updates;
            checks["glass-retained-source-content-reaches-rendered-pixels"] = coralPixel.Red > bluePixel.Red + 4
                && coralPixel.Blue + 4 < bluePixel.Blue && bluePixel.Alpha == 255 && coralPixel.Alpha == 255;

            var materialBackground = surface.Background;
            surface.Background = System.Windows.Media.Brushes.Transparent; await Pump();
            checks["glass-transparent-material-releases-sampling"] = Released(live, sample as VisualBrush, frost);
            builds = surface.LiveSampleBuildCount;
            surface.Background = materialBackground; await Pump();
            checks["glass-restored-material-resamples-without-layout-change"] = Bounds(45, 55, 180, 95)
                && ReferenceEquals(Live()?.Visual, backdrop) && surface.LiveSampleBuildCount == builds + 1;

            live = Live(); sample = Sample(); frost = Frost();
            surface.Material = GlassMaterial.Opaque; await Pump();
            checks["glass-opaque-material-releases-unseen-resources"] = Released(live, sample as VisualBrush, frost);
            builds = surface.LiveSampleBuildCount;
            other.Width += 1; await Pump();
            checks["glass-opaque-material-does-not-sample-layout"] = Live() is null && surface.LiveSampleBuildCount == builds;
            surface.Material = GlassMaterial.Regular; await Pump();
            checks["glass-regular-material-restores-explicit-source"] = Bounds(45, 55, 180, 95)
                && ReferenceEquals(Live()?.Visual, backdrop) && surface.LiveSampleBuildCount == builds + 1;

            live = Live(); sample = Sample(); frost = Frost();
            OperatorAppearance.Configure(false, true); await Pump();
            checks["glass-disabled-preference-releases-sampling"] = Released(live, sample as VisualBrush, frost);
            OperatorAppearance.Configure(true, true); await Pump();
            checks["glass-enabled-preference-restores-explicit-source"] = Bounds(45, 55, 180, 95)
                && ReferenceEquals(Live()?.Visual, backdrop);

            live = Live(); sample = Sample(); frost = Frost();
            surface.GlassSource = null; await Pump();
            var inheritedScene = OperatorDesign.GetBackdrop(surface);
            observations["inheritedBackdrop"] = new
            {
                installed = inheritedScene is not null,
                sourceMatches = ReferenceEquals(Live()?.Visual, inheritedScene),
                viewbox = Live()?.Viewbox.ToString(),
                surface.LiveSampleBuildCount, surface.LiveSampleUpdateCount
            };
            checks["glass-source-change-detaches-old-graph"] = live?.Visual is null
                && (sample is not VisualBrush oldSample || oldSample.Visual is null)
                && (frost is null || frost.Background is null);
            checks["glass-inherited-backdrop-is-used"] = inheritedScene is not null
                && ReferenceEquals(Live()?.Visual, inheritedScene) && Bounds(45, 55, 180, 95);
            surface.Material = GlassMaterial.Opaque; await Pump();
            surface.Material = GlassMaterial.Regular; await Pump();
            checks["glass-inherited-source-restores-after-opaque-without-layout-change"] = ReferenceEquals(Live()?.Visual, inheritedScene)
                && Bounds(45, 55, 180, 95);
            surface.Margin = new Thickness(60, 65, 0, 0); await Pump();
            checks["glass-inherited-source-resumes-observing-after-opaque"] = ReferenceEquals(Live()?.Visual, inheritedScene)
                && Bounds(60, 65, 180, 95);
            OperatorAppearance.Configure(false, true); await Pump();
            OperatorAppearance.Configure(true, true); await Pump();
            checks["glass-inherited-source-restores-after-disabled-without-layout-change"] = ReferenceEquals(Live()?.Visual, inheritedScene)
                && Bounds(60, 65, 180, 95);
            surface.Margin = new Thickness(70, 75, 0, 0); await Pump();
            checks["glass-inherited-source-resumes-observing-after-disabled"] = ReferenceEquals(Live()?.Visual, inheritedScene)
                && Bounds(70, 75, 180, 95);

            live = Live(); sample = Sample(); frost = Frost();
            surface.Visibility = Visibility.Collapsed; await Pump();
            checks["glass-hidden-surface-releases-live-graph"] = Released(live, sample as VisualBrush, frost);
            surface.Visibility = Visibility.Visible; await Pump();
            checks["glass-visible-surface-reacquires-current-geometry"] = Bounds(70, 75, 180, 95);
            live = Live(); sample = Sample(); frost = Frost();
            root.Children.Remove(surface); await Pump();
            builds = surface.LiveSampleBuildCount;
            other.Width += 1; await Pump();
            checks["glass-unload-detaches-graph-and-layout-observation"] = !surface.IsLoaded
                && Released(live, sample as VisualBrush, frost) && surface.LiveSampleBuildCount == builds;
            root.Children.Add(surface); await Pump();
            checks["glass-reload-creates-one-current-sample"] = surface.IsLoaded
                && ReferenceEquals(Live()?.Visual, inheritedScene) && Bounds(70, 75, 180, 95)
                && surface.LiveSampleBuildCount == builds + 1;
            return checks;
        }
        finally
        {
            window.Close();
            OperatorAppearance.Configure(oldGlass, oldReduction);
            OperatorAppearance.ConfigureMaterial(oldIntensity, oldBackdrop);
            foreach (var (path, bytes) in files)
                if (bytes is null) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllBytes(path, bytes);
            string observationsPath = SelfTestStorage.PathFor("glass-resource-checks", "observations.json");
            Directory.CreateDirectory(Path.GetDirectoryName(observationsPath)!);
            File.WriteAllText(observationsPath,
                System.Text.Json.JsonSerializer.Serialize(observations, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
