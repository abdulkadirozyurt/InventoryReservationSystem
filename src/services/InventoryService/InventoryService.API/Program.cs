using InventoryService.API.Grpc;
using InventoryService.Infrastructure;
using InventoryService.Infrastructure.Mongo;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddGrpc();

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

