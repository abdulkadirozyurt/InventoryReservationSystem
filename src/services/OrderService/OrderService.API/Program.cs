using OrderService.API.Endpoints;
using OrderService.API.Infrastructure;
using OrderService.Application;
using OrderService.Infrastructure;
using OrderService.Infrastructure.CollectionInitializers;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<OrderServiceExceptionHandler>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.GetRequiredService<MongoCollectionInitializer>().InitializeAsync();

app.UseCorrelationId();
app.UseCors();
app.UseExceptionHandler();


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