using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using System.Windows.Input;
namespace RocheScoreboard.Mac.Native;
public static class NativeRangeCommands
{
    private sealed class Step(double direction):ICommand
    {
        public event EventHandler? CanExecuteChanged{add{}remove{}}
        public bool CanExecute(object? parameter)=>parameter is RangeBase range&&range.IsEffectivelyEnabled;
        public void Execute(object? parameter)
        {
            if(parameter is ScrollBar bar)bar.Value=Math.Clamp(bar.Value+direction*bar.LargeChange,bar.Minimum,bar.Maximum);
            else if(parameter is Slider slider)slider.Value=Math.Clamp(slider.Value+direction*slider.LargeChange,slider.Minimum,slider.Maximum);
        }
    }
    public static ICommand PageUpCommand{get;}=new Step(-1);
    public static ICommand PageDownCommand{get;}=new Step(1);
    public static ICommand PageLeftCommand=>PageUpCommand;
    public static ICommand PageRightCommand=>PageDownCommand;
    public static ICommand DecreaseLarge=>PageUpCommand;
    public static ICommand IncreaseLarge=>PageDownCommand;
}
public static class NativeOrientation
{
    public static IValueConverter VerticalReverse{get;}=new FuncValueConverter<Avalonia.Layout.Orientation,bool>(o=>o==Avalonia.Layout.Orientation.Vertical);
}
