namespace Api.Cqrs;

/// <summary>Why a handler did not succeed. <see cref="Controllers.ApiControllerBase"/> turns each kind into one HTTP status.</summary>
public enum FailureKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    IdempotencyMismatch,
}
