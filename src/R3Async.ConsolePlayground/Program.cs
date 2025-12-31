using R3Async;

var subscription = await AsyncObservable.Interval(TimeSpan.FromSeconds(1))
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
                                        .SubscribeAsync();

Console.WriteLine("AAA");
Console.ReadLine();
await subscription.DisposeAsync();
Console.WriteLine("Stopped");