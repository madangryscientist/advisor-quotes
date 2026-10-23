namespace Api.Cqrs;

/// <summary>A value or a typed <see cref="Failure"/>. Handlers <c>return value;</c> or <c>return Failure.NotFound("…");</c>.</summary>
public readonly record struct Result<T>
{
    public T? Value { get; private init; }

    public Failure? Failure { get; private init; }

    public static implicit operator Result<T>(T value) => new() { Value = value };

    public static implicit operator Result<T>(Failure failure) => new() { Failure = failure };
}
