using OrderTracker.Application.Orders.SubmitOrder;

namespace OrderTracker.Api.Contracts;

// The HTTP request shapes. Fields are nullable so the application validator can report missing or
// malformed values with field-level messages instead of the serializer failing with a generic 400.

public sealed record CreateOrderRequest(
    string? ExternalReference,
    CustomerRequest? Customer,
    string? Currency,
    string? Notes,
    List<LineItemRequest?>? LineItems)
{
    public SubmitOrderCommand ToCommand() => new(
        ExternalReference,
        Customer is null ? null : new CustomerInput(Customer.Email, Customer.Name),
        Currency,
        Notes,
        LineItems?.Select(l => l is null ? null : new LineItemInput(l.Sku, l.Name, l.Quantity, l.UnitPrice)).ToList());
}

public sealed record CustomerRequest(string? Email, string? Name);

/// <summary>Quantity is a decimal so "1.5" gets a clear "must be a whole number" message.</summary>
public sealed record LineItemRequest(string? Sku, string? Name, decimal? Quantity, decimal? UnitPrice);

public sealed record ChangeStatusRequest(string? Status);

public sealed record ChangeCurrencyRequest(string? Currency);
