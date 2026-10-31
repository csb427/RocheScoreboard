using Roche_Scoreboard.Models;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class TrainingPolicyNotificationTests
{
    [Fact]
    public void PolicyChangesNotifyOnceWithoutChangingTheQueueOrStartingItsClock()
    {
        var session = new TrainingSession();
        var first = new TrainingTimer("First", TimeSpan.FromSeconds(40));
        var second = new TrainingTimer("Second", TimeSpan.FromSeconds(20));
        session.AddTimer(first);
        session.AddTimer(second);
        int changes = 0, structuralChanges = 0;
        session.Changed += () => changes++;
        session.StructuralChanged += () => structuralChanges++;

        Assert.True(session.AutoAdvance);
        session.AutoAdvance = true;
        Assert.Equal(0, changes);
        session.AutoAdvance = false;
        Assert.Equal(1, changes);
        session.AutoAdvance = false;
        Assert.Equal(1, changes);
        session.AutoAdvance = true;
        Assert.Equal(2, changes);
        Assert.Equal(0, structuralChanges);
        Assert.Same(first, session.Timers[0]);
        Assert.Same(second, session.Timers[1]);
        Assert.Equal(0, session.ActiveIndex);
        Assert.False(session.IsRunning);
    }
}
