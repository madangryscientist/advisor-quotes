namespace Api.Cqrs;

public sealed record Failure(FailureKind Kind, string Detail, IDictionary<string, string[]>? Errors = null)
{
    public static Failure Validation(string detail, IDictionary<string, string[]>? errors = null) => new(FailureKind.Validation, detail, errors);

    public static Failure Unauthorized(string detail) => new(FailureKind.Unauthorized, detail);

    public static Failure Forbidden(string detail) => new(FailureKind.Forbidden, detail);

    public static Failure NotFound(string detail) => new(FailureKind.NotFound, detail);

    public static Failure Conflict(string detail) => new(FailureKind.Conflict, detail);

    public static Failure IdempotencyMismatch(string detail) => new(FailureKind.IdempotencyMismatch, detail);
}
