using OrderTracker.Api.Contracts;
using OrderTracker.Api.Domain;
using OrderTracker.Api.Infrastructure;
using OrderTracker.Api.Infrastructure.ExchangeRates;

namespace OrderTracker.Api.Application;

public enum CreateOrderOutcome
{
    /// <summary>A new order was stored.</summary>
    Created,

    /// <summary>The same submission was already received; the existing order is returned and nothing new is stored.</summary>
    AlreadySubmitted,

    /// <summary>The reference is already used by an order with different contents.</summary>
    ReferenceConflict,

    Invalid,
}

public sealed record CreateOrderResult(
    CreateOrderOutcome Outcome,
    Order? Order = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public enum ChangeStatusOutcome
{
    Changed,

    /// <summary>The order was already in the requested status; treated as a successful no-op.</summary>
    Unchanged,

    NotFound,
    NotAllowed,
    InvalidStatus,
}

public sealed record ChangeStatusResult(ChangeStatusOutcome Outcome, Order? Order = null, string? Message = null);

public enum ChangeCurrencyOutcome
{
    Changed,

    /// <summary>The order is already in the requested currency; nothing changed.</summary>
    Unchanged,

    NotFound,

    /// <summary>The order is no longer Pending, so its prices are locked.</summary>
    NotAllowed,

    InvalidCurrency,

    /// <summary>The rate source has no rate for this currency pair.</summary>
    RateNotAvailable,

    /// <summary>The rate source is down and no cached rates are usable.</summary>
    RatesUnavailable,
}

public sealed record ChangeCurrencyResult(ChangeCurrencyOutcome Outcome, Order? Order = null, string? Message = null);

public sealed class OrderService(IOrderRepository repository, ExchangeRateService exchangeRates, TimeProvider clock)
{
    public const int MaxPageSize = 200;
    private const int MaxConcurrencyRetries = 5;

    public CreateOrderResult Create(CreateOrderRequest request)
    {
        var validation = CreateOrderValidator.Validate(request);
        if (!validation.IsValid)
            return new CreateOrderResult(CreateOrderOutcome.Invalid, Errors: validation.Errors);

        var candidate = Order.Create(validation.Draft!, Guid.NewGuid(), clock.GetUtcNow());
        var (stored, added) = repository.AddIfReferenceIsNew(candidate);

        if (added)
            return new CreateOrderResult(CreateOrderOutcome.Created, stored);

        return stored.SubmissionFingerprint == candidate.SubmissionFingerprint
            ? new CreateOrderResult(CreateOrderOutcome.AlreadySubmitted, stored)
            : new CreateOrderResult(CreateOrderOutcome.ReferenceConflict, stored);
    }

    public Order? Get(Guid id) => repository.GetById(id);

    public OrderPage List(OrderStatus? status, string? search, int? page, int? pageSize) =>
        repository.List(new OrderQuery(
            status,
            search,
            Page: Math.Max(1, page ?? 1),
            PageSize: Math.Clamp(pageSize ?? 50, 1, MaxPageSize)));

    public ChangeStatusResult ChangeStatus(Guid id, string? requestedStatus)
    {
        if (!OrderStatusRules.TryParse(requestedStatus, out var target))
        {
            return new ChangeStatusResult(
                ChangeStatusOutcome.InvalidStatus,
                Message: $"'{requestedStatus}' is not a valid status. Valid statuses: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
        }

        // Optimistic concurrency: re-read and retry if another request changed the order in between.
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var current = repository.GetById(id);
            if (current is null)
                return new ChangeStatusResult(ChangeStatusOutcome.NotFound, Message: $"Order {id} was not found.");

            if (current.Status == target)
                return new ChangeStatusResult(ChangeStatusOutcome.Unchanged, current, $"Order is already {target}.");

            if (!OrderStatusRules.CanTransition(current.Status, target))
                return new ChangeStatusResult(ChangeStatusOutcome.NotAllowed, current, OrderStatusRules.ExplainRejection(current.Status, target));

            var updated = current.TransitionTo(target, clock.GetUtcNow());
            if (repository.TryUpdate(updated, current.Version))
                return new ChangeStatusResult(ChangeStatusOutcome.Changed, updated);
        }

        throw new InvalidOperationException($"Could not update order {id} after {MaxConcurrencyRetries} attempts due to concurrent changes.");
    }

    public async Task<ChangeCurrencyResult> ChangeCurrencyAsync(Guid id, string? requestedCurrency, CancellationToken cancellationToken)
    {
        var target = requestedCurrency?.Trim().ToUpperInvariant();
        if (target is null || !CreateOrderValidator.IsCurrencyCode(target))
            return new ChangeCurrencyResult(ChangeCurrencyOutcome.InvalidCurrency, Message: "Currency must be a 3-letter ISO 4217 code, e.g. EUR.");

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var current = repository.GetById(id);
            if (current is null)
                return new ChangeCurrencyResult(ChangeCurrencyOutcome.NotFound, Message: $"Order {id} was not found.");

            if (current.Currency == target)
                return new ChangeCurrencyResult(ChangeCurrencyOutcome.Unchanged, current, $"Order is already priced in {target}.");

            if (!current.CanChangeCurrency)
                return new ChangeCurrencyResult(ChangeCurrencyOutcome.NotAllowed, current, CurrencyRules.ExplainLocked(current.Status));

            RateQuote? quote;
            try
            {
                // Always convert from the base currency (see Order.ConvertCurrency).
                quote = await exchangeRates.GetRateAsync(current.BaseCurrency, target, cancellationToken);
            }
            catch (ExchangeRatesUnavailableException)
            {
                return new ChangeCurrencyResult(
                    ChangeCurrencyOutcome.RatesUnavailable, current,
                    "Exchange rates are temporarily unavailable, so the currency can't be changed right now. Try again shortly.");
            }

            if (quote is null)
            {
                return new ChangeCurrencyResult(
                    ChangeCurrencyOutcome.RateNotAvailable, current,
                    $"No exchange rate is available from {current.BaseCurrency} to {target}.");
            }

            var updated = current.ConvertCurrency(target, quote.Rate, quote.Date, quote.Source, clock.GetUtcNow());
            if (repository.TryUpdate(updated, current.Version))
                return new ChangeCurrencyResult(ChangeCurrencyOutcome.Changed, updated);
        }

        throw new InvalidOperationException($"Could not update order {id} after {MaxConcurrencyRetries} attempts due to concurrent changes.");
    }
}
