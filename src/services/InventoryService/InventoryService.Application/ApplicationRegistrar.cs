using InventoryService.Application.Inventory.Queries;
using InventoryService.Application.Reservations.Commands.ConfirmReservation;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Application.Reservations.Commands.ReserveBatch;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryService.Application;

public static class ApplicationRegistrar
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<GetStockQueryHandler>();
        services.AddScoped<ReserveBatchCommandHandler>();
        services.AddScoped<ReleaseBatchCommandHandler>();
        services.AddScoped<ConfirmReservationCommandHandler>();

        return services;
    }
}
