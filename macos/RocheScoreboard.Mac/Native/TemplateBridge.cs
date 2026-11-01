using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
namespace RocheScoreboard.Mac.Native;
public sealed class NativeElementFactory(Type type)
{
    private readonly List<Action<Control>> _values=[];
    public void SetValue(AvaloniaProperty property,object? value)=>_values.Add(c=>c.SetValue(property,value));
    public void SetBinding(AvaloniaProperty property,Binding binding)=>_values.Add(c=>c.Bind(property,binding));
    public Control Build(object? value)
    {var c=(Control)Activator.CreateInstance(type)!;c.DataContext=value;foreach(var set in _values)set(c);return c;}
}
public sealed class NativeDataTemplate : IDataTemplate
{
    public NativeElementFactory? VisualTree{get;set;}
    public bool Match(object? data)=>true;
    public Control? Build(object? data)=>VisualTree?.Build(data);
}
public class NativeSearchTextBox : TextBox
{
    protected override Type StyleKeyOverride => GetType() == typeof(NativeSearchTextBox) ? typeof(Avalonia.Controls.TextBox) : base.StyleKeyOverride;
    public object? ToolTip{get=>Avalonia.Controls.ToolTip.GetTip(this);set=>Avalonia.Controls.ToolTip.SetTip(this,value);}
    private INameScope? _scope;
    public NativeSearchTextBox(){TextChanged+=(_,e)=>OnNativeTextChanged(e);GotFocus+=(_,e)=>OnNativeGotFocus(e);}
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e){base.OnApplyTemplate(e);_scope=e.NameScope;OnNativeApplyTemplate();}
    public virtual void OnNativeApplyTemplate(){}
    protected object? GetTemplateChild(string name)=>_scope?.Find(name);
    protected virtual void OnNativeTextChanged(TextChangedEventArgs args){}
    protected virtual void OnNativeGotFocus(GotFocusEventArgs args){}
    protected override void OnKeyDown(KeyEventArgs e)=>OnNativePreviewKeyDown(e);
    protected virtual void OnNativePreviewKeyDown(KeyEventArgs args)=>base.OnKeyDown(args);
}
public sealed class NativeTextBlock:TextBlock
{
    public object? ToolTip{get=>Avalonia.Controls.ToolTip.GetTip(this);set=>Avalonia.Controls.ToolTip.SetTip(this,value);}
}
public static class NativeMenuTemplate
{
    public static IControlTemplate Create()=>new FuncControlTemplate<Button>((button,scope)=>
    {
        var presenter=new ContentPresenter{HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,VerticalContentAlignment=Avalonia.Layout.VerticalAlignment.Center};
        presenter.Bind(ContentPresenter.ContentProperty,new Binding(nameof(Button.Content)){Source=button});
        presenter.Bind(ContentPresenter.ContentTemplateProperty,new Binding(nameof(Button.ContentTemplate)){Source=button});
        presenter.Bind(ContentPresenter.PaddingProperty,new Binding(nameof(Button.Padding)){Source=button});
        var surface=new Border{CornerRadius=new CornerRadius(10),Child=presenter};
        surface.Bind(Border.BackgroundProperty,new Binding(nameof(Button.Background)){Source=button});
        surface.Bind(Border.BorderBrushProperty,new Binding(nameof(Button.BorderBrush)){Source=button});
        surface.Bind(Border.BorderThicknessProperty,new Binding(nameof(Button.BorderThickness)){Source=button});
        return surface;
    });
}
