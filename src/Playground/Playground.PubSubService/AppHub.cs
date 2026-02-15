using Microsoft.AspNetCore.SignalR;
using R3Async;
using R3Async.Subjects;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Playground.PubSubService;

public class AppHub(RefCountTable<string, ISubject<string>> table) : Hub
{
    public async IAsyncEnumerable<string> Subscribe(string channel, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var reference = await table.GetOrCreateAsync(channel, cancellationToken);
        await foreach (var text in reference.Value
                                            .Values
                                            .ToAsyncEnumerable(static () => Channel.CreateUnbounded<string>())
                                            .WithCancellation(cancellationToken))
        {
            yield return text;
        }
    }

    public async Task Publish(string channel, string message)
    {
        await using var reference = await table.GetOrCreateAsync(channel, Context.ConnectionAborted);
        await reference.Value.OnNextAsync(message, Context.ConnectionAborted);
    }
}