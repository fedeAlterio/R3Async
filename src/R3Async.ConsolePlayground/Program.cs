using System.Threading.Channels;
using R3Async;
using R3Async.Subjects;

var subjectHub = ConnectionHub.Create(static (string key) => Subject.Create<string>());

var producer = Task.Run(async () =>
{
    await Task.Delay(2000);
    await using var connection = await subjectHub.GetOrCreateConnectionAsync("myKey", CancellationToken.None);
    for (var i = 0; i < 3; i++)
    {
        await connection.Value.OnNextAsync($"Message {i}", CancellationToken.None);
    }

    await connection.Value.OnCompletedAsync(Result.Success);
});

var consumer = Task.Run(async () =>
{
    await using var connection = await subjectHub.GetOrCreateConnectionAsync("myKey", CancellationToken.None);
    await foreach (var message in connection.Value.Values.ToAsyncEnumerable(static () => Channel.CreateUnbounded<string>()))
    {
        Console.WriteLine($"Received: {message}");
    }
});

await Task.WhenAll(producer, consumer);

Console.ReadLine();