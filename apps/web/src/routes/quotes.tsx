import {
  Body,
  Callout,
  Caption,
  EmptyState,
  FilterGroup,
  ListLayout,
  Pill,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  TextLink,
  ToggleChip,
  ButtonLink,
} from "@hackcove/ui";
import { Link, createFileRoute, useNavigate } from "@tanstack/react-router";
import { formatDay, productLabel, statusLabel, statusTone } from "../labels";
import { money } from "../money";
import { listQuotes } from "../server/fns";
import type { QuoteStatus } from "../server/quotes-api";

/** Every status the list can be filtered by, in lifecycle order. The values are the API's own. */
const QUOTE_STATUSES = [
  "Draft",
  "SentToClient",
  "Accepted",
  "Declined",
] as const satisfies readonly QuoteStatus[];

export const Route = createFileRoute("/quotes")({
  // The status is the only filter and it lives in the URL, so a filtered list can be linked to.
  // Always return the key: the router merges the result over the raw search, so omitting it would keep a bad value.
  validateSearch: (search: Record<string, unknown>): { status: QuoteStatus | undefined } => ({
    status: QUOTE_STATUSES.find((s) => s === search["status"]),
  }),
  loaderDeps: ({ search }) => ({ status: search.status }),
  loader: ({ deps }) => listQuotes({ data: { status: deps.status } }),
  component: QuotesPage,
});

function QuotesPage() {
  const result = Route.useLoaderData();
  const { status } = Route.useSearch();
  const navigate = useNavigate({ from: Route.fullPath });

  const header = {
    title: "Quotes",
    subtitle: "Your clients' quotes, newest first",
    actions: (
      <ButtonLink variant="primary" render={(props) => <Link to="/quotes/new" {...props} />}>
        New quote
      </ButtonLink>
    ),
  };
  const filters = (
    <FilterGroup legend="Status">
      <ToggleChip
        pressed={status === undefined}
        onClick={() => void navigate({ search: { status: undefined } })}
      >
        All
      </ToggleChip>
      {QUOTE_STATUSES.map((s) => (
        <ToggleChip
          key={s}
          pressed={status === s}
          onClick={() => void navigate({ search: { status: s } })}
        >
          {statusLabel(s)}
        </ToggleChip>
      ))}
    </FilterGroup>
  );

  if (!result.ok) {
    return (
      <ListLayout {...header} filters={filters}>
        <Callout tone="danger" title="Could not load quotes">
          <Body>{result.error.message}</Body>
        </Callout>
      </ListLayout>
    );
  }
  return (
    <ListLayout
      {...header}
      filters={filters}
      isEmpty={result.value.length === 0}
      empty={<NoQuotes status={status} />}
    >
      <Table caption="Quotes">
        <TableHead>
          <TableRow>
            <TableHeaderCell>Reference</TableHeaderCell>
            <TableHeaderCell>Client</TableHeaderCell>
            <TableHeaderCell>Product</TableHeaderCell>
            <TableHeaderCell align="end">Cover</TableHeaderCell>
            <TableHeaderCell align="end">Premium / month</TableHeaderCell>
            <TableHeaderCell>Status</TableHeaderCell>
            <TableHeaderCell>Updated</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {result.value.map((quote) => (
            <QuoteRow key={quote.quoteId} quote={quote} />
          ))}
        </TableBody>
      </Table>
    </ListLayout>
  );
}

function NoQuotes({ status }: { status: QuoteStatus | undefined }) {
  return status === undefined ? (
    <EmptyState
      title="No quotes yet"
      actions={
        <ButtonLink variant="outline" render={(props) => <Link to="/quotes/new" {...props} />}>
          Create the first quote
        </ButtonLink>
      }
    >
      <Body as="p">This adviser has not created a quote.</Body>
    </EmptyState>
  ) : (
    <EmptyState title={`No ${statusLabel(status).toLowerCase()} quotes`}>
      <Body as="p">Pick another status, or All, to see the rest.</Body>
    </EmptyState>
  );
}

type QuoteSummary = Extract<Awaited<ReturnType<typeof listQuotes>>, { ok: true }>["value"][number];

/** The reference link stretches over the row, so the whole row opens the quote. */
function QuoteRow({ quote }: { quote: QuoteSummary }) {
  const currency = quote.currency ?? "ZAR";
  return (
    <TableRow>
      <TableCell mono>
        <TextLink
          stretch
          render={(props) => (
            <Link to="/quotes/$quoteId" params={{ quoteId: quote.quoteId ?? "" }} {...props} />
          )}
        >
          {quote.externalReference}
        </TextLink>
      </TableCell>
      <TableCell tone="strong">{quote.clientName}</TableCell>
      <TableCell>
        {productLabel(quote.productCode)}
        <Caption> · {quote.termYears} yrs</Caption>
      </TableCell>
      <TableCell align="end" numeric>
        {money(quote.coverAmount ?? 0, currency)}
      </TableCell>
      <TableCell align="end" tone="strong" numeric>
        {money(quote.premium ?? 0, currency)}
      </TableCell>
      <TableCell>
        <Pill tone={statusTone(quote.status)} dot>
          {statusLabel(quote.status)}
        </Pill>
      </TableCell>
      <TableCell tone="muted">{formatDay(quote.updatedAt)}</TableCell>
    </TableRow>
  );
}
