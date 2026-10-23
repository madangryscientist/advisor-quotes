import {
  Body,
  Button,
  Callout,
  Card,
  Checkbox,
  DatePicker,
  Field,
  FormLayout,
  Input,
  Select,
  Stack,
  Textarea,
  buttonClass,
  fromISODate,
  toISODate,
} from "@hackcove/ui";
import { Link } from "@tanstack/react-router";
import { useRef, useState } from "react";
import type { ChangeEvent, FormEvent, ReactNode } from "react";
import { PRODUCTS } from "../labels";
import { noAnswer } from "../server/api-error";
import type { ApiError } from "../server/api-error";
import { saveQuote } from "../server/fns";
import type { QuoteDetails, SaveQuoteInput } from "../server/quotes-api";

type Values = {
  clientName: string;
  clientDateOfBirth: string;
  clientIsSmoker: boolean;
  productCode: string;
  coverAmount: string;
  termYears: string;
  notes: string;
};

const BLANK: Values = {
  clientName: "",
  clientDateOfBirth: "",
  clientIsSmoker: false,
  productCode: "LIFE",
  coverAmount: "",
  termYears: "",
  notes: "",
};

function fromQuote(quote: QuoteDetails): Values {
  return {
    clientName: quote.clientName ?? "",
    clientDateOfBirth: quote.clientDateOfBirth ?? "",
    clientIsSmoker: quote.clientIsSmoker ?? false,
    productCode: quote.productCode ?? "LIFE",
    coverAmount: String(quote.coverAmount ?? ""),
    termYears: String(quote.termYears ?? ""),
    notes: quote.notes ?? "",
  };
}

const textOrNull = (value: string) => (value.trim() === "" ? null : value.trim());
const numberOrNull = (value: string) => (value.trim() === "" ? null : Number(value));

/** Empty inputs go as null so the API answers with a field error instead of a silent default. */
function toCommand(values: Values, quoteId: string | undefined): SaveQuoteInput {
  return {
    quoteId: quoteId ?? null,
    clientName: textOrNull(values.clientName),
    clientDateOfBirth: textOrNull(values.clientDateOfBirth),
    clientIsSmoker: values.clientIsSmoker,
    productCode: values.productCode,
    coverAmount: numberOrNull(values.coverAmount),
    termYears: numberOrNull(values.termYears),
    notes: textOrNull(values.notes),
  };
}

type TextName = Exclude<keyof Values, "clientIsSmoker">;
type TextChange = ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>;

/**
 * Sends the form and tracks the outcome. A create carries one Idempotency-Key: made on mount, sent again
 * by every retry, remade after a success. A second submit while one is in flight is ignored.
 */
function useSave(values: Values, quote: QuoteDetails | undefined, onSaved: SavedHandler) {
  const [error, setError] = useState<ApiError>();
  const [pending, setPending] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());
  // State lags a fast second click; the ref does not.
  const inFlight = useRef(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (inFlight.current) return;
    inFlight.current = true;
    setPending(true);
    try {
      const result = await saveQuote({
        data: {
          quote: toCommand(values, quote?.quoteId),
          idempotencyKey: quote ? undefined : idempotencyKey,
        },
      });
      if (result.ok) {
        setError(undefined);
        setIdempotencyKey(crypto.randomUUID());
        await onSaved(result.value.quoteId);
      } else {
        setError(result.error);
      }
    } catch {
      // The key is kept, so the retry replays the create if it did reach the API.
      setError(noAnswer);
    } finally {
      inFlight.current = false;
      setPending(false);
    }
  }

  return { error, pending, submit };
}

type SavedHandler = (quoteId: string) => void | Promise<void>;

