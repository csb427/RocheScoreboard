using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Markup.Xaml.Styling;
namespace RocheScoreboard.Mac.Native;
public static class NativeAssets
{
    public static Uri Resolve(Uri uri)
    {
        string path=uri.OriginalString;
        if(path.StartsWith("avares://",StringComparison.Ordinal))return uri;
        int component=path.IndexOf(";component/",StringComparison.OrdinalIgnoreCase);
        if(component>=0)path=path[(component+11)..];
        path=path.TrimStart('/');
        if(path.StartsWith("Resources/",StringComparison.Ordinal))path="Assets/"+path[10..];
        return new Uri("avares://RocheScoreboard.Mac/"+path);
    }
    public static Stream Open(Uri uri)=>AssetLoader.Open(Resolve(uri));
    public static ResourceInclude Dictionary(string name){var uri=new Uri("avares://RocheScoreboard.Mac/Native/Generated/Resources/"+name+".axaml");return new ResourceInclude(uri){Source=uri};}
    public static void EnsureDictionary(Control target,string name)
    {
        var uri=new Uri("avares://RocheScoreboard.Mac/Native/Generated/Resources/"+name+".axaml");
        if(!target.Resources.MergedDictionaries.OfType<ResourceInclude>().Any(x=>x.Source==uri))
            target.Resources.MergedDictionaries.Add(new ResourceInclude(uri){Source=uri});
    }
}
public sealed class NativeToolTipPolicy:AvaloniaObject
{
    public static T WithTip<T>(T control,object tip)where T:Control{ToolTip.SetTip(control,tip);return control;}
    public static Control? PlacementTarget(ToolTip tip)=>NativeDesktop.MainWindow;
    public static readonly AttachedProperty<bool> EnabledProperty=AvaloniaProperty.RegisterAttached<NativeToolTipPolicy,Control,bool>("Enabled",true);
    static NativeToolTipPolicy()
    {
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>((target,e)=>{if(e.NewValue is true&&!target.GetValue(EnabledProperty))ToolTip.SetIsOpen(target,false);});
        EnabledProperty.Changed.AddClassHandler<Control>((target,e)=>{if(e.NewValue is false)ToolTip.SetIsOpen(target,false);});
    }
}
public static class NativeNameScope
{
    public static void Register(Control root,string name,Control value)
    {
        // Compiled Avalonia name scopes are sealed. Runtime-created controls
        // use the production registry, which is also consulted by FindName.
        RocheScoreboard.Mac.Production.ProductionNames.RegistryFor(root)[name]=value;
    }
}
