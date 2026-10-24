using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace RocheScoreboard.Mac;

public enum DemoSport { Afl, Cricket, Training }
public enum Presentation { Scoreboard, Summary, Statistics, Scoreworm, Intro }

/// <summary>Both windows observe this one session. Clocks are ticked once, by the controller.</summary>
public sealed class DemoSession
{
    public MatchManager Afl { get; } = new();
    public CricketMatchManager Cricket { get; } = new();
    public TrainingSession Training { get; } = new();
    public DemoSport Sport { get; private set; }
    public Presentation Screen { get; set; }
    public string HomeColor { get; set; } = "#2F63D1";
    public string AwayColor { get; set; } = "#A63B3B";
    public string HomeTextColor { get; set; } = "#FFFFFF";
    public string AwayTextColor { get; set; } = "#FFFFFF";
    public string HomeLogo { get; set; } = "";
    public string AwayLogo { get; set; } = "";
    public bool Stacked { get; set; }
    public bool ReducedMotion { get; set; }
    public string LabelOverride { get; set; } = "";
    public List<string> Messages { get; set; } = [];
    public string Announcement { get; private set; } = "";
    public string Celebration { get; private set; } = "";
    private DateTime _announcementUntil;
    private DateTime _celebrationUntil;
    public event Action? Changed;
    public event Action? SportChanged;

    public DemoSession()
    {
        Cricket.TeamAPlayers = MakeRoster("Home");
        Cricket.TeamBPlayers = MakeRoster("Away");
        Cricket.StartMatch();
        Afl.MatchChanged += Notify;
        Cricket.MatchChanged += Notify;
        Training.StructuralChanged += Notify;
        Afl.ScoreEventAdded += score =>
        {
            if (score.Type == ScoreType.Goal)
                Celebrate($"GOAL · {(score.Team == TeamSide.Home ? Afl.HomeName : Afl.AwayName)}");
        };
        Afl.QuarterTimeExpired += () => Celebrate("TIME EXPIRED");
        Cricket.DeliveryAdded += delivery =>
        {
            if (delivery.Type is CricketDeliveryType.Four or CricketDeliveryType.Six or CricketDeliveryType.Wicket)
                Celebrate(delivery.Description);
        };
        Cricket.MilestoneReached += (name, runs) => Celebrate($"{name} · {runs} RUNS");
        Training.IntervalFinished += timer =>
        {
            bool isLast = Training.UpNext is null;
            Celebrate(isLast ? "SESSION COMPLETE" : $"{timer.Title} · COMPLETE", isLast ? 10 : 7.5);
            // Match the Windows view: the next stopwatch starts immediately,
            // while the finished interval's presentation remains visible.
            Training.AdvanceAfterFinish(!isLast && Training.AutoAdvance);
        };
    }

    public static List<CricketPlayer> MakeRoster(string prefix) => Enumerable.Range(1, 11)
        .Select(i => new CricketPlayer { FirstName = prefix, LastName = $"Player {i}" }).ToList();

    public void SwitchSport(DemoSport sport)
    {
        if (Sport == sport) return;
        Afl.PauseClock();
        Training.Pause();
        ClearCelebration();
        Sport = sport;
        Screen = Presentation.Scoreboard;
        Celebration = "";
        SportChanged?.Invoke();
        Notify();
    }

    public void ClearCelebration() { Celebration = ""; _celebrationUntil = DateTime.MinValue; }

    public void Tick()
    {
        if (Sport == DemoSport.Afl) Afl.Tick();
        if (Sport == DemoSport.Training)
        {
            Training.Tick();
        }
        if (DateTime.UtcNow >= _announcementUntil) Announcement = "";
        if (DateTime.UtcNow >= _celebrationUntil) Celebration = "";
    }

    public void Announce(string text)
    {
        Announcement = text.Trim();
        _announcementUntil = DateTime.UtcNow.AddSeconds(4.2);
        Notify();
    }

    public void Celebrate(string text, double seconds = 3)
    {
        Celebration = text;
        _celebrationUntil = DateTime.UtcNow.AddSeconds(seconds);
        Notify();
    }

    public string QuarterLabel => !string.IsNullOrWhiteSpace(LabelOverride) ? LabelOverride :
        Afl.IsFullTime ? "FT" : Afl.CanContinueQuarter ? Afl.CompletedQuarterCount switch
        { 1 => "QT", 2 => "HT", 3 => "3QT", _ => $"Q{Afl.Quarter}" } : $"Q{Afl.Quarter}";

    public string Header => Sport switch
    {
        DemoSport.Afl => $"{Afl.HomeName}  {Afl.HomeGoals}.{Afl.HomeBehinds} ({Afl.HomeTotal})     {QuarterLabel}  {Clock(Afl.DisplayClock)}     {Afl.AwayName}  {Afl.AwayGoals}.{Afl.AwayBehinds} ({Afl.AwayTotal})",
        DemoSport.Cricket => $"{Cricket.BattingTeamName}  {Cricket.ScoreDisplay}     {Cricket.OversDisplay} OVERS     INNINGS {Cricket.CurrentInningsNumber}",
        _ => $"{Training.Active?.Title ?? (Training.IsExhausted ? "SESSION COMPLETE" : "TRAINING")}     {Clock(Training.ActiveRemaining)}"
    };

    public static string Clock(TimeSpan value) => $"{(int)Math.Max(0, value.TotalMinutes):00}:{Math.Max(0, value.Seconds):00}";

    public string AflStory()
    {
        var run = AflGoalRunDetector.DetectBest(Afl.Events);
        if (run is not null)
        {
            string name = run.Team == TeamSide.Home ? Afl.HomeName : Afl.AwayName;
            return run.IsConsecutive ? $"{name} · {run.TeamGoals} CONSECUTIVE GOALS" : $"{name} · {run.TeamGoals} OF THE LAST {run.WindowGoals} GOALS";
        }
        var drought = AflDroughtCalculator.Calculate(Afl);
        if (drought.Match.Elapsed.TotalMinutes >= 10)
            return $"{(int)drought.Match.Elapsed.TotalMinutes} MINUTES WITHOUT A {drought.Match.Kind.ToString().ToUpperInvariant()}";
        return Afl.Margin == 0 ? "SCORES LEVEL" : $"{(Afl.Margin > 0 ? Afl.HomeName : Afl.AwayName)} LEADS BY {Math.Abs(Afl.Margin)}";
    }

    public void Notify() => Changed?.Invoke();
}
