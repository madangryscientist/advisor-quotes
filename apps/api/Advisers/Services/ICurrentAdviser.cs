namespace Api.Advisers.Services;

/// <summary>The adviser making the request. Handlers on endpoints that require one can rely on <see cref="Id"/>.</summary>
public interface ICurrentAdviser
{
    string Id { get; }
}
