using OrderTracker.Domain.Common;

namespace OrderTracker.Domain.Orders.Events;

public sealed record OrderSubmitted(OrderId OrderId, string Reference, decimal Total, string Currency, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderStatusChanged(OrderId OrderId, OrderStatus From, OrderStatus To, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderCurrencyConverted(OrderId OrderId, string From, string To, decimal RateFromBase, DateTimeOffset OccurredAt) : IDomainEvent;
