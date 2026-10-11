using System.Globalization;
using OrderTracker.Api.Domain;

namespace OrderTracker.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(3, "19.99", "59.97")]
    [InlineData(1, "0", "0")]
    [InlineData(1_000_000, "999.99", "999990000")]
    public void Line_total_is_quantity_times_unit_price(int quantity, string unitPrice, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Pricing.LineTotal(quantity, decimal.Parse(unitPrice, CultureInfo.InvariantCulture)));

    [Fact]
    public void Decimal_arithmetic_avoids_floating_point_drift()
    {
        // 0.1 + 0.2 != 0.3 in binary floating point; money must be exact.
        var lines = new[]
        {
            new LineItem("A", "A", 1, 0.10m, Pricing.LineTotal(1, 0.10m)),
            new LineItem("B", "B", 1, 0.20m, Pricing.LineTotal(1, 0.20m)),
        };

        Assert.Equal(0.30m, Pricing.Subtotal(lines));
    }

    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("2.344", "2.34")]
    [InlineData("-2.345", "-2.35")]
    public void Rounds_half_away_from_zero(string amount, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Pricing.Round(decimal.Parse(amount, CultureInfo.InvariantCulture)));
}
