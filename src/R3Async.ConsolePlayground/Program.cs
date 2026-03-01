using R3Async;

//var a = AsyncObservable.Create<int>(async (observer, token) =>
//{
//    await observer.OnNextAsync(1, default);
//    await Task.Delay(100);
//    await observer.OnCompletedAsync(Result.Failure(new InvalidOperationException()));
//    return AsyncDisposable.Empty;
//}).StatelessPublish(1).RefCount();

//var tasks = Enumerable.Range(1, 100)
//          .Select(x => a.SubscribeAsync(x => Console.WriteLine(x),
//                                        onCompleted: async r => await a.SubscribeAsync(x => Console.WriteLine($"inner  {x}"), cancellationToken: default))
//                        .AsTask());

//await Task.WhenAll(tasks);
//Console.ReadLine();

