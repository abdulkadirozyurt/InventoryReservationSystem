using OrderService.Application.Orders.Commands.CancelOrder;
using OrderService.Application.Orders.Results;

namespace OrderService.Application.Orders.Commands.BulkCancelOrders;

public sealed class BulkCancelOrdersCommandHandler(CancelOrderCommandHandler cancelOrderCommandHandler)
{
    public async Task<IReadOnlyCollection<OrderOperationResult>> HandleAsync(
        BulkCancelOrdersCommand command,
        CancellationToken cancellationToken = default)
    {
        var results = new List<OrderOperationResult>(command.OrderNumbers.Count);

        foreach (var orderNumber in command.OrderNumbers)
        {
            var result = await cancelOrderCommandHandler.HandleAsync(
                new CancelOrderCommand(orderNumber, command.CorrelationId, command.Reason),
                cancellationToken);

            results.Add(result);
        }

        return results;
    }
}
