namespace Roche_Scoreboard.Models;

public enum CricketBreakKind { None, Lunch, Tea, Rain, Drinks, Stumps, Other }
public enum CricketBatterReplacementReason { RetiredHurt, RetiredNotOut, RetiredOut, WrongPlayer }
public sealed record CricketLineupPlayer
{
    public int Index { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public bool IsWicketkeeper { get; init; }
    public bool IsCaptain { get; init; }
    public int BattingPosition { get; init; }
}

public sealed partial class CricketMatchManager
{
    public CricketBreakKind BreakKind { get; private set; }
    public string BreakNote { get; private set; } = "";
    public bool IsMatchFinalized { get; private set; }
    public string? FinalResult { get; private set; }
    public string LastOperationError { get; private set; } = "";
    public bool CompletionDecisionRequired => !TossPending && !IsMatchFinalized && CurrentInnings?.IsComplete == true;
    public string CompletionReason => CurrentInnings is not { } innings ? "No innings"
        : innings.IsAllOut ? "All out" : !innings.IgnoreTarget && Target is int target && innings.TotalRuns >= target ? "Target reached"
        : !innings.IgnoreOverLimit && Format == CricketFormat.LimitedOvers && innings.LegalBallsBowled >= CurrentOverLimit * 6 ? "Over limit reached"
        : innings.IsDeclared ? "Innings declared" : "Innings closed";
    public bool CanContinueCurrentInnings => !TossPending && !IsMatchFinalized && CurrentInnings is { IsComplete: true, IsAllOut: false };
    public bool HasScorecardAdjustments => CurrentInnings?.ScorecardChanges.Count > 0;

    public void SetBreak(CricketBreakKind kind, string note = "")
    {
        if (!Enum.IsDefined(kind) || IsMatchFinalized) return;
        BreakKind = kind; BreakNote = kind == CricketBreakKind.None ? "" : (note ?? "").Trim();
        MatchChanged?.Invoke();
    }
    public bool TryContinueCurrentInnings(out string error)
    {
        error = ""; LastOperationError = "";
        if (!CanContinueCurrentInnings)
        { error = LastOperationError = "This innings cannot continue. An all-out innings needs a scorecard correction or a new innings."; return false; }
        var innings = CurrentInnings!;
        innings.IgnoreTarget = true;
        innings.IgnoreOverLimit = true;
        innings.IsComplete = false; innings.IsDeclared = false;
        BreakKind = CricketBreakKind.None; BreakNote = "";
        RefreshBatterSelectionState(innings);
        innings.NeedsBowlerSelection = innings.CurrentBowler is null || innings.LegalBallsBowled > 0 && innings.BallsInCurrentOver == 0;
        DeliveryRevision++; MatchChanged?.Invoke(); return true;
    }
    public bool TryStartAdditionalInnings(bool teamABats, out string error)
    {
        error = ""; LastOperationError = "";
        if (TossPending || IsMatchFinalized || CurrentInnings is null)
        { error = LastOperationError = "Complete the toss and keep the game open before starting another innings."; return false; }
        string team = teamABats ? TeamAName : TeamBName;
        string other = teamABats ? TeamBName : TeamAName;
        if (AllInnings.Any(i => i.BattingTeamName == team) && !AllInnings.Any(i => i.BattingTeamName == other))
        { error = LastOperationError = "Both teams must have their first innings before either starts a second innings."; return false; }
        if (AllInnings.Count >= 4 || AllInnings.Count(i => i.BattingTeamName == team) >= 2)
        { error = LastOperationError = "Each team can bat at most twice. End the game or continue the current innings."; return false; }
        CurrentInnings.IsComplete = true; CurrentInnings.NeedsBatterSelection = CurrentInnings.NeedsBowlerSelection = false;
        CurrentInningsNumber = AllInnings.Count + 1;
        TotalInnings = Math.Max(TotalInnings, CurrentInningsNumber >= 3 ? 4 : CurrentInningsNumber);
        _nextBattingTeamOverride = teamABats;
        StartNewInnings();
        BreakKind = CricketBreakKind.None; BreakNote = "";
        DeliveryRevision++; MatchChanged?.Invoke(); return true;
    }
    public bool TryFinishMatch(out string error)
        => TrySetMatchOutcome(CricketMatchResultKind.ScoreBased, "", WorkflowToken, out error);
    private bool? _nextBattingTeamOverride;
    private void ResetWorkflowState()
    { BreakKind = CricketBreakKind.None; BreakNote = ""; IsMatchFinalized = false; FinalResult = null; LastOperationError = ""; _nextBattingTeamOverride = null; ResetExceptionalState(); }

    public List<CricketLineupPlayer> GetLineup(bool teamA)
        => (teamA ? TeamAPlayers : TeamBPlayers).Select((player, index) => new CricketLineupPlayer
        {
            Index = index,
            FirstName = player.FirstName,
            LastName = player.LastName,
            IsWicketkeeper = player.IsWicketkeeper,
            IsCaptain = player.IsCaptain,
            BattingPosition = player.BattingPosition > 0 ? player.BattingPosition : index + 1
        }).ToList();

