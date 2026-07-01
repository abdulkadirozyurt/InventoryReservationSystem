using InventoryService.API.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddGrpc();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapGrpcService<InventoryGrpcService>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}



app.Run();

