using OrderTracker.Domain.Common;

namespace OrderTracker.Domain.Orders;

/// <summary>A line as submitted, before it becomes part of an order.</summary>
public sealed record NewOrderLine(string Sku, string Name, int Quantity, decimal UnitPrice);

/// <summary>
/// One product on an order. Part of the <see cref="Order"/> aggregate: created and re-priced only by the order.
/// Keeps the price as submitted (<see cref="BaseUnitPrice"/>) so currency conversions never compound rounding.
/// </summary>
public sealed class OrderLine
{
    public const int MaxSkuLength = 64;
    public const int MaxNameLength = 200;
    public const int MaxQuantity = 1_000_000;
    public const decimal MaxUnitPrice = 1_000_000_000m;

    private OrderLine() { } // for EF Core

    internal OrderLine(int lineNo, NewOrderLine input)
    {
        var sku = input.Sku?.Trim() ?? "";
        var name = input.Name?.Trim() ?? "";
        Guard(sku.Length is > 0 and <= MaxSkuLength, "SKU must be 1 to 64 characters.");
        Guard(name.Length is > 0 and <= MaxNameLength, "Item name must be 1 to 200 characters.");
        Guard(input.Quantity is > 0 and <= MaxQuantity, "Quantity must be a whole number from 1 to 1,000,000.");
        Guard(input.UnitPrice >= 0 && input.UnitPrice <= MaxUnitPrice, "Unit price must be between 0 and 1,000,000,000.");
        Guard(input.UnitPrice == Pricing.Round(input.UnitPrice), "Unit price can have at most 2 decimal places.");

        LineNo = lineNo;
        Sku = sku;
        Name = name;
        Quantity = input.Quantity;
        BaseUnitPrice = input.UnitPrice;
        UnitPrice = input.UnitPrice;
        LineTotal = Pricing.LineTotal(Quantity, UnitPrice);
    }

    /// <summary>1-based position in the order.</summary>
    public int LineNo { get; private set; }

    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int Quantity { get; private set; }

    /// <summary>Price in the order's current currency.</summary>
    public decimal UnitPrice { get; private set; }

    public decimal LineTotal { get; private set; }

    /// <summary>Price as submitted, in the order's base currency.</summary>
    public decimal BaseUnitPrice { get; private set; }

    /// <summary>Re-prices from the base price. A null rate restores the submitted price exactly.</summary>
    internal void PriceAt(decimal? rateFromBase)
    {
        UnitPrice = rateFromBase is { } rate ? Pricing.Round(BaseUnitPrice * rate) : BaseUnitPrice;
        LineTotal = Pricing.LineTotal(Quantity, UnitPrice);
    }

    private static void Guard(bool condition, string message)
    {
        if (!condition) throw new DomainException(message);
    }
}

/// <summary>All money arithmetic lives here so it is computed once, on the server, the same way every time.</summary>
public static class Pricing
{
    public const int MoneyDecimals = 2;

    public static decimal LineTotal(int quantity, decimal unitPrice) => Round(quantity * unitPrice);

    public static decimal Subtotal(IEnumerable<decimal> lineTotals) => Round(lineTotals.Sum());

    /// <summary>No tax, shipping or discounts in scope yet, so the total equals the subtotal.</summary>
    public static decimal Total(decimal subtotal) => subtotal;

    public static decimal Round(decimal amount) => Math.Round(amount, MoneyDecimals, MidpointRounding.AwayFromZero);
}
