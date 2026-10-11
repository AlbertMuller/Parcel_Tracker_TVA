using OrderTracker.Api.Domain;

namespace OrderTracker.Tests;

public class OrderStatusRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    public void Allows_forward_moves_in_the_sales_flow(OrderStatus from, OrderStatus to) =>
        Assert.True(OrderStatusRules.CanTransition(from, to));

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
        Assert.False(OrderStatusRules.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.Fulfilled)]
    [InlineData(OrderStatus.Cancelled)]
    public void Fulfilled_and_cancelled_are_final(OrderStatus status)
    {
        Assert.True(OrderStatusRules.IsFinal(status));
        Assert.Empty(OrderStatusRules.AllowedTransitionsFrom(status));
    }

    [Fact]
    public void Explains_that_pending_orders_must_be_confirmed_before_fulfilment()
    {
        var message = OrderStatusRules.ExplainRejection(OrderStatus.Pending, OrderStatus.Fulfilled);

        Assert.Contains("must be confirmed before it can be fulfilled", message);
        Assert.Contains("Allowed next statuses: Confirmed, Cancelled", message);
    }

    [Fact]
    public void Explains_that_fulfilled_orders_cannot_be_cancelled()
    {
        var message = OrderStatusRules.ExplainRejection(OrderStatus.Fulfilled, OrderStatus.Cancelled);

        Assert.Contains("already been fulfilled", message);
        Assert.DoesNotContain("Allowed next statuses", message);
    }

    [Theory]
    [InlineData("Confirmed", OrderStatus.Confirmed)]
    [InlineData("  fulfilled ", OrderStatus.Fulfilled)]
    [InlineData("CANCELLED", OrderStatus.Cancelled)]
    public void Parses_status_names_case_insensitively(string input, OrderStatus expected)
    {
        Assert.True(OrderStatusRules.TryParse(input, out var status));
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Shipped")]
    [InlineData("1")]
    [InlineData("Confirmed,Cancelled")]
    public void Rejects_unknown_or_numeric_statuses(string? input) =>
        Assert.False(OrderStatusRules.TryParse(input, out _));
}
