using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var serviceA = builder.AddProject<Playground_ServiceA>("serviceA");
builder.AddProject<Playground_Avalonia>("avalonia")
       .WaitForStart(serviceA);

builder.Build().Run();
