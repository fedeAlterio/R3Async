using Playground.ServiceA;
using Playground.ServiceA.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IChatService, SignalRChatService>();
builder.AddRedisClient(connectionName: "Redis");

var app = builder.Build();
app.MapOpenApi();

app.UseHttpsRedirection();
app.MapHub<AppHub>("/hub");
app.Run();
