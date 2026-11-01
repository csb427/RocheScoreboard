using System.Collections.Generic;
using System.Text.Json;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Views;

namespace Roche_Scoreboard;

public partial class MainWindow
{
    // Existing --cricket-workflow-self-test-output entry point runs these on
    // the WPF dispatcher. A separate fixture never edits the harness's match.
    private Dictionary<string, bool> RunAflEditorInterruptionChecks()
    {
        var checks = new Dictionary<string, bool>();
        var fixture = new MainWindow();
        try
        {
            fixture._match.AddGoal(TeamSide.Home);
            var original = fixture._match.Events[0]; var before = original.Clone();
            fixture._match.UndoLastScore(); fixture._match.AddGoal(TeamSide.Away);
            string replaced = JsonSerializer.Serialize(fixture._match.ToState());
            foreach (var result in new[] { ScoreEventEditorWindow.EditorResult.Saved, ScoreEventEditorWindow.EditorResult.Deleted })
            {
                bool applied = fixture.TryApplyScoreLogEdit(original, before, result, TeamSide.Home, ScoreType.Behind,
                    before.GameTime, before.Quarter, out string error);
                checks["afl-stale-editor-" + result + "-preserves-replacement-event"] = !applied && error.Length > 0
                    && JsonSerializer.Serialize(fixture._match.ToState()) == replaced;
            }
            var current = fixture._match.Events[0]; var captured = current.Clone();
            fixture._match.AddBehind(TeamSide.Home);
            checks["afl-editor-permits-unrelated-later-score"] = fixture.TryApplyScoreLogEdit(current, captured,
                ScoreEventEditorWindow.EditorResult.Saved, TeamSide.Away, ScoreType.Behind, captured.GameTime, captured.Quarter, out _)
                && fixture._match.HomeTotal == 1 && fixture._match.AwayTotal == 1 && fixture._match.Events.Count == 2;
            string unchanged = JsonSerializer.Serialize(fixture._match.ToState());
            checks["afl-cancelled-editor-has-no-scoring-effect"] = !fixture.TryApplyScoreLogEdit(current, current.Clone(),
                ScoreEventEditorWindow.EditorResult.Cancelled, TeamSide.Home, ScoreType.Goal, current.GameTime, current.Quarter, out _)
                && JsonSerializer.Serialize(fixture._match.ToState()) == unchanged;
        }
        finally { fixture.ResetScoreAnimationQueue(); fixture.Close(); }
        return checks;
    }
}
