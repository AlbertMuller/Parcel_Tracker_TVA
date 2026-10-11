using System.Globalization;
using Microsoft.Extensions.Logging;
using OrderTracker.Application.Abstractions;
using OrderTracker.Domain.Common;
using OrderTracker.Domain.Orders;

namespace OrderTracker.Application.Orders.SubmitOrder;

// Fields are nullable on purpose: the validator reports missing or malformed values with field-level
// messages instead of the serializer failing with a generic error.

public sealed record SubmitOrderCommand(
    string? ExternalReference,
    CustomerInput? Customer,
    string? Currency,
    string? Notes,
    IReadOnlyList<LineItemInput?>? LineItems);

public sealed record CustomerInput(string? Email, string? Name);

/// <summary>Quantity is a decimal so "1.5" gets a clear "must be a whole number" message.</summary>
public sealed record LineItemInput(string? Sku, string? Name, decimal? Quantity, decimal? UnitPrice);

/// <summary>The stored order, and whether it already existed (a resubmission) rather than being created now.</summary>
public sealed record SubmittedOrder(OrderDto Order, bool AlreadySubmitted);

/// <summary>
/// Submits an order without ever creating a duplicate. The external reference is the idempotency key:
/// <list type="bullet">
/// <item>new reference → the order is created;</item>
/// <item>same reference, same content → the existing order is returned (<c>AlreadySubmitted</c>);</item>
/// <item>same reference, different content → <c>order.reference.conflict</c>, nothing changes.</item>
/// </list>
/// The lookup handles the usual case; if two identical requests race past it, the store's unique index
/// rejects the second insert and it is answered from the order that won.
/// </summary>
public sealed class SubmitOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SubmitOrderHandler> logger) : ICommandHandler<SubmitOrderCommand, Result<SubmittedOrder>>
{
    public async Task<Result<SubmittedOrder>> HandleAsync(SubmitOrderCommand command, CancellationToken cancellationToken = default)
    {
        var validated = SubmitOrderValidator.Validate(command);
        if (validated.IsFailure)
            return validated.Error;

        var input = validated.Value;
        var candidate = Order.Submit(OrderId.New(), input.Reference, input.Customer, input.Currency, input.Notes, input.Lines, clock.GetUtcNowForStorage());

        var existing = await orders.GetByReferenceAsync(input.Reference, cancellationToken);
        if (existing is null)
        {
            orders.Add(candidate);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new SubmittedOrder(OrderDto.From(candidate), AlreadySubmitted: false);
            }
            catch (DuplicateReferenceException)
            {
                logger.LogInformation("Concurrent submission of reference {Reference} lost the race; answering from the stored order.", input.Reference.Value);
                existing = await orders.GetByReferenceAsync(input.Reference, cancellationToken)
                    ?? throw new InvalidOperationException($"Reference {input.Reference.Value} is taken but no order was found.");
            }
        }

        if (existing.IsSameSubmissionAs(candidate))
        {
            logger.LogInformation("Repeat submission of reference {Reference}; returning order #{Number}, no duplicate created.", existing.Reference.Value, existing.Number);
            return new SubmittedOrder(OrderDto.From(existing), AlreadySubmitted: true);
        }

        logger.LogWarning("Reference {Reference} was resubmitted with different details; order #{Number} left unchanged.", existing.Reference.Value, existing.Number);
        return OrderErrors.ReferenceConflict(existing);
    }
}

/// <summary>A submission that passed validation, as domain values.</summary>
public sealed record ValidSubmission(
    ExternalReference Reference,
    CustomerInfo Customer,
    CurrencyCode Currency,
    string? Notes,
    IReadOnlyList<NewOrderLine> Lines);

/// <summary>
/// Checks every field and reports all problems at once, keyed by JSON path (e.g. <c>lineItems[1].quantity</c>)
/// so a UI can show each message next to the right input. Field rules come from the domain value objects
/// and constants, so they're defined in one place.
/// </summary>
public static class SubmitOrderValidator
{
    public static Result<ValidSubmission> Validate(SubmitOrderCommand? command)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }
        T? Check<T>(Result<T> result) where T : class
        {
            if (result.IsSuccess) return result.Value;
            foreach (var (field, messages) in ((ValidationError)result.Error).Errors)
                foreach (var message in messages) Add(field, message);
            return null;
        }

        if (command is null)
            return ValidationError.For("body", "A request body is required.");

        var reference = Check(ExternalReference.Create(command.ExternalReference));
        var email = Check(EmailAddress.Create(command.Customer?.Email));
        var name = Check(CustomerInfo.ValidateName(command.Customer?.Name));
        var currency = Check(CurrencyCode.Create(command.Currency));

        var notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim();
        if (notes?.Length > Order.MaxNotesLength)
            Add("notes", $"Notes must be at most {Order.MaxNotesLength} characters.");

        var lines = new List<NewOrderLine>();
        if (command.LineItems is null || command.LineItems.Count == 0)
            Add("lineItems", "At least one line item is required.");
        else if (command.LineItems.Count > Order.MaxLines)
            Add("lineItems", $"An order can have at most {Order.MaxLines} line items.");
        else
        {
            for (var i = 0; i < command.LineItems.Count; i++)
            {
                if (ValidateLine(command.LineItems[i], $"lineItems[{i}]", Add) is { } line)
                    lines.Add(line);
            }
        }

        if (errors.Count > 0)
            return new ValidationError(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        var customer = CustomerInfo.Create(name, email!).Value;
        return new ValidSubmission(reference!, customer, currency!, notes, lines);
    }

    private static NewOrderLine? ValidateLine(LineItemInput? item, string path, Action<string, string> add)
    {
        if (item is null)
        {
            add(path, "Line item is required.");
            return null;
        }

        var valid = true;
        void Fail(string field, string message)
        {
            add($"{path}.{field}", message);
            valid = false;
        }

        var sku = item.Sku?.Trim();
        if (string.IsNullOrEmpty(sku))
            Fail("sku", "SKU is required.");
        else if (sku.Length > OrderLine.MaxSkuLength)
            Fail("sku", $"SKU must be at most {OrderLine.MaxSkuLength} characters.");

        var name = item.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            Fail("name", "Item name is required.");
        else if (name.Length > OrderLine.MaxNameLength)
            Fail("name", $"Item name must be at most {OrderLine.MaxNameLength} characters.");

        var quantity = item.Quantity;
        if (quantity is null)
            Fail("quantity", "Quantity is required.");
        else if (quantity <= 0 || quantity != decimal.Truncate(quantity.Value))
            Fail("quantity", "Quantity must be a positive whole number.");
        else if (quantity > OrderLine.MaxQuantity)
            Fail("quantity", $"Quantity must be at most {OrderLine.MaxQuantity.ToString("N0", CultureInfo.InvariantCulture)}.");

        var unitPrice = item.UnitPrice;
        if (unitPrice is null)
            Fail("unitPrice", "Unit price is required.");
        else if (unitPrice < 0)
            Fail("unitPrice", "Unit price cannot be negative.");
        else if (unitPrice != Math.Round(unitPrice.Value, Pricing.MoneyDecimals))
            Fail("unitPrice", $"Unit price can have at most {Pricing.MoneyDecimals} decimal places.");
        else if (unitPrice > OrderLine.MaxUnitPrice)
            Fail("unitPrice", $"Unit price must be at most {OrderLine.MaxUnitPrice.ToString("N0", CultureInfo.InvariantCulture)}.");

        return valid ? new NewOrderLine(sku!, name!, (int)quantity!.Value, unitPrice!.Value) : null;
    }
}
