namespace InventoryService.Application.Reservations.Results.Release;

public sealed record ReleaseBatchResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage
);