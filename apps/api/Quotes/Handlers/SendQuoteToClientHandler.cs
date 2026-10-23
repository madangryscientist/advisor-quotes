using Api.Quotes.Services;
using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Commands;

namespace Api.Quotes.Handlers;

public sealed class SendQuoteToClientHandler(QuotesDbContext db, ICurrentAdviser adviser, TimeProvider time) : ICommandHandler<SendQuoteToClientCommand, QuoteStatusResult>
{
    public Task<Result<QuoteStatusResult>> Handle(SendQuoteToClientCommand command, CancellationToken cancellationToken) =>
        QuoteStatusChanger.ChangeAsync(db, adviser, command.QuoteId, q => q.SendToClient(adviser.Id, command.Reason, time), cancellationToken);
}
