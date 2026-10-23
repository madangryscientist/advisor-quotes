namespace Api.Advisers.Queries;

/// <summary>One entry in the picker.</summary>
public sealed record AdviserSummary
{
    public string AdviserId { get; init; } = null!;

    public string Name { get; init; } = null!;
}
