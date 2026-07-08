namespace OrderService.Application.Orders.Exceptions;

public sealed class OrderStoreUnavailableException(string message, Exception innerException) : Exception(message, innerException)
{
}
