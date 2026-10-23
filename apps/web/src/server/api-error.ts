/**
 * What a component sees when an API call fails: never a status code or a raw ProblemDetails.
 * `fieldErrors` is keyed by the command's property names (camelCase), so a form can show each one in place.
 */
export type ApiErrorKind =
  | "validation" // 400
  | "adviser" // 401, 403: the adviser is missing, unknown or not the owner
  | "not-found" // 404
  | "conflict" // 409: not allowed from the quote's status, or changed by someone else
  | "unprocessable" // 422: the Idempotency-Key was already used for a different quote
  | "unavailable"; // the API did not answer, or answered something unexpected

export type ApiError = {
  kind: ApiErrorKind;
  message: string;
  fieldErrors: Record<string, string[]>;
};

export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: ApiError };

const kindByStatus = new Map<number, ApiErrorKind>([
  [400, "validation"],
  [401, "adviser"],
  [403, "adviser"],
  [404, "not-found"],
  [409, "conflict"],
  [422, "unprocessable"],
]);

function text(body: unknown, key: string): string | undefined {
  const value = typeof body === "object" && body !== null ? Reflect.get(body, key) : undefined;
  return typeof value === "string" && value !== "" ? value : undefined;
}

/** The `errors` member of a validation ProblemDetails: field name to messages. Anything else is dropped. */
function fieldErrorsOf(body: unknown): Record<string, string[]> {
  const errors = typeof body === "object" && body !== null ? Reflect.get(body, "errors") : null;
  if (typeof errors !== "object" || errors === null) return {};
  const fieldErrors: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(errors)) {
    if (Array.isArray(messages)) {
      fieldErrors[field] = messages.filter((m) => typeof m === "string");
    }
  }
  return fieldErrors;
}

/** Maps a non-2xx response (ProblemDetails, or anything at all) to the error a component sees. */
export async function toApiError(response: Response): Promise<ApiError> {
  const body: unknown = await response.json().catch(() => null);
  const kind = kindByStatus.get(response.status) ?? "unavailable";
  const fallback =
    kind === "unavailable"
      ? `API answered ${response.status}`
      : `The API refused the request (${response.status}).`;
  return {
    kind,
    message: text(body, "detail") ?? text(body, "title") ?? fallback,
    fieldErrors: fieldErrorsOf(body),
  };
}

export function unreachable(baseUrl: string, error: unknown): ApiError {
  const reason = error instanceof Error ? error.message : String(error);
  return {
    kind: "unavailable",
    message: `API unreachable at ${baseUrl}: ${reason}`,
    fieldErrors: {},
  };
}

/** The browser's call to the BFF itself failed (offline, a dropped connection): the command may or may not have run. */
export const noAnswer: ApiError = {
  kind: "unavailable",
  message: "No answer from the server, so this may or may not have been saved. Try again.",
  fieldErrors: {},
};
