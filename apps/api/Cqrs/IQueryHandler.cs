namespace Api.Cqrs;

/// <summary>Reads straight into a response DTO with an untracked EF query, skipping the domain. One per query, reached by <c>GET /v1/queries/&lt;name&gt;</c>.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken cancellationToken);
}
