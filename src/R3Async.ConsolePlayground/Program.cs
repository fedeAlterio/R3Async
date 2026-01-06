using System.Threading.Channels;
using R3Async;

var obs = AsyncObservable.Interval(TimeSpan.FromSeconds(1));

await foreach (var x in obs.ToAsyncEnumerable(() => Channel.CreateBounded<long>(0)))
{
    Console.WriteLine($"Consumed {x}");
    var line = Console.ReadLine();
    if(line == "exit")
        break;
}

Console.WriteLine("Breaked");
Console.ReadLine();




