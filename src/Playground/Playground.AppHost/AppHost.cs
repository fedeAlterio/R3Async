using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("Redis")
                   .WithRedisInsight();

var pubSubSignalR = builder.AddProject<Playground_PubSubService>("pubSubSignalR");
var serviceA = builder.AddProject<Playground_ServiceA>("serviceA")
                      .WithReplicas(3)
                      .WithReference(redis)
                      .WithReference(pubSubSignalR)
                      .WaitForStart(redis)
                      .WaitForStart(pubSubSignalR);

for (var i = 0; i < 2; i++)
{
    builder.AddProject<Playground_Avalonia>($"AvaloniaClient{i}")
           .WaitForStart(serviceA);
}

builder.Build().Run();