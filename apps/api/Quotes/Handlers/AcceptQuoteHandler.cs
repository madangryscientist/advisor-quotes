using Api.Quotes.Services;
using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Commands;

namespace Api.Quotes.Handlers;

public sealed class AcceptQuoteHandler(QuotesDbContext db, ICurrentAdviser adviser, TimeProvider time) : ICommandHandler<AcceptQuoteCommand, QuoteStatusResult>
{
    public Task<Result<QuoteStatusResult>> Handle(AcceptQuoteCommand command, CancellationToken cancellationToken) =>
        QuoteStatusChanger.ChangeAsync(db, adviser, command.QuoteId, q => q.Accept(adviser.Id, command.Reason, time), cancellationToken);
}
