using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Api.Quotes.Commands;

/// <summary>
/// Creates a Draft quote (no <see cref="QuoteId"/>) or replaces the details of a Draft the adviser owns, and prices it.
/// Fields are nullable so a missing one is a field error rather than a default.
/// A create needs an <see cref="IdempotencyKey"/> (the <c>Idempotency-Key</c> header); an update ignores it.
/// </summary>
public sealed record SaveQuoteCommand(
    Guid? QuoteId,
    string? ClientName,
    DateOnly? ClientDateOfBirth,
    bool? ClientIsSmoker,
    string? ProductCode,
    decimal? CoverAmount,
    int? TermYears,
    string? Notes)
{
    public const int ClientNameMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public const int IdempotencyKeyMaxLength = 255;

    /// <summary>From the <c>Idempotency-Key</c> header, never the body.</summary>
    [JsonIgnore]
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// SHA-256 of the command's JSON without <see cref="QuoteId"/> or the key: what "the same create" means on a replay.
    /// The product code is hashed as stored (upper-cased), so "life" and "LIFE" are the same create.
    /// </summary>
    public string RequestHash() =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            this with { QuoteId = null, ProductCode = ProductCode?.ToUpperInvariant() })));

    /// <summary>Shape checks only. The ranges (age, cover, term, product) are the Pricing library's, reported by the handler.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(ClientName))
        {
            errors["clientName"] = ["Client name is required."];
        }
        else if (ClientName.Length > ClientNameMaxLength)
        {
            errors["clientName"] = [$"Client name must be at most {ClientNameMaxLength} characters."];
        }

        if (ClientDateOfBirth is null)
        {
            errors["clientDateOfBirth"] = ["Client date of birth is required."];
        }

        if (ClientIsSmoker is null)
        {
            errors["clientIsSmoker"] = ["Whether the client smokes is required."];
        }

        if (string.IsNullOrWhiteSpace(ProductCode))
        {
            errors["productCode"] = ["Product code is required."];
        }

        if (CoverAmount is null)
        {
            errors["coverAmount"] = ["Cover amount is required."];
        }

        if (TermYears is null)
        {
            errors["termYears"] = ["Term is required."];
        }

        if (Notes?.Length > NotesMaxLength)
        {
            errors["notes"] = [$"Notes must be at most {NotesMaxLength} characters."];
        }

        return errors;
    }
}
