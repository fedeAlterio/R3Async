using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("Redis")
                   .WithRedisInsight();

var serviceA = builder.AddProject<Playground_ServiceA>("serviceA")
                      .WithReference(redis)
                      .WaitForStart(redis);

for (var i = 0; i < 2; i++)
{
    builder.AddProject<Playground_Avalonia>($"AvaloniaClient{i}")
           .WaitForStart(serviceA);
}

builder.Build().Run();