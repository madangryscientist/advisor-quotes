import { Breadcrumbs } from "@hackcove/ui";
import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { QuotesCrumb } from "./-crumbs";
import { QuoteForm } from "./-quote-form";

export const Route = createFileRoute("/quotes_/new")({ component: NewQuotePage });

function NewQuotePage() {
  const navigate = useNavigate();
  return (
    <QuoteForm
      breadcrumbs={<Breadcrumbs crumbs={[<QuotesCrumb key="quotes" />, "New quote"]} />}
      title="New quote"
      subtitle="Who is covered, and for how much"
      onSaved={(quoteId) => navigate({ to: "/quotes/$quoteId", params: { quoteId } })}
    />
  );
}
