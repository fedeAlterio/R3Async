using System.Threading.Channels;
using R3;
using R3Async;
using R3Async.R3Interop;
using Unit = R3.Unit;

var timer = Observable.Interval(TimeSpan.FromSeconds(1))
    .Scan(0, (acc, x) => acc + 1);
timer.ToAsyncObservable(BackpressureStrategy.FromBoundedChannel(1))
    .ToObservable(new ToObservableConfiguration
    {
        DisposeStrategy = AsyncToSyncStrategy.FireAndForget(),
        SubscribeStrategy = AsyncToSyncStrategy.FireAndForget()
    });
await new TaskCompletionSource().Task;