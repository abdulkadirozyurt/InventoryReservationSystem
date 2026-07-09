namespace InventoryService.Application.Inventory.Exceptions;

public sealed class DuplicateReservationException(string message, Exception innerException) : Exception(message, innerException) { }
