using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RocheScoreboard.Mac.Native;

public static class NativeDesktop
{
    public static Window? MainWindow => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    public static Rect WorkArea
    {
        get
        {
            Window? main = MainWindow;
            var screen = main?.Screens.ScreenFromWindow(main);
            return screen is null ? new Rect(0, 0, 1440, 900) : screen.WorkingArea.ToRect(main!.RenderScaling);
        }
    }
}
internal static class NativeAwait
{
    public static T Run<T>(Task<T> task)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
}
public class NativeWindow : Window
{
    private Rect? _restoreBounds;
    public Rect RestoreBounds => _restoreBounds ?? new Rect(Left, Top, Bounds.Width, Bounds.Height);
    public double Left { get => Position.X / RenderScaling; set => Position = new PixelPoint((int)Math.Round(value * RenderScaling), Position.Y); }
    public double Top { get => Position.Y / RenderScaling; set => Position = new PixelPoint(Position.X, (int)Math.Round(value * RenderScaling)); }
    public Point PointFromScreen(Point point) => this.PointToClient(new PixelPoint((int)point.X, (int)point.Y));
    public object? FindName(string name) => NativeLookup.Find(this, name);
    public NativeWindow()
    {
        PositionChanged += (_, _) => Remember(); SizeChanged += (_, _) => Remember();
        void Remember() { if (WindowState == WindowState.Normal) _restoreBounds = new Rect(Left, Top, Bounds.Width, Bounds.Height); }
    }
    private Window? _dialogOwner;
    public new Window? Owner { get => _dialogOwner ?? base.Owner as Window; set => _dialogOwner = value; }
    private bool? _dialogResult;
    public bool? DialogResult { get => _dialogResult; set { _dialogResult = value; Close(value); } }
    public bool? ShowDialog() => NativeAwait.Run(base.ShowDialog<bool?>(Owner ?? NativeDesktop.MainWindow ?? throw new InvalidOperationException("A dialog requires an open operator window.")));
    public static Window? GetWindow(Control control) => TopLevel.GetTopLevel(control) as Window;
}
public class NativeOpenFileDialog
{
    public string Title { get; set; } = "Open file";
    public string Filter { get; set; } = "All files|*.*";
    public string FileName { get; set; } = "";
    public string[] FileNames { get; private set; } = Array.Empty<string>();
    public bool Multiselect { get; set; }
    public bool CheckFileExists { get; set; } = true;
    public int FilterIndex { get; set; } = 1;
    public bool DereferenceLinks { get; set; } = true;
    public string InitialDirectory { get; set; } = "";
    internal static List<FilePickerFileType> Types(string filter)
    {
        var parts = filter.Split('|'); var types = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
            types.Add(new(parts[i]) { Patterns = parts[i + 1].Split(';').Select(p => p == "*.*" ? "*" : p).ToArray() });
        return types;
    }
    public bool? ShowDialog(Window? owner = null) => NativeAwait.Run(ShowAsync(owner));
    private async Task<bool?> ShowAsync(Window? owner)
    {
        owner ??= NativeDesktop.MainWindow;
        var storage = owner?.StorageProvider ?? throw new InvalidOperationException("File picker requires an operator window.");
        var files = await storage.OpenFilePickerAsync(new()
        {
            Title = Title, AllowMultiple = Multiselect, FileTypeFilter = Types(Filter),
            SuggestedStartLocation = string.IsNullOrEmpty(InitialDirectory) ? null : await storage.TryGetFolderFromPathAsync(InitialDirectory)
        });
        FileNames = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        foreach (var file in files) file.Dispose();
        FileName = FileNames.FirstOrDefault() ?? "";
        return FileNames.Length > 0;
    }
}
public sealed class NativeSaveFileDialog
{
    public string Title { get; set; } = "Save file";
    public string Filter { get; set; } = "All files|*.*";
    public string FileName { get; set; } = "";
    public string DefaultExt { get; set; } = "";
    public bool AddExtension { get; set; } = true;
    public bool OverwritePrompt { get; set; } = true;
    public bool? ShowDialog(Window? owner = null) => NativeAwait.Run(ShowAsync(owner));
    private async Task<bool?> ShowAsync(Window? owner)
    {
        var storage = (owner ?? NativeDesktop.MainWindow)?.StorageProvider ?? throw new InvalidOperationException("File picker requires an operator window.");
        using var file = await storage.SaveFilePickerAsync(new() { Title = Title, SuggestedFileName = FileName, FileTypeChoices = NativeOpenFileDialog.Types(Filter), DefaultExtension = DefaultExt.TrimStart('.'), ShowOverwritePrompt = OverwritePrompt });
        if (file?.TryGetLocalPath() is not string path) return false;
        FileName = path; return true;
    }
}
public enum NativeMessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }
public enum NativeMessageBoxImage { None, Information, Warning, Error, Question }
public enum NativeMessageBoxResult { None, OK, Cancel, Yes, No }
public static class NativeMessageBox
{
    public static NativeMessageBoxResult Show(string text, string caption = "Roche Scoreboard", NativeMessageBoxButton buttons = NativeMessageBoxButton.OK, NativeMessageBoxImage image = NativeMessageBoxImage.None)
        => Show(NativeDesktop.MainWindow, text, caption, buttons, image);
    public static NativeMessageBoxResult Show(Window? owner, string text, string caption = "Roche Scoreboard", NativeMessageBoxButton buttons = NativeMessageBoxButton.OK, NativeMessageBoxImage image = NativeMessageBoxImage.None, NativeMessageBoxResult defaultResult = NativeMessageBoxResult.None)
    {
        var dialog = new Window { Title = caption, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#161B22")) };
        var layout = new StackPanel { Margin = new Thickness(24), Spacing = 24 };
        layout.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var choices = buttons switch
        {
            NativeMessageBoxButton.YesNo => new[] { NativeMessageBoxResult.Yes, NativeMessageBoxResult.No },
            NativeMessageBoxButton.YesNoCancel => new[] { NativeMessageBoxResult.Yes, NativeMessageBoxResult.No, NativeMessageBoxResult.Cancel },
            NativeMessageBoxButton.OKCancel => new[] { NativeMessageBoxResult.OK, NativeMessageBoxResult.Cancel },
            _ => new[] { NativeMessageBoxResult.OK }
        };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.ToString(), MinWidth = 72, IsDefault = choice == (defaultResult == NativeMessageBoxResult.None ? choices[0] : defaultResult), IsCancel = choice == NativeMessageBoxResult.Cancel };
            button.Click += (_, _) => dialog.Close(choice); actions.Children.Add(button);
        }
        layout.Children.Add(actions); dialog.Content = layout;
        return NativeAwait.Run(dialog.ShowDialog<NativeMessageBoxResult>(owner ?? throw new InvalidOperationException("A message dialog requires an open window.")));
    }
}
