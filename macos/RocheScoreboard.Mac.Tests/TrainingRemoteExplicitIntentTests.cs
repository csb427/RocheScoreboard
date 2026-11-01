using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class TrainingRemoteExplicitIntentTests
{
    public static IEnumerable<object[]> Invalid()
    {
        string id="11111111-2222-3333-4444-555555555555";
        foreach(string json in new[]{"{}","null","[]","{\"enabled\":null}","{\"enabled\":\"false\"}","{\"enabled\":1}"})yield return new object[]{RemoteEventTypes.TrainingSetAutoAdvance,json};
        foreach(string direction in new[]{"","sideways"," up "})yield return new object[]{RemoteEventTypes.TrainingReorder,JsonSerializer.Serialize(new{id,direction})};
        foreach(string json in new[]{"{\"id\":\""+id+"\"}","{\"id\":\""+id+"\",\"direction\":false}","{\"direction\":\"up\"}"})yield return new object[]{RemoteEventTypes.TrainingReorder,json};
    }
    [AvaloniaTheory] [MemberData(nameof(Invalid))] [Trait("Evidence","TrainingRemoteExplicitIntent")]
    public void MissingOrMalformedIntentIsNotReinterpretedAsOnOrDown(string type,string json)
    {
        var target=DispatchProxy.Create<IRemoteCommandTarget,TrainingPayloadTarget>();var log=(TrainingPayloadTarget)target;var jobs=new Queue<Action>();var d=new RemoteCommandDispatcher(target,jobs.Enqueue);
        using var doc=JsonDocument.Parse(json);var ev=new RemoteEvent{Id=Guid.NewGuid(),EventType=type,Payload=doc.RootElement.Clone()};d.Dispatch(ev);d.Dispatch(ev);Assert.Single(jobs);jobs.Dequeue()();Assert.Empty(log.Calls);
        var valid=type==RemoteEventTypes.TrainingSetAutoAdvance?JsonSerializer.SerializeToElement(new{enabled=false}):JsonSerializer.SerializeToElement(new{id=Guid.NewGuid(),direction="up"});
        d.Dispatch(new RemoteEvent{Id=Guid.NewGuid(),EventType=type,Payload=valid});Assert.Single(jobs);jobs.Dequeue()();Assert.Single(log.Calls);
    }
    [AvaloniaTheory] [InlineData(true)] [InlineData(false)] [Trait("Evidence","TrainingRemoteExplicitIntent")]
    public void ExplicitBooleansRetainTheirExactValue(bool value)
    {
        var target=DispatchProxy.Create<IRemoteCommandTarget,TrainingPayloadTarget>();var d=new RemoteCommandDispatcher(target,a=>a());d.Dispatch(new RemoteEvent{Id=Guid.NewGuid(),EventType=RemoteEventTypes.TrainingSetAutoAdvance,Payload=JsonSerializer.SerializeToElement(new{enabled=value})});
        var call=Assert.Single(((TrainingPayloadTarget)target).Calls);Assert.Equal(nameof(IRemoteCommandTarget.TrainingSetAutoAdvance),call.Name);Assert.Equal(value,Assert.IsType<bool>(call.Args[0]));
    }
    [AvaloniaTheory] [InlineData("up")] [InlineData("down")] [InlineData("UP")] [InlineData("DOWN")] [Trait("Evidence","TrainingRemoteExplicitIntent")]
    public void SupportedDirectionsAndReplayDedupKeepTheirMeaning(string direction)
    {
        var target=DispatchProxy.Create<IRemoteCommandTarget,TrainingPayloadTarget>();var d=new RemoteCommandDispatcher(target,a=>a());Guid id=Guid.NewGuid();var ev=new RemoteEvent{Id=Guid.NewGuid(),EventType=RemoteEventTypes.TrainingReorder,Payload=JsonSerializer.SerializeToElement(new{id,direction})};d.Dispatch(ev);d.Dispatch(ev);
        var call=Assert.Single(((TrainingPayloadTarget)target).Calls);Assert.Equal(nameof(IRemoteCommandTarget.TrainingReorder),call.Name);Assert.Equal(id,Assert.IsType<Guid>(call.Args[0]));Assert.Equal(direction,call.Args[1]);
    }
    [AvaloniaTheory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [Trait("Evidence","TrainingRemoteSnapshotRange")]
    public void SnapshotSecondsPreserveOrdinaryAndLargeModelDurationsWithoutInt32Wrap(int range)
    {
        long seconds=range==0?95:range==1?int.MaxValue:(long)int.MaxValue+1;
        var session=new TrainingSession{AutoAdvance=false};int count=range==1?2:1;for(int i=0;i<count;i++)session.AddTimer(new TrainingTimer("Timer "+i,TimeSpan.FromSeconds(seconds)));
        var snap=Roche_Scoreboard.MainWindow.BuildTrainingLiveStateSnapshot(session);var t=Assert.IsType<TrainingSnapshotDto>(snap.Training);
        Assert.Equal(seconds*count,t.SessionTotalSeconds);Assert.Equal(seconds*count,t.SessionRemainingSeconds);Assert.Equal(seconds,t.ActiveTotalSeconds);Assert.Equal(seconds,t.ActiveRemainingSeconds);Assert.All(t.Timers,x=>{Assert.Equal(seconds,x.TotalSeconds);Assert.Equal(seconds,x.RemainingSeconds);});
        using var wire=JsonDocument.Parse(JsonSerializer.Serialize(snap,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower}));Assert.Equal(seconds*count,wire.RootElement.GetProperty("training").GetProperty("session_total_seconds").GetInt64());Assert.False(session.IsRunning);Assert.Equal(0,session.ActiveIndex);
    }
}
