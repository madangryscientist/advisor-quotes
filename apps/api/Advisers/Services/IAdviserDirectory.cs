namespace Api.Advisers.Services;

/// <summary>Who counts as a known adviser: a row in the <c>advisers</c> table.</summary>
public interface IAdviserDirectory
{
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken);
}
