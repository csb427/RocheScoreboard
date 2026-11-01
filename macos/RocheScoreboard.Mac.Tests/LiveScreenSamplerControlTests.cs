using System.Text.Json;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class BreakRotationModeIsolationTests
{
    private static Rect SamplerPainted(Window window, Control target)
    {
        var matrix = target.TransformToVisual(window); Assert.NotNull(matrix);
        var a = matrix.Value.Transform(default); var z = matrix.Value.Transform(new Point(target.Bounds.Width, target.Bounds.Height));
        return new Rect(a, z);
    }
    private static bool SamplerHit(Window window, Control target, Point point)
        => window.InputHitTest(point) is Visual hit && (ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target));
    private static async Task RevealLiveUtility(Roche_Scoreboard.MainWindow main, Control target)
    {
        var page = Named<ScrollViewer>(main, "AflSettingsPageViewport");
        Point[] Probes(Rect rect) => [rect.Center, new(rect.Left + rect.Width * .15, rect.Center.Y), new(rect.Right - rect.Width * .15, rect.Center.Y), new(rect.Center.X, rect.Top + rect.Height * .15), new(rect.Center.X, rect.Bottom - rect.Height * .15)];
        bool Ready()
        {
            using var frame = main.CaptureRenderedFrame(); Assert.NotNull(frame);
            var rect = SamplerPainted(main, target);
            return new Rect(main.ClientSize).Contains(rect) && Probes(rect).All(p => SamplerHit(main, target, p));
        }
        double amount = 1, previous = 0;
        for (int i = 0; i < 50 && !Ready(); i++)
        {
            var visible = SamplerPainted(main, page).Intersect(new Rect(main.ClientSize));
            double direction = SamplerPainted(main, target).Center.Y < visible.Center.Y ? 1 : -1;
            if (previous != 0 && previous != direction) amount *= .5; previous = direction;
            main.MouseWheel(visible.Center, new Vector(0, direction * amount), RawInputModifiers.None); await Pump(60);
        }
        Assert.True(Ready(), "All five sampling-action probes must be reachable through the actual live settings viewport.");
    }
    [AvaloniaTheory]
    [InlineData("Home", false, 1280, 850)] [InlineData("Home", true, 1280, 850)]
    [InlineData("Away", false, 1280, 850)] [InlineData("Away", true, 1280, 850)]
    [InlineData("Home", false, 420, 320)] [InlineData("Home", true, 420, 320)]
    [InlineData("Away", false, 420, 320)] [InlineData("Away", true, 420, 320)]
    public async Task ActualLiveSettingsScreenSamplerHasClearNameCodeNativeIconAndReachableTarget(string side, bool secondary, int width, int height)
    {
        await using var scope = new StorageScope(true, true); var f = await TeamSwatchOpenFromOwnedRecovery(scope);
        f.Main.Width = width; f.Main.Height = height; await Pump(100);
        string model = JsonSerializer.Serialize(f.Match.ToState()); var history = RecentColorStorage.Load().ToArray();
        string previewName = side + (secondary ? "SecondaryColorPreview" : "ColorPreview");
        var preview = await TeamSwatchReachByOriginalTab(f.Main, previewName, "sampler");
        var row = Assert.IsAssignableFrom<Panel>(preview.Parent); int index = row.Children.IndexOf(preview);
        var target = Assert.IsType<OperatorActionBorder>(row.Children[index + 1]);
        RotationKey(f.Main, PhysicalKey.Tab); await Pump(40); Assert.True(target.IsFocused);
        await RevealLiveUtility(f.Main, target);
        var bounds = SamplerPainted(f.Main, target); var peer = ControlAutomationPeer.CreatePeerForElement(target);
        string role = secondary ? "secondary" : "primary", expected = $"Sample {side.ToLowerInvariant()} {role} colour from screen";
        TeamSwatchEvidence($"sampler-{side}-{role}-{width}-{height}", new { side, role, width, height, bounds, name = peer?.GetName(), hasInvoke = peer is IInvokeProvider });
        TeamSwatchCapture(f.Main, $"sampler-{side}-{role}-{width}-{height}");
        Assert.True(bounds.Width >= 39.5 && bounds.Height >= 39.5, "Existing sampling action needs a comfortable40x40painted target.");
        Assert.NotNull(peer); Assert.Equal(expected, peer.GetName()); Assert.IsAssignableFrom<IInvokeProvider>(peer);
        var icon = Assert.IsType<OperatorSymbol>(target.Child); Assert.Equal("eyedropper", icon.Kind);
        // Deliberately do NOT invoke global sampling: it is an OS screen-capture
        // API, unsupported on Linux. No user desktop/access permission is probed.
        Assert.Equal(new[] { "#0A2A6A", "#FFFFFF", "#7A1A1A", "#E8E8E8" }, TeamSwatchValues(f.Main));
        Assert.Equal(6, f.Match.HomeTotal); Assert.Equal(1, f.Match.AwayTotal);
        Assert.Equal(model, JsonSerializer.Serialize(f.Match.ToState())); Assert.Equal(history, RecentColorStorage.Load().ToArray());
    }
}
