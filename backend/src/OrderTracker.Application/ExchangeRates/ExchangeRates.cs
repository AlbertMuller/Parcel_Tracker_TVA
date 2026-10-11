using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderTracker.Application.Abstractions;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.ExchangeRates;

/// <summary>Rates for one base currency on one day. <c>Rates[X]</c> = units of X per 1 unit of <see cref="Base"/>.</summary>
public sealed record ExchangeRateTable(string Base, DateOnly Date, IReadOnlyDictionary<string, decimal> Rates, string Source);

/// <summary>Port to an exchange-rate source (e.g. a public API). Implementations do no caching.</summary>
public interface IExchangeRateProvider
{
    /// <summary>Latest rates for <paramref name="baseCurrency"/>, or null if the source doesn't support that currency.</summary>
    /// <exception cref="ExchangeRatesUnavailableException">The source could not be reached or returned an error.</exception>
    Task<ExchangeRateTable?> GetLatestAsync(string baseCurrency, CancellationToken cancellationToken);

    /// <summary>Supported currencies: ISO code → display name.</summary>
    /// <exception cref="ExchangeRatesUnavailableException">The source could not be reached or returned an error.</exception>
    Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken);
}

public sealed class ExchangeRatesUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Bound from the <c>ExchangeRates</c> configuration section.</summary>
public sealed class ExchangeRateCacheOptions
{
    public const string Section = "ExchangeRates";

    /// <summary>How long fetched rates are reused before asking the source again. ECB rates change once a day.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>If the source is down, keep serving the last good rates for up to this long.</summary>
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromDays(3);
}

public sealed record ExchangeRatesResult(ExchangeRateTable Table, DateTimeOffset FetchedAt, bool IsStale);

public static class ExchangeRateErrors
{
    public static Error NotAvailable(CurrencyCode from, CurrencyCode to) =>
        new("exchange_rate.not_available", "No exchange rate", $"No exchange rate is available from {from} to {to}.", ErrorType.Unprocessable);

    public static Error Unavailable(string message) =>
        new("exchange_rate.unavailable", "Exchange rates unavailable", message, ErrorType.Unavailable);
}

/// <summary>
/// Caches rates from an <see cref="IExchangeRateProvider"/>. Fresh rates are reused for
/// <see cref="ExchangeRateCacheOptions.CacheDuration"/>. If the source fails, the last good rates are served
/// (flagged stale) for up to <see cref="ExchangeRateCacheOptions.MaxStaleness"/>, so an outage doesn't stop
/// reps working.
/// </summary>
public sealed class ExchangeRateService(
    IExchangeRateProvider provider,
    TimeProvider clock,
    IOptions<ExchangeRateCacheOptions> options,
    ILogger<ExchangeRateService> logger)
{
    private static readonly TimeSpan CurrencyListCacheDuration = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, ExchangeRatesResult> _ratesByBase = new(StringComparer.Ordinal);
    private (IReadOnlyDictionary<string, string> Currencies, DateTimeOffset FetchedAt)? _currencies;

    /// <summary>Latest rates for <paramref name="baseCurrency"/>, or null if no rates exist for it.</summary>
    /// <exception cref="ExchangeRatesUnavailableException">Source is down and there are no usable cached rates.</exception>
    public async Task<ExchangeRatesResult?> GetRatesAsync(CurrencyCode baseCurrency, CancellationToken cancellationToken)
    {
        var key = baseCurrency.Value;
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
    /// <exception cref="ExchangeRatesUnavailableException">Source is down and there are no usable cached rates.</exception>
    public async Task<ExchangeRate?> GetRateAsync(CurrencyCode from, CurrencyCode to, CancellationToken cancellationToken)
    {
        if (from == to)
            return ExchangeRate.Identity(from, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        var result = await GetRatesAsync(from, cancellationToken);
        if (result is null || !result.Table.Rates.TryGetValue(to.Value, out var rate) || rate <= 0)
            return null;

        return new ExchangeRate(from, to, rate, result.Table.Date, result.Table.Source);
    }

    /// <exception cref="ExchangeRatesUnavailableException">Source is down and the list was never fetched.</exception>
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

public sealed record ExchangeRatesDto(
    string Base,
    DateOnly Date,
    string Source,
    DateTimeOffset FetchedAt,
    bool IsStale,
    IReadOnlyDictionary<string, decimal> Rates);

public sealed record GetCurrenciesQuery;

public sealed class GetCurrenciesHandler(ExchangeRateService rates)
    : IQueryHandler<GetCurrenciesQuery, Result<IReadOnlyDictionary<string, string>>>
{
    public async Task<Result<IReadOnlyDictionary<string, string>>> HandleAsync(GetCurrenciesQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var currencies = await rates.GetCurrenciesAsync(cancellationToken);
            return new SortedDictionary<string, string>(currencies.ToDictionary(), StringComparer.Ordinal);
        }
        catch (ExchangeRatesUnavailableException ex)
        {
            return ExchangeRateErrors.Unavailable($"{ex.Message} Try again shortly.");
        }
    }
}

public sealed record GetExchangeRatesQuery(string? Base);

public sealed class GetExchangeRatesHandler(ExchangeRateService rates) : IQueryHandler<GetExchangeRatesQuery, Result<ExchangeRatesDto>>
{
    public async Task<Result<ExchangeRatesDto>> HandleAsync(GetExchangeRatesQuery query, CancellationToken cancellationToken = default)
    {
        var raw = string.IsNullOrWhiteSpace(query.Base) ? "USD" : query.Base;
        var code = CurrencyCode.Create(raw, field: "base");
        if (code.IsFailure)
            return ValidationError.For("base", $"'{raw}' is not a 3-letter ISO 4217 currency code.");

        try
        {
            var result = await rates.GetRatesAsync(code.Value, cancellationToken);
            if (result is null)
                return ValidationError.For("base", $"No exchange rates are available for {code.Value}.");

            var t = result.Table;
            return new ExchangeRatesDto(t.Base, t.Date, t.Source, result.FetchedAt, result.IsStale, t.Rates);
        }
        catch (ExchangeRatesUnavailableException ex)
        {
            return ExchangeRateErrors.Unavailable($"{ex.Message} Try again shortly.");
        }
    }
}
