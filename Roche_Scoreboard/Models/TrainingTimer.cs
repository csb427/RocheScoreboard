using System;

namespace Roche_Scoreboard.Models;

/// <summary>
/// A single training-session interval. The interval is owned by the
/// <see cref="TrainingSession"/>; this type is just a value-record snapshot of
/// its identity, configured duration, and live remaining time.
/// </summary>
public sealed class TrainingTimer
{
    public TrainingTimer(string title, TimeSpan duration)
    {
        Id = Guid.NewGuid();
        Title = string.IsNullOrWhiteSpace(title) ? "TIMER" : title.Trim();
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        Remaining = Duration;
    }

    public Guid Id { get; }

    public string Title { get; set; }

    /// <summary>Originally-configured duration of this interval.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Live remaining time. While the timer is running this is updated by
    /// <see cref="TrainingSession.Tick"/>; while paused it stays at the
    /// last-known value so resume picks up from there.
    /// </summary>
    public TimeSpan Remaining { get; set; }

    /// <summary>
    /// True once <see cref="Remaining"/> has hit zero and the post-finish
    /// flash has been triggered for this interval.
    /// </summary>
    public bool HasFinished { get; set; }
}
