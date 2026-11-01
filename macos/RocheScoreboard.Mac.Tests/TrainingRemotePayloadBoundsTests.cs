using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Roche_Scoreboard.Services.Cloud;
using Xunit;
namespace RocheScoreboard.Mac.Tests;
public sealed class TrainingRemotePayloadBoundsTests
{
    public static IEnumerable<object[]> InvalidPayloads()
    {
        string id="11111111-2222-3333-4444-555555555555";var cases=new (string Name,string Type,object Payload)[]{
            ("rounds101",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=101,workSeconds=20,restSeconds=0}}),
            ("rounds-intmax-no-expansion",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=int.MaxValue,workSeconds=20,restSeconds=0}}),
            ("bad-interval-no-single-fallback",RemoteEventTypes.TrainingAddTimer,new{seconds=30,interval=new{rounds=0,workSeconds=20}}),
            ("negative-rest",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=2,workSeconds=20,restSeconds=-1}}),
            ("rest-wrong-type",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=2,workSeconds=20,restSeconds="bad"}}),
            ("interval-wrong-type",RemoteEventTypes.TrainingAddTimer,new{seconds=30,interval="bad"}),
            ("prefix33",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=2,workSeconds=20,titlePrefix=new string('P',33)}}),
            ("update-zero",RemoteEventTypes.TrainingUpdateTimer,new{id,seconds=0,title="Invalid edit"}),
            ("update-missing-seconds",RemoteEventTypes.TrainingUpdateTimer,new{id,title="Invalid edit"}),
            ("update-seconds-string",RemoteEventTypes.TrainingUpdateTimer,new{id,seconds="30",title="Invalid edit"}),
            ("update-title65",RemoteEventTypes.TrainingUpdateTimer,new{id,seconds=30,title=new string('T',65)}),
            ("add-title65",RemoteEventTypes.TrainingAddTimer,new{seconds=30,title=new string('T',65)})
        };
        return cases.Select(c=>new object[]{c.Name,c.Type,JsonSerializer.Serialize(c.Payload)});
    }
    [AvaloniaTheory] [MemberData(nameof(InvalidPayloads))]
    [Trait("Evidence","TrainingRemotePayloadBounds")]
    public void InvalidTrainingPayloadDoesNotInvokeTargetOrBlockAFreshValidCommand(string name,string type,string json)
    {
        var target=DispatchProxy.Create<IRemoteCommandTarget,TrainingPayloadTarget>();var record=(TrainingPayloadTarget)target;var pending=new Queue<Action>();
        var dispatcher=new RemoteCommandDispatcher(target,pending.Enqueue);using var document=JsonDocument.Parse(json);
        var envelope=new RemoteEvent{Id=Guid.NewGuid(),EventType=type,Payload=document.RootElement.Clone()};
        dispatcher.Dispatch(envelope);dispatcher.Dispatch(envelope);Assert.True(pending.Count==1,name+": duplicate envelope must enqueue only once.");pending.Dequeue()();Assert.Empty(record.Calls);
        object valid=type==RemoteEventTypes.TrainingUpdateTimer?new{id=Guid.NewGuid().ToString(),seconds=30,title="Valid"}:new{seconds=30,title="Valid"};
        dispatcher.Dispatch(new RemoteEvent{Id=Guid.NewGuid(),EventType=type,Payload=JsonSerializer.SerializeToElement(valid)});Assert.Single(pending);pending.Dequeue()();
        var call=Assert.Single(record.Calls);Assert.Equal(type==RemoteEventTypes.TrainingUpdateTimer?nameof(IRemoteCommandTarget.TrainingUpdateTimer):nameof(IRemoteCommandTarget.TrainingAddTimer),call.Name);
    }
    public static IEnumerable<object[]> ValidPayloads()
    {
        string id="11111111-2222-3333-4444-555555555555";
        var cases=new (string Name,string Type,object Payload)[]{
            ("title64",RemoteEventTypes.TrainingAddTimer,new{title=new string('T',64),seconds=60}),
            ("interval-prefix32",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=1,workSeconds=20,restSeconds=0,titlePrefix=new string('P',32)}}),
            ("host-round-bound100",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=100,workSeconds=20,restSeconds=0}}),
            ("phone-bounds99-600",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=99,workSeconds=600,restSeconds=600}}),
            ("host-int32-duration",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=100,workSeconds=int.MaxValue,restSeconds=int.MaxValue}}),
            ("valid-update",RemoteEventTypes.TrainingUpdateTimer,new{id,title=new string('T',64),seconds=1}),
            ("optional-title",RemoteEventTypes.TrainingAddTimer,new{seconds=30}),
            ("optional-rest-prefix",RemoteEventTypes.TrainingAddTimer,new{interval=new{rounds=2,workSeconds=20}})
        };
        return cases.Select(c=>new object[]{c.Name,c.Type,JsonSerializer.Serialize(c.Payload)});
    }
    [AvaloniaTheory] [MemberData(nameof(ValidPayloads))]
    [Trait("Evidence","TrainingRemotePayloadBounds")]
    public void ExistingValidTrainingPayloadsAndDuplicatesKeepTheirMeaning(string name,string type,string json)
    {
        var target=DispatchProxy.Create<IRemoteCommandTarget,TrainingPayloadTarget>();var record=(TrainingPayloadTarget)target;var pending=new Queue<Action>();
        var dispatcher=new RemoteCommandDispatcher(target,pending.Enqueue);using var document=JsonDocument.Parse(json);
        var envelope=new RemoteEvent{Id=Guid.NewGuid(),EventType=type,Payload=document.RootElement.Clone()};dispatcher.Dispatch(envelope);dispatcher.Dispatch(envelope);
        Assert.True(pending.Count==1,name+": valid repeated envelope must enqueue once.");pending.Dequeue()();var call=Assert.Single(record.Calls);
        Assert.Equal(type==RemoteEventTypes.TrainingUpdateTimer?nameof(IRemoteCommandTarget.TrainingUpdateTimer):nameof(IRemoteCommandTarget.TrainingAddTimer),call.Name);
        if(name=="host-round-bound100")Assert.Equal(100,Assert.IsType<TrainingIntervalSpec>(call.Args[2]).Rounds);
        if(name=="optional-rest-prefix"){var interval=Assert.IsType<TrainingIntervalSpec>(call.Args[2]);Assert.Equal(0,interval.RestSeconds);Assert.Equal("Set",interval.TitlePrefix);}
        if(name=="title64")Assert.Equal(64,Assert.IsType<string>(call.Args[0]).Length);
    }

}
public class TrainingPayloadTarget:DispatchProxy
{
    public readonly List<(string? Name,object?[] Args)> Calls=[];
    protected override object? Invoke(MethodInfo? method,object?[]? args){Calls.Add((method?.Name,args?.ToArray()??[]));return null;}
}
