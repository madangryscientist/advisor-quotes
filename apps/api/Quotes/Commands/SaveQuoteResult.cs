using Api.Quotes.Domain;
using System.Text.Json.Serialization;

namespace Api.Quotes.Commands;

public sealed record SaveQuoteResult(Guid QuoteId, string ExternalReference, decimal Premium, string Currency, string PricingVersion, QuoteStatus Status)
{
    /// <summary>True when an earlier create with the same key is returned instead; sent as the <c>Idempotent-Replayed</c> header.</summary>
    [JsonIgnore]
    public bool Replayed { get; init; }

    public static SaveQuoteResult From(Quote quote, bool replayed = false) =>
        new(quote.Id, quote.ExternalReference, quote.Premium, quote.Currency, quote.PricingVersion, quote.Status) { Replayed = replayed };
}