    public bool TryUpdateTeam(bool teamA, string name, string abbreviation, IReadOnlyList<CricketLineupPlayer> players, out string error)
    {
        error = "";
        var roster = teamA ? TeamAPlayers : TeamBPlayers;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(abbreviation) || players is null || players.Count < 2
            || players.Any(p => p is null || string.IsNullOrWhiteSpace(p.FirstName + p.LastName)) || players.Count(p => p.IsCaptain) > 1)
        { error = "Enter a team name, abbreviation and at least two named players, with at most one captain."; return false; }
        var existing = players.Where(p => p.Index >= 0).ToArray();
        if (existing.Select(p => p.Index).Distinct().Count() != existing.Length || existing.Any(p => p.Index >= roster.Count)
            || Enumerable.Range(0, roster.Count).Any(index => !existing.Any(p => p.Index == index)))
        { error = "Keep existing squad players so historical balls remain linked. You can rename, reorder or add players."; return false; }
        string oldName = teamA ? TeamAName : TeamBName;
        if (string.Equals(name.Trim(), teamA ? TeamBName : TeamAName, StringComparison.OrdinalIgnoreCase))
        { error = "Use different team names so innings and totals remain distinguishable."; return false; }
        var values = players.Select((p, position) => p with { BattingPosition = p.BattingPosition > 0 ? p.BattingPosition : position + 1 }).ToList();
        foreach (var p in values)
        {
            int index = p.Index < 0 ? roster.Count : p.Index;
            if (p.Index < 0) roster.Add(new CricketPlayer());
            SetIdentity(roster[index], p, index);
        }
        string newName = name.Trim();
        foreach (var innings in AllInnings)
        {
            bool batting = innings.BattingTeamName == oldName;
            bool bowling = innings.BowlingTeamName == oldName;
            if (batting) { innings.BattingTeamName = newName; SyncIdentities(innings.BattingOrder, roster); }
            if (bowling) { innings.BowlingTeamName = newName; SyncIdentities(innings.BowlingAttack, roster); }
            for (int i = 0; i < innings.Deliveries.Count; i++)
            {
                var d = innings.Deliveries[i];
                var snapshot = d.StateBefore is null ? null : CopyStateWithIdentities(d.StateBefore, batting ? roster : null, bowling ? roster : null);
                innings.Deliveries[i] = CopyDelivery(d, snapshot, batsmanName: batting && d.StrikerIndex >= 0 && d.StrikerIndex < innings.BattingOrder.Count ? innings.BattingOrder[d.StrikerIndex].DisplayName : null,
                    bowlerName: bowling && d.BowlerIndex >= 0 && d.BowlerIndex < innings.BowlingAttack.Count ? innings.BowlingAttack[d.BowlerIndex].DisplayName : null);
            }
            for (int i = 0; i < innings.ScorecardChanges.Count; i++)
                innings.ScorecardChanges[i] = innings.ScorecardChanges[i] with { Before = CopyStateWithIdentities(innings.ScorecardChanges[i].Before, batting ? roster : null, bowling ? roster : null) };
        }
        if (TossWinner == oldName) TossWinner = newName;
        if (teamA) { TeamAName = newName; TeamAAbbr = abbreviation.Trim(); }
        else { TeamBName = newName; TeamBAbbr = abbreviation.Trim(); }
        RefreshFinalizedResult();
        DeliveryRevision++; MatchChanged?.Invoke(); return true;
    }
    public bool TryUpdateTeams(string nameA, string abbreviationA, IReadOnlyList<CricketLineupPlayer> playersA,
        string nameB, string abbreviationB, IReadOnlyList<CricketLineupPlayer> playersB, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB)
            || string.Equals(nameA.Trim(), nameB.Trim(), StringComparison.OrdinalIgnoreCase))
        { error = "Enter two distinct team names."; return false; }
        // Stage both changes on detached innings/player containers. A unique
        // temporary name allows A/B names to swap without exposing ambiguity
        // or notifying live views between either team's validation and commit.
        var staged = new CricketMatchManager();
        staged.LoadState(CapturePersistenceState());
        string temporary = "Roster update " + Guid.NewGuid().ToString("N");
        if (!staged.TryUpdateTeam(true, temporary, abbreviationA, playersA, out error)
            || !staged.TryUpdateTeam(false, nameB, abbreviationB, playersB, out error)
            || !staged.TryUpdateTeam(true, nameA, abbreviationA, staged.GetLineup(true), out error)) return false;
        LoadState(staged.CapturePersistenceState());
        return true;
    }

    private static void SetIdentity(CricketPlayer target, CricketLineupPlayer source, int rosterIndex)
    {
        target.FirstName = source.FirstName.Trim(); target.LastName = source.LastName.Trim(); target.IsCaptain = source.IsCaptain;
        target.IsWicketkeeper = source.IsWicketkeeper; target.BattingPosition = source.BattingPosition; target.RosterIndex = rosterIndex;
    }
    private static void SyncIdentities(List<CricketPlayer> players, List<CricketPlayer> roster)
    {
        for (int i = 0; i < players.Count; i++)
        {
            int index = players[i].RosterIndex >= 0 ? players[i].RosterIndex : i;
            if (index >= roster.Count) continue;
            var source = roster[index]; var target = players[i];
            target.FirstName = source.FirstName; target.LastName = source.LastName; target.IsCaptain = source.IsCaptain;
            target.IsWicketkeeper = source.IsWicketkeeper; target.BattingPosition = source.BattingPosition; target.RosterIndex = index;
        }
        var known = players.Select(p => p.RosterIndex).ToHashSet();
        foreach (var p in roster.Where(p => !known.Contains(p.RosterIndex))) { var copy = p.Clone(); copy.ResetBatting(); copy.ResetBowling(); players.Add(copy); }
    }
}
