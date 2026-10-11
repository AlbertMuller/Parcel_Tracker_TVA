using System.Net;
using System.Text;
using OrderTracker.Api.Application;
using OrderTracker.Api.Domain;
using OrderTracker.Api.Infrastructure.ExchangeRates;

namespace OrderTracker.Tests;

public class CurrencyConversionTests
{
    // Default test order: 3 × 19.99 + 1 × 5.50 USD = 65.47 USD. Fake rates: USD→EUR 0.9, USD→GBP 0.75.

    [Fact]
    public async Task Converting_reprices_each_line_from_the_base_prices_and_recomputes_totals()
    {
        var (service, _, clock, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;
        clock.Advance(TimeSpan.FromMinutes(10));

        var result = await service.ChangeCurrencyAsync(order.Id, "eur", CancellationToken.None);

        Assert.Equal(ChangeCurrencyOutcome.Changed, result.Outcome);
        var converted = result.Order!;
        Assert.Equal("EUR", converted.Currency);
        Assert.Equal("USD", converted.BaseCurrency);
        Assert.Equal([17.99m, 4.95m], converted.LineItems.Select(l => l.UnitPrice));   // 19.99 × 0.9 = 17.991 → 17.99
        Assert.Equal([53.97m, 4.95m], converted.LineItems.Select(l => l.LineTotal));
        Assert.Equal(58.92m, converted.Total);
        Assert.Equal(clock.GetUtcNow(), converted.UpdatedAt);

        var change = Assert.Single(converted.CurrencyHistory);
        Assert.Equal(("USD", "EUR", 0.9m, FakeExchangeRateProvider.RateDate), (change.From, change.To, change.RateFromBase, change.RateDate));
    }

    [Fact]
    public async Task Converting_back_to_the_base_currency_restores_the_exact_submitted_prices()
    {
        var (service, _, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;

        await service.ChangeCurrencyAsync(order.Id, "EUR", CancellationToken.None);
        await service.ChangeCurrencyAsync(order.Id, "GBP", CancellationToken.None);
        var back = (await service.ChangeCurrencyAsync(order.Id, "USD", CancellationToken.None)).Order!;

        Assert.Equal("USD", back.Currency);
        Assert.Equal([19.99m, 5.50m], back.LineItems.Select(l => l.UnitPrice));
        Assert.Equal(order.Total, back.Total);
        Assert.Equal(3, back.CurrencyHistory.Count);
    }

    [Fact]
    public async Task Each_conversion_starts_from_the_base_so_rounding_never_compounds()
    {
        var (service, _, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;

        await service.ChangeCurrencyAsync(order.Id, "EUR", CancellationToken.None);
        var gbp = (await service.ChangeCurrencyAsync(order.Id, "GBP", CancellationToken.None)).Order!;

        // USD→GBP directly (0.75), not USD→EUR→GBP: 19.99 × 0.75 = 14.9925 → 14.99; 5.50 × 0.75 = 4.125 → 4.13.
        Assert.Equal([14.99m, 4.13m], gbp.LineItems.Select(l => l.UnitPrice));
        Assert.Equal(49.10m, gbp.Total);
        Assert.Equal(0.75m, gbp.CurrencyHistory[^1].RateFromBase);
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Cancelled")]
    public async Task Currency_is_locked_once_the_order_leaves_pending(string status)
    {
        var (service, _, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;
        service.ChangeStatus(order.Id, status);

        var result = await service.ChangeCurrencyAsync(order.Id, "EUR", CancellationToken.None);

        Assert.Equal(ChangeCurrencyOutcome.NotAllowed, result.Outcome);
        Assert.Contains("only be changed while an order is Pending", result.Message);
        Assert.Equal("USD", service.Get(order.Id)!.Currency);
    }

    [Fact]
    public void Domain_refuses_to_convert_a_locked_order()
    {
        var (service, _, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;
        var confirmed = order.TransitionTo(OrderStatus.Confirmed, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => confirmed.ConvertCurrency("EUR", 0.9m, default, "x", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Requesting_the_current_currency_is_a_no_op()
    {
        var (service, _, _, rates) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;

        var result = await service.ChangeCurrencyAsync(order.Id, " usd ", CancellationToken.None);

        Assert.Equal(ChangeCurrencyOutcome.Unchanged, result.Outcome);
        Assert.Equal(order.Version, service.Get(order.Id)!.Version);
        Assert.Equal(0, rates.RateCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public async Task Invalid_currency_codes_are_rejected(string? currency)
    {
        var (service, _, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;

        var result = await service.ChangeCurrencyAsync(order.Id, currency, CancellationToken.None);

        Assert.Equal(ChangeCurrencyOutcome.InvalidCurrency, result.Outcome);
    }

    [Fact]
    public async Task Missing_rate_is_reported_and_the_order_is_unchanged()
    {
        var (service, _, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;

        var result = await service.ChangeCurrencyAsync(order.Id, "CHF", CancellationToken.None);

        Assert.Equal(ChangeCurrencyOutcome.RateNotAvailable, result.Outcome);
        Assert.Equal("No exchange rate is available from USD to CHF.", result.Message);
        Assert.Equal(order, service.Get(order.Id));
    }

    [Fact]
    public async Task Provider_outage_is_reported_and_the_order_is_unchanged()
    {
        var (service, _, _, rates) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest()).Order!;
        rates.Fail = true;

        var result = await service.ChangeCurrencyAsync(order.Id, "EUR", CancellationToken.None);

        Assert.Equal(ChangeCurrencyOutcome.RatesUnavailable, result.Outcome);
        Assert.Equal(order, service.Get(order.Id));
    }

    [Fact]
    public async Task Resubmitting_the_original_order_after_a_conversion_still_finds_it()
    {
        var (service, repository, _, _) = TestData.NewServiceWithRates();
        var order = service.Create(TestData.ValidRequest("PO-FX")).Order!;
        await service.ChangeCurrencyAsync(order.Id, "EUR", CancellationToken.None);

        var again = service.Create(TestData.ValidRequest("PO-FX"));

        Assert.Equal(CreateOrderOutcome.AlreadySubmitted, again.Outcome);
        Assert.Equal("EUR", again.Order!.Currency);
        Assert.Equal(1, repository.List(new()).TotalCount);
    }
}

public class ExchangeRateServiceTests
{
    private static (ExchangeRateService Service, FakeExchangeRateProvider Provider, ManualTimeProvider Clock) Create()
    {
        var provider = new FakeExchangeRateProvider();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
        return (TestData.NewRateService(provider, clock), provider, clock);
    }

    [Fact]
    public async Task Reuses_rates_within_the_cache_window_and_refreshes_after_it()
    {
        var (service, provider, clock) = Create();

        await service.GetRatesAsync("USD", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(59));
        await service.GetRatesAsync("usd", CancellationToken.None);
        Assert.Equal(1, provider.RateCalls);

        clock.Advance(TimeSpan.FromMinutes(2));
        await service.GetRatesAsync("USD", CancellationToken.None);
        Assert.Equal(2, provider.RateCalls);
    }

    [Fact]
    public async Task Serves_last_good_rates_flagged_stale_when_the_provider_is_down()
    {
        var (service, provider, clock) = Create();
        await service.GetRatesAsync("USD", CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(5));
        provider.Fail = true;

        var result = await service.GetRatesAsync("USD", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsStale);
        Assert.Equal(0.9m, result.Table.Rates["EUR"]);
    }

    [Fact]
    public async Task Gives_up_on_stale_rates_after_the_maximum_staleness()
    {
        var (service, provider, clock) = Create();
        await service.GetRatesAsync("USD", CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(4));
        provider.Fail = true;

        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => service.GetRatesAsync("USD", CancellationToken.None));
    }

    [Fact]
    public async Task Same_currency_rate_is_one_without_calling_the_provider()
    {
        var (service, provider, _) = Create();

        var quote = await service.GetRateAsync("EUR", "EUR", CancellationToken.None);

        Assert.Equal(1m, quote!.Rate);
        Assert.Equal(0, provider.RateCalls);
    }
}

public class FrankfurterExchangeRateProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://rates.test/v1/") };
    }

    private static (FrankfurterExchangeRateProvider Provider, StubHandler Handler) Create(HttpStatusCode status, string body = "") =>
        Create(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static (FrankfurterExchangeRateProvider Provider, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new FrankfurterExchangeRateProvider(new StubFactory(handler)), handler);
    }

    [Fact]
    public async Task Parses_latest_rates_exactly()
    {
        var (provider, handler) = Create(HttpStatusCode.OK,
            """{"amount":1.0,"base":"USD","date":"2026-10-09","rates":{"EUR":0.89238,"JPY":158.25}}""");

        var table = await provider.GetLatestAsync("USD", CancellationToken.None);

        Assert.Equal("https://rates.test/v1/latest?base=USD", handler.LastUri!.ToString());
        Assert.Equal("USD", table!.Base);
        Assert.Equal(new DateOnly(2026, 10, 9), table.Date);
        Assert.Equal(0.89238m, table.Rates["EUR"]);
        Assert.Equal(158.25m, table.Rates["JPY"]);
    }

    [Fact]
    public async Task Unsupported_base_currency_returns_null()
    {
        var (provider, _) = Create(HttpStatusCode.NotFound, """{"message":"not found"}""");

        Assert.Null(await provider.GetLatestAsync("XYZ", CancellationToken.None));
    }

    [Fact]
    public async Task Server_errors_bad_payloads_and_network_failures_become_unavailable()
    {
        var (serverError, _) = Create(HttpStatusCode.InternalServerError);
        var (badJson, _) = Create(HttpStatusCode.OK, "<html>oops</html>");
        var (network, _) = Create(_ => throw new HttpRequestException("DNS failure"));

        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => serverError.GetLatestAsync("USD", CancellationToken.None));
        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => badJson.GetLatestAsync("USD", CancellationToken.None));
        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => network.GetCurrenciesAsync(CancellationToken.None));
    }
}
