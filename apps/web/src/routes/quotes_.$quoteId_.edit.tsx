import { Body, Breadcrumbs, Callout, PageHeader, type LinkRenderProps } from "@hackcove/ui";
import { Link, createFileRoute, useNavigate } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { getQuote } from "../server/fns";
import type { QuoteDetails } from "../server/quotes-api";
import { quotesCrumb } from "./-crumbs";
import { QuoteForm } from "./-quote-form";

export const Route = createFileRoute("/quotes_/$quoteId_/edit")({
  loader: ({ params }) => getQuote({ data: params.quoteId }),
  component: EditQuotePage,
});

const TITLE = "Edit quote";
const SUBTITLE = "Only a Draft can be edited; saving reprices it";

function EditQuotePage() {
  const result = Route.useLoaderData();
  const navigate = useNavigate();
  const { quoteId } = Route.useParams();
  const breadcrumbs = (
    <Breadcrumbs
      crumbs={[
        quotesCrumb,
        ...(result.ok
          ? [
              {
                label: result.value.externalReference ?? "Quote",
                render: (props: LinkRenderProps) => (
                  <Link to="/quotes/$quoteId" params={{ quoteId }} {...props} />
                ),
              },
            ]
          : []),
        "Edit",
      ]}
    />
  );
  if (!result.ok) {
    return (
      <>
        <PageHeader breadcrumbs={breadcrumbs} title={TITLE} subtitle={SUBTITLE} />
        <Callout tone="danger" title="Could not load the quote">
          <Body>{result.error.message}</Body>
        </Callout>
      </>
    );
  }
  return (
    <DraftForm
      quote={result.value}
      breadcrumbs={breadcrumbs}
      onSaved={() => navigate({ to: "/quotes/$quoteId", params: { quoteId } })}
    />
  );
}

function DraftForm({
  quote,
  breadcrumbs,
  onSaved,
}: {
  quote: QuoteDetails;
  breadcrumbs: ReactNode;
  onSaved: () => Promise<void>;
}) {
  if (quote.status !== "Draft") {
    return (
      <>
        <PageHeader breadcrumbs={breadcrumbs} title={TITLE} subtitle={SUBTITLE} />
        <Callout tone="warn" title="Not editable">
          <Body>This quote is {quote.status}, so it can no longer be changed.</Body>
        </Callout>
      </>
    );
  }
  return (
    <QuoteForm
      quote={quote}
      breadcrumbs={breadcrumbs}
      title={TITLE}
      subtitle={SUBTITLE}
      onSaved={onSaved}
    />
  );
}
