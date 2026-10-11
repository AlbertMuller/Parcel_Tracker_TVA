using OrderTracker.Application.Orders.SubmitOrder;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;
using OrderTracker.Domain.Orders.Events;
using OrderTracker.TestKit;

namespace OrderTracker.Application.UnitTests;

public class SubmitOrderHandlerTests
{
    [Fact]
    public async Task New_order_is_stored_pending_with_server_side_totals_and_an_event()
    {
        var h = new Harness();

        var result = await h.Submit.HandleAsync(TestData.ValidCommand());

        Assert.True(result.IsSuccess);
        var (order, alreadySubmitted) = result.Value;
        Assert.False(alreadySubmitted);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1, order.Number);
        Assert.Equal([59.97m, 5.50m], order.LineItems.Select(l => l.LineTotal));
        Assert.Equal(65.47m, order.Total);
        Assert.Equal("USD", order.Currency);
        Assert.Equal("jane.doe@example.com", order.Customer.Email);
        Assert.Equal(h.Clock.GetUtcNow(), order.CreatedAt);
        Assert.IsType<OrderSubmitted>(Assert.Single(h.Store.DispatchedEvents));
    }

    [Fact]
    public async Task Invalid_submission_returns_field_errors_and_stores_nothing()
    {
        var h = new Harness();

        var result = await h.Submit.HandleAsync(TestData.ValidCommand() with { Currency = null });

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(["Currency is required."], error.Errors["currency"]);
        Assert.Empty(h.Store.All);
    }

    [Fact]
    public async Task Resubmitting_the_same_order_returns_the_original_without_a_duplicate()
    {
        var h = new Harness();
        var first = (await h.Submit.HandleAsync(TestData.ValidCommand("PO-42"))).Value.Order;
        h.Clock.Advance(TimeSpan.FromMinutes(5));

        var second = await h.Submit.HandleAsync(TestData.ValidCommand("PO-42"));

        Assert.True(second.Value.AlreadySubmitted);
        Assert.Equal(first.Id, second.Value.Order.Id);
        Assert.Equal(first.CreatedAt, second.Value.Order.CreatedAt);
        Assert.Single(h.Store.All);
        Assert.Single(h.Store.DispatchedEvents); // no event for a replay
    }

    [Theory]
    [InlineData("po-42")]
    [InlineData("  PO-42  ")]
    public async Task Reference_matching_ignores_case_and_surrounding_whitespace(string resubmitted)
    {
        var h = new Harness();
        await h.Submit.HandleAsync(TestData.ValidCommand("PO-42"));

        var second = await h.Submit.HandleAsync(TestData.ValidCommand(resubmitted));

        Assert.True(second.Value.AlreadySubmitted);
        Assert.Single(h.Store.All);
    }

    [Fact]
    public async Task Formatting_differences_in_the_payload_still_count_as_the_same_submission()
    {
        var h = new Harness();
        await h.Submit.HandleAsync(TestData.ValidCommand("PO-42"));

        var reformatted = TestData.ValidCommand("PO-42") with
        {
            Customer = new CustomerInput("  jane.doe@example.COM ", " Jane Doe "),
            Currency = "USD",
            LineItems = [new LineItemInput(" SKU-A", "Widget ", 3.0m, 19.990m), new LineItemInput("SKU-B", "Gadget", 1, 5.5m)],
        };

        Assert.True((await h.Submit.HandleAsync(reformatted)).Value.AlreadySubmitted);
    }

    [Fact]
    public async Task Reusing_a_reference_for_different_content_is_a_coded_conflict_and_changes_nothing()
    {
        var h = new Harness();
        var original = (await h.Submit.HandleAsync(TestData.ValidCommand("PO-42"))).Value.Order;

        var result = await h.Submit.HandleAsync(TestData.ValidCommand("PO-42", new LineItemInput("SKU-Z", "Other", 10, 1m)));

        Assert.Equal("order.reference.conflict", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(original.Id, result.Error.Metadata["existingOrderId"]);
        Assert.Equal(original.Number, result.Error.Metadata["existingOrderNumber"]);
        Assert.Equal(original.Total, Assert.Single(h.Store.All).Total);
    }

    [Fact]
    public async Task Resubmitting_after_the_status_changed_still_returns_the_existing_order()
    {
        var h = new Harness();
        var id = await h.SubmitAsync("PO-42");
        await h.ChangeStatus.HandleAsync(new(id, "Confirmed"));

        var again = await h.Submit.HandleAsync(TestData.ValidCommand("PO-42"));

        Assert.True(again.Value.AlreadySubmitted);
        Assert.Equal(OrderStatus.Confirmed, again.Value.Order.Status);
    }

    [Fact]
    public async Task Losing_a_race_to_an_identical_submission_returns_the_winner_as_a_replay()
    {
        var h = new Harness();
        // Another request stores the same order between our lookup and our save.
        var v = SubmitOrderValidator.Validate(TestData.ValidCommand("PO-RACE")).Value;
        var winner = Order.Submit(OrderId.New(), v.Reference, v.Customer, v.Currency, v.Notes, v.Lines, TestData.Start);
        h.Store.BeforeSave = () => h.Store.Seed(winner);

        var result = await h.Submit.HandleAsync(TestData.ValidCommand("PO-RACE"));

        Assert.True(result.Value.AlreadySubmitted);
        Assert.Equal(winner.Id.Value, result.Value.Order.Id);
        Assert.Single(h.Store.All);
    }

    [Fact]
    public async Task Losing_a_race_to_a_different_submission_is_a_conflict()
    {
        var h = new Harness();
        h.Store.BeforeSave = () => h.Store.Seed(TestData.NewOrder("PO-RACE", lines: new NewOrderLine("X", "Other", 1, 1m)));

        var result = await h.Submit.HandleAsync(TestData.ValidCommand("PO-RACE"));

        Assert.Equal("order.reference.conflict", result.Error.Code);
        Assert.Single(h.Store.All);
    }
}

