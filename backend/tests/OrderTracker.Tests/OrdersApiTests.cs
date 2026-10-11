using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderTracker.Api.Infrastructure.ExchangeRates;

namespace OrderTracker.Tests;

/// <summary>Runs the real app in memory, with the exchange rate provider swapped for a fake so tests never hit the network.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeExchangeRateProvider Rates { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IExchangeRateProvider>(Rates)));
}

/// <summary>End-to-end checks of the HTTP contract: status codes, headers and problem responses.</summary>
public class OrdersApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object NewOrder(string reference, int quantity = 2) => new
    {
        externalReference = reference,
        customer = new { email = "rep@example.com", name = "Acme Ltd" },
        currency = "EUR",
        lineItems = new[] { new { sku = "SKU-1", name = "Bolt", quantity, unitPrice = 1.25m } },
    };

    [Fact]
    public async Task First_submission_is_201_and_repeat_is_200_with_the_same_order()
    {
        var reference = $"API-{Guid.NewGuid():N}";

        var first = await _client.PostAsJsonAsync("/api/orders", NewOrder(reference));
        var second = await _client.PostAsJsonAsync("/api/orders", NewOrder(reference));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replay").Single());

        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        var b = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
        Assert.Equal(2.50m, a.GetProperty("total").GetDecimal());
        Assert.Equal("Pending", a.GetProperty("status").GetString());
        Assert.EndsWith($"/api/orders/{a.GetProperty("id").GetString()}", first.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
    }

    [Fact]
    public async Task Reusing_a_reference_with_different_details_is_409_pointing_at_the_existing_order()
    {
        var reference = $"API-{Guid.NewGuid():N}";
        var created = await (await _client.PostAsJsonAsync("/api/orders", NewOrder(reference))).Content.ReadFromJsonAsync<JsonElement>();

        var response = await _client.PostAsJsonAsync("/api/orders", NewOrder(reference, quantity: 99));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created.GetProperty("id").GetString(), problem.GetProperty("existingOrderId").GetString());
    }

    [Fact]
    public async Task Validation_errors_are_400_with_field_level_messages()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            externalReference = "API-BAD",
            customer = new { email = "rep@example.com", name = "Acme" },
            currency = "EUR",
            lineItems = new[] { new { sku = "S", name = "N", quantity = 0, unitPrice = -1 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("lineItems[0].quantity", out _));
        Assert.True(errors.TryGetProperty("lineItems[0].unitPrice", out _));
    }

    [Fact]
    public async Task Malformed_json_is_400_not_500()
    {
        var response = await _client.PostAsync("/api/orders", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Disallowed_status_change_is_409_with_a_helpful_message()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", NewOrder($"API-{Guid.NewGuid():N}"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Fulfilled" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("must be confirmed", problem.GetProperty("detail").GetString());
        Assert.Equal("Pending", problem.GetProperty("currentStatus").GetString());
    }

    [Fact]
    public async Task Allowed_status_change_returns_the_updated_order()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", NewOrder($"API-{Guid.NewGuid():N}"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Confirmed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", order.GetProperty("status").GetString());
        Assert.Equal(["Fulfilled", "Cancelled"], order.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Unknown_order_is_404_and_bad_status_filter_is_400()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/orders/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/orders?status=Shipped")).StatusCode);
    }

    [Fact]
    public async Task List_returns_newest_first()
    {
        var older = $"API-OLD-{Guid.NewGuid():N}";
        var newer = $"API-NEW-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/orders", NewOrder(older));
        await _client.PostAsJsonAsync("/api/orders", NewOrder(newer));

        var page = await _client.GetFromJsonAsync<JsonElement>("/api/orders?search=API-&pageSize=200");
        var references = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("externalReference").GetString()).ToList();

        Assert.True(references.IndexOf(newer) < references.IndexOf(older));
    }

    [Fact]
    public async Task Changing_currency_returns_the_repriced_order()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", NewOrder($"API-{Guid.NewGuid():N}"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/currency", new { currency = "USD" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("USD", order.GetProperty("currency").GetString());
        Assert.Equal("EUR", order.GetProperty("baseCurrency").GetString());
        Assert.Equal(2.76m, order.GetProperty("total").GetDecimal());   // 1.25 EUR × 1.1 = 1.375 → 1.38, × 2
        var line = order.GetProperty("lineItems")[0];
        Assert.Equal(1.38m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(1.25m, line.GetProperty("baseUnitPrice").GetDecimal());
        Assert.Equal(1.1m, order.GetProperty("currencyHistory")[0].GetProperty("rateFromBase").GetDecimal());
    }

    [Fact]
    public async Task Changing_currency_of_a_confirmed_order_is_409()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", NewOrder($"API-{Guid.NewGuid():N}"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Confirmed" });

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/currency", new { currency = "USD" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False((await _client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}")).GetProperty("canChangeCurrency").GetBoolean());
    }

    [Fact]
    public async Task Exchange_rate_and_currency_endpoints_return_provider_data()
    {
        var rates = await _client.GetFromJsonAsync<JsonElement>("/api/exchange-rates?base=usd");
        Assert.Equal("USD", rates.GetProperty("base").GetString());
        Assert.Equal(0.9m, rates.GetProperty("rates").GetProperty("EUR").GetDecimal());

        var currencies = await _client.GetFromJsonAsync<JsonElement>("/api/currencies");
        Assert.Equal("Euro", currencies.GetProperty("EUR").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/exchange-rates?base=ABC")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/exchange-rates?base=dollars")).StatusCode);
    }
}
