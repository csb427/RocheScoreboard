using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class LiveSettingsClockChoiceTests
{
    [AvaloniaFact][Trait("Evidence", "QuarterBreakActualSequentialKeyboard")]
    public async Task QuarterBreakIsReachedByRealSequentialTabAtCompactSize()
    {
        await using var f = await AflFixture.Create(420, 320);
        await OpenBreakCardSection(f, true);
        var toggle = Named<CheckBox>(f.Window, "SettingsBreakScreenToggle");
        var card = Named<Border>(f.Window, "SettingsBreakScreenCard");
        await RevealBreakCardActor(f, toggle); // Geometry readiness only.
        string before = JsonSerializer.Serialize(f.Match.ToState());
        bool initial = toggle.IsChecked == true;

        // Begin again from the actual Ctrl+F route. Do not explicitly Focus
        // the target: only physical Tab may establish the witnessed access.
        Key(f.Window, PhysicalKey.F, RawInputModifiers.Control);
        var search = Assert.Single(f.Window.GetVisualDescendants().OfType<OperatorSearchBox>(), value => value.IsEffectivelyVisible);
        Assert.True(search.IsFocused);
        var visited = new List<string>();
        for (int step = 0; step < 48 && !Within(toggle, f.Window.FocusManager?.GetFocusedElement()); step++)
        {
            Key(f.Window, PhysicalKey.Tab);
            await Settle(25);
            var focused = f.Window.FocusManager?.GetFocusedElement();
            visited.Add((focused as Control)?.Name + ":" + focused?.GetType().Name);
            Assert.NotSame(card, focused);
            Assert.Equal(initial, toggle.IsChecked == true);
        }
        Assert.True(Within(toggle, f.Window.FocusManager?.GetFocusedElement()), string.Join(" -> ", visited));
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.True(Mr5FinalsPart<Border>(toggle, "FocusFrame").IsEffectivelyVisible);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(card) is IInvokeProvider);

        int changes = 0;
        toggle.IsCheckedChanged += (_, _) => changes++;
        Key(f.Window, PhysicalKey.Space);
        Assert.Equal(!initial, toggle.IsChecked == true);
        Assert.Equal(!initial, BreakCardValue(f));
        Assert.Equal(1, changes);
        Assert.Equal(before, JsonSerializer.Serialize(f.Match.ToState()));
        Key(f.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
        await Settle(50);
        Assert.False(Within(toggle, f.Window.FocusManager?.GetFocusedElement()));
        Assert.NotSame(card, f.Window.FocusManager?.GetFocusedElement());
        Assert.Equal(1, changes);
        Assert.Equal(before, JsonSerializer.Serialize(f.Match.ToState()));
        Capture(f.Window, "quarter-break-real-sequential-tab-420");
    }
}
