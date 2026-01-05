using System.Threading.Channels;
using R3Async;

var obs = AsyncObservable.CreateAsBackgroundJob<int>(async (observer, token) =>
{
    try
    {
        var i = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await observer.OnNextAsync(i++, token);
        }
    }
    catch (OperationCanceledException e)
    {
        Console.WriteLine("Canceling");
        await Task.Delay(2000);
        Console.WriteLine("Canceled");
        throw;
    }
});

await foreach (var x in obs.ToAsyncEnumerable(() => Channel.CreateBounded<int>(0)))
{
    Console.WriteLine($"Consumed {x}");
    var line = Console.ReadLine();
    if(line == "exit")
        break;
}

Console.WriteLine("Breaked");
Console.ReadLine();




