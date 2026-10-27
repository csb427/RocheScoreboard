using System.Diagnostics;
using Roche_Scoreboard.Services;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    private CricketRecoveryWriter? _cricketRecoveryWriter;

    private void QueueCricketGameStateSave()
    {
#if DEBUG
        if (_cricketPreviewBackup is not null) return;
#endif
        if (System.Windows.Application.Current is App { IsSelfTestMode: true } || _cricketMatch?.CurrentInnings is null) return;
        if (_cricketMatch.IsMatchFinalized) { ClearCricketGameStateSave(); return; }
        bool hasStarted = _cricketMatch.AllInnings.Any(innings => innings.Deliveries.Count > 0)
            || _cricketMatch.CurrentInnings.StrikerIndex >= 0 || _cricketMatch.CurrentInnings.CurrentBowlerIndex >= 0;
        if (!hasStarted) return;
        var cricket = _cricketMatch.CapturePersistenceState();
        var state = new GameState
        {
            Sport = "cricket",
            CricketMatchState = cricket,
            HomePrimaryColor = cricket.TeamAPrimaryColor,
            HomeSecondaryColor = cricket.TeamASecondaryColor,
            AwayPrimaryColor = cricket.TeamBPrimaryColor,
            AwaySecondaryColor = cricket.TeamBSecondaryColor,
            HomeLogoPath = cricket.TeamALogoPath,
            AwayLogoPath = cricket.TeamBLogoPath,
            Messages = [.. cricket.Messages]
        };
        (_cricketRecoveryWriter ??= new CricketRecoveryWriter()).Queue(state);
    }
    private void ClearCricketGameStateSave()
    {
        if (System.Windows.Application.Current is App { IsSelfTestMode: true }) return;
        (_cricketRecoveryWriter ??= new CricketRecoveryWriter()).Clear();
    }
    private void FlushCricketGameStateSave()
    {
        if (_cricketRecoveryWriter is null) return;
        if (!_cricketRecoveryWriter.Flush(TimeSpan.FromSeconds(5)))
            Debug.WriteLine("[CricketRecovery] Final save did not finish within five seconds or encountered an I/O error. The last successfully committed recovery/backup remains available.");
    }
}
