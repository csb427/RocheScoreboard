using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using Canvas = System.Windows.Controls.Canvas;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketOutputHandoffChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var match=CricketReliabilityChecks.CreateMatch(true,20);
        var bug=new CricketScorebugControl {EventAnimationsEnabled=false};
        var host=new CricketOverlayHost(match) {AutomaticEnabled=false};host.Scene.Children.Add(bug);
        match.MatchChanged+=()=>bug.UpdateFromMatch(match);bug.UpdateFromMatch(match);
        var window=new Window {Content=host,Width=386,Height=193,ShowInTaskbar=false};ApplyOutputWindowChrome(window);window.Show();
        string telemetry=Path.Combine(directory,"output-handoff-motion.ndjson");File.WriteAllText(telemetry,"");
        void Trace(string stage)
        {
            var transform=bug.StrikerBand.RenderTransform as TranslateTransform;
            File.AppendAllText(telemetry,System.Text.Json.JsonSerializer.Serialize(new
            {
                stage,windowWidth=window.ActualWidth,windowHeight=window.ActualHeight,bugWidth=bug.ActualWidth,bugHeight=bug.ActualHeight,
                designWidth=bug.CricketDesignRoot.Width,designHeight=bug.CricketDesignRoot.Height,
                bandY=bug.StrikerBand.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y,bandHeight=bug.StrikerBand.ActualHeight,
                transformY=transform?.Y,baseY=transform is null ? (double?)null : (double)transform.GetAnimationBaseValue(TranslateTransform.YProperty),
                animated=transform?.HasAnimatedProperties,scoreFont=bug.ScoreRuns.FontSize,playerFont=bug.StrikerRuns.FontSize
            })+Environment.NewLine);
        }
        bug.SizeChanged+=(_,args)=>Trace("size-event "+args.PreviousSize+" -> "+args.NewSize);
        try
        {
            await Task.Delay(750);
            double scoreFont=bug.ScoreRuns.FontSize,playerFont=bug.StrikerRuns.FontSize;
            double before=bug.StrikerBand.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y;
            Trace("before-entry");
            host.Request(CricketOverlayKind.Extras);
            Trace("after-entry-immediate");
            checks["compact-entry-leases-original-vector-headers-instead-of-rasterizing-them"]=bug.BattingHeaderPanel.Parent is Canvas && bug.BowlingHeaderPanel.Parent is Canvas && bug.ExtrasValuePanel.Parent is Canvas;
            checks["compact-entry-preserves-current-player-pose-and-score-fonts"]=Math.Abs(bug.StrikerBand.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y-before)<.5 && bug.ScoreRuns.FontSize==scoreFont && bug.StrikerRuns.FontSize==playerFont;
            await Task.Delay(180);double reversing=bug.StrikerBand.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y;Trace("before-reversal");host.Cancel();Trace("after-reversal-immediate");
            checks["compact-reversal-starts-from-current-visual-pose"]=Math.Abs(bug.StrikerBand.TranslatePoint(new System.Windows.Point(),bug.MainScoreboard).Y-reversing)<.5;
            await Task.Delay(850);
            checks["reversed-compaction-restores-original-header-parents-and-auto-sizing"]=ReferenceEquals(bug.BattingHeaderPanel.Parent,bug.PlayerBands) && ReferenceEquals(bug.BowlingHeaderPanel.Parent,bug.PlayerBands)
                && ReferenceEquals(bug.ExtrasValuePanel.Parent,bug.ScoreRailGrid) && double.IsNaN(bug.BattingHeaderPanel.Width) && double.IsNaN(bug.BattingHeaderPanel.Height) && bug.BattingHeaderPanel.Opacity>.99;
            host.Request(CricketOverlayKind.Extras);await Task.Delay(120);Trace("before-pixel-scaling");window.Width=772;window.Height=386;Trace("after-size-properties");await Task.Delay(80);Trace("after-pixel-scaling-pump");
            checks["pure-pixel-scaling-does-not-snap-an-active-compaction-transition"]=!SystemParameters.ClientAreaAnimation || bug.StrikerBand.RenderTransform.HasAnimatedProperties;
            await Task.Delay(650);host.Cancel();await Task.Delay(850);
            match.RecordDelivery(0,CricketDeliveryType.Wicket);match.SelectNewBatter(2);await Task.Delay(80);
            var content=new[] {bug.StrikerContents,bug.NonStrikerContents}.FirstOrDefault(row=>row.Clip is RectangleGeometry {HasAnimatedProperties:true});
            checks["new-batter-uses-a-team-seamed-mask-reveal-on-a-fixed-baseline"]=!SystemParameters.ClientAreaAnimation || content is not null && content.Opacity>.99 && !content.RenderTransform.HasAnimatedProperties
                && QaDescendants(bug).OfType<Border>().Count(border=>Equals(border.Tag,"actor-change-seam"))==1;
            CaptureCricketQa(host,Path.Combine(directory,"batter-team-seam-vector-handoff.png"));
            match.SetStriker(3);await Task.Delay(80);
            checks["rapid-batter-replacement-retains-only-one-outgoing-composite-and-seam"]=!SystemParameters.ClientAreaAnimation || QaDescendants(bug).OfType<Image>().Count(image=>Equals(image.Tag,"actor-change-ghost"))==1
                && QaDescendants(bug).OfType<Border>().Count(border=>Equals(border.Tag,"actor-change-seam"))==1;
            await WaitCricketQaStateAsync(window,()=>new[] {bug.StrikerContents,bug.NonStrikerContents}.All(row=>row.Clip is null && row.Opacity>.99) && !QaDescendants(bug).OfType<Border>().Any(border=>Equals(border.Tag,"actor-change-seam")));
            checks["batter-handoff-removes-every-temporary-mask-and-seam"]=new[] {bug.StrikerContents,bug.NonStrikerContents}.All(row=>row.Clip is null && row.Opacity>.99)
                && !QaDescendants(bug).OfType<Border>().Any(border=>Equals(border.Tag,"actor-change-seam"));
            for(int ball=0;ball<6 && !match.CurrentInnings!.NeedsBowlerSelection;ball++)match.RecordDelivery(0,CricketDeliveryType.Dot);
            match.ConfirmBowler(1);await Task.Delay(80);
            checks["new-bowler-reveals-from-the-opposite-side-without-rolling-old-bowler-figures"]=!SystemParameters.ClientAreaAnimation || bug.BowlerContents.Clip is RectangleGeometry mask && mask.Rect.Left>0
                && bug.BowlerFigures.Opacity>.99 && bug.BowlerContents.Opacity>.99;
            await Task.Delay(750);
            host.MotionEnabled=false;bug.OverlayMotionEnabled=false;match.SetStriker(4);await Task.Delay(30);
            checks["reduced-motion-replacement-is-immediate-and-has-no-temporary-mask"]=bug.StrikerContents.Clip is null && bug.NonStrikerContents.Clip is null
                && !QaDescendants(bug).OfType<Border>().Any(border=>Equals(border.Tag,"actor-change-seam"));
            host.MotionEnabled=true;host.Request(CricketOverlayKind.Extras);await Task.Delay(90);
            window.Content=null;await Task.Delay(30);window.Content=host;await Task.Delay(120);host.Cancel();
            await WaitCricketQaStateAsync(window,()=>ReferenceEquals(bug.BattingHeaderPanel.Parent,bug.PlayerBands)
                && ReferenceEquals(bug.BowlingHeaderPanel.Parent,bug.PlayerBands) && bug.BattingHeaderPanel.Opacity>.99 && double.IsNaN(bug.BattingHeaderPanel.Width));
            checks["closing-during-compaction-cannot-orphan-the-real-headers"]=ReferenceEquals(bug.BattingHeaderPanel.Parent,bug.PlayerBands)
                && ReferenceEquals(bug.BowlingHeaderPanel.Parent,bug.PlayerBands) && bug.BattingHeaderPanel.Opacity>.99 && double.IsNaN(bug.BattingHeaderPanel.Width);
        }
        finally {host.Dispose();bug.Dispose();window.Close();}
        return checks;
    }
}
