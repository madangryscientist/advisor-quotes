namespace Api.Quotes.Domain;

/// <summary>Another request with the same adviser and <c>Idempotency-Key</c> created its quote first.</summary>
public sealed class DuplicateIdempotencyKeyException(Exception? inner = null)
    : Exception("A quote with this Idempotency-Key already exists for the adviser.", inner);
