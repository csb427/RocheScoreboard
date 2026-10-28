using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Roche_Scoreboard.Services.Cloud;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

public sealed class RefinementRemoteTests
{
    [AvaloniaTheory]
    [InlineData(RemoteEventTypes.HomeGoal)]
    [InlineData(RemoteEventTypes.CricketDelivery)]
    [InlineData(RemoteEventTypes.TrainingSkip)]
    public void ConcurrentDuplicateRemoteEnvelopesApplyOneOperatorAction(string command)
    {
        var target = DispatchProxy.Create<IRemoteCommandTarget, RecordingRemoteTarget>();
        var recorder = (RecordingRemoteTarget)target;
        var dispatcher = new RemoteCommandDispatcher(target, Dispatcher.UIThread);
        var envelope = new RemoteEvent { EventType = command, Payload = JsonSerializer.SerializeToElement(new { type = "Runs", runs = 1 }) };
        Parallel.For(0, 20, _ => dispatcher.Dispatch(envelope)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, recorder.Applied);
        if (command == RemoteEventTypes.CricketDelivery) Assert.Equal(1, recorder.Cricket.TotalRuns);
        if (command == RemoteEventTypes.HomeGoal) Assert.Equal(1, recorder.Afl.HomeGoals);
        if (command == RemoteEventTypes.TrainingSkip) Assert.Equal(1, recorder.Training.ActiveIndex);
        dispatcher.Dispatch(envelope with { Id = Guid.NewGuid() }); Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, recorder.Applied);
    }
}

// DispatchProxy implements the broad operator interface without hiding a
// second production command path or maintaining dozens of empty test methods.
public class RecordingRemoteTarget : DispatchProxy
{
    public CricketMatchManager Cricket { get; } = CricketReliabilityChecks.CreateMatch(true);
    public MatchManager Afl { get; } = new();
    public TrainingSession Training { get; } = new();
    public int Applied { get; private set; }
    public RecordingRemoteTarget()
    {
        for (int i = 0; i < 3; i++) Training.AddTimer(new TrainingTimer("Test", TimeSpan.FromSeconds(5)));
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Applied++;
        switch (method?.Name)
        {
            case nameof(IRemoteCommandTarget.HomeGoal): Afl.AddGoal(TeamSide.Home); break;
            case nameof(IRemoteCommandTarget.CricketDelivery): Cricket.RecordDelivery((int)args![1]!, Enum.Parse<CricketDeliveryType>((string)args[0]!)); break;
            case nameof(IRemoteCommandTarget.TrainingSkip): Training.Skip(); break;
            default: throw new InvalidOperationException("Unexpected command in deduplication regression: " + method?.Name);
        }
        return null;
    }
}