public class SubmitOrderValidatorTests
{
    [Fact]
    public void Accepts_a_valid_submission_and_normalises_it()
    {
        var result = SubmitOrderValidator.Validate(TestData.ValidCommand(" po-1001 ") with { Notes = "   " });

        var valid = result.Value;
        Assert.Equal("po-1001", valid.Reference.Value);
        Assert.Equal("PO-1001", valid.Reference.Normalized);
        Assert.Equal("jane.doe@example.com", valid.Customer.Email);
        Assert.Equal("USD", valid.Currency.Value);
        Assert.Null(valid.Notes);
        Assert.Equal(2, valid.Lines.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void Quantity_must_be_a_positive_whole_number(double quantity)
    {
        var result = SubmitOrderValidator.Validate(TestData.ValidCommand(lines: new LineItemInput("SKU-A", "Widget", (decimal)quantity, 10m)));

        Assert.Equal(["Quantity must be a positive whole number."], Errors(result)["lineItems[0].quantity"]);
    }

    [Theory]
    [InlineData("-0.01", "Unit price cannot be negative.")]
    [InlineData("1.005", "Unit price can have at most 2 decimal places.")]
    [InlineData("1000000000.01", "Unit price must be at most 1,000,000,000.")]
    public void Unit_price_rules(string price, string message)
    {
        var result = SubmitOrderValidator.Validate(TestData.ValidCommand(lines: new LineItemInput("SKU-A", "Widget", 1, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal([message], Errors(result)["lineItems[0].unitPrice"]);
    }

    [Fact]
    public void Free_items_are_allowed() =>
        Assert.True(SubmitOrderValidator.Validate(TestData.ValidCommand(lines: new LineItemInput("FREE", "Sample", 2, 0m))).IsSuccess);

    [Fact]
    public void Reports_every_problem_with_the_json_path_of_the_field()
    {
        var command = new SubmitOrderCommand(
            ExternalReference: "",
            Customer: new CustomerInput("not-an-email", ""),
            Currency: "dollars",
            Notes: new string('n', 1001),
            LineItems: [new LineItemInput("SKU-A", "Widget", 1, 1m), new LineItemInput("", null, null, null), null]);

        var errors = Errors(SubmitOrderValidator.Validate(command));

        Assert.Equivalent(
            new[]
            {
                "externalReference", "customer.email", "customer.name", "currency", "notes",
                "lineItems[1].sku", "lineItems[1].name", "lineItems[1].quantity", "lineItems[1].unitPrice", "lineItems[2]",
            },
            errors.Keys);
    }

    [Theory]
    [InlineData(0, "At least one line item is required.")]
    [InlineData(101, "An order can have at most 100 line items.")]
    public void Line_count_limits(int count, string message)
    {
        var lines = Enumerable.Range(0, count).Select(i => new LineItemInput($"S{i}", "Item", 1, 1m)).ToArray();
        var command = TestData.ValidCommand() with { LineItems = lines };

        Assert.Equal([message], Errors(SubmitOrderValidator.Validate(command))["lineItems"]);
    }

    [Fact]
    public void One_hundred_lines_is_fine()
    {
        var lines = Enumerable.Range(0, 100).Select(i => new LineItemInput($"S{i}", "Item", 1, 1m)).ToArray();
        Assert.True(SubmitOrderValidator.Validate(TestData.ValidCommand(lines: lines)).IsSuccess);
    }

    [Fact]
    public void Missing_body_is_reported() =>
        Assert.True(Errors(SubmitOrderValidator.Validate(null)).ContainsKey("body"));

    private static IReadOnlyDictionary<string, string[]> Errors<T>(Result<T> result) =>
        Assert.IsType<ValidationError>(result.Error).Errors;
}
