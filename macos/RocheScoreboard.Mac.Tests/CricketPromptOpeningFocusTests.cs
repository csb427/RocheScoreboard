using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;
public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketPromptOpeningFocus")]
    public async Task AnUntouchedOutcomeFormStillFocusesItsFirstEditableField(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(500);
        string before=State(match);var opened=await PromptOpen(window,panel,"form");await Settle(120);
        var field=Assert.Single(opened.Sheet.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"outcome-note"));
        Assert.Same(field,window.FocusManager?.GetFocusedElement());Assert.Equal(before,State(match));
        KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,opened.Sheet);Assert.Equal(before,State(match));
    }
    [AvaloniaTheory]
    [InlineData(1280,850)][InlineData(420,320)]
    [Trait("Evidence","CricketPromptOpeningFocus")]
    public async Task OpeningAnOutcomeFormYieldsToANewerFocusChoiceInThatForm(int width,int height)
    {
        using var scope=new Scope();var match=CricketReliabilityChecks.CreateMatch(true);
        match.RecordDelivery(4,CricketDeliveryType.Four);string before=State(match);
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(500);
        var host=Named<Grid>(panel,"CricketRoot");Button? chosen=null;bool witnessed=false;Border? observed=null;
        void Changed(object? sender,NotifyCollectionChangedEventArgs args)
        {
            if(args.NewItems is null)return;
            foreach(var value in args.NewItems)
            {
                if(value is not Border sheet || !Equals(sheet.Tag,"cricket-decision-sheet") || !sheet.GetLogicalDescendants().OfType<TextBox>().Any(x=>Equals(x.Tag,"outcome-note")))continue;
                observed=sheet;chosen=Assert.Single(sheet.GetLogicalDescendants().OfType<Button>(),x=>Equals(x.Tag,"cancel"));
                void FocusNewer(object? sender,Avalonia.Interactivity.RoutedEventArgs e)
                {
                    chosen.Loaded-=FocusNewer;
                    witnessed=chosen.Focus(NavigationMethod.Tab)&&ReferenceEquals(window.FocusManager?.GetFocusedElement(),chosen);
                }
                // Public tree/load observation selects another real action
                // before the product's deferred initial-focus callback runs.
                if(chosen.IsLoaded)FocusNewer(null,new());else chosen.Loaded+=FocusNewer;
            }
        }
        host.Children.CollectionChanged+=Changed;
        try
        {
            var opened=await PromptOpen(window,panel,"form");await Settle(120);
            Assert.Same(observed,opened.Sheet);Assert.True(witnessed,"Fixture must establish a real newer focus choice before the product callback.");
            Assert.Same(chosen,window.FocusManager?.GetFocusedElement());Assert.Equal(before,State(match));
            KeyNow(window,PhysicalKey.Escape);await PromptRetired(panel,opened.Sheet);Assert.Equal(before,State(match));
        }
        finally{host.Children.CollectionChanged-=Changed;}
    }
}
