using OrderTracker.Api.Application;
using OrderTracker.Api.Contracts;

namespace OrderTracker.Tests;

public class CreateOrderValidatorTests
{
    [Fact]
    public void Accepts_a_valid_order_and_normalizes_it()
    {
        var request = TestData.ValidRequest(" po-1001 ") with { Notes = "   " };

        var result = CreateOrderValidator.Validate(request);

        Assert.True(result.IsValid);
        var draft = result.Draft!;
        Assert.Equal("po-1001", draft.ExternalReference);
        Assert.Equal("jane.doe@example.com", draft.CustomerEmail);
        Assert.Equal("USD", draft.Currency);
        Assert.Null(draft.Notes);
        Assert.Equal(2, draft.LineItems.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void Quantity_must_be_a_positive_whole_number(double quantity)
    {
        var request = TestData.ValidRequest(lines: new LineItemInput("SKU-A", "Widget", (decimal)quantity, 10m));

        var result = CreateOrderValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(["Quantity must be a positive whole number."], result.Errors["lineItems[0].quantity"]);
    }

    [Fact]
    public void Unit_price_cannot_be_negative()
    {
        var request = TestData.ValidRequest(lines: new LineItemInput("SKU-A", "Widget", 1, -0.01m));

        var result = CreateOrderValidator.Validate(request);

        Assert.Equal(["Unit price cannot be negative."], result.Errors["lineItems[0].unitPrice"]);
    }

    [Fact]
    public void Unit_price_of_zero_is_allowed()
    {
        var request = TestData.ValidRequest(lines: new LineItemInput("SKU-FREE", "Free sample", 2, 0m));

        Assert.True(CreateOrderValidator.Validate(request).IsValid);
    }

    [Fact]
    public void Unit_price_with_more_than_two_decimals_is_rejected()
    {
        var request = TestData.ValidRequest(lines: new LineItemInput("SKU-A", "Widget", 1, 1.005m));

        var result = CreateOrderValidator.Validate(request);

        Assert.True(result.Errors.ContainsKey("lineItems[0].unitPrice"));
    }

    [Fact]
    public void Reports_every_problem_with_the_path_of_the_offending_field()
    {
        var request = new CreateOrderRequest(
            ExternalReference: "",
            Customer: new CustomerInput("not-an-email", ""),
            Currency: "dollars",
            Notes: null,
            LineItems: [new LineItemInput("SKU-A", "Widget", 1, 1m), new LineItemInput("", null, null, null)]);

        var result = CreateOrderValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equivalent(
            new[]
            {
                "externalReference", "customer.email", "customer.name", "currency",
                "lineItems[1].sku", "lineItems[1].name", "lineItems[1].quantity", "lineItems[1].unitPrice",
            },
            result.Errors.Keys);
    }

    [Fact]
    public void At_least_one_line_item_is_required()
    {
        var request = TestData.ValidRequest() with { LineItems = [] };

        var result = CreateOrderValidator.Validate(request);

        Assert.Equal(["At least one line item is required."], result.Errors["lineItems"]);
    }

    [Fact]
    public void Missing_body_is_reported()
    {
        var result = CreateOrderValidator.Validate(null);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("body"));
    }
}
