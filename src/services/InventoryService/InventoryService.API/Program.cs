using InventoryService.API.Extensions;
using InventoryService.API.Grpc;
using InventoryService.Application;
using InventoryService.Infrastructure;
using InventoryService.Infrastructure.CollectionInitializers;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseInventorySerilog();

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddGrpc();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

// Initialize MongoDB collections with rules
await app.Services.GetRequiredService<MongoCollectionInitializer>().InitializeAsync();

app.UseCorrelationId();

app.MapDefaultEndpoints();
app.MapGrpcService<InventoryGrpcService>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}



app.Run();

