using Api.Quotes.Services;
using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Commands;

namespace Api.Quotes.Handlers;

public sealed class DeclineQuoteHandler(QuotesDbContext db, ICurrentAdviser adviser, TimeProvider time) : ICommandHandler<DeclineQuoteCommand, QuoteStatusResult>
{
    public Task<Result<QuoteStatusResult>> Handle(DeclineQuoteCommand command, CancellationToken cancellationToken) =>
        QuoteStatusChanger.ChangeAsync(db, adviser, command.QuoteId, q => q.Decline(adviser.Id, command.Reason, time), cancellationToken);
}
