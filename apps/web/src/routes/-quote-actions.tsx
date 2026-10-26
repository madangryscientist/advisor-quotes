import {
  Body,
  Button,
  ButtonGroup,
  ButtonLink,
  Callout,
  Card,
  Field,
  Stack,
  Textarea,
} from "@hackcove/ui";
import { Link, useRouter } from "@tanstack/react-router";
import { useRef, useState } from "react";
import type { ChangeEvent } from "react";
import { noAnswer } from "../server/api-error";
import type { ApiError } from "../server/api-error";
import { acceptQuote, declineQuote, sendQuoteToClient } from "../server/fns";
import type { QuoteDetails, QuoteStatus } from "../server/quotes-api";

type StatusChange = Parameters<typeof sendQuoteToClient>[0]["data"];
type QuoteAction = {
  label: string;
  /** The button's label while this move is in flight. */
  busyLabel: string;
  /** A decline must say why; the other moves take an optional note. */
  reasonRequired: boolean;
  /** Accepting is the one move that is good news, so it is green. */
  tone?: "positive";
  /** Each move is its own command; the arrow defers the server function to the click. */
  run: (data: StatusChange) => ReturnType<typeof sendQuoteToClient>;
};

const send: QuoteAction = {
  label: "Send to client",
  busyLabel: "Sending…",
  reasonRequired: false,
  run: (data) => sendQuoteToClient({ data }),
};
const accept: QuoteAction = {
  label: "Accept",
  busyLabel: "Accepting…",
  reasonRequired: false,
  tone: "positive",
  run: (data) => acceptQuote({ data }),
};
const decline: QuoteAction = {
  label: "Decline",
  busyLabel: "Declining…",
  reasonRequired: true,
  run: (data) => declineQuote({ data }),
};

/**
 * The moves the UI offers from each status. The legal moves live in the API (`QuoteStatusRules.cs`) and the
 * OpenAPI document does not carry them, so this is the UI's one copy: it only decides which buttons to
 * draw, and a copy that drifts fails safe because the API still answers 409. Accepted and Declined are final.
 */
const actionsByStatus = new Map<string, readonly QuoteAction[]>([
  ["Draft", [send, decline]],
  ["SentToClient", [accept, decline]],
] satisfies [QuoteStatus, readonly QuoteAction[]][]);

/**
 * Runs one status command and tracks the outcome. After a success the router reloads, so the detail shows
 * the new status and history, and the list (stale from then on) reloads on the next visit.
 */
function useStatusChange(quoteId: string) {
  const router = useRouter();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<ApiError>();
  const [missingReason, setMissingReason] = useState(false);
  // The move in flight: its button shows busy and the others say why they wait.
  const [pending, setPending] = useState<QuoteAction>();
  const inFlight = useRef(false);

  async function run(action: QuoteAction) {
    const note = reason.trim();
    setMissingReason(action.reasonRequired && note === "");
    if ((action.reasonRequired && note === "") || inFlight.current) return;
    inFlight.current = true;
    setPending(action);
    try {
      const result = await action.run({ quoteId, reason: note === "" ? null : note });
      if (result.ok) {
        setError(undefined);
        setReason("");
        await router.invalidate();
      } else {
        setError(result.error);
      }
    } catch {
      // A retry of a move that did go through is a 409, which offers the refresh.
      setError(noAnswer);
    } finally {
      inFlight.current = false;
      setPending(undefined);
    }
  }

  const refresh = () => void router.invalidate();
  const reasonError = missingReason
    ? "A reason is required to decline."
    : error?.fieldErrors["reason"]?.join(" ");
  return { reason, setReason, error, reasonError, pending, run, refresh };
}

/** A 409 is "not allowed from the status the server now has", so the page is stale: say so and offer a reload. */
function ChangeError({ error, refresh }: { error: ApiError; refresh: () => void }) {
  if (error.kind !== "conflict") {
    return (
      <Callout tone="danger" title="Could not change the status">
        {error.message}
      </Callout>
    );
  }
  return (
    <Callout tone="danger" title="This quote changed, refresh">
      {error.message}{" "}
      <Button variant="outline" onClick={refresh}>
        Refresh
      </Button>
    </Callout>
  );
}

/** What each status means for the adviser, above the buttons. */
const nextStepByStatus = new Map<string, string>([
  ["Draft", "Edit it until it is right, then send it to the client, or decline it."],
  ["SentToClient", "Waiting on the client: record their answer."],
  ["Accepted", "The client accepted. The quote is final."],
  ["Declined", "The quote was declined. It is final."],
]);

/** The status moves as buttons, and Edit at the far end while the quote is a Draft. */
function MoveButtons({
  quoteId,
  actions,
  editable,
  change,
}: {
  quoteId: string;
  actions: readonly QuoteAction[];
  editable: boolean;
  change: ReturnType<typeof useStatusChange>;
}) {
  return (
    <ButtonGroup
      end={
        editable ? (
          <ButtonLink
            variant="ghost"
            render={(props) => <Link to="/quotes/$quoteId/edit" params={{ quoteId }} {...props} />}
          >
            Edit
          </ButtonLink>
        ) : undefined
      }
    >
      {actions.map((action) => (
        <Button
          key={action.label}
          variant={action.reasonRequired ? "outline" : "primary"}
          tone={action.tone}
          busy={change.pending === action}
          busyLabel={action.busyLabel}
          disabledReason={change.pending ? "Another change is in progress" : undefined}
          onClick={() => void change.run(action)}
        >
          {action.label}
        </Button>
      ))}
    </ButtonGroup>
  );
}

/** The moves legal from the quote's status, each its own command, plus Edit while it is a Draft. */
export function QuoteActions({ quote }: { quote: QuoteDetails }) {
  const quoteId = quote.quoteId ?? "";
  const change = useStatusChange(quoteId);
  const actions = actionsByStatus.get(quote.status ?? "") ?? [];
  const editable = quote.status === "Draft";
  const nextStep = nextStepByStatus.get(quote.status ?? "");
  const movable = actions.length > 0 || editable;

  return (
    <Card
      title="Next step"
      footer={
        movable ? (
          <MoveButtons quoteId={quoteId} actions={actions} editable={editable} change={change} />
        ) : undefined
      }
    >
      <Stack>
        {nextStep && <Body as="p">{nextStep}</Body>}
        {movable && (
          <>
            {change.error && <ChangeError error={change.error} refresh={change.refresh} />}
            <Field
              label="Reason (required to decline)"
              htmlFor="reason"
              hint="Saved in the history with the move."
              error={change.reasonError}
            >
              <Textarea
                id="reason"
                rows={2}
                value={change.reason}
                onChange={(event: ChangeEvent<HTMLTextAreaElement>) =>
                  change.setReason(event.target.value)
                }
              />
            </Field>
          </>
        )}
      </Stack>
    </Card>
  );
}
