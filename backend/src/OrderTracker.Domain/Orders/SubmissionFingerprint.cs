using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OrderTracker.Domain.Orders;

/// <summary>
/// Stable hash of everything that was submitted. Two submissions with the same reference and the same
/// fingerprint are the same order sent twice; a different fingerprint means the reference was reused.
/// Inputs are already normalized (trimmed, lower-case email, upper-case currency), so formatting
/// differences such as "10" vs "10.00" or extra spaces don't change the hash. Line order is significant.
/// </summary>
/// <remarks>The format must not change: fingerprints of existing orders are stored in the database.</remarks>
public static class SubmissionFingerprint
{
    public static string Compute(
        ExternalReference reference,
        CustomerInfo customer,
        CurrencyCode currency,
        string? notes,
        IEnumerable<OrderLine> lines)
    {
        var sb = new StringBuilder()
            .Append(reference.Normalized).Append('\n')
            .Append(customer.Email).Append('\n')
            .Append(customer.Name).Append('\n')
            .Append(currency.Value).Append('\n')
            .Append(notes ?? "").Append('\n');

        foreach (var line in lines)
        {
            sb.Append(line.Sku).Append('|')
              .Append(line.Name).Append('|')
              .Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(line.BaseUnitPrice.ToString("0.############################", CultureInfo.InvariantCulture))
              .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
