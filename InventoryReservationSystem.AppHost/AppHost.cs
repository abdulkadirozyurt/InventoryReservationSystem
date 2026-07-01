var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.InventoryService_API>("inventoryservice-api");

builder.AddProject<Projects.OrderService_API>("orderservice-api");

builder.Build().Run();
