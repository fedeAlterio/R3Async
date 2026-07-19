using System.Threading.Channels;
using R3;
using R3Async;
using R3Async.R3Interop;
using R3Async.Subjects;
using Unit = R3.Unit;

UnhandledExceptionHandler.Register(e => Console.WriteLine(e));
var pipelineRequests = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
{
    var i = 0;
    while (!token.IsCancellationRequested)
    {
        var a = Console.ReadLine();
        if (a == "ended") return;

        await observer.OnNextAsync(i++, token);
    }
}).Share();

await pipelineRequests.SubscribeAsync(async (x, token) => Console.WriteLine(x));

var aa = pipelineRequests
    .ToAsyncEnumerable(() => Channel.CreateBounded<int>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    }))
    .ToAsyncObservable()
    .Select(x => AsyncObservable.FromAsync(async token =>
    {
        Console.WriteLine($"Starting pipeline {x}");
        try
        {
            await Task.Delay(10000, token);
            Console.WriteLine($"Pipeline {x} completed");
        }
        catch(OperationCanceledException)
        {
            Console.WriteLine($"Pipeline {x} Canceling");
            await Task.Delay(1000);
            Console.WriteLine($"Pipeline {x} Canceled");
        }
    })).Switch();

await aa.WaitCompletionAsync();
Console.WriteLine("Ended");

await new TaskCompletionSource().Task;