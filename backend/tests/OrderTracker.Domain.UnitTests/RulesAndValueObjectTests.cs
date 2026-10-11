using System.Globalization;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Domain.UnitTests;

public class OrderStatusPolicyTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    public void Allows_forward_moves_in_the_sales_flow(OrderStatus from, OrderStatus to) =>
        Assert.True(OrderStatusPolicy.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Fulfilled)]
    public void Rejects_moves_that_make_no_sense(OrderStatus from, OrderStatus to) =>
        Assert.False(OrderStatusPolicy.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Cancelled)]
    public void Fulfilled_and_cancelled_are_final(OrderStatus status)
    {
        Assert.True(OrderStatusPolicy.IsFinal(status));
        Assert.Empty(OrderStatusPolicy.AllowedTransitionsFrom(status));
    }

    [Fact]
    public void Explains_rejections_in_plain_language()
    {
        Assert.Equal(
            "This order must be confirmed before it can be fulfilled. Allowed next statuses: Confirmed, Cancelled.",
            OrderStatusPolicy.ExplainRejection(OrderStatus.Pending, OrderStatus.Fulfilled));
        Assert.Equal(
            "This order has already been fulfilled, so it can no longer be cancelled.",
            OrderStatusPolicy.ExplainRejection(OrderStatus.Fulfilled, OrderStatus.Cancelled));
    }

    [Theory]
    [InlineData("Confirmed", OrderStatus.Confirmed)]
    [InlineData("  fulfilled ", OrderStatus.Fulfilled)]
    [InlineData("CANCELLED", OrderStatus.Cancelled)]
    public void Parses_status_names_case_insensitively(string input, OrderStatus expected)
    {
        Assert.True(OrderStatusPolicy.TryParse(input, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Shipped")]
    [InlineData("1")]
    [InlineData("Confirmed,Cancelled")]
    public void Rejects_unknown_or_numeric_statuses(string? input) =>
        Assert.False(OrderStatusPolicy.TryParse(input, out _));
}

public class PricingTests
{
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(3, "19.99", "59.97")]
    [InlineData(1, "0", "0")]
    [InlineData(1_000_000, "999.99", "999990000")]
    public void Line_total_is_quantity_times_unit_price(int quantity, string unitPrice, string expected) =>
        Assert.Equal(D(expected), Pricing.LineTotal(quantity, D(unitPrice)));

    [Fact]
    public void Decimal_arithmetic_avoids_floating_point_drift() =>
        Assert.Equal(0.30m, Pricing.Subtotal([0.10m, 0.20m])); // 0.1 + 0.2 != 0.3 in binary floating point

    [Theory]
    [InlineData("2.345", "2.35")]
    [InlineData("2.344", "2.34")]
    [InlineData("-2.345", "-2.35")]
    public void Rounds_half_away_from_zero(string amount, string expected) =>
        Assert.Equal(D(expected), Pricing.Round(D(amount)));
}

public class ValueObjectTests
{
    [Fact]
    public void External_reference_is_trimmed_for_display_and_normalised_for_matching()
    {
        var reference = ExternalReference.Create("  po-1001 ").Value;

        Assert.Equal("po-1001", reference.Value);
        Assert.Equal("PO-1001", reference.Normalized);
        Assert.Equal(ExternalReference.Create("PO-1001").Value, reference);
    }

    [Theory]
    [InlineData(null, "External reference is required.")]
    [InlineData("   ", "External reference is required.")]
    [InlineData("PO\u0007BELL", "External reference contains invalid characters.")]
    public void External_reference_rejects_bad_input(string? raw, string message)
    {
        var error = Assert.IsType<ValidationError>(ExternalReference.Create(raw).Error);
        Assert.Equal([message], error.Errors["externalReference"]);
    }

    [Fact]
    public void External_reference_allows_64_characters_but_not_65()
    {
        Assert.True(ExternalReference.Create(new string('A', 64)).IsSuccess);
        Assert.True(ExternalReference.Create(new string('A', 65)).IsFailure);
    }

    [Fact]
    public void Email_is_trimmed_and_lower_cased_and_must_be_valid()
    {
        Assert.Equal("jane.doe@example.com", EmailAddress.Create("  Jane.Doe@Example.COM ").Value.Value);
        Assert.True(EmailAddress.Create("not-an-email").IsFailure);
        Assert.True(EmailAddress.Create("jane@localhost").IsFailure);
    }

    [Fact]
    public void Customer_id_is_derived_from_the_email()
    {
        var email = EmailAddress.Create("jane@example.com").Value;

        var a = CustomerInfo.Create("Jane", email).Value;
        var b = CustomerInfo.Create("Jane D.", email).Value;

        Assert.StartsWith("cus_", a.Id);
        Assert.Equal(a.Id, b.Id);
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" eur ", "EUR")]
    public void Currency_codes_are_normalised(string raw, string expected) =>
        Assert.Equal(expected, CurrencyCode.Create(raw).Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("dollars")]
    [InlineData("E1R")]
    public void Invalid_currency_codes_are_rejected(string? raw) =>
        Assert.True(CurrencyCode.Create(raw).IsFailure);

    [Fact]
    public void Exchange_rates_must_be_positive() =>
        Assert.Throws<DomainException>(() => new ExchangeRate(CurrencyCode.From("USD"), CurrencyCode.From("EUR"), 0m, default, "x"));
}

