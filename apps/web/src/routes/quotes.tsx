import {
  Body,
  Callout,
  Card,
  FilterGroup,
  ListLayout,
  Pill,
  Stack,
  ToggleChip,
  buttonClass,
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
      <Link to="/quotes/new" className={buttonClass("primary")}>
        New quote
      </Link>
    ),
  };
  const filters = (
    <FilterGroup legend="Status">
      <ToggleChip
        pressed={status === undefined}
        onClick={() => navigate({ search: { status: undefined } })}
      >
        All
      </ToggleChip>
      {QUOTE_STATUSES.map((s) => (
        <ToggleChip
          key={s}
          pressed={status === s}
          onClick={() => navigate({ search: { status: s } })}
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
      <section className="overflow-x-auto rounded-xl border border-slate-200/70 bg-white shadow-sm">
        <table className="w-full text-left text-sm whitespace-nowrap">
          <thead className="border-b border-slate-200 bg-slate-50 text-xs tracking-wide text-slate-500 uppercase">
            <tr>
              <th className="px-5 py-3 font-medium">Reference</th>
              <th className="px-5 py-3 font-medium">Client</th>
              <th className="px-5 py-3 font-medium">Product</th>
              <th className="px-5 py-3 text-right font-medium">Cover</th>
              <th className="px-5 py-3 text-right font-medium">Premium / month</th>
              <th className="px-5 py-3 font-medium">Status</th>
              <th className="px-5 py-3 font-medium">Updated</th>
            </tr>
          </thead>
          <tbody>
            {result.value.map((quote) => (
              <QuoteRow key={quote.quoteId} quote={quote} />
            ))}
          </tbody>
        </table>
      </section>
    </ListLayout>
  );
}

function NoQuotes({ status }: { status: QuoteStatus | undefined }) {
  return (
    <Card>
      <Stack>
        <Body as="p">
          {status === undefined
            ? "No quotes yet for this adviser."
            : `No quotes are ${statusLabel(status).toLowerCase()}.`}
        </Body>
        {status === undefined && (
          <Link to="/quotes/new" className={buttonClass("outline")}>
            Create the first quote
          </Link>
        )}
      </Stack>
    </Card>
  );
}

type QuoteSummary = Extract<Awaited<ReturnType<typeof listQuotes>>, { ok: true }>["value"][number];

/** The reference link stretches over the row, so the whole row opens the quote. */
function QuoteRow({ quote }: { quote: QuoteSummary }) {
  const currency = quote.currency ?? "ZAR";
  return (
    <tr className="relative border-t border-slate-200 text-slate-700 first:border-t-0 hover:bg-slate-50">
      <td className="px-5 py-3 font-mono text-xs">
        <Link
          to="/quotes/$quoteId"
          params={{ quoteId: quote.quoteId ?? "" }}
          className="text-violet-600 after:absolute after:inset-0 hover:text-violet-700"
        >
          {quote.externalReference}
        </Link>
      </td>
      <td className="px-5 py-3 font-medium text-slate-900">{quote.clientName}</td>
      <td className="px-5 py-3">
        {productLabel(quote.productCode)}
        <span className="text-slate-500"> · {quote.termYears} yrs</span>
      </td>
      <td className="px-5 py-3 text-right tabular-nums">
        {money(quote.coverAmount ?? 0, currency)}
      </td>
      <td className="px-5 py-3 text-right font-medium text-slate-900 tabular-nums">
        {money(quote.premium ?? 0, currency)}
      </td>
      <td className="px-5 py-3">
        <Pill tone={statusTone(quote.status)} dot>
          {statusLabel(quote.status)}
        </Pill>
      </td>
      <td className="px-5 py-3 whitespace-nowrap text-slate-500">{formatDay(quote.updatedAt)}</td>
    </tr>
  );
}
