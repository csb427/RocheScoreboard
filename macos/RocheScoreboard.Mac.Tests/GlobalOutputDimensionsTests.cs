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

public sealed class GlobalOutputDimensionsTests
{
    private static async Task Settle(int milliseconds = 80)
    {
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button Action(GlobalSettingsScreen view, string caption)
        => Assert.Single(view.FindControl<StackPanel>("OutputDimensionsFieldPanel")!
            .GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, caption));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static TextBlock Error(GlobalSettingsScreen view)
    {
        var error = view.FindControl<TextBlock>("OutputDimensionsValidationText");
        Assert.NotNull(error);
        Assert.True(error.IsEffectivelyVisible, "Rejected dimensions must have visible field-related feedback.");
        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        return error;
    }

    private static void AssertInside(Window window, Control control)
    {
        var position = control.TranslatePoint(default, window);
        Assert.NotNull(position);
        Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(position.Value, control.Bounds.Size)),
            $"{control.Name} must be fully reachable, at {position} with size {control.Bounds.Size}.");
    }

    // A view emits the only request that MainWindow uses to resize output and
    // queue its preference save. No real MainWindow or operator data is mutated
    // by these tests; unchanged stored defaults are checked separately.
    [AvaloniaTheory]
    [InlineData("", "193", "width")]
    [InlineData("386", "", "height")]
    [InlineData("-", "193", "width")]
    [InlineData("386", "-", "height")]
    [InlineData("12ab", "193", "width")]
    [InlineData("386", "1.5", "height")]
    [InlineData(" ", "193", "width")]
    [InlineData("386", "NaN", "height")]
    [InlineData("49", "193", "width")]
    [InlineData("7681", "193", "width")]
    [InlineData("386", "49", "height")]
    [InlineData("386", "4321", "height")]
    [InlineData("-1", "193", "width")]
    [InlineData("386", "0", "height")]
    public async Task InvalidDraftsDoNotResizeOrRequestPreferenceSaves(string width, string height, string invalidField)
    {
        var view = new GlobalSettingsScreen();
        var window = new NativeWindow { Width = 800, Height = 550, Content = view };
        var requests = new List<(int Width, int Height)>();
        (int Width, int Height) applied = (640, 320);
        view.OutputDimensionsChanged += (w, h) => { requests.Add((w, h)); applied = (w, h); };
        window.Show();
        try
        {
            await Settle();
            view.SetOutputPreferences("clean", 640, 320, false, false, false);
            string savedDefaults = JsonSerializer.Serialize(OperatorWorkflowPreferences.Load());
            var widthBox = view.FindControl<TextBox>("OutputWidthBox")!;
            var heightBox = view.FindControl<TextBox>("OutputHeightBox")!;
            widthBox.Text = width;
            heightBox.Text = height;
            Assert.Empty(requests); // Editing is a draft, never a live resize.
            var apply = Action(view, "Apply");
            apply.Focus();
            Click(apply);
            await Settle();

            Assert.Empty(requests);
            Assert.Equal((640, 320), applied);
            Assert.Equal(savedDefaults, JsonSerializer.Serialize(OperatorWorkflowPreferences.Load()));
            Assert.Equal(width, widthBox.Text);
            Assert.Equal(height, heightBox.Text);
            string message = Error(view).Text!.ToLowerInvariant();
            Assert.Contains(invalidField, message);
            Assert.Contains("50", message);
            Assert.Contains(invalidField == "width" ? "7680" : "4320", message);
            Assert.True((invalidField == "width" ? widthBox : heightBox).IsFocused,
                "The first invalid dimension should receive focus for correction.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(386, 193)]
    [InlineData(640, 320)]
    [InlineData(772, 386)]
    [InlineData(1920, 1080)]
    [InlineData(50, 50)]
    [InlineData(7680, 4320)]
    [InlineData(7680, 50)]
    [InlineData(50, 4320)]
    public async Task ExistingSupportedSizesCommitExactlyOnceOnlyOnApply(int width, int height)
    {
        var view = new GlobalSettingsScreen();
        var window = new NativeWindow { Width = 800, Height = 550, Content = view };
        var requests = new List<(int Width, int Height)>();
        view.OutputDimensionsChanged += (w, h) => requests.Add((w, h));
        window.Show();
        try
        {
            await Settle();
            view.FindControl<TextBox>("OutputWidthBox")!.Text = width.ToString();
            view.FindControl<TextBox>("OutputHeightBox")!.Text = height.ToString();
            await Settle();
            Assert.Empty(requests);
            Click(Action(view, "Apply"));
            Assert.Equal((width, height), Assert.Single(requests));
            Assert.Equal(width.ToString(), view.FindControl<TextBox>("OutputWidthBox")!.Text);
            Assert.Equal(height.ToString(), view.FindControl<TextBox>("OutputHeightBox")!.Text);
            Assert.False(view.FindControl<TextBlock>("OutputDimensionsValidationText")?.IsVisible ?? false);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(800, 550, false)]
    [InlineData(420, 320, true)]
    public async Task BulkInvalidTextGetsReachableFeedbackAndKeyboardCorrectionCommits(int width, int height, bool reduced)
    {
        bool? previousMotion = OperatorMotion.AnimationOverride;
        OperatorMotion.AnimationOverride = !reduced;
        var view = new GlobalSettingsScreen();
        var window = new NativeWindow { Width = width, Height = height, Content = view };
        var requests = new List<(int Width, int Height)>();
        view.OutputDimensionsChanged += (w, h) => requests.Add((w, h));
        window.Show();
        try
        {
            await Settle(180);
            var widthBox = view.FindControl<TextBox>("OutputWidthBox")!;
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            widthBox.Focus(NavigationMethod.Tab);
            widthBox.BringIntoView();
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            widthBox.SelectAll();
            window.KeyTextInput("12ab"); // One inserted buffer, as with pasted text.
            Assert.Equal("12ab", widthBox.Text);
            Assert.Empty(requests);

            var apply = Action(view, "Apply");
            apply.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Settle();
            Assert.Empty(requests);
            var error = Error(view);
            Assert.True(widthBox.IsFocused);
            Assert.True(error.RenderTransform is null || error.RenderTransform.Value == Matrix.Identity,
                "Validation feedback must stay still, including with reduced motion.");

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            error.BringIntoView();
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await Settle();
            AssertInside(window, error);
            Assert.True(Action(view, "Reset dimensions").IsEnabled);

            widthBox.Focus(NavigationMethod.Tab);
            widthBox.SelectAll();
            window.KeyTextInput("640");
            view.FindControl<TextBox>("OutputHeightBox")!.Text = "320";
            apply.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Settle();
            Assert.Equal((640, 320), Assert.Single(requests));
            Assert.False(error.IsVisible);
        }
        finally { window.Close(); OperatorMotion.AnimationOverride = previousMotion; }
    }

    [AvaloniaFact]
    public async Task ARejectedFollowupCannotReplaceTheLastAcceptedDimensions()
    {
        var view = new GlobalSettingsScreen();
        var window = new NativeWindow { Width = 800, Height = 550, Content = view };
        var requests = new List<(int Width, int Height)>();
        view.OutputDimensionsChanged += (w, h) => requests.Add((w, h));
        window.Show();
        try
        {
            await Settle();
            view.FindControl<TextBox>("OutputWidthBox")!.Text = "1920";
            view.FindControl<TextBox>("OutputHeightBox")!.Text = "1080";
            Click(Action(view, "Apply"));
            Assert.Equal((1920, 1080), Assert.Single(requests));
            view.FindControl<TextBox>("OutputHeightBox")!.Text = "4321";
            Click(Action(view, "Apply"));
            Assert.Equal((1920, 1080), Assert.Single(requests));
            Assert.Contains("height", Error(view).Text!.ToLowerInvariant());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PreviewAndResetRemainImmediateFixedSizesAndClearOldValidation()
    {
        var view = new GlobalSettingsScreen();
        var window = new NativeWindow { Width = 800, Height = 550, Content = view };
        var requests = new List<(int Width, int Height)>();
        view.OutputDimensionsChanged += (w, h) => requests.Add((w, h));
        window.Show();
        try
        {
            await Settle();
            view.FindControl<TextBox>("OutputWidthBox")!.Text = "bad";
            Click(Action(view, "Apply"));
            var error = Error(view);
            Assert.Empty(requests);
            Click(Action(view, "2× preview"));
            Assert.Equal((772, 386), Assert.Single(requests));
            Assert.Equal("772", view.FindControl<TextBox>("OutputWidthBox")!.Text);
            Assert.Equal("386", view.FindControl<TextBox>("OutputHeightBox")!.Text);
            Assert.False(error.IsVisible);

            view.FindControl<TextBox>("OutputHeightBox")!.Text = "bad";
            Click(Action(view, "Apply"));
            Assert.True(error.IsVisible);
            Click(Action(view, "Reset dimensions"));
            Assert.Equal(new[] { (772, 386), (386, 193) }, requests);
            Assert.Equal("386", view.FindControl<TextBox>("OutputWidthBox")!.Text);
            Assert.Equal("193", view.FindControl<TextBox>("OutputHeightBox")!.Text);
            Assert.False(error.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ProgrammaticRefreshKeepsExistingBoundsAndFocusedDraftProtectionWithoutCommitting()
    {
        var view = new GlobalSettingsScreen();
        var window = new NativeWindow { Width = 800, Height = 550, Content = view };
        var requests = new List<(int Width, int Height)>();
        view.OutputDimensionsChanged += (w, h) => requests.Add((w, h));
        window.Show();
        try
        {
            await Settle();
            view.SetOutputPreferences("clean", 49, 4321, false, false, false);
            var widthBox = view.FindControl<TextBox>("OutputWidthBox")!;
            var heightBox = view.FindControl<TextBox>("OutputHeightBox")!;
            Assert.Equal("50", widthBox.Text);
            Assert.Equal("4320", heightBox.Text);
            widthBox.Focus(NavigationMethod.Tab);
            widthBox.Text = "12ab";
            view.SetOutputDimensions(640, 320);
            Assert.Equal("12ab", widthBox.Text);
            Assert.Equal("320", heightBox.Text);
            heightBox.Focus(NavigationMethod.Tab);
            heightBox.Text = "-";
            view.SetOutputDimensions(7681, 193);
            Assert.Equal("7680", widthBox.Text);
            Assert.Equal("-", heightBox.Text);
            Assert.Empty(requests);
        }
        finally { window.Close(); }
    }
}
