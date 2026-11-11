using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    public async Task SegmentedPreferencePointerAndKeyboardCommitOneWritePerSelection()
    {
        SelfTestStorage.Initialize(true);
        var previous=OperatorAppearance.AccentPalette;
        byte[]? saved=File.Exists(OperatorAppearance.StoragePath)?File.ReadAllBytes(OperatorAppearance.StoragePath):null;
        var settings=new OperatorAccentPreferences();
        var window=new NativeWindow{Width=620,Height=500,Content=settings};
        window.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("ControlCentre"));
        window.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("OperatorControls"));
        int writes=0;Action written=()=>writes++;
        try
        {
            OperatorAppearance.ConfigureAccentPalette(OperatorAccentPalette.Sky);
            window.Show();
            await Task.Delay(100);Dispatcher.UIThread.RunJobs();
            var choices=settings.GetVisualDescendants().OfType<RadioButton>().ToArray();
            var teal=Assert.Single(choices,choice=>choice.Content?.ToString()=="Teal");
            var violet=Assert.Single(choices,choice=>choice.Content?.ToString()=="Violet");
            Assert.True(teal.IsEffectivelyVisible&&teal.IsEnabled);
            OperatorAppearance.Changed+=written;

            var center=teal.TranslatePoint(new Point(teal.Bounds.Width/2,teal.Bounds.Height/2),window);
            Assert.NotNull(center);
            var hit=window.InputHitTest(center.Value);
            Assert.True(ReferenceEquals(hit,teal)||hit is Visual child&&child.GetVisualAncestors().Contains(teal),
                "A real pointer probe must reach the visible Teal radio choice.");
            window.MouseMove(center.Value);window.MouseDown(center.Value,MouseButton.Left);window.MouseUp(center.Value,MouseButton.Left);
            await Task.Delay(50);Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatorAccentPalette.Teal,OperatorAppearance.AccentPalette);
            Assert.Equal(1,writes);
            Assert.True(teal.IsFocused,"Pointer selection should leave focus on its radio opener.");

            Assert.True(violet.Focus(NavigationMethod.Tab),"The next native radio choice should accept keyboard focus.");
            window.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);
            await Task.Delay(50);Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatorAccentPalette.Violet,OperatorAppearance.AccentPalette);
            Assert.Equal(2,writes);
            Assert.True(violet.IsFocused,"Arrow navigation should move focus with the selected radio.");
        }
        finally
        {
            OperatorAppearance.Changed-=written;
            window.Close();OperatorAppearance.ConfigureAccentPalette(previous);
            if(saved is null)File.Delete(OperatorAppearance.StoragePath);else File.WriteAllBytes(OperatorAppearance.StoragePath,saved);
        }
    }

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
            var initialAccent=OperatorAccentPreferences.AccentBrushResource;
            teal.IsChecked=true;Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatorAccentPalette.Teal,OperatorAppearance.AccentPalette);
            Assert.Equal(Color.FromRgb(93,224,199),Application.Current!.Resources["Operator.CustomAccentColor"]);
            var tealAccent=OperatorAccentPreferences.AccentBrushResource;
            Assert.NotSame(initialAccent,tealAccent);
            Assert.Equal(Color.FromRgb(93,224,199),tealAccent.Color);
            using(var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(OperatorAppearance.StoragePath)))
                Assert.Equal((int)OperatorAccentPalette.Teal,json.RootElement.GetProperty("AccentPalette").GetInt32());
            var reset=Assert.Single(settings.GetVisualDescendants().OfType<Button>(),button=>button.Content?.ToString()=="Restore Sky accent");
            reset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatorAccentPalette.Sky,OperatorAppearance.AccentPalette);
            Assert.Equal(Color.FromRgb(123,184,255),Application.Current.Resources["Operator.CustomAccentColor"]);
            Assert.NotSame(tealAccent,OperatorAccentPreferences.AccentBrushResource);
            Assert.Equal(Color.FromRgb(123,184,255),OperatorAccentPreferences.AccentBrushResource.Color);
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
