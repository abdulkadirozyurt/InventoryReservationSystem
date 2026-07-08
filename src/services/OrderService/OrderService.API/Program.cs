using InventoryReservationSystem.Contracts.Inventory;
using OrderService.API.Endpoints;
using OrderService.Infrastructure;
using OrderService.Infrastructure.CollectionInitializers;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddInfrastructureServices(builder.Configuration);


builder.Services.AddOpenApi();
builder.Services.AddGrpcClient<InventoryReservations.InventoryReservationsClient>(options =>
{
    options.Address = new Uri(builder.Configuration["InventoryService:Address"]!);
});

var app = builder.Build();

await app.Services.GetRequiredService<MongoCollectionInitializer>().InitializeAsync();

app.UseCorrelationId();


//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//    app.MapScalarApiReference();
//}
app.MapOpenApi();

app.MapScalarApiReference();

app.MapDefaultEndpoints();
var api = app.MapGroup("/api");
api.MapOrderEndpoints();

app.Run();