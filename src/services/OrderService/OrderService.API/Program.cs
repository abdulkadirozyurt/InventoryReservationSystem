using OrderService.API.Endpoints;
using OrderService.Application;
using OrderService.Infrastructure;
using OrderService.Infrastructure.CollectionInitializers;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddOpenApi();

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