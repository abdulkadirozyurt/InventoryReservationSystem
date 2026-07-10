using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Orders.Commands.BulkCancelOrders;
using OrderService.Application.Orders.Commands.CancelOrder;
using OrderService.Application.Orders.Commands.ConfirmOrder;
using OrderService.Application.Orders.Commands.CreateOrder;
using OrderService.Application.Orders.Queries.GetOrder;
using OrderService.Application.Orders.Queries.GetOrderAnalytics;
using OrderService.Application.Orders.Queries.ListOrders;

namespace OrderService.Application;

public static class ApplicationRegistrar
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<CreateOrderCommandHandler>();
        services.AddScoped<GetOrderQueryHandler>();
        services.AddScoped<ListOrdersQueryHandler>();
        services.AddScoped<GetOrderAnalyticsQueryHandler>();
        services.AddScoped<ConfirmOrderCommandHandler>();
        services.AddScoped<CancelOrderCommandHandler>();
        services.AddScoped<BulkCancelOrdersCommandHandler>();

        return services;
    }
}
