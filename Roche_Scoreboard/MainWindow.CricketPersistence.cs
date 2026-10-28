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
        // Finalised outcomes remain available for review/reopening after a
        // restart. Explicit reset still clears recovery through its own path.
        if (!_cricketMatch.HasRecoverableCricketState) return;
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
