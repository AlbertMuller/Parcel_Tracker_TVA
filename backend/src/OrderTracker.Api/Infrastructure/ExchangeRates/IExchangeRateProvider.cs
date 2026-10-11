namespace OrderTracker.Api.Infrastructure.ExchangeRates;

/// <summary>Rates for one base currency on one day. <c>Rates[X]</c> = units of X per 1 unit of <see cref="Base"/>.</summary>
public sealed record ExchangeRateTable(string Base, DateOnly Date, IReadOnlyDictionary<string, decimal> Rates, string Source);

/// <summary>A source of exchange rates (e.g. a public API). Implementations do no caching.</summary>
public interface IExchangeRateProvider
{
    /// <summary>Latest rates for <paramref name="baseCurrency"/>, or null if the provider doesn't support that currency.</summary>
    /// <exception cref="ExchangeRatesUnavailableException">The provider could not be reached or returned an error.</exception>
    Task<ExchangeRateTable?> GetLatestAsync(string baseCurrency, CancellationToken cancellationToken);

    /// <summary>Supported currencies: ISO code → display name.</summary>
    /// <exception cref="ExchangeRatesUnavailableException">The provider could not be reached or returned an error.</exception>
    Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken);
}

public sealed class ExchangeRatesUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ExchangeRateOptions
{
    public const string Section = "ExchangeRates";

    public string BaseUrl { get; set; } = "https://api.frankfurter.dev/v1/";

    /// <summary>How long fetched rates are reused before asking the provider again. ECB rates change once a day.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>If the provider is down, keep serving the last good rates for up to this long.</summary>
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromDays(3);

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
