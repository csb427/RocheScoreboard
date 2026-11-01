using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class GalleryDiagnosticContractTests
{
    private static void Record(string name, object value)
    {
        string? directory = Environment.GetEnvironmentVariable("ROCHE_GALLERY_EVIDENCE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class PreferenceScope : IDisposable
    {
        private readonly App app = (App)Application.Current!;
        private readonly bool selfTest;
        private readonly AppTheme theme;
        private readonly UiDensity density;
        private readonly AppTheme preferenceTheme;
        private readonly UiDensity preferenceDensity;
        private readonly string path;
        private readonly byte[]? bytes;
        internal PreferenceScope()
        {
            SelfTestStorage.Initialize(true);
            selfTest = app.IsSelfTestMode;
            app.IsSelfTestMode = true;
            theme = app.ThemeService.CurrentTheme;
            density = app.ThemeService.CurrentDensity;
            preferenceTheme = app.UxPreferences.Theme;
            preferenceDensity = app.UxPreferences.Density;
            path = UxPreferences.GetStoragePath();
            bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        public void Dispose()
        {
            app.ThemeService.SetTheme(theme);
            app.ThemeService.SetDensity(density);
            app.UxPreferences.Theme = preferenceTheme;
            app.UxPreferences.Density = preferenceDensity;
            app.IsSelfTestMode = selfTest;
            if (bytes is null) File.Delete(path); else File.WriteAllBytes(path, bytes);
            Assert.Equal(bytes, File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
    }

    private static DesignSystemGalleryWindow ShowGallery()
    {
        var gallery = new DesignSystemGalleryWindow();
        gallery.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(gallery.IsVisible);
        Assert.True(gallery.Bounds.Width > 0);
        return gallery;
    }

    [AvaloniaFact]
    public void ReportKeepsSchemaAndLeavesUnobservedKeyboardAndCloseUnverified()
    {
        using var preferences = new PreferenceScope();
        var gallery = ShowGallery();
        int closing = 0, closed = 0;
        gallery.Closing += (_, _) => closing++;
        gallery.Closed += (_, _) => closed++;
        try
        {
            var report = gallery.RunSelfTest();
            Record(nameof(ReportKeepsSchemaAndLeavesUnobservedKeyboardAndCloseUnverified), new { report, closing, closed, gallery.IsVisible });
            Assert.Equal(0, closing);
            Assert.Equal(0, closed);
            Assert.True(gallery.IsVisible);
            Assert.Equal("unverified", report.Keyboard);
            Assert.Equal("unverified", report.CloseBehavior);
            Assert.Contains("keyboard=unverified", gallery.FindControl<TextBlock>("SelfTestStatusText")!.Text);
            Assert.Contains("close=unverified", gallery.FindControl<TextBlock>("SelfTestStatusText")!.Text);
            Assert.Contains("Tab/Shift+Tab", report.Notes);
            Assert.Contains("Closing/Closed", report.Notes);
            Assert.Contains("programmatic", report.Notes);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
            string[] expected = ["Theme", "Density", "FocusBrush", "ControlBorderBrush", "WindowBackground", "TextPrimary", "ButtonHeight", "TextBoxHeight", "ComboBoxHeight", "ControlPadding", "ThemeAndVisuals", "DensityAndLayout", "Keyboard", "Tooltip", "DisabledStates", "CloseBehavior", "Notes"];
            Assert.Equal(expected, json.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.All(json.RootElement.EnumerateObject().Where(property => !property.Name.EndsWith("Height")), property => Assert.Equal(JsonValueKind.String, property.Value.ValueKind));
            Assert.NotEqual("unavailable", report.FocusBrush);
            Assert.True(report.ButtonHeight > 0);
        }
        finally { gallery.Close(); }
        Assert.Equal(1, closing);
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public void ConsumedRealTabCannotBecomeAReportedKeyboardPass()
    {
        using var preferences = new PreferenceScope();
        var gallery = ShowGallery();
        int consumed = 0;
        gallery.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Tab) { consumed++; args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        try
        {
            var start = gallery.FindControl<Button>("BtnToggleTheme")!;
            Assert.True(start.Focus());
            gallery.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            gallery.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, consumed);
            Assert.Same(start, gallery.FocusManager!.GetFocusedElement());
            var report = gallery.RunSelfTest();
            Record(nameof(ConsumedRealTabCannotBecomeAReportedKeyboardPass), new { consumed, focusBeforeReport = start.Name, report });
            Assert.Equal("unverified", report.Keyboard);
        }
        finally { gallery.Close(); }
    }

    [AvaloniaFact]
    public void CancelledPublicCloseCannotBecomeAReportedClosePass()
    {
        using var preferences = new PreferenceScope();
        var gallery = ShowGallery();
        int attempts = 0, closed = 0;
        bool cancel = true;
        gallery.Closing += (_, args) => { attempts++; args.Cancel = cancel; };
        gallery.Closed += (_, _) => closed++;
        try
        {
            gallery.Close();
            Assert.Equal(1, attempts);
            Assert.Equal(0, closed);
            Assert.True(gallery.IsVisible);
            var report = gallery.RunSelfTest();
            Record(nameof(CancelledPublicCloseCannotBecomeAReportedClosePass), new { attempts, closed, gallery.IsVisible, report });
            Assert.Equal(1, attempts);
            Assert.Equal("unverified", report.CloseBehavior);
        }
        finally { cancel = false; gallery.Close(); }
        Assert.Equal(2, attempts);
        Assert.Equal(1, closed);
        Assert.False(gallery.IsVisible);
    }

    [AvaloniaFact]
    public void ExplicitFeedbackPolicyProbeLeavesGallerySpecimensOutsideOperatorChoreography()
    {
        using var preferences = new PreferenceScope();
        var gallery = ShowGallery();
        try
        {
            bool admitted = OperatorFeedback.IsOperatorWindow(gallery);
            OperatorFeedback.Enable(gallery); // Policy probe; native gallery startup does not call this.
            Dispatcher.UIThread.RunJobs();
            var scopes = typeof(OperatorFeedback).GetField("Scopes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var arrivals = typeof(OperatorFeedback).GetField("Arrivals", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            bool Contains(object table) => (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, [gallery, null])!;
            bool scope = Contains(scopes), arrival = Contains(arrivals);
            int backdrops = gallery.GetVisualDescendants().OfType<OperatorSceneBackdrop>().Count();
            Record(nameof(ExplicitFeedbackPolicyProbeLeavesGallerySpecimensOutsideOperatorChoreography), new { admitted, enabled = OperatorDesign.GetIsEnabled(gallery), scope, arrival, backdrops });
            Assert.False(admitted);
            Assert.False(OperatorDesign.GetIsEnabled(gallery));
            Assert.False(scope);
            Assert.False(arrival);
            Assert.Equal(0, backdrops);
            Assert.DoesNotContain(gallery.Resources.MergedDictionaries, dictionary => dictionary is Avalonia.Markup.Xaml.Styling.ResourceInclude include && include.Source?.OriginalString.Contains("ControlCentre") == true);
            Assert.NotNull(gallery.FindControl<Button>("PrimaryButtonSample"));

        }
        finally { gallery.Close(); }
    }

    [AvaloniaFact]
    public void FeedbackPolicyPreservesOrdinaryWindowsAndExistingOutputExclusions()
    {
        using var preferences = new PreferenceScope();
        var namedLikeGallery = new NativeWindow { Title = "DesignSystemGalleryWindow" };
        var output = new NativeWindow { Title = "Public Display" };
        var aflGallery = new AflPresentationGalleryWindow();
        try
        {
            Assert.True(OperatorFeedback.IsOperatorWindow(namedLikeGallery));
            Assert.False(OperatorFeedback.IsOperatorWindow(output));
            Assert.False(OperatorFeedback.IsOperatorWindow(aflGallery));
        }
        finally { namedLikeGallery.Close(); output.Close(); aflGallery.Close(); }
    }

    [AvaloniaFact]
    public void ExistingThemeAndDensityButtonActionsStillChangeAndRestoreSpecimenResources()
    {
        using var preferences = new PreferenceScope();
        var app = (App)Application.Current!;
        var gallery = ShowGallery();
        try
        {
            var theme = app.ThemeService.CurrentTheme;
            var density = app.ThemeService.CurrentDensity;
            string initialThemeText = gallery.FindControl<TextBlock>("ThemeStateText")!.Text!;
            object initialPadding = gallery.FindResource("ScoreGrid.Padding.Control")!;
            void Click(string name)
            {
                var button = gallery.FindControl<Button>(name)!;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), gallery)!.Value;
                gallery.MouseDown(point, MouseButton.Left);
                gallery.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            Click("BtnToggleTheme");
            Assert.NotEqual(theme, app.ThemeService.CurrentTheme);
            Assert.Equal(app.ThemeService.CurrentTheme, app.UxPreferences.Theme);
            Assert.NotEqual(initialThemeText, gallery.FindControl<TextBlock>("ThemeStateText")!.Text);
            Assert.Equal(app.ThemeService.CurrentTheme, UxPreferences.Load().Theme);
            Click("BtnToggleTheme");
            Assert.Equal(theme, app.ThemeService.CurrentTheme);
            Click("BtnToggleDensity");
            Assert.NotEqual(density, app.ThemeService.CurrentDensity);
            Assert.NotEqual(initialPadding, gallery.FindResource("ScoreGrid.Padding.Control"));
            Assert.Equal(app.ThemeService.CurrentDensity, UxPreferences.Load().Density);
            Click("BtnToggleDensity");
            Assert.Equal(density, app.ThemeService.CurrentDensity);
            Assert.Equal(initialPadding, gallery.FindResource("ScoreGrid.Padding.Control"));
            Record(nameof(ExistingThemeAndDensityButtonActionsStillChangeAndRestoreSpecimenResources), new { theme, density, initialThemeText, initialPadding });
        }
        finally { gallery.Close(); }
    }
}
