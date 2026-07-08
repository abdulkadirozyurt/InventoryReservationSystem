using InventoryService.Application.Inventory.Results;
using InventoryService.Application.Inventory.Services;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Commands.IncreaseStock;

public sealed class IncreaseStockCommandHandler(
    InventoryStockAdjustmentService stockAdjustmentService,
    ILogger<IncreaseStockCommandHandler> logger)
{
    public async Task<StockAdjustmentResult> HandleAsync(IncreaseStockCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Handling increase stock command. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, Quantity: {Quantity}",
            command.CorrelationId,
            command.Sku,
            command.WarehouseId,
            command.Quantity);

        return await stockAdjustmentService.IncreaseAsync(
            command.Sku,
            command.WarehouseId,
            command.Quantity,
            command.Reason,
            command.CorrelationId,
            cancellationToken);
    }
}
