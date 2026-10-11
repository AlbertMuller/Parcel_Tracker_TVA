using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OrderTracker.Api.Infrastructure.ExchangeRates;

namespace OrderTracker.Api.Application;

public sealed record ExchangeRatesResult(ExchangeRateTable Table, DateTimeOffset FetchedAt, bool IsStale);

public sealed record RateQuote(string From, string To, decimal Rate, DateOnly Date, string Source, bool IsStale);

/// <summary>
/// Caches rates from an <see cref="IExchangeRateProvider"/>. Fresh rates are reused for
/// <see cref="ExchangeRateOptions.CacheDuration"/>. If the provider fails, the last good rates are served
/// (flagged stale) for up to <see cref="ExchangeRateOptions.MaxStaleness"/>, so a provider outage doesn't
/// stop reps working.
/// </summary>
public sealed class ExchangeRateService(
    IExchangeRateProvider provider,
    TimeProvider clock,
    IOptions<ExchangeRateOptions> options,
    ILogger<ExchangeRateService> logger)
{
    private static readonly TimeSpan CurrencyListCacheDuration = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, ExchangeRatesResult> _ratesByBase = new(StringComparer.Ordinal);
    private (IReadOnlyDictionary<string, string> Currencies, DateTimeOffset FetchedAt)? _currencies;

    /// <summary>Latest rates for <paramref name="baseCurrency"/>, or null if no rates exist for it.</summary>
    /// <exception cref="ExchangeRatesUnavailableException">Provider is down and there are no usable cached rates.</exception>
    public async Task<ExchangeRatesResult?> GetRatesAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var key = baseCurrency.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();
        _ratesByBase.TryGetValue(key, out var cached);

        if (cached is not null && now - cached.FetchedAt < options.Value.CacheDuration)
            return cached;

        try
        {
            var table = await provider.GetLatestAsync(key, cancellationToken);
            if (table is null)
                return null;

            var fresh = new ExchangeRatesResult(table, now, IsStale: false);
            _ratesByBase[key] = fresh;
            return fresh;
        }
        catch (ExchangeRatesUnavailableException ex) when (cached is not null && now - cached.FetchedAt < options.Value.MaxStaleness)
        {
            logger.LogWarning(ex, "Exchange rate refresh for {Base} failed; serving rates fetched at {FetchedAt}.", key, cached.FetchedAt);
            return cached with { IsStale = true };
        }
    }

    /// <summary>The rate to convert 1 unit of <paramref name="from"/> into <paramref name="to"/>, or null if unavailable.</summary>
    public async Task<RateQuote?> GetRateAsync(string from, string to, CancellationToken cancellationToken)
    {
        if (from == to)
            return new RateQuote(from, to, 1m, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), "Same currency", false);

        var result = await GetRatesAsync(from, cancellationToken);
        if (result is null || !result.Table.Rates.TryGetValue(to, out var rate))
            return null;

        return new RateQuote(from, to, rate, result.Table.Date, result.Table.Source, result.IsStale);
    }

    /// <exception cref="ExchangeRatesUnavailableException">Provider is down and the list was never fetched.</exception>
    public async Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (_currencies is { } cached && now - cached.FetchedAt < CurrencyListCacheDuration)
            return cached.Currencies;

        try
        {
            var currencies = await provider.GetCurrenciesAsync(cancellationToken);
            _currencies = (currencies, now);
            return currencies;
        }
        catch (ExchangeRatesUnavailableException ex) when (_currencies is not null)
        {
            logger.LogWarning(ex, "Currency list refresh failed; serving the cached list.");
            return _currencies.Value.Currencies;
        }
    }
}
