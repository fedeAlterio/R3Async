using Playground.PubSubService;
using R3Async;
using R3Async.Subjects;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<RefCountTable<string, ISubject<string>>>(_ =>
{
    return new RefCountTable<string, ISubject<string>>(static (key, token) =>
    {
        var disposableValue = new AsyncDisposableValue<ISubject<string>>
        {
            Value = Subject.Create<string>(),
            Disposable = AsyncDisposable.Empty
        };

        return Task.FromResult(disposableValue);
    });
});
var app = builder.Build();

    app.MapOpenApi();

app.UseHttpsRedirection();
app.MapHub<AppHub>("/hub");
app.Run();
