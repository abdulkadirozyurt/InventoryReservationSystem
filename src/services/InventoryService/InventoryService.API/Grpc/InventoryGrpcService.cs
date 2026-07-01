using Grpc.Core;
using InventoryReservationSystem.Contracts.Inventory;

namespace InventoryService.API.Grpc;

public sealed class InventoryGrpcService : InventoryReservations.InventoryReservationsBase
{
    public override Task<ReserveInventoryResponse> Reserve(ReserveInventoryRequest request, ServerCallContext context)
    {
        var response = new ReserveInventoryResponse
        {
            Success = true,
            ReservationId = Guid.CreateVersion7().ToString("N")
        };

        return Task.FromResult(response);
    }

    public override Task<ConfirmReservationResponse> Confirm(ConfirmReservationRequest request, ServerCallContext context)
    {
        var response = new ConfirmReservationResponse
        {
            Success = true,
        };
        return Task.FromResult(response);
    }

    public override Task<ReleaseReservationResponse> Release(ReleaseReservationRequest request, ServerCallContext context)
    {
        var response = new ReleaseReservationResponse
        {
            Success = true,
        };
        return Task.FromResult(response);
    }

}
