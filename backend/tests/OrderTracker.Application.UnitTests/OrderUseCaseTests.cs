using OrderTracker.Application.ExchangeRates;
using OrderTracker.Application.Orders.ChangeOrderStatus;
using OrderTracker.Application.Orders.ConvertOrderCurrency;
using OrderTracker.Application.Orders.ListOrders;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;
using OrderTracker.Domain.Orders.Events;
using OrderTracker.TestKit;

namespace OrderTracker.Application.UnitTests;

public class ChangeOrderStatusHandlerTests
{
    [Fact]
    public async Task Walks_an_order_through_the_happy_path_and_records_history()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();

        h.Clock.Advance(TimeSpan.FromHours(1));
        await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Confirmed"));
        h.Clock.Advance(TimeSpan.FromHours(1));
        var result = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "fulfilled"));

        var order = result.Value;
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(h.Clock.GetUtcNow(), order.UpdatedAt);
        Assert.Equal([OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Fulfilled], order.StatusHistory.Select(s => s.To));
        Assert.Empty(order.AllowedTransitions);
        Assert.Equal(2, h.Store.DispatchedEvents.OfType<OrderStatusChanged>().Count());
    }

    [Fact]
    public async Task Rejected_change_returns_the_coded_error_and_leaves_the_order_unchanged()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();

        var result = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Fulfilled"));

        Assert.Equal("order.status.invalid_transition", result.Error.Code);
        Assert.Contains("must be confirmed before it can be fulfilled", result.Error.Message);
        Assert.Equal(OrderStatus.Pending, (await h.Get.HandleAsync(new(id))).Value.Status);
    }

    [Fact]
    public async Task Cancelled_orders_cannot_be_reopened()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Cancelled"));

        var result = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Confirmed"));

        Assert.Equal("order.status.final", result.Error.Code);
    }

    [Fact]
    public async Task Requesting_the_current_status_again_is_a_harmless_no_op()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Confirmed"));

        var again = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Confirmed"));

        Assert.True(again.IsSuccess);
        Assert.Equal(2, again.Value.StatusHistory.Count);
    }

    [Fact]
    public async Task Unknown_status_and_unknown_order_are_reported()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();

        var badStatus = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Shipped"));
        var missing = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(Guid.NewGuid(), "Confirmed"));

        Assert.True(Assert.IsType<ValidationError>(badStatus.Error).Errors.ContainsKey("status"));
        Assert.Equal("order.not_found", missing.Error.Code);
        Assert.Equal(ErrorType.NotFound, missing.Error.Type);
    }

    [Fact]
    public async Task A_concurrent_change_is_retried_against_the_fresh_order()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        h.Store.ConcurrencyConflictsToSimulate = 2;

        var result = await h.ChangeStatus.HandleAsync(new ChangeOrderStatusCommand(id, "Confirmed"));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, result.Value.Status);
    }
}

public class ConvertOrderCurrencyHandlerTests
{
    // Default order: 3 × 19.99 + 1 × 5.50 USD = 65.47 USD. Fake rates: USD→EUR 0.9, USD→GBP 0.75.

    [Fact]
    public async Task Converting_reprices_from_the_base_prices_and_records_the_rate()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        h.Clock.Advance(TimeSpan.FromMinutes(10));

        var order = (await h.ConvertCurrency.HandleAsync(new ConvertOrderCurrencyCommand(id, "eur"))).Value;

