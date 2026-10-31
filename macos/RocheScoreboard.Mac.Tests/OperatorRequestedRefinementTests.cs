using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class OperatorRequestedRefinementTests
{
    private static async Task Settle(int ms=180){await Task.Delay(ms);Dispatcher.UIThread.RunJobs();}
    private static Button Tagged(Control root,string tag)=>root.GetVisualDescendants().OfType<Button>().First(b=>Equals(b.Tag,tag)&&b.IsEffectivelyVisible);
    private static TextBox Field(Control root,string tag)=>root.GetVisualDescendants().OfType<TextBox>().Single(b=>Equals(b.Tag,tag));
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Capture(Window window,string name)
    {
        string dir=Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp/roche-batch-5-captures";
        Directory.CreateDirectory(dir);using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);frame.Save(Path.Combine(dir,name+".png"));
    }
    [AvaloniaTheory]
    [InlineData(850,600)][InlineData(420,320)]
    public async Task RelatedBatterAndWideEditsApplyAtomicallyAndSurviveReload(int width,int height)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=width,Height=height,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle();
            Click(Tagged(panel,"scorecard-edit-totals"));await Settle(400);
            Field(panel,"scorecard-edit-batter-0").Text="3";Field(panel,"scorecard-edit-wides").Text="1";
            Field(panel,"scorecard-edit-bowler-runs-0").Text="4";Field(panel,"scorecard-edit-bowler-wides-0").Text="1";
            await Settle(30);Assert.Equal("4",Field(panel,"scorecard-edit-team-total").Text);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>Equals(t.Tag,"scorecard-totals-equation")&&(t.Text??"").Contains("Total balances."));
            Assert.Equal(0,match.TotalRuns);Assert.Empty(match.CurrentInnings!.ScorecardChanges);
            var apply=Tagged(panel,"apply");apply.Focus();apply.BringIntoView();await Settle(400);Capture(window,$"atomic-edit-{width}x{height}");
            var p=apply.TranslatePoint(default,window)!.Value;Assert.True(new Rect(window.Bounds.Size).Contains(new Rect(p,apply.Bounds.Size)));
            Click(apply);await Settle(550);
            Assert.Equal(4,match.TotalRuns);Assert.Equal(3,match.CurrentInnings.BattingOrder[0].Runs);Assert.Equal(1,match.CurrentInnings.Wides);
            Assert.Equal(1,match.CurrentInnings.TotalExtras);Assert.Single(match.CurrentInnings.ScorecardChanges);
            Assert.Empty(match.ValidateScorecard(match.CreateScorecardDraft(1)));
            var restored=CricketReliabilityChecks.CreateMatch(true);restored.LoadState(JsonSerializer.Deserialize<CricketSerializableState>(JsonSerializer.Serialize(match.ToState()))!);
            Assert.Equal(4,restored.TotalRuns);Assert.Equal(1,restored.CurrentInnings!.Wides);Assert.Equal(3,restored.CurrentInnings.BattingOrder[0].Runs);
            Assert.True(restored.TryRevertLatestScorecardCorrection(1,out var error),error);Assert.Equal(0,restored.TotalRuns);Assert.Equal(0,restored.CurrentInnings.Wides);
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaTheory]
    [InlineData("cancel")][InlineData("invalid")][InlineData("stale")]
    public async Task CancelInvalidAndStaleTotalsDoNotPartiallyApply(string action)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);
        var window=new NativeWindow{Width=700,Height=500,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle();
            Click(Tagged(panel,"scorecard-edit-totals"));await Settle(400);Field(panel,"scorecard-edit-batter-0").Text="7";Field(panel,"scorecard-edit-wides").Text=action=="invalid"?"-1":"2";
            if(action=="stale")match.RecordDelivery(1,CricketDeliveryType.Runs);
            string before=JsonSerializer.Serialize(match.ToState());
            Click(Tagged(panel,action=="cancel"?"back":"apply"));await Settle(500);
            Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
            Assert.Empty(match.CurrentInnings!.ScorecardChanges);
            if(action=="invalid")Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>(t.Text??"").Contains("Enter whole numbers"));
            if(action=="stale")Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(),t=>(t.Text??"").Contains("match changed"));
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [Fact]
    public void ScorecardArithmeticUsesCurrentValuesIncludingWideAndNoBallPenaltiesExactlyOnce()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var draft=match.CreateScorecardDraft(1);
        draft.Batters[0].Runs=7;draft.Wides=2;draft.NoBalls=1;draft.Byes=3;draft.LegByes=1;draft.TotalRuns=14;
        draft.Bowlers[0].RunsConceded=10;draft.Bowlers[0].Wides=2;draft.Bowlers[0].NoBalls=1;
        Assert.Equal(7,draft.ExtrasRuns);Assert.Equal(14,draft.CalculatedTotal);Assert.Empty(match.ValidateScorecard(draft));
        draft.Batters[0].Runs=9;draft.Wides=3;draft.TotalRuns=17;draft.Bowlers[0].RunsConceded=13;draft.Bowlers[0].Wides=3;
        Assert.Equal(17,draft.CalculatedTotal);Assert.True(match.TryApplyScorecard(draft,out var error),error);Assert.Equal(17,match.TotalRuns);
        var invalid=match.CreateScorecardDraft(1);invalid.TotalRuns=18;string before=JsonSerializer.Serialize(match.ToState());
        Assert.False(match.TryApplyScorecard(invalid,out error));Assert.Contains("Batters (9) + extras (8)",error);Assert.Equal(before,JsonSerializer.Serialize(match.ToState()));
    }
    [AvaloniaFact]
    public async Task BalancedTeamTotalDoesNotClaimItIsWrongWhenOnlyBowlingAllocationNeedsReview()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var panel=new CricketControlPanel();panel.SetMatch(match);var window=new NativeWindow{Width=800,Height=550,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle();Click(Tagged(panel,"scorecard-edit-totals"));await Settle(400);
            Field(panel,"scorecard-edit-batter-0").Text="3";Field(panel,"scorecard-edit-wides").Text="1";Click(Tagged(panel,"apply"));await Settle(500);
            var text=panel.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
            Assert.Contains("Review related scorecard figures",text);Assert.Contains(text,t=>(t??"").Contains("This total balances."));Assert.DoesNotContain("Team total does not balance",text);
            Assert.Equal(0,match.TotalRuns);Click(Tagged(panel,"go-back"));await Settle(400);Assert.Equal("3",Field(panel,"scorecard-edit-batter-0").Text);Assert.Equal("1",Field(panel,"scorecard-edit-wides").Text);
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaTheory]
    [InlineData(800,550)][InlineData(420,320)]
    public async Task SettingsHaveSeparateGeneralCricketAndAflScopes(int width,int height)
    {
        var view=new GlobalSettingsScreen();var window=new NativeWindow{Width=width,Height=height,Content=view};window.Show();
        try
        {
            await Settle(600);Capture(window,$"settings-general-{width}x{height}");
            view.SetOutputPreferences("clean",772,386,false,false,false);int emissions=0;
            view.GoalAnimationChanged+=_=>emissions++;view.BreakScreenEnabledChanged+=_=>emissions++;view.AutoOverlaysEnabledChanged+=_=>emissions++;
            var general=view.FindControl<RadioButton>("GeneralSettingsTab");Assert.NotNull(general);
            var cricket=view.FindControl<RadioButton>("CricketSettingsScopeTab");Assert.NotNull(cricket);
            var afl=view.FindControl<RadioButton>("AflSettingsTab");Assert.NotNull(afl);
            Assert.True(general.IsChecked);Assert.False(view.FindControl<ComboBox>("GoalAnimationCombo")!.IsEffectivelyVisible);
            afl.IsChecked=true;await Settle(600);Capture(window,$"settings-afl-{width}x{height}");
            Assert.True(view.FindControl<ComboBox>("GoalAnimationCombo")!.IsEffectivelyVisible);
            Assert.True(view.FindControl<ComboBox>("GoalAnimationCombo")!.Bounds.Width>=180,"Goal choices must retain a usable width");
            Assert.True(view.FindControl<CheckBox>("BreakScreenToggle")!.IsEffectivelyVisible);
            cricket.IsChecked=true;await Settle(600);Capture(window,$"settings-cricket-{width}x{height}");
            Assert.False(view.FindControl<CheckBox>("BreakScreenToggle")!.IsEffectivelyVisible);
            Assert.True(view.FindControl<CheckBox>("CricketEventsDefaultToggle")!.IsEffectivelyVisible);
            general.IsChecked=true;await Settle();Assert.True(view.FindControl<TextBox>("OutputWidthBox")!.IsEffectivelyVisible);
            Assert.Equal("772",view.FindControl<TextBox>("OutputWidthBox")!.Text);Assert.Equal(3,view.FindControl<ComboBox>("GoalAnimationCombo")!.SelectedIndex);
            Assert.False(view.FindControl<CheckBox>("BreakScreenToggle")!.IsChecked);Assert.Equal(0,emissions);
        }
        finally{window.Close();}
    }
    [Fact]
    public void CricketAutomaticOverlayPreferenceMigratesOnceWithoutFollowingLaterAflChanges()
    {
        var legacy=new OperatorWorkflowPreferencesData{AflAutoOverlaysEnabled=false,AflGoalAnimation="clean",GoalAnimationPreferenceVersion=1};
        var normalize=typeof(OperatorWorkflowPreferences).GetMethod("Normalize",BindingFlags.NonPublic|BindingFlags.Static)!;
        normalize.Invoke(null,[legacy]);Assert.False(legacy.CricketAutomaticOverlays);Assert.Equal("clean",legacy.AflGoalAnimation);
        legacy.AflAutoOverlaysEnabled=true;normalize.Invoke(null,[legacy]);Assert.False(legacy.CricketAutomaticOverlays);
        legacy.CricketAutomaticOverlays=true;legacy.AflAutoOverlaysEnabled=false;normalize.Invoke(null,[legacy]);Assert.True(legacy.CricketAutomaticOverlays);
    }
    [AvaloniaFact]
    public async Task ScorecardOffersAtomicRunAndExtrasEditing()
    {
        var panel=new CricketControlPanel();panel.SetMatch(CricketReliabilityChecks.CreateMatch(true));
        var window=new NativeWindow{Width=850,Height=600,Content=panel};window.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("ScorecardTab");await Settle();Capture(window,"scorecard");
            Assert.Single(panel.GetVisualDescendants().OfType<Button>(),b=>Equals(b.Tag,"scorecard-edit-totals"));
        }
        finally{panel.ClearMatch();window.Close();}
    }
    [AvaloniaFact]
    public async Task ExtrasAndBoundariesAreDiscoverableOnTheMountedOutputPage()
    {
        var match=CricketReliabilityChecks.CreateMatch(true);match.RecordDelivery(4,CricketDeliveryType.Four);match.RecordDelivery(2,CricketDeliveryType.Wide);
        using var host=new CricketOverlayHost(match){MotionEnabled=false,AutomaticEnabled=false};var panel=new CricketControlPanel();panel.SetMatch(match);panel.SetOverlayHost(host);
        var window=new NativeWindow{Width=950,Height=650,Content=panel};window.Show();var output=new NativeWindow{Title="Cricket Display",Width=386,Height=193,Content=host};output.Show();
        try
        {
            panel.FindControl<TabControl>("MainTabControl")!.SelectedItem=panel.FindControl<TabItem>("OutputTab");await Settle();Capture(window,"overlay-controls");
            var buttons=panel.GetVisualDescendants().OfType<Button>().Where(b=>b.Tag is CricketOverlayKind).ToArray();
            foreach(var kind in new[]{CricketOverlayKind.Extras,CricketOverlayKind.Boundaries})
            {
                var button=Assert.Single(buttons,b=>Equals(b.Tag,kind));Assert.True(button.IsEffectivelyVisible);button.BringIntoView();await Settle();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(550);
                Assert.Equal(kind==CricketOverlayKind.Extras?"ON AIR · EXTRAS":"ON AIR · INNINGS BOUNDARIES",host.Status);
                var labels=host.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
                Assert.Contains(kind==CricketOverlayKind.Extras?"WIDES":"FOURS",labels);
                Assert.DoesNotContain(kind==CricketOverlayKind.Extras?"FOURS":"WIDES",labels);Capture(output,"overlay-"+kind);
            }
        }
        finally{panel.ClearMatch();window.Close();output.Close();}
    }
    [AvaloniaFact]
    public async Task SavedClubActionAndFilterHaveInterruptibleSelectionSurfaces()
    {
        bool? old=OperatorMotion.AnimationOverride;OperatorMotion.AnimationOverride=true;
        var window=new TeamLibraryWindow("Home");window.Show();
        try
        {
            await Settle(650);var button=window.FindControl<Button>("FavouriteButton")!;button.IsEnabled=true;
            Assert.NotEmpty(button.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            var filter=window.FindControl<CheckBox>("FavouriteOnly")!;
            Assert.NotEmpty(filter.GetVisualDescendants().OfType<OperatorChoiceSurface>());
            OperatorChoiceMotion.SetSelected(button,true);await Settle(65);
            Assert.InRange(OperatorChoiceMotion.GetProgress(button),.001,.999);
            for(int i=0;i<8;i++){Capture(window,$"saved-selection-{i:D2}");await Settle(45);}
            OperatorChoiceMotion.SetSelected(button,false);await Settle(30);OperatorChoiceMotion.SetSelected(button,true);await Settle(600);
            Assert.Equal(1,OperatorChoiceMotion.GetProgress(button));
            OperatorMotion.AnimationOverride=false;OperatorChoiceMotion.SetSelected(button,false);Assert.Equal(0,OperatorChoiceMotion.GetProgress(button));
            filter.IsChecked=true;await Settle();Assert.Equal(1,OperatorChoiceMotion.GetProgress(filter));
        }
        finally{window.Close();OperatorMotion.AnimationOverride=old;}
    }
    [AvaloniaFact]
    public async Task CricketNumeralsRetainFontAndTabularWidthsWithReservedWicketSpace()
    {
        var bug=new CricketScorebugControl{RollingFiguresEnabled=false,EventAnimationsEnabled=false};
        var window=new NativeWindow{Width=386,Height=193,Content=bug};window.Show();
        try
        {
            await Settle(450);var runs=bug.FindControl<TextBlock>("ScoreRuns")!;
            var features=runs.FontFeatures;Assert.NotNull(features);Assert.Contains(features,f=>f.Tag.ToString()=="tnum");
            var probe=new TextBlock{FontFamily=runs.FontFamily,FontSize=40,FontWeight=runs.FontWeight,FontFeatures=features};
            var advances=new List<double>();
            for(int i=0;i<10;i++){probe.Text=new string((char)('0'+i),4);probe.Measure(Size.Infinity);advances.Add(probe.TextLayout.WidthIncludingTrailingWhitespace);}
            var face=new Avalonia.Media.Typeface(runs.FontFamily, runs.FontStyle, runs.FontWeight).GlyphTypeface;
            var glyphWidths=Enumerable.Range(0,10).Select(i=>face.GetGlyphAdvance(face.GetGlyph((uint)('0'+i)))).ToArray();
            string metrics=JsonSerializer.Serialize(new {Font=runs.FontFamily.ToString(),Features=features.Select(f=>f.ToString()).ToArray(),Advances=advances,GlyphAdvances=glyphWidths});
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("ROCHE_CAPTURE_DIR")??"/tmp", "numeric-font-metrics.json"),metrics);
            Assert.True(advances.Max()-advances.Min()<.05,metrics);
            var wicket=bug.FindControl<TextBlock>("ScoreWickets")!;var word=bug.FindControl<TextBlock>("ScoreSlash")!;
            double anchor=0,font=runs.FontSize;
            foreach(string value in new[]{"9","10","11"})
            {
                wicket.Text=value;runs.Text=value=="9"?"99":"100";await Settle();
                double x=word.TranslatePoint(default,bug)!.Value.X;if(anchor==0)anchor=x;else Assert.InRange(Math.Abs(x-anchor),0,.1);
                Assert.Equal(font,runs.FontSize);Assert.True(wicket.DesiredSize.Width<=wicket.Bounds.Width+.1);
                Capture(window,"numeric-"+value);
            }
        }
        finally{bug.Dispose();window.Close();}
    }
    [Fact]
    public void AflRepeatedScoringUndoAndReloadPreserveSixPointGoalArithmetic()
    {
        var match=new MatchManager();var expected=new List<(TeamSide Side,bool Goal)>();var random=new Random(427);
        for(int i=0;i<180;i++)
        {
            if(i%5==4&&expected.Count>0){Assert.True(match.UndoLastScore());expected.RemoveAt(expected.Count-1);}
            else{var side=random.Next(2)==0?TeamSide.Home:TeamSide.Away;bool goal=random.Next(2)==0;if(goal)match.AddGoal(side);else match.AddBehind(side);expected.Add((side,goal));}
            Assert.Equal(expected.Where(e=>e.Side==TeamSide.Home).Sum(e=>e.Goal?6:1),match.HomeTotal);
            Assert.Equal(expected.Where(e=>e.Side==TeamSide.Away).Sum(e=>e.Goal?6:1),match.AwayTotal);
            Assert.Empty(match.ValidateScoreSequence());
            if(i%17==0){var copy=new MatchManager();copy.LoadState(match.ToState());match=copy;}
        }
        Assert.Equal(expected.Count,match.Events.Count);
    }
    [AvaloniaTheory]
    [InlineData(99)][InlineData(100)][InlineData(1_000_000)]
    public async Task CompactCricketOutputKeepsSupportedLargeCorrectedFiguresInsideTheirCells(int runs)
    {
        var match=CricketReliabilityChecks.CreateMatch(true);var draft=match.CreateScorecardDraft(1);
        draft.TotalRuns=runs;draft.Batters[0].Runs=runs-1;draft.Wides=1;draft.Bowlers[0].Wides=1;draft.Bowlers[0].RunsConceded=runs;
        Assert.True(match.TryApplyScorecard(draft,out string error),error);
        var bug=new CricketScorebugControl{RollingFiguresEnabled=false,EventAnimationsEnabled=false};bug.UpdateFromMatch(match);
        var window=new NativeWindow{Width=386,Height=193,Content=bug};window.Show();
        try
        {
            await Settle(550);Capture(window,"numeric-corrected-"+runs);
            foreach(string name in new[]{"ScoreRuns","StrikerRuns","BowlerFigures"})
            {
                var text=bug.FindControl<TextBlock>(name)!;var cell=(Control)text.Parent!;
                Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace<=cell.Bounds.Width+.3,$"{name}={text.Text} must fit {cell.Bounds.Width}: {text.TextLayout.WidthIncludingTrailingWhitespace}");
            }
        }
        finally{bug.Dispose();window.Close();}
    }
}
