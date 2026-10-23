namespace Api.Advisers.Domain;

/// <summary>An adviser who can own quotes. Reference data: rows are seeded by a migration, never created through the API.</summary>
public sealed class Adviser
{
    public const int IdMaxLength = 32;

    public required string Id { get; init; }

    public required string Name { get; init; }
}
