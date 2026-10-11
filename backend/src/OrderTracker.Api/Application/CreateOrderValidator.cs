using System.Net.Mail;
using System.Text.RegularExpressions;
using OrderTracker.Api.Contracts;
using OrderTracker.Api.Domain;

namespace OrderTracker.Api.Application;

/// <summary>
/// Validates a create-order request and, if valid, turns it into a normalized <see cref="OrderDraft"/>.
/// Error keys use the JSON path of the offending field (e.g. <c>lineItems[1].quantity</c>) so a UI can
/// show each message next to the right input.
/// </summary>
public static partial class CreateOrderValidator
{
    public const int MaxReferenceLength = 64;
    public const int MaxNameLength = 200;
    public const int MaxSkuLength = 64;
    public const int MaxNotesLength = 1000;
    public const int MaxLineItems = 100;
    public const int MaxQuantity = 1_000_000;
    public const decimal MaxUnitPrice = 1_000_000_000m;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    public static bool IsCurrencyCode(string value) => CurrencyCode().IsMatch(value);

    public static ValidationResult Validate(CreateOrderRequest? request)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        if (request is null)
        {
            Add("body", "A request body is required.");
            return ValidationResult.Failed(errors);
        }

        var reference = request.ExternalReference?.Trim();
        if (string.IsNullOrEmpty(reference))
            Add("externalReference", "External reference is required.");
        else if (reference.Length > MaxReferenceLength)
            Add("externalReference", $"External reference must be at most {MaxReferenceLength} characters.");
        else if (reference.Any(char.IsControl))
            Add("externalReference", "External reference contains invalid characters.");

        var email = request.Customer?.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
            Add("customer.email", "Customer email is required.");
        else if (!IsValidEmail(email))
            Add("customer.email", "Customer email is not a valid email address.");

        var customerName = request.Customer?.Name?.Trim();
        if (string.IsNullOrEmpty(customerName))
            Add("customer.name", "Customer name is required.");
        else if (customerName.Length > MaxNameLength)
            Add("customer.name", $"Customer name must be at most {MaxNameLength} characters.");

        var currency = request.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency))
            Add("currency", "Currency is required.");
        else if (!CurrencyCode().IsMatch(currency))
            Add("currency", "Currency must be a 3-letter ISO 4217 code, e.g. USD.");

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes?.Length > MaxNotesLength)
            Add("notes", $"Notes must be at most {MaxNotesLength} characters.");

        var lines = new List<LineItemDraft>();
        if (request.LineItems is null || request.LineItems.Count == 0)
        {
            Add("lineItems", "At least one line item is required.");
        }
        else if (request.LineItems.Count > MaxLineItems)
        {
            Add("lineItems", $"An order can have at most {MaxLineItems} line items.");
        }
        else
        {
            for (var i = 0; i < request.LineItems.Count; i++)
            {
                var item = request.LineItems[i];
                var path = $"lineItems[{i}]";
                if (item is null)
                {
                    Add(path, "Line item is required.");
                    continue;
                }

                var sku = item.Sku?.Trim();
                if (string.IsNullOrEmpty(sku))
                    Add($"{path}.sku", "SKU is required.");
                else if (sku.Length > MaxSkuLength)
                    Add($"{path}.sku", $"SKU must be at most {MaxSkuLength} characters.");

                var name = item.Name?.Trim();
                if (string.IsNullOrEmpty(name))
                    Add($"{path}.name", "Item name is required.");
                else if (name.Length > MaxNameLength)
                    Add($"{path}.name", $"Item name must be at most {MaxNameLength} characters.");

                var quantity = item.Quantity;
                if (quantity is null)
                    Add($"{path}.quantity", "Quantity is required.");
                else if (quantity <= 0 || quantity != decimal.Truncate(quantity.Value))
                    Add($"{path}.quantity", "Quantity must be a positive whole number.");
                else if (quantity > MaxQuantity)
                    Add($"{path}.quantity", $"Quantity must be at most {MaxQuantity:N0}.");

                var unitPrice = item.UnitPrice;
                if (unitPrice is null)
                    Add($"{path}.unitPrice", "Unit price is required.");
                else if (unitPrice < 0)
                    Add($"{path}.unitPrice", "Unit price cannot be negative.");
                else if (unitPrice != Math.Round(unitPrice.Value, Pricing.MoneyDecimals))
                    Add($"{path}.unitPrice", $"Unit price can have at most {Pricing.MoneyDecimals} decimal places.");
                else if (unitPrice > MaxUnitPrice)
                    Add($"{path}.unitPrice", $"Unit price must be at most {MaxUnitPrice:N0}.");

                if (errors.Keys.All(k => !k.StartsWith(path, StringComparison.Ordinal)))
                    lines.Add(new LineItemDraft(sku!, name!, (int)quantity!.Value, unitPrice!.Value));
            }
        }

        if (errors.Count > 0)
            return ValidationResult.Failed(errors);

        return ValidationResult.Ok(new OrderDraft(reference!, email!, customerName!, currency!, notes, lines));
    }

    private static bool IsValidEmail(string email) =>
        MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && parsed.Host.Contains('.');
}

public sealed class ValidationResult
{
    public OrderDraft? Draft { get; private init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; private init; } = new Dictionary<string, string[]>();
    public bool IsValid => Draft is not null;

    public static ValidationResult Ok(OrderDraft draft) => new() { Draft = draft };

    public static ValidationResult Failed(Dictionary<string, List<string>> errors) =>
        new() { Errors = errors.ToDictionary(e => e.Key, e => e.Value.ToArray()) };
}
