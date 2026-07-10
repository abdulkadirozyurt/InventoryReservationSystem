namespace InventoryService.Infrastructure.Mongo;

public sealed class SeedDataOptions
{
    public const string SectionName = "SeedData";

    public bool Enabled { get; set; } = true;
}
