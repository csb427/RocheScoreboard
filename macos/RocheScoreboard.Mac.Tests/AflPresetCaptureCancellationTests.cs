// OUTSIDE ONLY. UNCOMPILED/UNRUN. Root integrates after full1342 freeze.
// Make existing ClubPresetStage2GatedTests partial; reuse exclusive collection,
// proven StoreFixture, original PointerTarget diagnostics + real AtomicFile backend.
// No private field/handler invocation; pointer captured from actual raw Down.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class ClubPresetStage2GatedTests
{
    private static byte[]?[] PresetCaptureBytes(StoreFixture fixture)=>
        new[]{fixture.PathFor(Afl),fixture.PathFor(Afl)+".bak"}.Select(path=>File.Exists(path)?File.ReadAllBytes(path):null).ToArray();
    private static void AssertPresetCaptureBytes(byte[]?[] before,StoreFixture fixture)
    {var after=PresetCaptureBytes(fixture);Assert.Equal(before.Length,after.Length);for(int i=0;i<before.Length;i++)Assert.Equal(before[i],after[i]);}
    private static string PresetCaptureDraft(SetupWizard wizard)=>string.Join("|",new[]{"SetupHomeNameBox","SetupHomeAbbrBox","SetupHomeColorBox","SetupHomeSecondaryBox","SetupAwayNameBox","SetupAwayAbbrBox","SetupAwayColorBox","SetupAwaySecondaryBox"}.Select(name=>Named<TextBox>(wizard,name).Text));
    private static async Task WaitPresetQueue(StoreFixture fixture)=>await fixture.Read(Afl);
    private static void PresetCaptureEvidence(string stage,Window window,SetupWizard wizard,Border oldTile,IPointer pointer,int losses,int writes,string beforeOrder,string afterOrder)
    {
        string directory=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??Path.Combine(Path.GetTempPath(),"roche-preset-capture");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,stage+".json"),System.Text.Json.JsonSerializer.Serialize(new
        {scope="Actual original standalone public SetupWizard + raw pointer Down/Move; external public IPointer capture transfer; NOT native WPF capture acceptance or Home E2E.",
            stage,losses,writes,beforeOrder,afterOrder,draft=PresetCaptureDraft(wizard),oldTile.Opacity,oldTile.Bounds,
            oldTileCaptured=NativePointer.IsCaptured(oldTile),captured=pointer.Captured?.GetType().FullName,viewport=window.ClientSize},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }

    [AvaloniaFact]
    public async Task ExternalCaptureLossBeforeDragThresholdDoesNotChooseClubButNormalMouseUpStillDoes()
    {
        await using var fixture=await StoreFixture.Create();await fixture.Seed(Afl,[Other("First captured club"),Other("Second retained club")]);
        var wizard=new SetupWizard();var window=fixture.Track(new NativeWindow{Width=1280,Height=1000,Content=wizard});window.Show();
        await Until(()=>wizard.IsLoaded&&Named<WrapPanel>(wizard,"HomePresetPanel").IsEffectivelyVisible&&Tiles(wizard).Count==2&&PresetActions(wizard).All(b=>b.IsEnabled),"Actual original AFL preset panel must load confirmed isolated clubs.");
        var panel=Named<WrapPanel>(wizard,"HomePresetPanel");Border tile=Assert.Single(panel.Children.OfType<Border>(),b=>((TeamPreset)b.Tag!).PresetName=="First captured club");
        TextBox sink=Named<TextBox>(wizard,"SetupHomeNameBox");Point start=await PointerTarget(window,tile);
        var gate=fixture.GateWrites();gate.Release(); // Actual backend pass-through counter; no pending gate.
        string draft=PresetCaptureDraft(wizard),order=Json((await fixture.Read(Afl)).Presets);byte[]?[] bytes=PresetCaptureBytes(fixture);
        IPointer? pointer=null;int losses=0;tile.AddHandler(InputElement.PointerPressedEvent,(_,e)=>pointer=e.Pointer,RoutingStrategies.Tunnel,handledEventsToo:true);tile.PointerCaptureLost+=(_,_)=>losses++;
        NativePointer.EnsureInitialized(); // Initializes the existing production capture bridge before raw input.
        try
        {
            window.MouseMove(start);window.MouseDown(start,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            Assert.NotNull(pointer);Assert.Same(tile,pointer.Captured);Assert.True(NativePointer.IsCaptured(tile));
            // No Move/Up: external capture transfer is the cancellation cause.
            // Use the SAME real pointer from original Down, not a constructed event.
            pointer.Capture(sink);await WaitPresetQueue(fixture);await Task.Delay(40);Dispatcher.UIThread.RunJobs();
            Assert.True(losses>=1,"Actual capture loss event must be witnessed before asserting cancellation.");
            string afterOrder=Json((await fixture.Read(Afl)).Presets);PresetCaptureEvidence("preset-cancel-press",window,wizard,tile,pointer,losses,gate.WriteCalls,order,afterOrder);
            // FIRST original semantic oracles, before future API/part assumptions.
            Assert.Equal(draft,PresetCaptureDraft(wizard));Assert.Equal(order,afterOrder);Assert.Equal(0,gate.WriteCalls);AssertPresetCaptureBytes(bytes,fixture);
            Assert.Same(sink,pointer.Captured);Assert.False(NativePointer.IsCaptured(tile));
            pointer.Capture(null);window.MouseUp(start,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            // Positive control: original accepted MouseUp intentionally releases
            // capture after clearing _dragTile and MUST still select this club.
            Border fresh=Assert.Single(Named<WrapPanel>(wizard,"HomePresetPanel").Children.OfType<Border>(),b=>((TeamPreset)b.Tag!).PresetName=="First captured club");
            Point accepted=await PointerTarget(window,fresh);window.MouseMove(accepted);window.MouseDown(accepted,MouseButton.Left);window.MouseUp(accepted,MouseButton.Left);
            await Until(()=>Named<TextBox>(wizard,"SetupHomeNameBox").Text=="First captured club","Normal original click release must still choose the current club.");
            await WaitPresetQueue(fixture);Assert.Equal(0,gate.WriteCalls);Assert.Equal(order,Json((await fixture.Read(Afl)).Presets));AssertPresetCaptureBytes(bytes,fixture);Assert.False(NativePointer.IsCaptureWithin(wizard));
        }
        finally{pointer?.Capture(null);window.MouseUp(start,MouseButton.Left);Dispatcher.UIThread.RunJobs();}
    }

    [AvaloniaFact]
    public async Task ExternalCaptureLossAfterDragPreviewDoesNotPersistOrderButNormalMouseUpStillCommitsOnce()
    {
        await using var fixture=await StoreFixture.Create();await fixture.Seed(Afl,[Other("First captured club"),Other("Second retained club"),Other("Third retained club")]);
        var wizard=new SetupWizard();var window=fixture.Track(new NativeWindow{Width=1280,Height=1000,Content=wizard});window.Show();
        await Until(()=>wizard.IsLoaded&&Named<WrapPanel>(wizard,"HomePresetPanel").IsEffectivelyVisible&&Tiles(wizard).Count==3&&PresetActions(wizard).All(b=>b.IsEnabled),"Actual original AFL preset panel must load confirmed isolated clubs.");
        var panel=Named<WrapPanel>(wizard,"HomePresetPanel");Border tile=Assert.Single(panel.Children.OfType<Border>(),b=>((TeamPreset)b.Tag!).PresetName=="First captured club");
        Border next=Assert.Single(panel.Children.OfType<Border>(),b=>((TeamPreset)b.Tag!).PresetName=="Second retained club");
        Point start=await PointerTarget(window,tile),destination=await PointerTarget(window,next);Assert.True(Math.Sqrt(Math.Pow(destination.X-start.X,2)+Math.Pow(destination.Y-start.Y,2))>6);
        TextBox sink=Named<TextBox>(wizard,"SetupHomeNameBox");var gate=fixture.GateWrites();gate.Release();
        string draft=PresetCaptureDraft(wizard),order=Json((await fixture.Read(Afl)).Presets);byte[]?[] bytes=PresetCaptureBytes(fixture);
        IPointer? pointer=null;int losses=0;tile.AddHandler(InputElement.PointerPressedEvent,(_,e)=>pointer=e.Pointer,RoutingStrategies.Tunnel,handledEventsToo:true);tile.PointerCaptureLost+=(_,_)=>losses++;
        NativePointer.EnsureInitialized();
        try
        {
            window.MouseMove(start);window.MouseDown(start,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            Assert.NotNull(pointer);Assert.Same(tile,pointer.Captured);window.MouseMove(destination);Dispatcher.UIThread.RunJobs();
            Assert.True(NativePointer.IsCaptured(tile));Assert.Equal(.85,tile.Opacity); // Actual original threshold/preview branch reached.
            pointer.Capture(sink);await WaitPresetQueue(fixture);await Task.Delay(60);Dispatcher.UIThread.RunJobs();
            Assert.True(losses>=1);string afterOrder=Json((await fixture.Read(Afl)).Presets);PresetCaptureEvidence("preset-cancel-drag",window,wizard,tile,pointer,losses,gate.WriteCalls,order,afterOrder);
            // FIRST state/persistence oracles: current implementation may have
            // committed the preview via LostCapture→CompleteDrag. No private calls.
            Assert.Equal(order,afterOrder);Assert.Equal(0,gate.WriteCalls);AssertPresetCaptureBytes(bytes,fixture);Assert.Equal(draft,PresetCaptureDraft(wizard));
            Assert.Same(sink,pointer.Captured);pointer.Capture(null);window.MouseUp(destination,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            // Reacquire actual current tiles: cancellation may repaint but must
            // preserve order. Fresh real release is the ONLY commit positive.
            Border first=Assert.Single(Named<WrapPanel>(wizard,"HomePresetPanel").Children.OfType<Border>(),b=>((TeamPreset)b.Tag!).PresetName=="First captured club");
            Border second=Assert.Single(Named<WrapPanel>(wizard,"HomePresetPanel").Children.OfType<Border>(),b=>((TeamPreset)b.Tag!).PresetName=="Second retained club");
            Point from=await PointerTarget(window,first),to=await PointerTarget(window,second);
            window.MouseMove(from);window.MouseDown(from,MouseButton.Left);window.MouseMove(to);window.MouseUp(to,MouseButton.Left);
            var accepted=await fixture.Read(Afl);await Until(()=>gate.WriteCalls==1,"One accepted original drag must persist exactly one actual backend write.");
            Assert.Equal(new[]{"Second retained club","First captured club","Third retained club"},accepted.Presets.Select(p=>p.PresetName));
            await WaitPresetQueue(fixture);Assert.Equal(1,gate.WriteCalls);Assert.Equal(draft,PresetCaptureDraft(wizard));Assert.False(NativePointer.IsCaptureWithin(wizard));
        }
        finally{pointer?.Capture(null);window.MouseUp(destination,MouseButton.Left);Dispatcher.UIThread.RunJobs();}
    }
}
