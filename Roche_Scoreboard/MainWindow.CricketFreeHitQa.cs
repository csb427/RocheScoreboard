using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Views;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private async Task<Dictionary<string,bool>> RunCricketFreeHitChecksAsync(string directory)
    {
        var checks=new Dictionary<string,bool>();
        var profiles=new[] {(Width:386,Height:193,Motion:true),(Width:772,Height:386,Motion:true),(Width:286,Height:193,Motion:false)};
        for(int profile=0;profile<profiles.Length;profile++)
        {
            var size=profiles[profile];
            var match=CricketReliabilityChecks.CreateMatch(true,20); match.FreeHitsEnabled=true;
            var bug=new CricketScorebugControl {EventAnimationsEnabled=true};
            var host=new CricketOverlayHost(match) {AutomaticEnabled=false,MotionEnabled=size.Motion};
            host.Scene.Children.Add(bug); match.MatchChanged+=()=>bug.UpdateFromMatch(match); bug.UpdateFromMatch(match);
            var window=new Window {Content=host,Width=size.Width,Height=size.Height,ShowInTaskbar=false};
            ApplyOutputWindowChrome(window); window.Show();
            try
            {
                await Task.Delay(120);
                match.RecordDelivery(1,CricketDeliveryType.NoBall); await Task.Delay(750);
                var alarm=QaDescendants(host).OfType<FrameworkElement>().Single(element=>Equals(element.Tag,"free-hit-alarm"));
                var title=QaDescendants(alarm).OfType<TextBlock>().Single(text=>text.Text=="FREE HIT");
                var field=QaDescendants(host).OfType<FrameworkElement>().Single(element=>Equals(element.Tag,"free-hit-warning-field"));
                checks[$"free-hit-{size.Width}x{size.Height}-automatic-alarm-ignores-generic-auto-overlay-toggle"]=!host.AutomaticEnabled && match.FreeHitPending && host.FreeHitAlarmShowing && host.Status.Contains("FREE HIT");
                checks[$"free-hit-{size.Width}x{size.Height}-large-title-fits-output-without-overlap"]=title.FontSize==126 && CricketQaBoundsWithin(title,host);
                double titleCentre=title.TranslatePoint(new System.Windows.Point(title.ActualWidth/2,0),host).X;
                await Task.Delay(180);
                checks[$"free-hit-{profile}-warning-field-moves-behind-a-fixed-title"]=field.IsVisible && QaDescendants(field).OfType<System.Windows.Shapes.Rectangle>().Count()==2
                    && !title.RenderTransform.HasAnimatedProperties && Math.Abs(titleCentre-title.TranslatePoint(new System.Windows.Point(title.ActualWidth/2,0),host).X)<.1
                    && (size.Motion && SystemParameters.ClientAreaAnimation ? host.FreeHitMotionClockCount==4 : host.FreeHitMotionClockCount==0);
                if(!size.Motion)
                    checks["free-hit-reduced-motion-keeps-a-static-readable-alarm"]=QaDescendants(alarm).OfType<TextBlock>().All(text=>!text.RenderTransform.HasAnimatedProperties);
                for(int redraw=0;redraw<5;redraw++) match.RaiseMatchChanged();
                checks[$"free-hit-{profile}-redraw-does-not-restart-or-rebuild-the-alarm"]=ReferenceEquals(alarm,QaDescendants(host).OfType<FrameworkElement>().Single(element=>Equals(element.Tag,"free-hit-alarm")));
                CaptureCricketQa(host,Path.Combine(directory,$"free-hit-alarm-{size.Width}x{size.Height}.png"));
                if(profile>0) continue;

                await Task.Delay(250); double before=host.FreeHitAlarmRemaining;
                match.RecordDelivery(1,CricketDeliveryType.NoBall);
                checks["repeat-no-ball-extends-one-alarm-without-another-entry-or-text-rebuild"]=host.FreeHitAlarmRemaining>before+.5 && ReferenceEquals(alarm,QaDescendants(host).OfType<FrameworkElement>().Single(element=>Equals(element.Tag,"free-hit-alarm")));
                match.RecordDelivery(1,CricketDeliveryType.Wide);
                checks["wide-carries-free-hit-without-restarting-the-alarm"]=match.FreeHitPending && match.CurrentInnings!.Deliveries.Last().WasFreeHit && host.FreeHitAlarmShowing;
                host.Cancel();
                await WaitCricketQaStateAsync(window,()=>host.FreeHitMotionClockCount==0 && !field.IsVisible && !host.IsShowing && bug.InfoLabel.Text=="NEXT BALL" && bug.InfoValue.Text=="FREE HIT");
                checks["cancelled-free-hit-warning-field-stops-all-looping-clocks"]=host.FreeHitMotionClockCount==0 && !field.IsVisible;
                checks["operator-can-cancel-alarm-while-pending-free-hit-remains-in-scorebug-context"]=!host.IsShowing && match.FreeHitPending && bug.InfoLabel.Text=="NEXT BALL" && bug.InfoValue.Text=="FREE HIT";
                match.RecordDelivery(1,CricketDeliveryType.NoBall); await Task.Delay(750);
                match.RecordDelivery(0,CricketDeliveryType.Dot);
                await WaitCricketQaStateAsync(window,()=>!match.FreeHitPending && !host.IsShowing && bug.BattingHeaderPanel.Opacity>.99 && bug.BowlingHeaderPanel.Opacity>.99 && bug.InfoValue.Text!="FREE HIT");
                checks["legitimate-delivery-consumes-free-hit-and-smoothly-restores-both-team-bars"]=!match.FreeHitPending && !host.IsShowing && bug.BattingHeaderPanel.Opacity>.99 && bug.BowlingHeaderPanel.Opacity>.99 && bug.InfoValue.Text!="FREE HIT";
                match.RecordDelivery(1,CricketDeliveryType.NoBall); await Task.Delay(150); match.UndoLastDelivery();
                await WaitCricketQaStateAsync(window,()=>!host.IsShowing && !match.FreeHitPending);
                for(int redraw=0;redraw<3;redraw++) match.RaiseMatchChanged();
                checks["undo-during-alarm-entry-cancels-it-without-a-stale-replay"]=!host.IsShowing && !match.FreeHitPending;

                match.RecordDelivery(1,CricketDeliveryType.NoBall); await Task.Delay(750);
                var last=match.CurrentInnings!.Deliveries.Last();
                var correction=CricketMatchManager.DescribeDelivery(match.CurrentInnings,last) with {Type=CricketDeliveryType.Dot,Runs=0};
                bool corrected=match.TryEditDelivery(last,correction,out _);
                await WaitCricketQaStateAsync(window,()=>!host.IsShowing && !match.FreeHitPending);
                checks["editing-no-ball-to-dot-removes-alarm-and-pending-indication"]=corrected && !host.IsShowing && !match.FreeHitPending;
                match.RecordDelivery(1,CricketDeliveryType.NoBall); await Task.Delay(150); match.EndCurrentInnings();
                await WaitCricketQaStateAsync(window,()=>!host.IsShowing && !match.FreeHitPending);
                checks["ending-innings-cancels-free-hit-alarm-and-any-pending-callback"]=!host.IsShowing && !match.FreeHitPending;
                match.StartNextInnings(); match.SetStriker(0); match.SetNonStriker(1); match.ConfirmBowler(0);
                match.RecordDelivery(1,CricketDeliveryType.NoBall); await Task.Delay(750);
                match.RecordDelivery(6,CricketDeliveryType.Six);
                checks["free-hit-boundary-waits-for-alarm-exit-before-celebration"]=bug.MomentAwaitingLayout && !bug.EventShowing && !match.FreeHitPending;
                await WaitCricketQaStateAsync(window,()=>bug.EventShowing && bug.EventWord.Text=="SIX" && !host.IsShowing && bug.BattingHeaderPanel.Opacity>.99);
                checks["free-hit-boundary-celebration-starts-only-after-full-layout-restoration"]=bug.EventShowing && bug.EventWord.Text=="SIX" && !host.IsShowing && bug.BattingHeaderPanel.Opacity>.99;
                match.UndoLastDelivery();
                await WaitCricketQaStateAsync(window,()=>match.FreeHitPending && !host.IsShowing && !bug.EventShowing && bug.InfoValue.Text=="FREE HIT");
                checks["undoing-free-hit-boundary-restores-pending-rule-without-replaying-an-old-alarm"]=match.FreeHitPending && !host.IsShowing && !bug.EventShowing && bug.InfoValue.Text=="FREE HIT";
                match.LoadState(CricketReliabilityChecks.CreateMatch(true,20).ToState()); match.RaiseMatchChanged();
                await WaitCricketQaStateAsync(window,()=>!match.FreeHitPending && !host.IsShowing && !bug.EventShowing && !bug.MomentAwaitingLayout);
                checks["loading-reset-match-clears-alarm-pending-indication-and-delayed-celebration"]=!match.FreeHitPending && !host.IsShowing && !bug.EventShowing && !bug.MomentAwaitingLayout;
            }
            finally {host.Dispose();checks[$"free-hit-{profile}-dispose-stops-every-warning-clock"]=host.FreeHitMotionClockCount==0;bug.Dispose();window.Close();}
        }
        return checks;
    }
}
