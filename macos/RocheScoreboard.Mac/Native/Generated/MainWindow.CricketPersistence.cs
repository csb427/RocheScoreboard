// Generated from MainWindow.CricketPersistence.cs; edit translator/adapters.
// Source SHA-256: 081e9a210b400a017fbd743db95a08831198bacbed015f4999fefc7dde47b093
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        if (Avalonia.Application.Current is RocheScoreboard.Mac.App { IsSelfTestMode: true } || _cricketMatch?.CurrentInnings is null) return;
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
        if (Avalonia.Application.Current is RocheScoreboard.Mac.App { IsSelfTestMode: true }) return;
        (_cricketRecoveryWriter ??= new CricketRecoveryWriter()).Clear();
    }
    private void FlushCricketGameStateSave()
    {
        if (_cricketRecoveryWriter is null) return;
        if (!_cricketRecoveryWriter.Flush(TimeSpan.FromSeconds(5)))
            Debug.WriteLine("[CricketRecovery] Final save did not finish within five seconds or encountered an I/O error. The last successfully committed recovery/backup remains available.");
    }
}
