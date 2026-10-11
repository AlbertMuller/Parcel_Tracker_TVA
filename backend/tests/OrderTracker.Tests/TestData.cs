using OrderTracker.Api.Application;
using OrderTracker.Api.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderTracker.Api.Infrastructure;
using OrderTracker.Api.Infrastructure.ExchangeRates;

namespace OrderTracker.Tests;

internal static class TestData
{
    public static CreateOrderRequest ValidRequest(string reference = "PO-1001", params LineItemInput[] lines) => new(
        ExternalReference: reference,
        Customer: new CustomerInput("Jane.Doe@Example.com", "Jane Doe"),
        Currency: "usd",
        Notes: "Deliver to loading bay 2",
        LineItems: lines.Length > 0
            ? lines.ToList()
            : [new LineItemInput("SKU-A", "Widget", 3, 19.99m), new LineItemInput("SKU-B", "Gadget", 1, 5.50m)]);

    public static (OrderService Service, InMemoryOrderRepository Repository, ManualTimeProvider Clock) NewService()
    {
        var (service, repository, clock, _) = NewServiceWithRates();
        return (service, repository, clock);
    }

    public static (OrderService Service, InMemoryOrderRepository Repository, ManualTimeProvider Clock, FakeExchangeRateProvider Rates) NewServiceWithRates()
    {
        var repository = new InMemoryOrderRepository();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
        var rates = new FakeExchangeRateProvider();
        return (new OrderService(repository, NewRateService(rates, clock), clock), repository, clock, rates);
    }

    public static ExchangeRateService NewRateService(IExchangeRateProvider provider, TimeProvider clock) =>
        new(provider, clock, Options.Create(new ExchangeRateOptions()), NullLogger<ExchangeRateService>.Instance);
}

/// <summary>Deterministic clock for tests.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>In-memory rates with a switch to simulate an outage. Rates are fixed so expected totals are exact.</summary>
public sealed class FakeExchangeRateProvider : IExchangeRateProvider
{
    public static readonly DateOnly RateDate = new(2026, 1, 14);

    private readonly Dictionary<string, Dictionary<string, decimal>> _rates = new()
    {
        ["USD"] = new() { ["EUR"] = 0.9m, ["GBP"] = 0.75m, ["JPY"] = 150m },
        ["EUR"] = new() { ["USD"] = 1.1m, ["GBP"] = 0.85m },
        ["GBP"] = new() { ["USD"] = 1.3m, ["EUR"] = 1.2m },
    };

    public bool Fail { get; set; }
    public int RateCalls { get; private set; }

    public Task<ExchangeRateTable?> GetLatestAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        RateCalls++;
        if (Fail) throw new ExchangeRatesUnavailableException("Simulated outage.");
        return Task.FromResult(_rates.TryGetValue(baseCurrency, out var rates)
            ? new ExchangeRateTable(baseCurrency, RateDate, rates, "Test rates")
            : null);
    }

    public Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken)
    {
        if (Fail) throw new ExchangeRatesUnavailableException("Simulated outage.");
        IReadOnlyDictionary<string, string> currencies = new Dictionary<string, string>
        {
            ["USD"] = "US Dollar", ["EUR"] = "Euro", ["GBP"] = "British Pound", ["JPY"] = "Japanese Yen",
        };
        return Task.FromResult(currencies);
    }
}
