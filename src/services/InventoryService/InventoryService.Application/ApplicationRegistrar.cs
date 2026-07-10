using Microsoft.Extensions.DependencyInjection;
using InventoryService.Application.Inventory.Queries;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability;
using InventoryService.Application.Inventory.Commands.DecreaseStock;
using InventoryService.Application.Inventory.Commands.IncreaseStock;
using InventoryService.Application.Inventory.Commands.CreateSnapshot;
using InventoryService.Application.Inventory.Commands.RebalanceWarehouse;
using InventoryService.Application.Inventory.Commands.RestoreSnapshot;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Application.Reservations.Commands.ReserveBatch;
using InventoryService.Application.Reservations.Commands.ConfirmReservation;
using InventoryService.Application.Reservations.Commands.AdminReleaseReservation;
using InventoryService.Application.Observability.Abstractions;

namespace InventoryService.Application;

public static class ApplicationRegistrar
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<GetStockQueryHandler>();
        services.AddScoped<ReserveBatchCommandHandler>();
        services.AddScoped<ReleaseBatchCommandHandler>();
        services.AddScoped<ConfirmReservationCommandHandler>();
        services.AddScoped<AdminReleaseReservationCommandHandler>();
        services.AddScoped<InventoryStockAdjustmentService>();
        services.AddScoped<LowStockAlertService>();
        services.AddScoped<IncreaseStockCommandHandler>();
        services.AddScoped<DecreaseStockCommandHandler>();
        services.AddScoped<RebalanceWarehouseCommandHandler>();
        services.AddScoped<CreateInventorySnapshotCommandHandler>();
        services.AddScoped<RestoreInventorySnapshotCommandHandler>();
        services.AddSingleton<IInventoryServiceMetrics, InventoryServiceMetrics>();

        return services;
    }
}
