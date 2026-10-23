import { createServerFn } from "@tanstack/react-start";
import { getCookie, setCookie } from "@tanstack/react-start/server";
import { ADVISER_COOKIE, DEFAULT_ADVISER_ID, createQuotesApi, quotesApiUrl } from "./quotes-api";
import { tracedFetch } from "./telemetry";
import type { QuoteStatus, SaveQuoteInput } from "./quotes-api";

/** The adviser the browser is acting as: the cookie, or the default before anyone has picked. */
function currentAdviserId(): string {
  return getCookie(ADVISER_COOKIE) ?? DEFAULT_ADVISER_ID;
}

/** The API client for the current request, acting as the adviser in the cookie. Each call continues the trace. */
function quotesApi() {
  return createQuotesApi({
    baseUrl: quotesApiUrl(),
    adviserId: currentAdviserId(),
    fetch: tracedFetch(fetch),
  });
}

export const getAdviserId = createServerFn().handler(() => currentAdviserId());

/** Not authentication: the cookie is only which adviser the demo is acting as. */
export const setAdviserId = createServerFn({ method: "POST" })
  .validator((adviserId: string) => adviserId)
  .handler(({ data }) => {
    setCookie(ADVISER_COOKIE, data, { path: "/", sameSite: "lax" });
  });

export const listAdvisers = createServerFn().handler(() => quotesApi().listAdvisers());

/** The current adviser's quotes; there is no adviser argument, the cookie is the filter. */
export const listQuotes = createServerFn()
  .validator((input: { status: QuoteStatus | undefined }) => input)
  .handler(({ data }) => quotesApi().listQuotes(data.status));

export const getQuote = createServerFn()
  .validator((quoteId: string) => quoteId)
  .handler(({ data }) => quotesApi().getQuote(data));

/** A create carries its `idempotencyKey`; an edit (the quote has a `quoteId`) does not. */
export const saveQuote = createServerFn({ method: "POST" })
  .validator((input: { quote: SaveQuoteInput; idempotencyKey: string | undefined }) => input)
  .handler(({ data }) => quotesApi().saveQuote(data.quote, data.idempotencyKey));

type StatusChange = { quoteId: string; reason: string | null };
const statusChange = (input: StatusChange) => input;

export const sendQuoteToClient = createServerFn({ method: "POST" })
  .validator(statusChange)
  .handler(({ data }) => quotesApi().sendQuoteToClient(data));

export const acceptQuote = createServerFn({ method: "POST" })
  .validator(statusChange)
  .handler(({ data }) => quotesApi().acceptQuote(data));

export const declineQuote = createServerFn({ method: "POST" })
  .validator(statusChange)
  .handler(({ data }) => quotesApi().declineQuote(data));

/**
 * Links for the About page, set only on the hosted demo. Unset locally, the page shows the repo links alone.
 * `DEMO_TRACES_URL` may carry the trace viewer's login token: it is public on purpose, and the traces hold demo data only.
 */
export const getDemoLinks = createServerFn().handler(() => ({
  videoUrl: process.env["DEMO_VIDEO_URL"] ?? null,
  tracesUrl: process.env["DEMO_TRACES_URL"] ?? null,
  resetsNightly: process.env["DEMO_RESETS_NIGHTLY"] === "true",
}));

export type Theme = "light" | "dark";
const THEME_COOKIE = "theme";

/** Light (Thrifty Paper) unless the browser has picked dark; a cookie, so the server renders the right one. */
export const getTheme = createServerFn().handler((): Theme =>
  getCookie(THEME_COOKIE) === "dark" ? "dark" : "light",
);

export const setTheme = createServerFn({ method: "POST" })
  .validator((theme: Theme) => theme)
  .handler(({ data }) => {
    setCookie(THEME_COOKIE, data, { path: "/", sameSite: "lax", maxAge: 60 * 60 * 24 * 365 });
  });
