using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OrderTracker.Api.Domain;

public sealed record LineItemDraft(string Sku, string Name, int Quantity, decimal UnitPrice);

/// <summary>A validated, normalized order submission (trimmed strings, upper-case currency, lower-case email).</summary>
public sealed record OrderDraft(
    string ExternalReference,
    string CustomerEmail,
    string CustomerName,
    string Currency,
    string? Notes,
    IReadOnlyList<LineItemDraft> LineItems)
{
    /// <summary>
    /// Stable hash of everything the client submitted. Two submissions with the same reference and the
    /// same fingerprint are the same order sent twice; a different fingerprint means the reference was reused.
    /// </summary>
    public string Fingerprint()
    {
        var sb = new StringBuilder()
            .Append(ExternalReferences.Normalize(ExternalReference)).Append('\n')
            .Append(CustomerEmail).Append('\n')
            .Append(CustomerName).Append('\n')
            .Append(Currency).Append('\n')
            .Append(Notes ?? "").Append('\n');

        foreach (var line in LineItems)
        {
            sb.Append(line.Sku).Append('|')
              .Append(line.Name).Append('|')
              .Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append('|')
              // "10", "10.0" and "10.00" must hash the same.
              .Append(line.UnitPrice.ToString("0.############################", CultureInfo.InvariantCulture))
              .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

public static class ExternalReferences
{
    /// <summary>The uniqueness key: " po-123 " and "PO-123" are the same reference.</summary>
    public static string Normalize(string reference) => reference.Trim().ToUpperInvariant();
}

public static class CustomerIds
{
    /// <summary>
    /// There is no customer registry in scope, so a customer is identified by email address.
    /// The id is derived deterministically so the same email always maps to the same customer.
    /// </summary>
    public static string FromEmail(string normalizedEmail) =>
        "cus_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))[..12].ToLowerInvariant();
}
