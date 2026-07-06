namespace InventoryService.Application.Inventory.Exceptions;

public sealed class InventoryStoreUnavailableException(string message, Exception innerException) : Exception(message, innerException) { }