        Assert.Equal(("EUR", "USD"), (order.Currency, order.BaseCurrency));
        Assert.Equal([17.99m, 4.95m], order.LineItems.Select(l => l.UnitPrice));
        Assert.Equal([19.99m, 5.50m], order.LineItems.Select(l => l.BaseUnitPrice));
        Assert.Equal(58.92m, order.Total);
        Assert.Equal(h.Clock.GetUtcNow(), order.UpdatedAt);
        var change = Assert.Single(order.CurrencyHistory);
        Assert.Equal(("USD", "EUR", 0.9m, FakeExchangeRateProvider.RateDate), (change.From, change.To, change.RateFromBase, change.RateDate));
        Assert.Single(h.Store.DispatchedEvents.OfType<OrderCurrencyConverted>());
    }

    [Fact]
    public async Task Converting_back_to_the_base_currency_restores_the_exact_prices()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        await h.ConvertCurrency.HandleAsync(new(id, "EUR"));
        await h.ConvertCurrency.HandleAsync(new(id, "GBP"));

        var back = (await h.ConvertCurrency.HandleAsync(new(id, "USD"))).Value;

        Assert.Equal([19.99m, 5.50m], back.LineItems.Select(l => l.UnitPrice));
        Assert.Equal(65.47m, back.Total);
        Assert.Equal(3, back.CurrencyHistory.Count);
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Cancelled")]
    public async Task Currency_is_locked_once_the_order_leaves_pending(string status)
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        await h.ChangeStatus.HandleAsync(new(id, status));

        var result = await h.ConvertCurrency.HandleAsync(new(id, "EUR"));

        Assert.Equal("order.currency.locked", result.Error.Code);
        Assert.Contains("only be changed while an order is Pending", result.Error.Message);
    }

    [Fact]
    public async Task Requesting_the_current_currency_is_a_no_op_without_fetching_rates()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();

        var result = await h.ConvertCurrency.HandleAsync(new(id, " usd "));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.CurrencyHistory);
        Assert.Equal(0, h.Rates.RateCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public async Task Invalid_currency_codes_are_rejected(string? currency)
    {
        var h = new Harness();
        var id = await h.SubmitAsync();

        var result = await h.ConvertCurrency.HandleAsync(new(id, currency));

        Assert.Equal("validation_failed", result.Error.Code);
    }

    [Fact]
    public async Task Missing_rate_is_reported_and_the_order_is_unchanged()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();

        var result = await h.ConvertCurrency.HandleAsync(new(id, "CHF"));

        Assert.Equal("exchange_rate.not_available", result.Error.Code);
        Assert.Equal("No exchange rate is available from USD to CHF.", result.Error.Message);
        Assert.Equal("USD", (await h.Get.HandleAsync(new(id))).Value.Currency);
    }

    [Fact]
    public async Task Provider_outage_is_reported_and_the_order_is_unchanged()
    {
        var h = new Harness();
        var id = await h.SubmitAsync();
        h.Rates.Fail = true;

        var result = await h.ConvertCurrency.HandleAsync(new(id, "EUR"));

        Assert.Equal("exchange_rate.unavailable", result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    [Fact]
    public async Task Resubmitting_the_original_order_after_a_conversion_still_finds_it()
    {
        var h = new Harness();
        var id = await h.SubmitAsync("PO-FX");
        await h.ConvertCurrency.HandleAsync(new(id, "EUR"));

        var again = await h.Submit.HandleAsync(TestData.ValidCommand("PO-FX"));

        Assert.True(again.Value.AlreadySubmitted);
        Assert.Equal("EUR", again.Value.Order.Currency);
    }
}

public class ListOrdersHandlerTests
{
    [Fact]
    public async Task Lists_most_recent_orders_first_including_ties()
    {
        var h = new Harness();
        await h.SubmitAsync("PO-1");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.SubmitAsync("PO-2");
        await h.SubmitAsync("PO-3"); // same instant as PO-2: the later number comes first

        var page = (await h.List.HandleAsync(new ListOrdersQuery())).Value;

        Assert.Equal(["PO-3", "PO-2", "PO-1"], page.Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public async Task Filters_by_status_and_search_and_pages_results()
    {
        var h = new Harness();
        for (var i = 1; i <= 5; i++)
        {
            await h.SubmitAsync($"PO-{i}");
            h.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        var target = (await h.List.HandleAsync(new ListOrdersQuery(Search: "PO-2"))).Value.Items.Single();
        await h.ChangeStatus.HandleAsync(new(target.Id, "Confirmed"));

        var confirmed = (await h.List.HandleAsync(new ListOrdersQuery(Status: "confirmed"))).Value;
        var secondPage = (await h.List.HandleAsync(new ListOrdersQuery(Page: 2, PageSize: 2))).Value;

        Assert.Equal(["PO-2"], confirmed.Items.Select(o => o.ExternalReference));
        Assert.Equal(5, secondPage.TotalCount);
        Assert.Equal(["PO-3", "PO-2"], secondPage.Items.Select(o => o.ExternalReference));
    }

    [Fact]
    public async Task Page_size_is_clamped_and_unknown_status_rejected()
    {
        var h = new Harness();

        var clamped = (await h.List.HandleAsync(new ListOrdersQuery(Page: 0, PageSize: 500))).Value;
        var bad = await h.List.HandleAsync(new ListOrdersQuery(Status: "Shipped"));

        Assert.Equal((1, ListOrdersHandler.MaxPageSize), (clamped.Page, clamped.PageSize));
        Assert.Equal("validation_failed", bad.Error.Code);
    }
}

public class ExchangeRateServiceTests
{
    private static readonly CurrencyCode Usd = CurrencyCode.From("USD");

    private static (ExchangeRateService Service, FakeExchangeRateProvider Provider, ManualTimeProvider Clock) Create()
    {
        var provider = new FakeExchangeRateProvider();
        var clock = new ManualTimeProvider();
        return (Harness.NewRateService(provider, clock), provider, clock);
    }

    [Fact]
    public async Task Reuses_rates_within_the_cache_window_and_refreshes_after_it()
    {
        var (service, provider, clock) = Create();

        await service.GetRatesAsync(Usd, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(59));
        await service.GetRatesAsync(Usd, CancellationToken.None);
        Assert.Equal(1, provider.RateCalls);

        clock.Advance(TimeSpan.FromMinutes(2));
        await service.GetRatesAsync(Usd, CancellationToken.None);
        Assert.Equal(2, provider.RateCalls);
    }

    [Fact]
    public async Task Serves_last_good_rates_flagged_stale_when_the_provider_is_down()
    {
        var (service, provider, clock) = Create();
        await service.GetRatesAsync(Usd, CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(5));
        provider.Fail = true;

        var result = await service.GetRatesAsync(Usd, CancellationToken.None);

        Assert.True(result!.IsStale);
        Assert.Equal(0.9m, result.Table.Rates["EUR"]);
    }

    [Fact]
    public async Task Gives_up_on_stale_rates_after_the_maximum_staleness()
    {
        var (service, provider, clock) = Create();
        await service.GetRatesAsync(Usd, CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(4));
        provider.Fail = true;

        await Assert.ThrowsAsync<ExchangeRatesUnavailableException>(() => service.GetRatesAsync(Usd, CancellationToken.None));
    }

    [Fact]
    public async Task Same_currency_rate_is_one_without_calling_the_provider()
    {
        var (service, provider, _) = Create();

        var rate = await service.GetRateAsync(Usd, Usd, CancellationToken.None);

        Assert.Equal(1m, rate!.Rate);
        Assert.Equal(0, provider.RateCalls);
    }
}
