// Public scorecard route and real input. Original invalid Apply must keep its
// same editor mounted; no private Figure/Form/Finish/Commit invocation.
using System.Collections.Specialized;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static string FigureDraft(CricketControlPanel panel) =>
        JsonSerializer.Serialize(Read<CricketScorecardDraft>(panel,"_scorecardDraft"));
    private static TextBox FigureField(Control panel) => Assert.Single(panel.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"scorecard-figure-input"));
    private static async Task FigureType(Window window,TextBox field,string raw)
    {
        await Reveal(window,field);await Key(window,PhysicalKey.A,RawInputModifiers.Control);
        window.KeyTextInput(raw);await Settle();Assert.Equal(raw,field.Text);
    }
    private static async Task FigureSubmit(Window window,Border sheet,TextBox field,bool keyboard)
    {
        if(keyboard){Assert.True(Within(field,window.FocusManager?.GetFocusedElement()),"The already typed field must own input before actual default Enter.");await Key(window,PhysicalKey.Enter);}
        else{var apply=PromptAction(sheet,"apply");await Reveal(window,apply);await StableReachable(window,apply);TapNow(window,apply);}
    }
    private static async Task<(CricketControlPanel Panel,Window Window,Border Sheet,TextBox Field)> FigureOpen(Scope scope,CricketMatchManager match,int width,int height,bool overs)
    {
        var panel=new CricketControlPanel();panel.SetMatch(match);var window=scope.Window(panel,width,height);await Settle(650);
        var tab=Named<TabItem>(panel,"ScorecardTab");await Reveal(window,tab);await StableReachable(window,tab);TapNow(window,tab);await Settle(650);
        Assert.Same(tab,Named<TabControl>(panel,"MainTabControl").SelectedItem);
        var bowling=Assert.Single(panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-team-bowling"));
        await Reveal(window,bowling);
        if(width<500)
        {
            var box=Painted(window,bowling);using var rendered=window.CaptureRenderedFrame();
            Record($"figure-compact-team-tab-{OperatorAppearance.ReduceMotion}-{overs}",new{requested=new{width,height},actual=window.ClientSize,bowling.Bounds,painted=box,ready=Ready(window,bowling),
                probes=new[]{box.Center,new Avalonia.Point(box.Left+4,box.Center.Y),new Avalonia.Point(box.Right-4,box.Center.Y),new Avalonia.Point(box.Center.X,box.Top+4),new Avalonia.Point(box.Center.X,box.Bottom-4)}
                    .Select(point=>new{point,within=Within(bowling,window.InputHitTest(point)),hit=window.InputHitTest(point)?.GetType().Name,hitName=(window.InputHitTest(point) as Control)?.Name}).ToArray(),
                ancestors=bowling.GetVisualAncestors().OfType<Control>().Select(x=>new{type=x.GetType().Name,x.Name,x.Bounds,x.ClipToBounds,painted=Painted(window,x)}).ToArray(),
                scrolls=bowling.GetVisualAncestors().OfType<ScrollViewer>().Select(x=>new{x.Name,x.Offset,x.Extent,x.Viewport}).ToArray()});
            PromptCapture(window,$"figure-compact-team-tab-{OperatorAppearance.ReduceMotion}-{overs}");
            await Key(window,PhysicalKey.Enter);
        }
        else{await StableReachable(window,bowling);TapNow(window,bowling);}
        await Settle(650);
        var row=Assert.Single(panel.GetVisualDescendants().OfType<Grid>(),x=>Equals(x.Tag,"scorecard-player-bowling-0"));
        var figure=Assert.Single(row.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,null)&&Equals(ToolTip.GetTip(x),overs?"Edit Overs":"Edit Runs conceded"));
        await Reveal(window,figure);await StableReachable(window,figure);TapNow(window,figure);await Settle(650);
        var sheet=PromptSheet(panel);var field=FigureField(sheet);Assert.True(field.IsLoaded);Assert.True(panel.HasActiveDecision);
        return(panel,window,sheet,field);
    }
    [AvaloniaTheory]
    [InlineData(900,700,false,false,false)]
    [InlineData(420,320,true,false,true)]
    [InlineData(900,700,true,true,false)]
    [InlineData(420,320,false,true,true)]
    public async Task InvalidSingleFigureKeepsOriginalSheetRawFieldAndRepairFocus(int width,int height,bool reduced,bool overs,bool keyboard)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await FigureOpen(scope,match,width,height,overs);var panel=f.Panel;var window=f.Window;var sheet=f.Sheet;var field=f.Field;
        string raw=overs?"1.6":"-1",before=State(match),token=match.WorkflowToken,draft=FigureDraft(panel);
        int revision=match.DeliveryRevision,changes=0;void Changed()=>changes++;match.MatchChanged+=Changed;
        var host=Named<Grid>(panel,"CricketRoot");var timeline=new List<object>();int removed=0,added=0;
        void Collection(object? sender,NotifyCollectionChangedEventArgs args)
        {
            if(args.OldItems is not null)foreach(var value in args.OldItems.OfType<Border>().Where(x=>ReferenceEquals(x,sheet))){removed++;timeline.Add(new{kind="original-sheet-removed",raw=field.Text,field.IsLoaded});}
            if(args.NewItems is not null)foreach(var value in args.NewItems.OfType<Border>().Where(x=>Equals(x.Tag,"cricket-decision-sheet"))){added++;timeline.Add(new{kind="replacement-sheet-added",same=ReferenceEquals(value,sheet),raw=field.Text});}
        }
        try
        {
            await FigureType(window,field,raw);int selectionStart=field.SelectionStart,selectionEnd=field.SelectionEnd;
            ((INotifyCollectionChanged)host.Children).CollectionChanged+=Collection;
            await FigureSubmit(window,sheet,field,keyboard);
            await Until(()=>panel.GetVisualDescendants().OfType<TextBlock>().Any(x=>Equals(x.Tag,"scorecard-figure-error")&&!string.IsNullOrEmpty(x.Text))
                || panel.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Check the figure"),"Invalid Apply must resolve to observable inline validation or the original error sheet.");
            Assert.Equal(before,State(match));Assert.Equal(token,match.WorkflowToken);Assert.Equal(revision,match.DeliveryRevision);
            Assert.Equal(draft,FigureDraft(panel));Assert.Equal(0,changes);Assert.Equal(raw,field.Text);
            bool same=host.Children.Contains(sheet)&&field.IsLoaded&&ReferenceEquals(FigureFieldIfPresent(panel),field);
            Record($"figure-invalid-{width}-{height}-{reduced}-{overs}-{keyboard}",new{width,height,actual=window.ClientSize,reduced,overs,keyboard,same,removed,added,raw=field.Text,
                beforeSelection=new{selectionStart,selectionEnd},afterSelection=new{field.SelectionStart,field.SelectionEnd},focus=(window.FocusManager?.GetFocusedElement() as Control)?.Name,
                originalMounted=host.Children.Contains(sheet),timeline,labels=panel.GetVisualDescendants().OfType<TextBlock>().Select(x=>new{x.Tag,x.Text}).ToArray(),unchangedState=before==State(match),unchangedDraft=draft==FigureDraft(panel),changes});
            PromptCapture(window,$"figure-invalid-{width}-{height}-{reduced}-{overs}-{keyboard}");
            Assert.True(same,"Rejected numeric input removed the original editor and replaced it with a separate error sheet; the same field must remain mounted for repair.");
            Assert.Equal(0,removed);Assert.Equal(0,added);Assert.True(Within(field,window.FocusManager?.GetFocusedElement()));
            Assert.Equal(selectionStart,field.SelectionStart);Assert.Equal(selectionEnd,field.SelectionEnd);
            var error=Assert.Single(sheet.GetVisualDescendants().OfType<TextBlock>(),x=>Equals(x.Tag,"scorecard-figure-error"));Assert.True(error.IsEffectivelyVisible);Assert.Contains("valid non-negative",error.Text);
            // Rejected repeat remains the same no-op rather than closing/reopening.
            await FigureSubmit(window,sheet,field,keyboard);await Settle(250);
            Assert.Same(field,FigureField(panel));Assert.Contains(sheet,host.Children);Assert.Equal(0,removed);Assert.Equal(0,added);
            Assert.Equal(before,State(match));Assert.Equal(draft,FigureDraft(panel));Assert.Equal(0,changes);
            await Reveal(window,field);await Key(window,PhysicalKey.Escape);await PromptRetired(panel,sheet);
            Assert.Equal(before,State(match));Assert.Equal(token,match.WorkflowToken);Assert.Equal(draft,FigureDraft(panel));Assert.Equal(0,changes);
        }
        finally{((INotifyCollectionChanged)host.Children).CollectionChanged-=Collection;match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task InvalidFigureRepairToOriginalIsNoOp(bool reduced)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);
        var f=await FigureOpen(scope,match,900,700,false);string before=State(match),token=match.WorkflowToken;int revision=match.DeliveryRevision,events=0;
        void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            await FigureType(f.Window,f.Field,"-1");await FigureSubmit(f.Window,f.Sheet,f.Field,true);
            Assert.Same(f.Field,FigureField(f.Panel));Assert.Contains(f.Sheet,Named<Grid>(f.Panel,"CricketRoot").Children);
            await FigureType(f.Window,f.Field,"0");await FigureSubmit(f.Window,f.Sheet,f.Field,true);await PromptRetired(f.Panel,f.Sheet);
            Assert.Equal(before,State(match));Assert.Equal(token,match.WorkflowToken);Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            Assert.Empty(match.CurrentInnings!.ScorecardChanges);Assert.False(Read<bool>(f.Panel,"_scorecardDirty"));
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task RepairedValidOversPreservesExplicitWarningAndSingleCorrectionRecovery(bool reduced,bool accept)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(0,CricketDeliveryType.Dot);
        var f=await FigureOpen(scope,match,900,700,true);string before=State(match),token=match.WorkflowToken;int revision=match.DeliveryRevision,events=0;
        int deliveries=match.CurrentInnings!.Deliveries.Count,oldBowlerBalls=match.CurrentInnings.BowlingAttack[0].BallsBowled;
        void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            await FigureType(f.Window,f.Field,"1.6");await FigureSubmit(f.Window,f.Sheet,f.Field,true);
            Assert.Same(f.Field,FigureField(f.Panel));Assert.Equal(before,State(match));Assert.Equal(0,events);
            await FigureType(f.Window,f.Field,"1.5");await FigureSubmit(f.Window,f.Sheet,f.Field,true);
            await Until(()=>f.Panel.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="Review related scorecard figures"),"Existing arithmetic warning must remain required.");
            var warning=PromptSheet(f.Panel);await Settle(650);
            Assert.NotSame(f.Sheet,warning);Assert.Equal(before,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            Assert.Contains(f.Panel.GetVisualDescendants().OfType<TextBlock>(),x=>(x.Text??"").Contains("Bowler balls do not add up"));
            var action=PromptAction(warning,accept?"continue-anyway":"go-back");await Reveal(f.Window,action);await StableReachable(f.Window,action);TapNow(f.Window,action);
            if(!accept)
            {
                await Until(()=>FigureFieldIfPresent(f.Panel) is not null,"Go back must restore the owned figure for repair.");await Settle(650);
                Assert.Same(f.Field,FigureField(f.Panel));Assert.Equal("1.5",f.Field.Text);Assert.Equal(before,State(match));Assert.Equal(token,match.WorkflowToken);Assert.Equal(0,events);
                var sheet=PromptSheet(f.Panel);var cancel=PromptAction(sheet,"back");await Reveal(f.Window,cancel);await StableReachable(f.Window,cancel);TapNow(f.Window,cancel);await PromptRetired(f.Panel,sheet);
                Assert.Equal(before,State(match));Assert.Equal(0,events);Assert.False(Read<bool>(f.Panel,"_scorecardDirty"));return;
            }
            await Until(()=>!f.Panel.HasActiveDecision&&match.DeliveryRevision==revision+1,"Accepted correction must finish once.");
            Assert.Equal(1,events);Assert.Equal(revision+1,match.DeliveryRevision);Assert.Single(match.CurrentInnings.ScorecardChanges);
            Assert.Equal(11,match.CurrentInnings.BowlingAttack[0].BallsBowled);Assert.Equal(1,match.CurrentInnings.LegalBallsBowled);Assert.Equal(deliveries,match.CurrentInnings.Deliveries.Count);
            Assert.Contains("Bowler balls do not add up",match.CurrentInnings.ManualScorecardWarning);
            var restored=CricketReliabilityChecks.CreateMatch(true);restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(State(match))!);
            Assert.Equal(11,restored.CurrentInnings!.BowlingAttack[0].BallsBowled);Assert.Single(restored.CurrentInnings.ScorecardChanges);Assert.Equal(deliveries,restored.CurrentInnings.Deliveries.Count);
            Assert.True(restored.TryRevertLatestScorecardCorrection(1,out var error),error);Assert.Equal(oldBowlerBalls,restored.CurrentInnings.BowlingAttack[0].BallsBowled);Assert.Empty(restored.CurrentInnings.ScorecardChanges);Assert.Equal(deliveries,restored.CurrentInnings.Deliveries.Count);
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task InvalidFigurePublicRebindOrOwnerCloseRetiresWithoutEditingEitherMatch(bool reduced,bool close)
    {
        using var scope=new Scope(reduced);var match=CricketReliabilityChecks.CreateMatch(true);var replacement=CricketReliabilityChecks.CreateMatch(true);
        var f=await FigureOpen(scope,match,900,700,true);string before=State(match),other=State(replacement);int events=0;void Changed()=>events++;
        match.MatchChanged+=Changed;replacement.MatchChanged+=Changed;
        try
        {
            await FigureType(f.Window,f.Field,"1.6");await FigureSubmit(f.Window,f.Sheet,f.Field,true);Assert.Same(f.Field,FigureField(f.Panel));
            if(close)f.Window.Close();else f.Panel.SetMatch(replacement);
            await Until(()=>!f.Field.IsLoaded&&!f.Panel.HasActiveDecision,"Retired editor must lose UI and workflow authority.");await Settle(650);
            Assert.Equal(before,State(match));Assert.Equal(other,State(replacement));Assert.Equal(0,events);
            Assert.DoesNotContain(Named<Grid>(f.Panel,"CricketRoot").Children,x=>ReferenceEquals(x,f.Sheet));
        }
        finally{match.MatchChanged-=Changed;replacement.MatchChanged-=Changed;}
    }
    private static TextBox? FigureFieldIfPresent(Control panel)=>panel.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(x=>Equals(x.Tag,"scorecard-figure-input"));
}
