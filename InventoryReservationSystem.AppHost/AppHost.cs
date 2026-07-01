var builder = DistributedApplication.CreateBuilder(args);

var inventoryService = builder.AddProject<Projects.InventoryService_API>("inventoryservice-api");

builder.AddProject<Projects.OrderService_API>("orderservice-api")
    .WithReference(inventoryService)
    .WaitFor(inventoryService);

builder.Build().Run();