public class SubmissionFingerprintTests
{
    private static Order Submit(string reference = "PO-1", string email = "jane@example.com", string currency = "USD", string? notes = "n", params NewOrderLine[] lines) =>
        Order.Submit(OrderId.New(), ExternalReference.Create(reference).Value,
            CustomerInfo.Create("Jane", EmailAddress.Create(email).Value).Value,
            CurrencyCode.Create(currency).Value, notes,
            lines.Length > 0 ? lines : [new NewOrderLine("A", "Widget", 2, 10m), new NewOrderLine("B", "Gadget", 1, 5m)],
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Is_a_sha256_hex_string_and_stable_across_versions()
    {
        // Stored fingerprints of existing orders must keep matching: this value must never change. It equals
        // SHA-256 of "PO-1\njane@example.com\nJane\nUSD\nn\nA|Widget|2|10\nB|Gadget|1|5\n", computed independently.
        Assert.Equal("3608FC20680E23B7E3300EF2C5EC816BDA20D5618C308B98EEA21A115A2B91C0", Submit().SubmissionFingerprint);
    }

    [Fact]
    public void Formatting_differences_give_the_same_fingerprint()
    {
        var baseline = Submit();
        var variant = Submit(reference: " po-1 ", email: "JANE@Example.com", currency: "usd",
            lines: [new NewOrderLine(" A ", "Widget ", 2, 10.00m), new NewOrderLine("B", "Gadget", 1, 5.0m)]);

        Assert.Equal(baseline.SubmissionFingerprint, variant.SubmissionFingerprint);
    }

    public static TheoryData<string, Order> MaterialDifferences => new()
    {
        { "quantity", Submit(lines: [new NewOrderLine("A", "Widget", 3, 10m), new NewOrderLine("B", "Gadget", 1, 5m)]) },
        { "price", Submit(lines: [new NewOrderLine("A", "Widget", 2, 10.01m), new NewOrderLine("B", "Gadget", 1, 5m)]) },
        { "line order", Submit(lines: [new NewOrderLine("B", "Gadget", 1, 5m), new NewOrderLine("A", "Widget", 2, 10m)]) },
        { "SKU casing", Submit(lines: [new NewOrderLine("a", "Widget", 2, 10m), new NewOrderLine("B", "Gadget", 1, 5m)]) },
        { "extra line", Submit(lines: [new NewOrderLine("A", "Widget", 2, 10m), new NewOrderLine("B", "Gadget", 1, 5m), new NewOrderLine("C", "Extra", 1, 1m)]) },
        { "notes", Submit(notes: "different") },
        { "currency", Submit(currency: "EUR") },
        { "customer", Submit(email: "someone@example.com") },
    };

    [Theory]
    [MemberData(nameof(MaterialDifferences))]
    public void Material_differences_change_the_fingerprint(string difference, Order changed) =>
        Assert.True(Submit().SubmissionFingerprint != changed.SubmissionFingerprint, $"{difference} should change the fingerprint");
}
