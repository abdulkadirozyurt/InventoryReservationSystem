namespace InventoryService.Application.Observability;

internal static class InventoryErrorClass
{
    public const string Validation = "validation";
    public const string Business = "business";
    public const string Timeout = "timeout";
    public const string Transient = "transient";
    public const string System = "system";
}
