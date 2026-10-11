namespace OrderTracker.Api.Contracts;

// Request fields are nullable on purpose: the validator reports missing or malformed values
// with field-level messages instead of the serializer failing with a generic 400.

public sealed record CreateOrderRequest(
    string? ExternalReference,
    CustomerInput? Customer,
    string? Currency,
    string? Notes,
    List<LineItemInput>? LineItems);

public sealed record CustomerInput(string? Email, string? Name);

/// <summary>Quantity is a decimal so "1.5" gets a clear "must be a whole number" message.</summary>
public sealed record LineItemInput(string? Sku, string? Name, decimal? Quantity, decimal? UnitPrice);

public sealed record ChangeStatusRequest(string? Status);

public sealed record ChangeCurrencyRequest(string? Currency);
