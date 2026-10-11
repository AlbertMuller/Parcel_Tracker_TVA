using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderTracker.Application.ExchangeRates;

namespace OrderTracker.Infrastructure.ExchangeRates;

/// <summary>Bound from the <c>ExchangeRates</c> configuration section (alongside the cache settings).</summary>
public sealed class FrankfurterOptions
{
    public string BaseUrl { get; set; } = "https://api.frankfurter.dev/v1/";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Reads European Central Bank reference rates from the free Frankfurter API (https://frankfurter.dev).
/// No API key needed. Rates are published once per working day around 16:00 CET.
/// </summary>
public sealed class FrankfurterExchangeRateProvider(IHttpClientFactory httpClientFactory) : IExchangeRateProvider
{
    public const string HttpClientName = "frankfurter";
    private const string SourceName = "European Central Bank (via Frankfurter)";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record LatestResponse(string Base, string Date, Dictionary<string, decimal> Rates);

    public async Task<ExchangeRateTable?> GetLatestAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"latest?base={Uri.EscapeDataString(baseCurrency)}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            return null; // Unsupported base currency.

        var body = await ReadAsync<LatestResponse>(response, cancellationToken);
        return new ExchangeRateTable(
            body.Base,
            DateOnly.ParseExact(body.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            body.Rates,
            SourceName);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync("currencies", cancellationToken);
        return await ReadAsync<Dictionary<string, string>>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClientFactory.CreateClient(HttpClientName).GetAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ExchangeRatesUnavailableException("The exchange rate service could not be reached.", ex);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new ExchangeRatesUnavailableException($"The exchange rate service returned {(int)response.StatusCode}.");

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw new ExchangeRatesUnavailableException("The exchange rate service returned an empty response.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new ExchangeRatesUnavailableException("The exchange rate service returned an unexpected response.", ex);
        }
    }
}
