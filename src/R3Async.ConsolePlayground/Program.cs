using R3Async;

var allBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var total = Environment.ProcessorCount * 10;
int count = total;
for (var i = 0; i < total; i++)
{
    Task.Run(() =>
    {
        if (Interlocked.Decrement(ref count) == 0)
        {
            allBlocked.SetResult();
        }

        new TaskCompletionSource().Task.GetAwaiter().GetResult(); // block indefinitely
    });
}

Console.WriteLine($"Inizio {DateTime.Now}");
await allBlocked.Task;
Console.WriteLine($"Fine {DateTime.Now}");
Console.ReadLine();