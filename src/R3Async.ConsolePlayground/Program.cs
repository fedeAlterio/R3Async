using R3Async;

var timer1 = AsyncObservable.Interval(TimeSpan.FromSeconds(1))
                            .Select(x => AsyncObservable.Defer(() =>
                            {
                                Console.WriteLine($"Subscribed to inner {x}");
                                return AsyncObservable.Interval(TimeSpan.FromMilliseconds(100))
                                                      .Do(y => Console.WriteLine($"Outer tick {x}. Inner tick {y}"))
                                                      .Finally(async () =>
                                                      {
                                                          await Task.Delay(500);
                                                          Console.WriteLine($"Disposed Inner {x}");
                                                      });
                            }))
                            .Switch()
                            .Finally(async () =>
                            {
                                await Task.Delay(500);
                                Console.WriteLine("Disposed outer");
                            })
                            .Select(x => true);

var timer2 = AsyncObservable.Interval(TimeSpan.FromMilliseconds(100))
                            .Select(x => $"Timer 2 tick {x}")
                            .Do(x => Console.WriteLine(x))
                            .Select(x => true)
                            .Finally(async () =>
                            {
                                Console.WriteLine("Disposing timer 2");
                                await Task.Delay(100);
                                Console.WriteLine("Disposed timer 2");
                            });

var merged = timer1.Merge(timer2);
var subscription = await merged.SubscribeAsync(
    async (x, token) => { },
    async (ex, token) => { },
    async result => Console.WriteLine("Completed"),
    CancellationToken.None);

Console.WriteLine("AAA");
Console.ReadLine();
Console.WriteLine("Disposing");
await subscription.DisposeAsync();
Console.WriteLine("Stopped");