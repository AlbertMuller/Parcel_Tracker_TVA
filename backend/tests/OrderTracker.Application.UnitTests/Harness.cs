using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Application.Orders.ChangeOrderStatus;
using OrderTracker.Application.Orders.ConvertOrderCurrency;
using OrderTracker.Application.Orders.GetOrder;
using OrderTracker.Application.Orders.ListOrders;
using OrderTracker.Application.Orders.SubmitOrder;
using OrderTracker.TestKit;

namespace OrderTracker.Application.UnitTests;

/// <summary>All use-case handlers wired to one in-memory store, one fake clock and fake exchange rates.</summary>
internal sealed class Harness
{
    public InMemoryOrderStore Store { get; } = new();
    public ManualTimeProvider Clock { get; } = new();
    public FakeExchangeRateProvider Rates { get; } = new();

    public ExchangeRateService RateService { get; }
    public SubmitOrderHandler Submit { get; }
    public ChangeOrderStatusHandler ChangeStatus { get; }
    public ConvertOrderCurrencyHandler ConvertCurrency { get; }
    public GetOrderHandler Get { get; }
    public ListOrdersHandler List { get; }

    public Harness()
    {
        RateService = NewRateService(Rates, Clock);
        Submit = new SubmitOrderHandler(Store, Store, Clock, NullLogger<SubmitOrderHandler>.Instance);
        ChangeStatus = new ChangeOrderStatusHandler(Store, Store, Clock, NullLogger<ChangeOrderStatusHandler>.Instance);
        ConvertCurrency = new ConvertOrderCurrencyHandler(Store, Store, RateService, Clock, NullLogger<ConvertOrderCurrencyHandler>.Instance);
        Get = new GetOrderHandler(Store);
        List = new ListOrdersHandler(Store);
    }

    public static ExchangeRateService NewRateService(IExchangeRateProvider provider, TimeProvider clock) =>
        new(provider, clock, Options.Create(new ExchangeRateCacheOptions()), NullLogger<ExchangeRateService>.Instance);

    /// <summary>Submits the default valid order (or one with <paramref name="reference"/>) and returns its id.</summary>
    public async Task<Guid> SubmitAsync(string reference = "PO-1001") =>
        (await Submit.HandleAsync(TestData.ValidCommand(reference))).Value.Order.Id;
}
