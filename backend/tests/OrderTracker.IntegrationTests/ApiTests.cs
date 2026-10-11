using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Infrastructure;
using OrderTracker.TestKit;

namespace OrderTracker.IntegrationTests;

/// <summary>
/// Runs the real app in memory against its own migrated PostgreSQL database (Testcontainers).
/// Only the exchange-rate provider is faked, so tests never call the internet.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _connectionString = "";

    public async Task InitializeAsync() => _connectionString = await TestPostgres.CreateMigratedDatabaseAsync();

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.AppConnectionName}", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IExchangeRateProvider>(new FakeExchangeRateProvider())));
    }
}

/// <summary>The HTTP contract end to end: status codes, headers, problem responses and their stable codes.</summary>
[Trait("Category", "Database")]
public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object NewOrder(string reference, int quantity = 2) => new
    {
        externalReference = reference,
        customer = new { email = "rep@example.com", name = "Acme Ltd" },
        currency = "EUR",
        lineItems = new[] { new { sku = "SKU-1", name = "Bolt", quantity, unitPrice = 1.25m } },
    };

    private async Task<string> CreateAsync()
    {
        var created = await (await _client.PostAsJsonAsync("/api/orders", NewOrder($"API-{Guid.NewGuid():N}"))).Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task First_submission_is_201_and_repeat_is_200_with_the_same_order()
    {
        var reference = $"API-{Guid.NewGuid():N}";

        var first = await _client.PostAsJsonAsync("/api/orders", NewOrder(reference));
        var second = await _client.PostAsJsonAsync("/api/orders", NewOrder(reference));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replay").Single());

        var a = await Json(first);
        var b = await Json(second);
        Assert.Equal(a.GetProperty("id").GetString(), b.GetProperty("id").GetString());
        Assert.Equal(2.50m, a.GetProperty("total").GetDecimal());
        Assert.Equal("Pending", a.GetProperty("status").GetString());
        Assert.EndsWith($"/api/orders/{a.GetProperty("id").GetString()}", first.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first.Headers.Location, second.Headers.Location);
    }

    [Fact]
    public async Task Response_keeps_the_json_contract_the_frontend_uses()
    {
        var order = await Json(await _client.PostAsJsonAsync("/api/orders", NewOrder($"API-{Guid.NewGuid():N}")));

        var properties = order.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(
            ["id", "number", "externalReference", "customer", "currency", "baseCurrency", "canChangeCurrency", "notes", "lineItems",
             "subtotal", "total", "status", "allowedTransitions", "statusHistory", "currencyHistory", "createdAt", "updatedAt"],
            properties);
        Assert.Equal(["sku", "name", "quantity", "unitPrice", "lineTotal", "baseUnitPrice"],
            order.GetProperty("lineItems")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(["id", "email", "name"], order.GetProperty("customer").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Reusing_a_reference_with_different_details_is_409_pointing_at_the_existing_order()
    {
        var reference = $"API-{Guid.NewGuid():N}";
        var created = await Json(await _client.PostAsJsonAsync("/api/orders", NewOrder(reference)));

        var response = await _client.PostAsJsonAsync("/api/orders", NewOrder(reference, quantity: 99));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("order.reference.conflict", problem.GetProperty("code").GetString());
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
        var problem = await Json(response);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("lineItems[0].quantity", out _));
        Assert.True(errors.TryGetProperty("lineItems[0].unitPrice", out _));
    }

    [Fact]
    public async Task Malformed_json_is_400_not_500()
    {
        var response = await _client.PostAsync("/api/orders", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("request.malformed", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disallowed_status_change_is_409_with_a_helpful_message()
    {
        var id = await CreateAsync();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Fulfilled" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("order.status.invalid_transition", problem.GetProperty("code").GetString());
        Assert.Contains("must be confirmed", problem.GetProperty("detail").GetString());
        Assert.Equal("Pending", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Fulfilled", problem.GetProperty("requestedStatus").GetString());
    }

    [Fact]
    public async Task Allowed_status_change_returns_the_updated_order()
    {
        var id = await CreateAsync();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Confirmed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await Json(response);
        Assert.Equal("Confirmed", order.GetProperty("status").GetString());
        Assert.Equal(["Fulfilled", "Cancelled"], order.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Unknown_order_is_404_and_bad_status_filter_is_400()
    {
        var missing = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("order.not_found", (await Json(missing)).GetProperty("code").GetString());
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
        Assert.Equal(["items", "page", "pageSize", "totalCount"], page.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Changing_currency_returns_the_repriced_order()
    {
        var id = await CreateAsync();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/currency", new { currency = "USD" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await Json(response);
        Assert.Equal("USD", order.GetProperty("currency").GetString());
        Assert.Equal("EUR", order.GetProperty("baseCurrency").GetString());
        Assert.Equal(2.76m, order.GetProperty("total").GetDecimal()); // 1.25 EUR × 1.1 = 1.375 → 1.38, × 2
        var line = order.GetProperty("lineItems")[0];
        Assert.Equal(1.38m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(1.25m, line.GetProperty("baseUnitPrice").GetDecimal());
        Assert.Equal(1.1m, order.GetProperty("currencyHistory")[0].GetProperty("rateFromBase").GetDecimal());
    }

    [Fact]
    public async Task Changing_currency_of_a_confirmed_order_is_409()
    {
        var id = await CreateAsync();
        await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "Confirmed" });

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/currency", new { currency = "USD" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order.currency.locked", (await Json(response)).GetProperty("code").GetString());
        Assert.False((await _client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}")).GetProperty("canChangeCurrency").GetBoolean());
    }

    [Fact]
    public async Task Missing_rate_is_422_with_a_code()
    {
        var id = await CreateAsync();

        var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/currency", new { currency = "CHF" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("exchange_rate.not_available", (await Json(response)).GetProperty("code").GetString());
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

    [Fact]
    public async Task Simultaneous_identical_submissions_create_one_order()
    {
        var reference = $"API-RACE-{Guid.NewGuid():N}";
        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => _client.PostAsJsonAsync("/api/orders", NewOrder(reference))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        var ids = await Task.WhenAll(responses.Select(async r => (await Json(r)).GetProperty("id").GetString()));
        Assert.Single(ids.Distinct());

        var page = await _client.GetFromJsonAsync<JsonElement>($"/api/orders?search={reference}");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Health_check_reports_the_database()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
