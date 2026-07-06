using InventoryService.Application.Inventory.Queries;
using InventoryService.Application.Reservations.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryService.Application;

public static class ApplicationRegistrar
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<GetStockQueryHandler>();
        services.AddScoped<ReserveBatchCommandHandler>();

        return services;
    }
}
