namespace Api.Cqrs;

/// <summary>Changes state through the domain model and EF Core. One per command, reached by <c>POST /v1/commands/&lt;name&gt;</c>.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken cancellationToken);
}