/** Creates a quote, or edits `quote` (a Draft) when given. `onSaved` gets the saved quote's id. */
export function QuoteForm({
  quote,
  breadcrumbs,
  title,
  subtitle,
  onSaved,
}: {
  quote?: QuoteDetails;
  breadcrumbs: ReactNode;
  title: string;
  subtitle: string;
  onSaved: SavedHandler;
}) {
  const [values, setValues] = useState(() => (quote ? fromQuote(quote) : BLANK));
  const { error, pending, submit } = useSave(values, quote, onSaved);

  const text = (name: TextName) => ({
    id: name,
    value: values[name],
    onChange: (event: TextChange) => setValues({ ...values, [name]: event.target.value }),
  });
  const errorOf = (name: string) => error?.fieldErrors[name]?.join(" ");
  const submitLabel = quote ? "Save changes" : "Create quote";

  return (
    <FormLayout
      breadcrumbs={breadcrumbs}
      title={title}
      subtitle={subtitle}
      onSubmit={(event: FormEvent) => void submit(event)}
      noValidate
      actions={<FormActions quote={quote} pending={pending} submitLabel={submitLabel} />}
    >
      {error && (
        <Callout tone="danger" title="Could not save the quote">
          {error.message}
        </Callout>
      )}
      <Card title="Client">
        <Stack>
          <Field label="Client name" htmlFor="clientName" error={errorOf("clientName")}>
            <Input autoComplete="off" {...text("clientName")} />
          </Field>
          {/* DatePicker has no way to show a server error, so the API's own is shown under it. A half-typed date reports null. */}
          <Stack gap="sm">
            <DatePicker
              label="Date of birth"
              name="clientDateOfBirth"
              description={
                errorOf("clientDateOfBirth")
                  ? undefined
                  : "Aged 18 to 65 when the quote is created."
              }
              value={fromISODate(values.clientDateOfBirth)}
              onChange={(date) => setValues({ ...values, clientDateOfBirth: toISODate(date) })}
            />
            {errorOf("clientDateOfBirth") && (
              <p role="alert" className="text-xs text-red-700">
                {errorOf("clientDateOfBirth")}
              </p>
            )}
          </Stack>
          <Field label="Smoker" htmlFor="clientIsSmoker" error={errorOf("clientIsSmoker")}>
            <Checkbox
              id="clientIsSmoker"
              label="The client smokes"
              checked={values.clientIsSmoker}
              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                setValues({ ...values, clientIsSmoker: event.target.checked })
              }
            />
          </Field>
        </Stack>
      </Card>
      <CoverFields text={text} errorOf={errorOf} />
    </FormLayout>
  );
}

/** Cancel goes back where the form came from: the quote when editing, the list when creating. */
function FormActions({
  quote,
  pending,
  submitLabel,
}: {
  quote: QuoteDetails | undefined;
  pending: boolean;
  submitLabel: string;
}) {
  return (
    <>
      <Body>The premium is calculated on the server when you save.</Body>
      {quote ? (
        <Link
          to="/quotes/$quoteId"
          params={{ quoteId: quote.quoteId ?? "" }}
          className={buttonClass("ghost")}
        >
          Cancel
        </Link>
      ) : (
        <Link to="/quotes" search={{ status: undefined }} className={buttonClass("ghost")}>
          Cancel
        </Link>
      )}
      <Button type="submit" variant="primary" busy={pending} busyLabel="Saving…">
        {submitLabel}
      </Button>
    </>
  );
}

type FieldProps = {
  text: (name: TextName) => { id: string; value: string; onChange: (event: TextChange) => void };
  errorOf: (name: string) => string | undefined;
};

/** What is covered: product, amount and term, the inputs to the premium, plus the adviser's notes. */
function CoverFields({ text, errorOf }: FieldProps) {
  return (
    <Card title="Cover">
      <Stack>
        <Field label="Product" htmlFor="productCode" error={errorOf("productCode")}>
          <Select {...text("productCode")}>
            {PRODUCTS.map((product) => (
              <option key={product.code} value={product.code}>
                {product.label}
              </option>
            ))}
          </Select>
        </Field>
        <Field
          label="Cover amount (ZAR)"
          htmlFor="coverAmount"
          hint="R10 000 to R5 000 000."
          error={errorOf("coverAmount")}
        >
          <Input type="number" inputMode="numeric" step={1000} {...text("coverAmount")} />
        </Field>
        <Field
          label="Term (years)"
          htmlFor="termYears"
          hint="1 to 30 years."
          error={errorOf("termYears")}
        >
          <Input type="number" inputMode="numeric" {...text("termYears")} />
        </Field>
        <Field label="Notes" htmlFor="notes" hint="Optional." error={errorOf("notes")}>
          <Textarea {...text("notes")} />
        </Field>
      </Stack>
    </Card>
  );
}
