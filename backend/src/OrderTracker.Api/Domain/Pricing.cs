namespace OrderTracker.Api.Domain;

/// <summary>All money arithmetic lives here so it is computed once, on the server, the same way every time.</summary>
public static class Pricing
{
    public const int MoneyDecimals = 2;

    public static decimal LineTotal(int quantity, decimal unitPrice) => Round(quantity * unitPrice);

    public static decimal Subtotal(IEnumerable<LineItem> lines) => Round(lines.Sum(l => l.LineTotal));

    /// <summary>No tax, shipping or discounts in scope yet, so the total equals the subtotal.</summary>
    public static decimal Total(decimal subtotal) => subtotal;

    public static decimal Round(decimal amount) =>
        Math.Round(amount, MoneyDecimals, MidpointRounding.AwayFromZero);
}
