using Avalonia.Media;
using System.Collections.Specialized;
// Native fallback regression and preservation of explicit derived themes.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed partial class CricketPlayerMenuPreferencesBaselineTests
{
    private static CricketMatchManager DockWicketMatch()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(0,CricketDeliveryType.Dot);match.RecordDelivery(0,CricketDeliveryType.Wicket,"bowled");match.SelectNewBatter(2);return match;
    }
    private static TextBox DockWicketField(CricketControlPanel panel)=>Assert.Single(panel.GetVisualDescendants().OfType<TextBox>(),x=>Equals(x.Tag,"scorecard-fow-entry"));
    [AvaloniaFact]
    public async Task UnstyledActualFallOfWicketEditorDisplaysAndAcceptsRealTextWithoutApplyingIt()
    {
        using var scope=new Scope(true);var match=DockWicketMatch();var f=await DockOpen(scope,match,420,320);
        string initial=State(match),raw="1-0 (0.2) checked";int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        try
        {
            var field=DockWicketField(f.Panel);Assert.Null(field.Theme);Assert.NotNull(field.Template);Assert.Contains(field.GetVisualDescendants(),x=>x.GetType().Name=="TextPresenter");
            await FigureType(f.Window,field,raw);Assert.Equal(raw,field.Text);Assert.Equal(raw,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets[0]);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
            Assert.True(Within(field,f.Window.InputHitTest(Painted(f.Window,field).Center)));Assert.True(Within(field,f.Window.FocusManager?.GetFocusedElement()));
            Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            Record("dock-fow-real-input",new{field.Text,field.IsFocused,field.SelectionStart,field.SelectionEnd,hasExplicitTheme=field.Theme is not null,hasTemplate=field.Template is not null,children=field.GetVisualDescendants().Select(x=>x.GetType().FullName).ToArray(),unchangedState=initial==State(match),events});
            PromptCapture(f.Window,"dock-fow-real-input");
            await Key(f.Window,PhysicalKey.A,RawInputModifiers.Control);await Key(f.Window,PhysicalKey.Backspace);Assert.Equal("",field.Text);
            var errorColour=Assert.IsAssignableFrom<ISolidColorBrush>(field.BorderBrush).Color;
            f.Window.KeyTextInput(" ");await Settle(150);
            Assert.Equal("",Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets[0]);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
            Assert.Equal(errorColour,Assert.IsAssignableFrom<ISolidColorBrush>(field.BorderBrush).Color);
            Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            Record("dock-fow-empty-feedback",new{raw=field.Text,normalized=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets[0],errorColour=errorColour.ToString(),dirty=Read<bool>(f.Panel,"_scorecardDirty"),events});
        }
        finally{match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task FocusedPendingFallOfWicketDraftAndCaretSurviveResizeThenDiscardSafely(bool reduced)
    {
        using var scope=new Scope(reduced);var match=DockWicketMatch();var f=await DockOpen(scope,match,900,700);f.Window.Activate();await Settle(100);Assert.True(f.Window.IsActive,"Real window activation is a fixture prerequisite for the existing resize-focus return contract.");
        string initial=State(match),raw="1-0 (0.2) pending";int revision=match.DeliveryRevision,events=0;void Changed()=>events++;match.MatchChanged+=Changed;
        var timeline=new List<object>();var rowsHost=Read<StackPanel>(f.Panel,"_scorecardRows");var observed=new List<(TextBox Field,EventHandler<TextChangedEventArgs> Handler)>();
        void Rebuilt(object? sender,NotifyCollectionChangedEventArgs args)
        {
            if(args.NewItems is null)return;
            foreach(var region in args.NewItems.OfType<StackPanel>().Where(x=>Equals(x.Tag,"scorecard-inline-fow")))
            foreach(var current in region.GetVisualDescendants().OfType<TextBox>().Where(x=>Equals(x.Tag,"scorecard-fow-entry")))
            {
                timeline.Add(new{kind="new-field-added",raw=current.Text,dirty=Read<bool>(f.Panel,"_scorecardDirty"),draftValue=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets.ToArray(),current.IsLoaded});
                EventHandler<TextChangedEventArgs> handler=(_,_)=>timeline.Add(new{kind="new-field-TextChanged",raw=current.Text,dirty=Read<bool>(f.Panel,"_scorecardDirty"),draftValue=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets.ToArray(),current.IsLoaded});
                current.TextChanged+=handler;observed.Add((current,handler));
            }
        }
        try
        {
            var field=DockWicketField(f.Panel);await FigureType(f.Window,field,raw);var retainedBeforeResize=field;await Key(f.Window,PhysicalKey.Home);await Key(f.Window,PhysicalKey.ArrowRight,RawInputModifiers.Shift);
            int start=field.SelectionStart,end=field.SelectionEnd;Assert.NotEqual(start,end);var draft=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft");string pending=FigureDraft(f.Panel);
            foreach(var size in new[]{new Size(420,320),new Size(760,560),new Size(900,700)})
            {
                f.Window.Width=size.Width;f.Window.Height=size.Height;await Settle(650);
                field=DockWicketField(f.Panel);
                Record($"dock-fow-pending-resize-{reduced}-{size.Width}-{size.Height}",new{requested=size,actual=f.Window.ClientSize,field.Text,field.SelectionStart,field.SelectionEnd,field.IsFocused,f.Window.IsActive,sameDraft=ReferenceEquals(draft,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft")),unchangedPending=pending==FigureDraft(f.Panel),unchangedState=initial==State(match)});
                Assert.True(Within(field,f.Window.FocusManager?.GetFocusedElement()),"Existing public resize must restore the pending FOW editor focus without a test focus repair.");
                Assert.Same(draft,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft"));Assert.Equal(pending,FigureDraft(f.Panel));Assert.Equal(raw,field.Text);Assert.Equal(start,field.SelectionStart);Assert.Equal(end,field.SelectionEnd);
                Assert.True(Ready(f.Window,field));Assert.False(Painted(f.Window,field).Intersects(Painted(f.Window,DockSurface(f.Panel))));Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);
            }
            Assert.NotSame(retainedBeforeResize,field);Assert.False(retainedBeforeResize.IsLoaded);
            retainedBeforeResize.Text="late detached same-draft callback";await Settle(100);
            Assert.Equal(pending,FigureDraft(f.Panel));Assert.Equal(raw,field.Text);Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
            await Key(f.Window,PhysicalKey.End);f.Window.KeyTextInput(" ");await Settle(100);
            Assert.Equal(pending,FigureDraft(f.Panel));Assert.True(Read<bool>(f.Panel,"_scorecardDirty"));
            var recovery=Assert.Single(f.Panel.GetVisualDescendants().OfType<Button>(),x=>Equals(x.Tag,"scorecard-recovery"));await Reveal(f.Window,recovery);await StableReachable(f.Window,recovery);TapNow(f.Window,recovery);await Settle(650);
            var sheet=PromptSheet(f.Panel);var discard=PromptAction(sheet,"discard-pending");await Reveal(f.Window,discard);await StableReachable(f.Window,discard);
            ((INotifyCollectionChanged)rowsHost.Children).CollectionChanged+=Rebuilt;
            TapNow(f.Window,discard);await PromptRetired(f.Panel,sheet);await Settle(250);
            Record("dock-fow-after-discard-"+reduced,new{dirty=Read<bool>(f.Panel,"_scorecardDirty"),samePendingDraft=ReferenceEquals(draft,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft")),raw=DockWicketField(f.Panel).Text,expected=match.CreateScorecardDraft(1).FallOfWickets,draft=Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets,timeline,unchangedState=initial==State(match),revision=match.DeliveryRevision,events});
            Assert.False(Read<bool>(f.Panel,"_scorecardDirty"));Assert.Equal(match.CreateScorecardDraft(1).FallOfWickets,DockWicketField(f.Panel).Text is{} current?new[]{current}:[]);
            var reloaded=DockWicketField(f.Panel);await Reveal(f.Window,reloaded);await Key(f.Window,PhysicalKey.End);f.Window.KeyTextInput(" ");await Settle(150);
            Assert.False(Read<bool>(f.Panel,"_scorecardDirty"));Assert.Equal(match.CreateScorecardDraft(1).FallOfWickets,Read<CricketScorecardDraft>(f.Panel,"_scorecardDraft").FallOfWickets);
            Assert.Equal(initial,State(match));Assert.Equal(revision,match.DeliveryRevision);Assert.Equal(0,events);Assert.Empty(match.CurrentInnings!.ScorecardChanges);
        }
        finally{((INotifyCollectionChanged)rowsHost.Children).CollectionChanged-=Rebuilt;foreach(var value in observed)value.Field.TextChanged-=value.Handler;match.MatchChanged-=Changed;}
    }
    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ExplicitOperatorSearchAndSettingsInputThemesKeepTheirOwnPartsAndKeyboardBehavior(bool reduced)
    {
        using var scope=new Scope(reduced);var body=new StackPanel{Margin=new Thickness(24)};body.Resources.MergedDictionaries.Add(NativeAssets.Dictionary("ControlCentre"));
        var search=new OperatorSearchBox{Placeholder="Search operator settings",Height=44};var input=new NativeSearchTextBox{Text="Original draft",Height=44,Margin=new Thickness(0,16,0,0)};
        input.SetResourceReference(StyledElement.ThemeProperty,"SettingsInput");body.Children.Add(search);body.Children.Add(input);var window=scope.Window(body,760,560);await Settle(650);
        Assert.Same(body.FindResource(typeof(OperatorSearchBox)),search.Theme);Assert.IsType<ControlTheme>(search.Theme);Assert.Same(body.FindResource("SettingsInput"),input.Theme);
        var clear=Assert.Single(search.GetVisualDescendants().OfType<Button>(),x=>x.Name=="PART_Clear");var placeholder=Assert.Single(search.GetVisualDescendants().OfType<TextBlock>(),x=>x.Name=="PART_Placeholder");var symbol=Assert.Single(search.GetVisualDescendants().OfType<OperatorSymbol>(),x=>x.Name=="PART_SearchSymbol");
        Assert.Equal("search",symbol.Kind);Assert.True(placeholder.IsVisible);Assert.False(clear.IsVisible);Assert.False(search.HasQuery);
        await Reveal(window,search);window.KeyTextInput("overs");await Settle(150);Assert.Equal("overs",search.Text);Assert.True(search.HasQuery);Assert.True(clear.IsVisible);Assert.False(placeholder.IsVisible);
        await StableReachable(window,clear);TapNow(window,clear);await Settle(250);Assert.True(string.IsNullOrEmpty(search.Text));Assert.False(search.HasQuery);Assert.True(placeholder.IsVisible);Assert.False(clear.IsVisible);Assert.True(search.IsFocused);
        window.KeyTextInput("bowler");await Settle(150);await Key(window,PhysicalKey.Escape);Assert.True(string.IsNullOrEmpty(search.Text));Assert.False(search.HasQuery);Assert.True(placeholder.IsVisible);Assert.False(clear.IsVisible);Assert.True(search.IsFocused);Assert.True(window.IsVisible);
        await Reveal(window,input);await Key(window,PhysicalKey.A,RawInputModifiers.Control);window.KeyTextInput("Current numeric draft");await Settle(150);Assert.Equal("Current numeric draft",input.Text);Assert.NotNull(input.Template);Assert.True(Ready(window,input));Assert.Same(body.FindResource("SettingsInput"),input.Theme);
        Record("dock-explicit-input-search-"+reduced,new{search.Text,search.HasQuery,placeholder.IsVisible,clearVisible=clear.IsVisible,symbol.Kind,inputText=input.Text,inputTheme=input.Theme?.TargetType?.FullName,searchTheme=search.Theme?.TargetType?.FullName});PromptCapture(window,"dock-explicit-input-search-"+reduced);
    }
}
