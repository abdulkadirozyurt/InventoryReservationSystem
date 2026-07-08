using InventoryService.Application.Inventory.Results;
using InventoryService.Application.Inventory.Services;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Commands.DecreaseStock;

public sealed class DecreaseStockCommandHandler(
    InventoryStockAdjustmentService stockAdjustmentService,
    ILogger<DecreaseStockCommandHandler> logger)
{
    public async Task<StockAdjustmentResult> HandleAsync(DecreaseStockCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Handling decrease stock command. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, Quantity: {Quantity}",
            command.CorrelationId,
            command.Sku,
            command.WarehouseId,
            command.Quantity);

        return await stockAdjustmentService.DecreaseAsync(
            command.Sku,
            command.WarehouseId,
            command.Quantity,
            command.Reason,
            command.CorrelationId,
            cancellationToken);
    }
}
