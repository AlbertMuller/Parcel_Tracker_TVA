using Microsoft.Extensions.Logging;
using OrderTracker.Application.Abstractions;
using OrderTracker.Application.ExchangeRates;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.Orders.ConvertOrderCurrency;

public sealed record ConvertOrderCurrencyCommand(Guid OrderId, string? Currency);

/// <summary>
/// Re-prices a Pending order in another currency at the latest exchange rate. The rate is always quoted
/// from the order's base currency; the aggregate does the re-pricing and enforces the Pending-only rule.
/// </summary>
public sealed class ConvertOrderCurrencyHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    ExchangeRateService exchangeRates,
    TimeProvider clock,
    ILogger<ConvertOrderCurrencyHandler> logger) : ICommandHandler<ConvertOrderCurrencyCommand, Result<OrderDto>>
{
    internal const int MaxAttempts = 5;

    public async Task<Result<OrderDto>> HandleAsync(ConvertOrderCurrencyCommand command, CancellationToken cancellationToken = default)
    {
        var target = CurrencyCode.Create(command.Currency);
        if (target.IsFailure)
            return target.Error;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var order = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
            if (order is null)
                return OrderErrors.NotFound(command.OrderId);

            if (order.Currency == target.Value)
                return OrderDto.From(order);

            if (!order.CanChangeCurrency)
                return OrderErrors.CurrencyLocked(order.Status);

            var rate = await GetRateAsync(order.BaseCurrency, target.Value, cancellationToken);
            if (rate.IsFailure)
                return rate.Error;

            var result = order.ConvertCurrency(rate.Value, clock.GetUtcNowForStorage());
            if (result.IsFailure)
                return result.Error;

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return OrderDto.From(order);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                logger.LogInformation("Order {OrderId} changed concurrently; retrying currency conversion (attempt {Attempt}).", command.OrderId, attempt);
            }
        }

        throw new InvalidOperationException("Unreachable: the last attempt either returns or throws.");
    }

    private async Task<Result<ExchangeRate>> GetRateAsync(CurrencyCode baseCurrency, CurrencyCode target, CancellationToken cancellationToken)
    {
        // Converting back to the base currency restores the submitted prices; no rate needed.
        if (target == baseCurrency)
            return ExchangeRate.Identity(baseCurrency, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

        try
        {
            var rate = await exchangeRates.GetRateAsync(baseCurrency, target, cancellationToken);
            return rate is null ? ExchangeRateErrors.NotAvailable(baseCurrency, target) : rate;
        }
        catch (ExchangeRatesUnavailableException)
        {
            return ExchangeRateErrors.Unavailable("Exchange rates are temporarily unavailable, so the currency can't be changed right now. Try again shortly.");
        }
    }
}
