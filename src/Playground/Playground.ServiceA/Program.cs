using Playground.ServiceA;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IChatService, ChatService>();

var app = builder.Build();
app.MapOpenApi();

app.UseHttpsRedirection();
app.MapHub<AppHub>("/hub");
app.Run();
