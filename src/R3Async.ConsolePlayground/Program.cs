using System.Threading.Channels;
using R3Async;
using R3Async.Subjects;

var obs = AsyncObservable.Create<int>(async (observer, token) =>
{
    _ = Task.Run(async () =>
    {
        for (var i = 0; i < 100; i++)
        {
            Console.WriteLine($"Producing {i}");
            await observer.OnNextAsync(i, CancellationToken.None);
            Console.WriteLine($"Produced {i}");
        }
    });
    return AsyncDisposable.Empty;
});


await foreach (var x in obs.ToAsyncEnumerable(() => Channel.CreateBounded<int>(0)))
{
    Console.WriteLine($"Consumed {x}");
    Console.ReadLine();
}



