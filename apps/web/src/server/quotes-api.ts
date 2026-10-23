import "@tanstack/react-start/server-only";
import { type ApiResult, toApiError, unreachable } from "./api-error";
import type { components, operations } from "./api.gen";

/**
 * The BFF seam: the only module that knows where the .NET API lives. Server functions call the API
 * through here; the browser never does, so the API needs no CORS.
 */

/** The fetch this module calls. Tests pass a fake; the server passes the global. */
export type Fetch = (input: string, init: RequestInit) => Promise<Response>;

export const ADVISER_COOKIE = "adviser-id";
/** Every request acts as this adviser until the header picker sets the cookie. */
export const DEFAULT_ADVISER_ID = "ADV-001";

/** Server env only, never a VITE_ var: the URL is not shipped to the browser. */
export function quotesApiUrl(): string {
  return process.env["QUOTES_API_URL"] ?? "http://localhost:5180";
}

/** The API version every call targets; the API serves nothing outside it. */
const API_VERSION_PREFIX = "/v1";

type Schemas = components["schemas"];
/** The 200 body of an operation, straight from the generated types. */
type Ok<Operation extends keyof operations> =
  operations[Operation]["responses"][200]["content"]["application/json"];
type Body<Operation extends keyof operations> = NonNullable<
  operations[Operation]["requestBody"]
>["content"]["application/json"];

export type SaveQuoteInput = Body<"saveQuote">;
export type QuoteStatus = Schemas["QuoteStatus"];
export type QuoteDetails = Schemas["QuoteDetails"];

export type ApiHealth = { ok: true; status: string } | { ok: false; error: string };

export function createQuotesApi(options: { baseUrl: string; adviserId: string; fetch: Fetch }) {
  const get = (path: string) =>
    options.fetch(new URL(path, options.baseUrl).toString(), {
      headers: { "X-Adviser-Id": options.adviserId, Accept: "application/json" },
    });

  /** Calls one endpoint and maps every failure, including a dead network, to an `ApiError`. */
  async function call<T>(
    path: string,
    init: {
      method: "GET" | "POST";
      query?: Record<string, string | undefined>;
      body?: unknown;
      headers?: Record<string, string>;
    },
  ): Promise<ApiResult<T>> {
    const url = new URL(path, options.baseUrl);
    for (const [name, value] of Object.entries(init.query ?? {})) {
      if (value !== undefined) url.searchParams.set(name, value);
    }
    try {
      const response = await options.fetch(url.toString(), {
        method: init.method,
        headers: {
          "X-Adviser-Id": options.adviserId,
          Accept: "application/json",
          ...(init.body === undefined ? {} : { "Content-Type": "application/json" }),
          ...init.headers,
        },
        ...(init.body === undefined ? {} : { body: JSON.stringify(init.body) }),
      });
      // The API's own document generated these types, so the body is trusted rather than re-validated here.
      return response.ok
        ? { ok: true, value: await response.json() }
        : { ok: false, error: await toApiError(response) };
    } catch (error) {
      return { ok: false, error: unreachable(options.baseUrl, error) };
    }
  }

  const command =
    <Operation extends keyof operations>(name: string) =>
    (body: Body<Operation>) =>
      call<Ok<Operation>>(`${API_VERSION_PREFIX}/commands/${name}`, { method: "POST", body });

  return {
    async health(): Promise<ApiHealth> {
      try {
        const response = await get("/health");
        if (!response.ok) return { ok: false, error: `API answered ${response.status}` };
        const body: unknown = await response.json();
        const status =
          typeof body === "object" && body !== null ? Reflect.get(body, "status") : null;
        return { ok: true, status: typeof status === "string" ? status : "unknown" };
      } catch (error) {
        const reason = error instanceof Error ? error.message : String(error);
        return { ok: false, error: `API unreachable at ${options.baseUrl}: ${reason}` };
      }
    },

    /**
     * Creates a quote (no `quoteId`) or edits a draft. A create needs the `Idempotency-Key` header, so
     * the caller makes one key per create attempt and passes the same one on a retry; without it the
     * API answers 400. An edit never sends it.
     */
    saveQuote: (input: SaveQuoteInput, idempotencyKey?: string) =>
      call<Ok<"saveQuote">>(`${API_VERSION_PREFIX}/commands/save-quote`, {
        method: "POST",
        body: input,
        headers:
          input.quoteId || idempotencyKey === undefined
            ? {}
            : { "Idempotency-Key": idempotencyKey },
      }),
    sendQuoteToClient: command<"sendQuoteToClient">("send-quote-to-client"),
    acceptQuote: command<"acceptQuote">("accept-quote"),
    declineQuote: command<"declineQuote">("decline-quote"),

    getQuote: (quoteId: string) =>
      call<Ok<"getQuote">>(`${API_VERSION_PREFIX}/queries/get-quote`, {
        method: "GET",
        query: { QuoteId: quoteId },
      }),
    listQuotes: (status?: QuoteStatus) =>
      call<Ok<"listQuotes">>(`${API_VERSION_PREFIX}/queries/list-quotes`, {
        method: "GET",
        query: { Status: status ?? undefined },
      }),
    listAdvisers: () =>
      call<Ok<"listAdvisers">>(`${API_VERSION_PREFIX}/queries/list-advisers`, { method: "GET" }),
  };
}
