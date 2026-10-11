using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using OrderTracker.Domain.Common;

namespace OrderTracker.Domain.Orders;

/// <summary>Strongly typed order identity, so an order id can't be confused with any other Guid.</summary>
public readonly record struct OrderId(Guid Value)
{
    /// <summary>Version 7 GUIDs are time-ordered, which keeps the primary-key index compact.</summary>
    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The client-provided reference (usually the customer's PO number) and the idempotency key for submissions.
/// Two references are the same if they match after trimming and ignoring case: " po-1001 " equals "PO-1001".
/// </summary>
public sealed record ExternalReference
{
    public const int MaxLength = 64;

    private ExternalReference() { }

    /// <summary>As entered (trimmed), for display.</summary>
    public string Value { get; private init; } = "";

    /// <summary>Trimmed and upper-cased: the uniqueness key.</summary>
    public string Normalized { get; private init; } = "";

    public static Result<ExternalReference> Create(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
            return Invalid("External reference is required.");
        if (value.Length > MaxLength)
            return Invalid($"External reference must be at most {MaxLength} characters.");
        if (value.Any(char.IsControl))
            return Invalid("External reference contains invalid characters.");

        return new ExternalReference { Value = value, Normalized = Normalize(value) };
    }

    public static string Normalize(string raw) => raw.Trim().ToUpperInvariant();

    public bool Equals(ExternalReference? other) => other is not null && Normalized == other.Normalized;

    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    private static ValidationError Invalid(string message) => ValidationError.For("externalReference", message);
}

/// <summary>A valid, lower-cased email address.</summary>
public sealed record EmailAddress
{
    public const int MaxLength = 320;

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static Result<EmailAddress> Create(string? raw, string field = "customer.email")
    {
        var value = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
            return ValidationError.For(field, "Customer email is required.");
        if (value.Length > MaxLength || !IsValid(value))
            return ValidationError.For(field, "Customer email is not a valid email address.");
        return new EmailAddress(value);
    }

    /// <summary>For values already validated, e.g. when loading from the database.</summary>
    public static EmailAddress From(string value)
    {
        var result = Create(value);
        return result.IsSuccess ? result.Value : throw new DomainException($"'{value}' is not a valid email address.");
    }

    public override string ToString() => Value;

    private static bool IsValid(string email) =>
        MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && parsed.Host.Contains('.');
}

/// <summary>
/// Who the order is for, captured at submission time. There is no customer registry, so the customer is
/// identified by email and <see cref="Id"/> is derived from it: the same email always gives the same id.
/// </summary>
public sealed record CustomerInfo
{
    public const int MaxNameLength = 200;

    private CustomerInfo() { }

    public string Id { get; private init; } = "";

    /// <summary>Validated and lower-cased by <see cref="EmailAddress"/>; kept as text so it can be searched.</summary>
    public string Email { get; private init; } = "";

    public string Name { get; private init; } = "";

    public static Result<CustomerInfo> Create(string? name, EmailAddress email)
    {
        var validName = ValidateName(name);
        return validName.IsSuccess
            ? new CustomerInfo { Id = IdFor(email), Email = email.Value, Name = validName.Value }
            : validName.Error;
    }

    /// <summary>The trimmed name, or why it's invalid. Separate so all customer errors can be reported at once.</summary>
    public static Result<string> ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return ValidationError.For("customer.name", "Customer name is required.");
        if (trimmed.Length > MaxNameLength)
            return ValidationError.For("customer.name", $"Customer name must be at most {MaxNameLength} characters.");
        return trimmed;
    }

    private static string IdFor(EmailAddress email) =>
        "cus_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Value)))[..12].ToLowerInvariant();
}

/// <summary>An ISO 4217-format currency code (three upper-case letters), e.g. USD.</summary>
public sealed record CurrencyCode
{
    private CurrencyCode(string value) => Value = value;

    public string Value { get; }

    public static Result<CurrencyCode> Create(string? raw, string field = "currency")
    {
        var value = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value))
            return ValidationError.For(field, "Currency is required.");
        if (!IsCode(value))
            return ValidationError.For(field, "Currency must be a 3-letter ISO 4217 code, e.g. USD.");
        return new CurrencyCode(value);
    }

    /// <summary>For values already validated, e.g. when loading from the database.</summary>
    public static CurrencyCode From(string value) =>
        IsCode(value) ? new CurrencyCode(value) : throw new DomainException($"'{value}' is not a currency code.");

    public static bool IsCode(string value) => value.Length == 3 && value.All(c => c is >= 'A' and <= 'Z');

    public override string ToString() => Value;
}

/// <summary>A quoted exchange rate: 1 unit of <see cref="From"/> = <see cref="Rate"/> units of <see cref="To"/>.</summary>
public sealed record ExchangeRate
{
    public ExchangeRate(CurrencyCode from, CurrencyCode to, decimal rate, DateOnly date, string source)
    {
        if (rate <= 0)
            throw new DomainException("Exchange rate must be positive.");
        (From, To, Rate, Date, Source) = (from, to, rate, date, source);
    }

    public CurrencyCode From { get; }
    public CurrencyCode To { get; }
    public decimal Rate { get; }
    public DateOnly Date { get; }
    public string Source { get; }

    /// <summary>Converting a currency into itself.</summary>
    public static ExchangeRate Identity(CurrencyCode currency, DateOnly date) => new(currency, currency, 1m, date, "Same currency");
}
