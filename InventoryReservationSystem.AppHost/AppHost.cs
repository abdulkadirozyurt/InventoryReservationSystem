var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");
var mongodb = builder.AddMongoDB("mongodb");

var inventoryService = builder.AddProject<Projects.InventoryService_API>("inventoryservice-api")
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(mongodb)
    .WaitFor(mongodb);

builder.AddProject<Projects.OrderService_API>("orderservice-api")
    .WithReference(inventoryService)
    .WaitFor(inventoryService);

builder.Build().Run();
