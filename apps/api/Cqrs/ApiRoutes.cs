using Asp.Versioning;

namespace Api.Cqrs;

/// <summary>The version every controller is routed under. A route template starts with <see cref="Prefix"/>, so a controller cannot exist without it.</summary>
public static class ApiRoutes
{
    /// <summary>Reads as <c>1.0</c>, for <c>[ApiVersion]</c>.</summary>
    public const string Version = "1.0";

    public static readonly ApiVersion V1 = new(1, 0);

    /// <summary>Reads as <c>/v1</c>; the version is a route parameter so Asp.Versioning can read it from the URL.</summary>
    public const string Prefix = "v{version:apiVersion}";

    /// <summary><c>/v1/commands</c></summary>
    public const string Commands = Prefix + "/commands";

    /// <summary><c>/v1/queries</c></summary>
    public const string Queries = Prefix + "/queries";
}
