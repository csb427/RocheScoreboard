using System.Text.Json;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;

namespace RocheScoreboard.Mac;

public sealed class DemoSnapshot
{
    public int Version { get; set; } = 1;
    public DemoSport Sport { get; set; }
    public Presentation Screen { get; set; }
    public SerializableState Afl { get; set; } = new();
    public CricketSerializableState Cricket { get; set; } = new();
    public List<TimerSnapshot> Timers { get; set; } = [];
    public int ActiveTimerIndex { get; set; }
    public bool AutoAdvance { get; set; } = true;
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
}

public sealed record TimerSnapshot(string Title, long DurationTicks, long RemainingTicks, bool HasFinished);

public sealed class SessionStore
{
    public string Path { get; }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SessionStore(string? path = null) => Path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RocheScoreboardMacDemo", "session.json");

    public static DemoSnapshot Capture(DemoSession session) => new()
    {
        Sport = session.Sport, Screen = session.Screen, Afl = session.Afl.ToState(), Cricket = session.Cricket.ToState(),
        Timers = session.Training.Timers.Select((t, i) => new TimerSnapshot(t.Title, t.Duration.Ticks,
            i == session.Training.ActiveIndex ? session.Training.ActiveRemaining.Ticks : t.Remaining.Ticks, t.HasFinished)).ToList(),
        ActiveTimerIndex = session.Training.ActiveIndex, AutoAdvance = session.Training.AutoAdvance,
        HomeColor = session.HomeColor, AwayColor = session.AwayColor,
        HomeTextColor = session.HomeTextColor, AwayTextColor = session.AwayTextColor,
        HomeLogo = session.HomeLogo, AwayLogo = session.AwayLogo,
        Stacked = session.Stacked, ReducedMotion = session.ReducedMotion,
        LabelOverride = session.LabelOverride, Messages = [.. session.Messages]
    };

    public static string Serialize(DemoSession session) => JsonSerializer.Serialize(Capture(session), Options);

    public static void Restore(DemoSession session, string json)
    {
        var snapshot = JsonSerializer.Deserialize<DemoSnapshot>(json, Options)
            ?? throw new JsonException("The session file is empty.");
        if (snapshot.Version != 1 || !Enum.IsDefined(snapshot.Sport) || !Enum.IsDefined(snapshot.Screen))
            throw new JsonException("This session format is not supported.");
        if (snapshot.Afl is null || snapshot.Cricket is null || snapshot.Timers is null || snapshot.Messages is null
            || snapshot.Cricket.TeamAPlayers is null || snapshot.Cricket.TeamBPlayers is null || snapshot.Cricket.AllInnings is null)
            throw new JsonException("This session is missing required data.");
        // Validate on detached engines before touching the live session.
        snapshot.Afl.ClockRunning = false;
        new MatchManager().LoadState(snapshot.Afl);
        new CricketMatchManager().LoadState(snapshot.Cricket);
        if (snapshot.Timers.Any(t => t is null || t.DurationTicks < 0 || t.RemainingTicks < 0 || t.RemainingTicks > t.DurationTicks)
            || snapshot.ActiveTimerIndex < -1 || snapshot.ActiveTimerIndex > snapshot.Timers.Count)
            throw new JsonException("Invalid training timer data.");
        session.Afl.PauseClock();
        session.Training.Pause();
        session.ClearCelebration();
        session.SwitchSport(snapshot.Sport);
        session.Afl.LoadState(snapshot.Afl);
        session.Cricket.LoadState(snapshot.Cricket);
        session.Training.Clear();
        foreach (var timer in snapshot.Timers)
            session.Training.AddTimer(new TrainingTimer(timer.Title, TimeSpan.FromTicks(timer.DurationTicks))
            { Remaining = TimeSpan.FromTicks(timer.RemainingTicks), HasFinished = timer.HasFinished });
        for (int i = 0; i < snapshot.ActiveTimerIndex; i++) session.Training.AdvanceAfterFinish(false);
        // A save made during the completion hold must not strand a finished active timer.
        if (session.Training.Active?.HasFinished == true) session.Training.AdvanceAfterFinish(false);
        session.Training.AutoAdvance = snapshot.AutoAdvance;
        session.HomeColor = snapshot.HomeColor; session.AwayColor = snapshot.AwayColor;
        session.HomeTextColor = snapshot.HomeTextColor; session.AwayTextColor = snapshot.AwayTextColor;
        session.HomeLogo = snapshot.HomeLogo; session.AwayLogo = snapshot.AwayLogo;
        session.Stacked = snapshot.Stacked; session.ReducedMotion = snapshot.ReducedMotion;
        session.LabelOverride = snapshot.LabelOverride;
        session.Messages = snapshot.Messages;
        session.Screen = snapshot.Screen;
        session.Notify();
    }

    public void Save(DemoSession session) => AtomicFile.WriteAllText(Path, Serialize(session));

    public string? TryRestore(DemoSession session)
    {
        if (!File.Exists(Path) && !File.Exists(Path + ".bak")) return null;
        foreach (string path in new[] { Path, Path + ".bak" })
        {
            try
            {
                if (!File.Exists(path)) continue;
                Restore(session, File.ReadAllText(path));
                return path == Path ? "Previous demo restored. Clocks are paused." : "Demo recovered from backup. Clocks are paused.";
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NullReferenceException) { }
        }
        return "The previous demo could not be recovered. Its files have been preserved.";
    }
}
