using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;
using OrderTracker.Domain.Orders.Events;

namespace OrderTracker.Domain.UnitTests;

public class OrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");
    private static readonly CurrencyCode Eur = CurrencyCode.From("EUR");
    private static readonly CurrencyCode Gbp = CurrencyCode.From("GBP");

    // 3 × 19.99 + 1 × 5.50 USD = 65.47 USD
    internal static Order NewOrder(string reference = "PO-1001", string? notes = "Bay 2", params NewOrderLine[] lines) => Order.Submit(
        OrderId.New(),
        ExternalReference.Create(reference).Value,
        CustomerInfo.Create("Jane Doe", EmailAddress.Create("jane@example.com").Value).Value,
        Usd,
        notes,
        lines.Length > 0 ? lines : [new NewOrderLine("SKU-A", "Widget", 3, 19.99m), new NewOrderLine("SKU-B", "Gadget", 1, 5.50m)],
        T0);

    private static ExchangeRate Rate(CurrencyCode to, decimal rate) => new(Usd, to, rate, new DateOnly(2026, 1, 14), "Test");

    // ── Submitting ────────────────────────────────────────────────────

    [Fact]
    public void Submit_computes_totals_and_starts_pending_with_history_and_an_event()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal([59.97m, 5.50m], order.Lines.Select(l => l.LineTotal));
        Assert.Equal([1, 2], order.Lines.Select(l => l.LineNo));
        Assert.Equal(65.47m, order.Subtotal);
        Assert.Equal(65.47m, order.Total);
        Assert.Equal((T0, T0, 1), (order.CreatedAt, order.UpdatedAt, order.Version));
        Assert.Equal(Usd, order.BaseCurrency);
        var created = Assert.Single(order.StatusHistory);
        Assert.Equal((null, OrderStatus.Pending), (created.From, created.To));
        var submitted = Assert.IsType<OrderSubmitted>(Assert.Single(order.DomainEvents));
        Assert.Equal((order.Id, "PO-1001", 65.47m), (submitted.OrderId, submitted.Reference, submitted.Total));
    }

    [Fact]
    public void Whitespace_notes_are_dropped()
    {
        Assert.Null(NewOrder(notes: "   ").Notes);
    }

    public static TheoryData<NewOrderLine> InvalidLines => new()
    {
        new NewOrderLine("SKU", "Item", 0, 1m),
        new NewOrderLine("SKU", "Item", -1, 1m),
        new NewOrderLine("SKU", "Item", 1, -0.01m),
        new NewOrderLine("SKU", "Item", 1, 1.005m),
        new NewOrderLine("", "Item", 1, 1m),
        new NewOrderLine("SKU", " ", 1, 1m),
    };

    [Theory]
    [MemberData(nameof(InvalidLines))]
    public void The_domain_refuses_invalid_lines_even_if_validation_was_bypassed(NewOrderLine line) =>
        Assert.Throws<DomainException>(() => NewOrder(lines: line));

    [Fact]
    public void An_order_needs_between_one_and_one_hundred_lines()
    {
        Assert.Throws<DomainException>(() => Order.Submit(OrderId.New(), ExternalReference.Create("X").Value,
            CustomerInfo.Create("A", EmailAddress.Create("a@b.com").Value).Value, Usd, null, [], T0));
        var tooMany = Enumerable.Range(0, Order.MaxLines + 1).Select(i => new NewOrderLine($"S{i}", "Item", 1, 1m)).ToArray();
        Assert.Throws<DomainException>(() => NewOrder(lines: tooMany));
    }

    // ── Status ────────────────────────────────────────────────────────

    [Fact]
    public void Allowed_status_change_updates_state_history_version_and_raises_an_event()
    {
        var order = NewOrder();
        order.ClearDomainEvents();

        var result = order.ChangeStatus(OrderStatus.Confirmed, T0.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(T0.AddHours(1), order.UpdatedAt);
        Assert.Equal(2, order.Version);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Confirmed], order.StatusHistory.Select(h => h.To));
        Assert.Equal([OrderStatus.Fulfilled, OrderStatus.Cancelled], order.AllowedTransitions);
        var changed = Assert.IsType<OrderStatusChanged>(Assert.Single(order.DomainEvents));
        Assert.Equal((OrderStatus.Pending, OrderStatus.Confirmed), (changed.From, changed.To));
    }

    [Fact]
    public void Rejected_status_change_returns_a_coded_error_and_changes_nothing()
    {
        var order = NewOrder();

        var result = order.ChangeStatus(OrderStatus.Fulfilled, T0.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal("order.status.invalid_transition", result.Error.Code);
        Assert.Contains("must be confirmed before it can be fulfilled", result.Error.Message);
        Assert.Equal(["Confirmed", "Cancelled"], (string[])result.Error.Metadata["allowedTransitions"]!);
        Assert.Equal((OrderStatus.Pending, 1, T0), (order.Status, order.Version, order.UpdatedAt));
        Assert.Single(order.StatusHistory);
    }

    [Fact]
    public void Final_orders_report_order_status_final()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Cancelled, T0);

        var result = order.ChangeStatus(OrderStatus.Confirmed, T0);

        Assert.Equal("order.status.final", result.Error.Code);
    }

    [Fact]
    public void Asking_for_the_current_status_is_a_harmless_no_op()
    {
        var order = NewOrder();

        Assert.True(order.ChangeStatus(OrderStatus.Pending, T0.AddHours(1)).IsSuccess);
        Assert.Equal((1, T0), (order.Version, order.UpdatedAt));
    }

    // ── Currency ──────────────────────────────────────────────────────

    [Fact]
    public void Converting_reprices_each_line_from_the_base_price_and_records_the_rate()
    {
        var order = NewOrder();

        var result = order.ConvertCurrency(Rate(Eur, 0.9m), T0.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(Eur, order.Currency);
        Assert.Equal([17.99m, 4.95m], order.Lines.Select(l => l.UnitPrice)); // 19.99 × 0.9 = 17.991 → 17.99
        Assert.Equal([19.99m, 5.50m], order.Lines.Select(l => l.BaseUnitPrice));
        Assert.Equal(58.92m, order.Total);
        var change = Assert.Single(order.CurrencyHistory);
        Assert.Equal((Usd, Eur, 0.9m), (change.From, change.To, change.RateFromBase));
        Assert.Equal(2, order.Version);
    }

    [Fact]
    public void Converting_back_to_the_base_currency_restores_the_exact_submitted_prices()
    {
        var order = NewOrder();
        order.ConvertCurrency(Rate(Eur, 0.9m), T0);
        order.ConvertCurrency(Rate(Gbp, 0.75m), T0);

        order.ConvertCurrency(ExchangeRate.Identity(Usd, new DateOnly(2026, 1, 14)), T0);

        Assert.Equal([19.99m, 5.50m], order.Lines.Select(l => l.UnitPrice));
        Assert.Equal(65.47m, order.Total);
        Assert.Equal(1m, order.CurrencyHistory[^1].RateFromBase);
    }

    [Fact]
    public void Rounding_never_compounds_because_every_conversion_starts_from_the_base()
    {
        var order = NewOrder();
        order.ConvertCurrency(Rate(Eur, 0.9m), T0);

        order.ConvertCurrency(Rate(Gbp, 0.75m), T0);

        // 19.99 × 0.75 = 14.9925 → 14.99 and 5.50 × 0.75 = 4.125 → 4.13, straight from USD.
        Assert.Equal([14.99m, 4.13m], order.Lines.Select(l => l.UnitPrice));
        Assert.Equal(49.10m, order.Total);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Currency_is_locked_once_the_order_leaves_pending(OrderStatus status)
    {
        var order = NewOrder();
        order.ChangeStatus(status, T0);

        var result = order.ConvertCurrency(Rate(Eur, 0.9m), T0);

        Assert.Equal("order.currency.locked", result.Error.Code);
        Assert.Equal(Usd, order.Currency);
    }

    [Fact]
    public void A_rate_quoted_from_another_currency_is_a_programming_error()
    {
        var order = NewOrder();
        var fromEuros = new ExchangeRate(Eur, Gbp, 0.85m, new DateOnly(2026, 1, 14), "Test");

        Assert.Throws<DomainException>(() => order.ConvertCurrency(fromEuros, T0));
    }

    [Fact]
    public void Same_submission_is_recognised_by_content_not_identity()
    {
        var first = NewOrder();
        var again = NewOrder();
        var different = NewOrder(lines: new NewOrderLine("SKU-Z", "Other", 1, 1m));

        Assert.NotEqual(first.Id, again.Id);
        Assert.True(first.IsSameSubmissionAs(again));
        Assert.False(first.IsSameSubmissionAs(different));
    }
}
