

using R3Async;
using R3Async.Subjects;
var s = Subject.Create<int>();
var b = from a in s.Values
        group a by a into g 
        select g.FirstOrDefaultAsync();
var subscription = await s.Values
 .GroupBy(x => x, static x => Subject.Create<int>())
 .Select(g => g.Take(1)
               .Do(x => Console.WriteLine(x))
               .OnDispose(() => Console.WriteLine("Finished")))
 .Merge()
 .SubscribeAsync();

await s.OnNextAsync(1, default);
await s.OnNextAsync(2, default);
await s.OnNextAsync(2, default);
await s.OnNextAsync(3, default);
await s.OnNextAsync(4, default);

await subscription.DisposeAsync();

Console.ReadLine();