using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrderService.Application.Orders.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Queries.GetOrderAnalytics;

public sealed class GetOrderAnalyticsQueryHandler(IOrderRepository orderRepository)
{
    public async Task<GetOrderAnalyticsQueryResult> HandleAsync(GetOrderAnalyticsQuery query, CancellationToken cancellationToken = default)
    {
        // Tarih aralığı doğrulaması (From <= To) üst katmanda API/Controller seviyesinde yapılarak 31 günlük sınır kontrol edilmiştir.
        // Bu katmanda verinin geçerli sınırlar içinde olduğu ve doğru zaman aralığını hedeflediği varsayılır.
        var orders = await orderRepository.ListDetailedAsync(
            query.From,
            query.To,
            query.Sku,
            query.WarehouseId,
            query.Status,
            cancellationToken);

        var totalOrders = orders.Count;

        // Bölme işlemlerinde paydanın sıfır olması durumu (DivideByZero) kontrol edilerek oranların sıfır dönmesi sağlanır.
        // Payda tespiti ve sıfır kontrolü oranların matematiksel doğruluğu için kritiktir.
        var confirmedCount = orders.Count(o => o.Status == OrderStatus.Confirmed);
        var failedCount = orders.Count(o => o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Expired);

        double successRatio = totalOrders > 0 ? (double)confirmedCount / totalOrders : 0.0;
        double failureRatio = totalOrders > 0 ? (double)failedCount / totalOrders : 0.0;

        // Envanter yoğunluğu (Density) hesabı yapılırken eğer SKU/Warehouse filtreleri geldiyse
        // eşleşme sadece ilgili kalemlerin (OrderLineItem) kendi SKU ve/veya WarehouseId değerlerine göre yapılır.
        // Filtre girilmemişse, filtrelenen siparişlerin tüm satır/kalem verileri hesaplamaya dahil edilir.
        double sumReserved = 0.0;
        double sumRequested = 0.0;

        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                var matchesSku = string.IsNullOrEmpty(query.Sku) || item.Sku == query.Sku;
                var matchesWarehouse = string.IsNullOrEmpty(query.WarehouseId) || item.WarehouseId == query.WarehouseId;

                if (matchesSku && matchesWarehouse)
                {
                    sumReserved += item.ReservedQuantity;
                    sumRequested += item.RequestedQuantity;
                }
            }
        }

        double reservationDensity = sumRequested > 0 ? sumReserved / sumRequested : 0.0;

        // Yalnızca başarılı (Confirmed) siparişler için ortalama tamamlanma süresi (sn cinsinden) hesaplanır.
        // UpdateAt - CreatedAt farkı alınır.
        var confirmedOrders = orders.Where(o => o.Status == OrderStatus.Confirmed).ToList();
        double averageFulfillmentDuration = confirmedOrders.Count > 0
            ? confirmedOrders.Average(o => (o.UpdatedAt - o.CreatedAt).TotalSeconds)
            : 0.0;

        return new GetOrderAnalyticsQueryResult(
            reservationDensity,
            successRatio,
            failureRatio,
            averageFulfillmentDuration,
            totalOrders);
    }
}
