using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Bson.Serialization.Serializers;
using OrderService.Domain.Orders;

namespace OrderService.Infrastructure.Mongo;

public static class MongoOrderMappings
{
    public static void Register()
    {
        RegisterOrderLineItem();
        RegisterOrder();
        RegisterOrderHistory();
    }

    private static void RegisterOrderLineItem()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(OrderLineItem)))
            return;

        BsonClassMap.RegisterClassMap<OrderLineItem>(classMap =>
        {
            classMap.AutoMap();
            classMap.GetMemberMap(nameof(OrderLineItem.Sku)).SetElementName("sku");
            classMap.GetMemberMap(nameof(OrderLineItem.WarehouseId)).SetElementName("warehouseId");
            classMap.GetMemberMap(nameof(OrderLineItem.RequestedQuantity)).SetElementName("requestedQuantity");
            classMap.GetMemberMap(nameof(OrderLineItem.ReservedQuantity)).SetElementName("reservedQuantity");
        });
    }

    private static void RegisterOrder()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(Order)))
            return;

        BsonClassMap.RegisterClassMap<Order>(classMap =>
        {
            classMap.AutoMap();
            classMap.SetIdMember(classMap.GetMemberMap(nameof(Order.Id)));
            classMap.IdMemberMap
                .SetElementName("_id")
                .SetSerializer(new StringSerializer(BsonType.ObjectId))
                .SetIdGenerator(StringObjectIdGenerator.Instance);
            classMap.GetMemberMap(nameof(Order.OrderNumber)).SetElementName("orderNumber");
            classMap.GetMemberMap(nameof(Order.ReservationId)).SetElementName("reservationId");
            classMap.GetMemberMap(nameof(Order.Status))
                .SetElementName("status")
                .SetSerializer(new EnumSerializer<OrderStatus>(BsonType.String));
            classMap.GetMemberMap(nameof(Order.Items)).SetElementName("items");
            classMap.GetMemberMap(nameof(Order.CreatedAt)).SetElementName("createdAt");
            classMap.GetMemberMap(nameof(Order.UpdatedAt)).SetElementName("updatedAt");
        });
    }

    private static void RegisterOrderHistory()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(OrderHistory)))
            return;

        BsonClassMap.RegisterClassMap<OrderHistory>(classMap =>
        {
            classMap.AutoMap();
            classMap.SetIdMember(classMap.GetMemberMap(nameof(OrderHistory.Id)));
            classMap.IdMemberMap
                .SetElementName("_id")
                .SetSerializer(new StringSerializer(BsonType.ObjectId))
                .SetIdGenerator(StringObjectIdGenerator.Instance);
            classMap.GetMemberMap(nameof(OrderHistory.OrderNumber)).SetElementName("orderNumber");
            classMap.GetMemberMap(nameof(OrderHistory.FromStatus))
                .SetElementName("fromStatus")
                .SetSerializer(new NullableSerializer<OrderStatus>(new EnumSerializer<OrderStatus>(BsonType.String)));
            classMap.GetMemberMap(nameof(OrderHistory.ToStatus))
                .SetElementName("toStatus")
                .SetSerializer(new EnumSerializer<OrderStatus>(BsonType.String));
            classMap.GetMemberMap(nameof(OrderHistory.ChangedAt)).SetElementName("changedAt");
            classMap.GetMemberMap(nameof(OrderHistory.CorrelationId)).SetElementName("correlationId");
            classMap.GetMemberMap(nameof(OrderHistory.Reason)).SetElementName("reason");
        });
    }
}
