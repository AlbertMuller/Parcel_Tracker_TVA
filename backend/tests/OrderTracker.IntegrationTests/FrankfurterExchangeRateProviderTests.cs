using System.Net;
using System.Text;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Infrastructure.ExchangeRates;

namespace OrderTracker.IntegrationTests;

/// <summary>The Frankfurter adapter against a stubbed HTTP handler (no network, no Docker).</summary>
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
        Assert.Equal(("USD", new DateOnly(2026, 10, 9)), (table!.Base, table.Date));
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
