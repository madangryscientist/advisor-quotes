import {
  Body,
  BodyStrong,
  Breadcrumbs,
  Callout,
  Caption,
  Card,
  DetailLayout,
  KeyValue,
  KeyValueList,
  Overline,
  PageHeader,
  PageHeading,
  Pill,
  Stack,
} from "@hackcove/ui";
import { createFileRoute } from "@tanstack/react-router";
import { formatDay, formatDayAndTime, productLabel, statusLabel, statusTone } from "../labels";
import { money } from "../money";
import { getQuote } from "../server/fns";
import type { QuoteDetails } from "../server/quotes-api";
import { QuotesCrumb } from "./-crumbs";
import { QuoteActions } from "./-quote-actions";

export const Route = createFileRoute("/quotes_/$quoteId")({
  loader: ({ params }) => getQuote({ data: params.quoteId }),
  component: QuotePage,
});

function QuotePage() {
  const result = Route.useLoaderData();
  if (!result.ok) {
    return (
      <>
        <PageHeader
          breadcrumbs={<Breadcrumbs crumbs={[<QuotesCrumb key="quotes" />, "Quote"]} />}
          title="Quote"
        />
        <Callout tone="danger" title="Could not load the quote">
          <Body>{result.error.message}</Body>
        </Callout>
      </>
    );
  }
  const quote = result.value;
  const currency = quote.currency ?? "ZAR";
  return (
    <DetailLayout
      breadcrumbs={
        <Breadcrumbs crumbs={[<QuotesCrumb key="quotes" />, quote.externalReference ?? "Quote"]} />
      }
      title={quote.externalReference ?? "Quote"}
      subtitle={quote.clientName ?? ""}
      aside={
        <>
          <QuoteActions quote={quote} />
          <StatusHistory history={quote.history ?? []} />
        </>
      }
    >
      <Card title="Quote">
        <Stack>
          <KeyValueList>
            <KeyValue label="Premium">
              <PageHeading as="span">{money(quote.premium ?? 0, currency)}</PageHeading>
            </KeyValue>
          </KeyValueList>
          <KeyValueList>
            <KeyValue label="Status">
              <Pill tone={statusTone(quote.status)} dot>
                {statusLabel(quote.status)}
              </Pill>
            </KeyValue>
            <KeyValue label="Cover">{money(quote.coverAmount ?? 0, currency)}</KeyValue>
            <KeyValue label="Product and term">
              {productLabel(quote.productCode)}, {quote.termYears} years
            </KeyValue>
            <KeyValue label="Created">{formatDay(quote.createdAt)}</KeyValue>
            <KeyValue label="Pricing version">{quote.pricingVersion}</KeyValue>
          </KeyValueList>
          <Caption>A monthly premium, priced on the server when the quote was saved.</Caption>
        </Stack>
      </Card>
      <ClientCard quote={quote} />
    </DetailLayout>
  );
}

function ClientCard({ quote }: { quote: QuoteDetails }) {
  return (
    <Card title="Client">
      <Stack>
        <KeyValueList>
          <KeyValue label="Name">{quote.clientName}</KeyValue>
          <KeyValue label="Date of birth">{formatDay(quote.clientDateOfBirth)}</KeyValue>
          <KeyValue label="Smoker">{quote.clientIsSmoker ? "Yes" : "No"}</KeyValue>
        </KeyValueList>
        {quote.notes && (
          <Stack gap="sm">
            <Overline>Notes</Overline>
            <Body as="p" className="whitespace-pre-line">
              {quote.notes}
            </Body>
          </Stack>
        )}
      </Stack>
    </Card>
  );
}

function StatusHistory({ history }: { history: NonNullable<QuoteDetails["history"]> }) {
  return (
    <Card title="History" count={history.length}>
      {history.length === 0 ? (
        <Body>No status changes yet.</Body>
      ) : (
        <ol aria-label="Status history" className="space-y-4">
          {history.map((change) => (
            <li key={`${change.changedAt}-${change.to}`}>
              <Stack gap="sm">
                <BodyStrong>
                  {change.from ? statusLabel(change.from) : "New"} → {statusLabel(change.to)}
                </BodyStrong>
                <Caption>
                  {change.changedBy} · {formatDayAndTime(change.changedAt)}
                </Caption>
                {change.reason && <Body as="p">{change.reason}</Body>}
              </Stack>
            </li>
          ))}
        </ol>
      )}
    </Card>
  );
}
