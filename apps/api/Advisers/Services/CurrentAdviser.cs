using Api.Cqrs;

namespace Api.Advisers.Services;

/// <summary>Scoped per request. The endpoint helper calls <see cref="ResolveAsync"/> from the <c>X-Adviser-Id</c> header before the handler runs.</summary>
public sealed class CurrentAdviser(IAdviserDirectory directory) : ICurrentAdviser
{
    public const string HeaderName = "X-Adviser-Id";

    private string? _id;

    public string Id => _id ?? throw new InvalidOperationException("No adviser was resolved: this endpoint opted out, or runs outside a request.");

    /// <summary>The resolved adviser, or null before <see cref="ResolveAsync"/> succeeds; for telemetry, which must not throw.</summary>
    public string? ResolvedId => _id;

    /// <summary>Null when the adviser is known (and now available as <see cref="Id"/>), otherwise the failure to return.</summary>
    public async Task<Failure?> ResolveAsync(string? header, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return Failure.Unauthorized($"The {HeaderName} header is required.");
        }

        if (!await directory.ExistsAsync(header, cancellationToken))
        {
            return Failure.Forbidden("unknown adviser");
        }

        _id = header;
        return null;
    }
}
