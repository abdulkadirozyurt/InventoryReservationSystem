using InventoryService.API.Grpc;
using InventoryService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddGrpc();

builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

app.UseCorrelationId();

app.MapDefaultEndpoints();
app.MapGrpcService<InventoryGrpcService>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}



app.Run();

