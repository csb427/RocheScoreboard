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
    private PixelPoint? _restorePixelPosition;
    public PixelPoint RestorePixelPosition=>_restorePixelPosition??Position;
    private bool _fitting, _fitQueued, _focusRevealQueued;
    private readonly DispatcherTimer _moveFit=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private double _preferredMinWidth, _preferredMinHeight, _preferredMaxWidth=double.PositiveInfinity, _preferredMaxHeight=double.PositiveInfinity;
    public bool IsOutputWindow { get; set; }
    public void FitToWorkArea()
    {
        if(_fitting || WindowState!=WindowState.Normal)return;
        var screen=Screens.ScreenFromWindow(this)??Screens.ScreenFromPoint(Position)??Screens.Primary??Screens.All.FirstOrDefault();
        if(screen is null)return;
        _fitting=true;
        try
        {
            double scale=screen.Scaling;
            var frame=FrameSize;
            var decoration=SystemDecorations==SystemDecorations.None?default:new Size(
                frame is {} f&&Bounds.Width>0?Math.Max(0,f.Width-Bounds.Width):0,
                frame is {} f2&&Bounds.Height>0?Math.Max(28,f2.Height-Bounds.Height):28);
            var placement=NativeWindowPlacement.Fit(Position,new Size(double.IsFinite(Width)?Width:Bounds.Width,double.IsFinite(Height)?Height:Bounds.Height),screen.WorkingArea,scale,decoration);
            double maxWidth=Math.Max(1,screen.WorkingArea.Width/scale-decoration.Width),maxHeight=Math.Max(1,screen.WorkingArea.Height/scale-decoration.Height);
            MinWidth=Math.Min(_preferredMinWidth,maxWidth);MinHeight=Math.Min(_preferredMinHeight,maxHeight);
            MaxWidth=Math.Max(MinWidth,Math.Min(_preferredMaxWidth,maxWidth));MaxHeight=Math.Max(MinHeight,Math.Min(_preferredMaxHeight,maxHeight));
            Width=Math.Clamp(placement.ClientSize.Width,MinWidth,MaxWidth);Height=Math.Clamp(placement.ClientSize.Height,MinHeight,MaxHeight);
            Position=placement.Position;
        }
        finally{_fitting=false;}
    }
    private void ScheduleFit()
    {
        if(_fitting||_fitQueued||!IsVisible)return;_fitQueued=true;
        Dispatcher.UIThread.Post(()=>{_fitQueued=false;if(IsVisible)FitToWorkArea();},DispatcherPriority.Background);
    }
    protected override void OnOpened(EventArgs e)
    {
        IsOutputWindow|=Title?.Contains("Display",StringComparison.OrdinalIgnoreCase)==true;
        _preferredMinWidth=MinWidth;_preferredMinHeight=MinHeight;_preferredMaxWidth=MaxWidth;_preferredMaxHeight=MaxHeight;
        if(!IsOutputWindow && Content is Control body && body is not NativeAccessibleWindowContent)
        {
            Content=null;Content=new NativeAccessibleWindowContent(body,this is Roche_Scoreboard.MainWindow);
        }
        if (_observedScreens is not null) _observedScreens.Changed -= ScreensChanged;
        _observedScreens = Screens;
        _observedScreens.Changed += ScreensChanged;
        FitToWorkArea();
        base.OnOpened(e);
        ScheduleFit();
    }
    public new void Show()
    {
        if(Owner is {} owner&&owner.IsVisible)base.Show(owner);else base.Show();
    }

    public Rect RestoreBounds => _restoreBounds ?? new Rect(Left, Top, Bounds.Width, Bounds.Height);
    public double Left { get => Position.X / RenderScaling; set => Position = new PixelPoint((int)Math.Round(value * RenderScaling), Position.Y); }
    public double Top { get => Position.Y / RenderScaling; set => Position = new PixelPoint(Position.X, (int)Math.Round(value * RenderScaling)); }
    public Point PointFromScreen(Point point) => this.PointToClient(new PixelPoint((int)point.X, (int)point.Y));
    public object? FindName(string name) => NativeLookup.Find(this, name);
    public NativeWindow()
    {
        PositionChanged += (_, _) => {Remember();if(!_fitting&&IsVisible){_moveFit.Stop();_moveFit.Start();}}; SizeChanged += (_, _) => {Remember();ScheduleFit();};
        PropertyChanged+=(_,e)=>
        {
            if(e.Property!=WindowStateProperty)return;
            if(WindowState is WindowState.FullScreen or WindowState.Maximized)
            {MaxWidth=_preferredMaxWidth;MaxHeight=_preferredMaxHeight;}
            else ScheduleFit();
        };
        _moveFit.Tick+=(_,_)=>{_moveFit.Stop();ScheduleFit();};
        ScalingChanged+=(_,_)=>ScheduleFit();
        AddHandler(Avalonia.Input.InputElement.GotFocusEvent,(_,args)=>
        {
            if(args.NavigationMethod is not (Avalonia.Input.NavigationMethod.Tab or Avalonia.Input.NavigationMethod.Directional)||_focusRevealQueued)return;
            _focusRevealQueued=true;
            Dispatcher.UIThread.Post(()=>
            {
                _focusRevealQueued=false;
                if(IsVisible&&FocusManager?.GetFocusedElement() is Control focused)NativeFocusReveal.Reveal(focused);
            },DispatcherPriority.Input);
        },Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
        Deactivated+=(_,_)=>NativeKeyboard.ClearPressed();
        Closed+=(_,_)=>
        {
            _moveFit.Stop();
            // Closed may run before the first Show, or after the backend has
            // been disposed. Unsubscribe the object that was actually observed.
            if (_observedScreens is not null) _observedScreens.Changed -= ScreensChanged;
            _observedScreens = null;
            if(Owner is {} owner&&owner.IsVisible)owner.Activate();
        };
        void Remember() { if (WindowState == WindowState.Normal && Bounds.Width>0 && Bounds.Height>0) {_restoreBounds = new Rect(Left, Top, Bounds.Width, Bounds.Height);_restorePixelPosition=Position;} }
    }
    private void ScreensChanged(object? sender,EventArgs args)=>ScheduleFit();
    private Screens? _observedScreens;
    private Window? _dialogOwner;
    public new Window? Owner { get => _dialogOwner ?? base.Owner as Window; set => _dialogOwner = value; }
    private bool? _dialogResult;
    public bool? DialogResult { get => _dialogResult; set { _dialogResult = value; Close(value); } }
    public bool? ShowDialog() => NativeAwait.Run(base.ShowDialog<bool?>(Owner ?? NativeDesktop.MainWindow ?? throw new InvalidOperationException("A dialog requires an open operator window.")));
    public static Window? GetWindow(AvaloniaObject target)=>target is Control control?GetWindow(control):null;
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
        var dialog = new NativeWindow { Title = caption, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#161B22")) };
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
