using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class OperatorAccentPreferenceTests
{
    [AvaloniaFact]
    public void PalettePreviewAppliesAndResetUpdatesTheOperatorSurface()
    {
        SelfTestStorage.Initialize(true);
        var previous=OperatorAppearance.AccentPalette;
        byte[]? saved=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        var settings=new OperatorAccentPreferences();
        var window=new NativeWindow{Width=620,Height=500,Content=settings};
        window.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("ControlCentre"));
        window.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("OperatorControls"));
        try
        {
            window.Show();Dispatcher.UIThread.RunJobs();
            var teal=Assert.Single(settings.GetVisualDescendants().OfType<RadioButton>(),button=>button.Content?.ToString()=="Teal");
            teal.IsChecked=true;Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatorAccentPalette.Teal,OperatorAppearance.AccentPalette);
            Assert.Equal(Color.FromRgb(93,224,199),Application.Current!.Resources["Operator.CustomAccentColor"]);
            using(var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(OperatorAppearance.StoragePath)))
                Assert.Equal((int)OperatorAccentPalette.Teal,json.RootElement.GetProperty("AccentPalette").GetInt32());
            var reset=Assert.Single(settings.GetVisualDescendants().OfType<Button>(),button=>button.Content?.ToString()=="Restore Sky accent");
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatorAccentPalette.Sky,OperatorAppearance.AccentPalette);
            Assert.Equal(Color.FromRgb(123,184,255),Application.Current.Resources["Operator.CustomAccentColor"]);
        }
        finally
        {
            window.Close();OperatorAppearance.ConfigureAccentPalette(previous);
            OperatorAccentPreferences.ApplyAccentResources();
            if(saved is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,saved);
        }
    }

    [Fact]
    public void AccentPaletteSelectionPersistsAndCanBeRestored()
    {
        SelfTestStorage.Initialize(true);
        var previous=OperatorAppearance.AccentPalette;
        byte[]? saved=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        try
        {
            OperatorAppearance.ConfigureAccentPalette(OperatorAccentPalette.Sky);
            OperatorAppearance.ConfigureAccentPalette(OperatorAccentPalette.Violet);
            Assert.Equal(OperatorAccentPalette.Violet,OperatorAppearance.AccentPalette);
            using(var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(OperatorAppearance.StoragePath)))
                Assert.Equal((int)OperatorAccentPalette.Violet,json.RootElement.GetProperty("AccentPalette").GetInt32());
            var load=typeof(OperatorAppearance).GetMethod("Load",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
            var reloaded=load.Invoke(null,null)!;
            Assert.Equal(OperatorAccentPalette.Violet,reloaded.GetType().GetProperty("AccentPalette")!.GetValue(reloaded));
            OperatorAppearance.ConfigureAccentPalette(OperatorAccentPalette.Sky);
            Assert.Equal(OperatorAccentPalette.Sky,OperatorAppearance.AccentPalette);
        }
        finally
        {
            OperatorAppearance.ConfigureAccentPalette(previous);
            if(saved is null)File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath,saved);
        }
    }
}
