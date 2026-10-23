import { describe, expect, test } from "vitest";
import { type Fetch, createQuotesApi } from "./quotes-api";

const baseUrl = "http://api.test";

const unreachable: Fetch = async () => {
  throw new TypeError("fetch failed");
};
const erroring: Fetch = async () => new Response("boom", { status: 503 });

describe("health", () => {
  test("API up: returns its status and sends the adviser id", async () => {
    const calls: { url: string; init: RequestInit }[] = [];
    const fetch: Fetch = async (url, init) => {
      calls.push({ url, init });
      return Response.json({ status: "ok" });
    };

    const health = await createQuotesApi({ baseUrl, adviserId: "ADV-007", fetch }).health();

    expect(health).toEqual({ ok: true, status: "ok" });
    expect(calls[0]?.url).toBe("http://api.test/health");
    expect(new Headers(calls[0]?.init.headers).get("X-Adviser-Id")).toBe("ADV-007");
  });

  test("API down: a readable error, not a throw", async () => {
    const health = await createQuotesApi({
      baseUrl,
      adviserId: "ADV-001",
      fetch: unreachable,
    }).health();

    expect(health).toEqual({
      ok: false,
      error: "API unreachable at http://api.test: fetch failed",
    });
  });

  test("API erroring: names the status code", async () => {
    const health = await createQuotesApi({
      baseUrl,
      adviserId: "ADV-001",
      fetch: erroring,
    }).health();

    expect(health).toEqual({ ok: false, error: "API answered 503" });
  });
});

const quoteId = "3f2b9f0e-6c1a-4a53-9d1c-0d6f6a1f6c11";
const newQuote = {
  quoteId: null,
  clientName: "Thandi Nkosi",
  clientDateOfBirth: "1990-04-12",
  clientIsSmoker: false,
  productCode: "TERM_LIFE",
  coverAmount: 500000,
  termYears: 20,
  notes: null,
};

function problem(status: number, body: object) {
  return async () =>
    Response.json(body, { status, headers: { "Content-Type": "application/problem+json" } });
}

function client(fetch: Fetch) {
  return createQuotesApi({ baseUrl, adviserId: "ADV-001", fetch });
}

describe("failures map to { kind, message, fieldErrors }", () => {
  test("400: the field errors, keyed by field", async () => {
    const result = await client(
      problem(400, {
        title: "One or more validation errors occurred.",
        status: 400,
        errors: { clientName: ["Client name is required."], termYears: ["Term is required."] },
      }),
    ).saveQuote({ ...newQuote, clientName: null, termYears: null });

    expect(result).toEqual({
      ok: false,
      error: {
        kind: "validation",
        message: "One or more validation errors occurred.",
        fieldErrors: {
          clientName: ["Client name is required."],
          termYears: ["Term is required."],
        },
      },
    });
  });

  test("404: not found, with the API's detail", async () => {
    const result = await client(problem(404, { status: 404, detail: "No such quote." })).getQuote(
      quoteId,
    );

    expect(result).toEqual({
      ok: false,
      error: { kind: "not-found", message: "No such quote.", fieldErrors: {} },
    });
  });

  test("409: a conflict", async () => {
    const result = await client(
      problem(409, { status: 409, detail: "A quote cannot move from Accepted to Declined." }),
    ).declineQuote({ quoteId, reason: "Changed my mind" });

    expect(result).toEqual({
      ok: false,
      error: {
        kind: "conflict",
        message: "A quote cannot move from Accepted to Declined.",
        fieldErrors: {},
      },
    });
  });

  test("422: the idempotency key was used for a different quote", async () => {
    const result = await client(
      problem(422, { status: 422, detail: "The Idempotency-Key was used for a different quote." }),
    ).saveQuote(newQuote, "key-1");

    expect(result).toEqual({
      ok: false,
      error: {
        kind: "unprocessable",
        message: "The Idempotency-Key was used for a different quote.",
        fieldErrors: {},
      },
    });
  });

  test.each([401, 403])("%i: an adviser error", async (status) => {
    const result = await client(
      problem(status, { status, detail: "Unknown adviser." }),
    ).listQuotes();

    expect(result).toMatchObject({
      ok: false,
      error: { kind: "adviser", message: "Unknown adviser." },
    });
  });

  test("a body that is not ProblemDetails still maps, to a readable message", async () => {
    const result = await client(erroring).acceptQuote({ quoteId, reason: null });

    expect(result).toEqual({
      ok: false,
      error: { kind: "unavailable", message: "API answered 503", fieldErrors: {} },
    });
  });

  test("the API unreachable: an error, not a throw", async () => {
    const result = await client(unreachable).listAdvisers();

    expect(result).toEqual({
      ok: false,
      error: {
        kind: "unavailable",
        message: "API unreachable at http://api.test: fetch failed",
        fieldErrors: {},
      },
    });
  });
});

describe("requests", () => {
  const saved = {
    quoteId,
    externalReference: "Q-1",
    premium: 120,
    currency: "ZAR",
    pricingVersion: "v1",
    status: "Draft",
  };

  function recording(body: unknown = saved) {
    const calls: { url: string; init: RequestInit }[] = [];
    const fetch: Fetch = async (url, init) => {
      calls.push({ url, init });
      return Response.json(body);
    };
    return { calls, fetch };
  }

  test("saveQuote on create sends the Idempotency-Key and returns the body", async () => {
    const { calls, fetch } = recording();

    const result = await client(fetch).saveQuote(newQuote, "key-1");

    expect(result).toEqual({ ok: true, value: saved });
    expect(calls[0]?.url).toBe("http://api.test/v1/commands/save-quote");
    expect(calls[0]?.init.method).toBe("POST");
    expect(new Headers(calls[0]?.init.headers).get("Idempotency-Key")).toBe("key-1");
    expect(new Headers(calls[0]?.init.headers).get("X-Adviser-Id")).toBe("ADV-001");
    const sent = calls[0]?.init.body;
    expect(typeof sent === "string" ? JSON.parse(sent) : null).toEqual(newQuote);
  });

  test("saveQuote on create without a key sends none, for the API to refuse", async () => {
    const { calls, fetch } = recording();

    await client(fetch).saveQuote(newQuote);

    expect(new Headers(calls[0]?.init.headers).has("Idempotency-Key")).toBe(false);
  });

  test("saveQuote on an edit never sends the Idempotency-Key", async () => {
    const { calls, fetch } = recording();

    await client(fetch).saveQuote({ ...newQuote, quoteId }, "key-1");

    expect(new Headers(calls[0]?.init.headers).has("Idempotency-Key")).toBe(false);
  });

  test("the status commands post to their own URL", async () => {
    const { calls, fetch } = recording({ quoteId, status: "SentToClient" });
    const api = client(fetch);

    await api.sendQuoteToClient({ quoteId, reason: null });
    await api.acceptQuote({ quoteId, reason: null });
    await api.declineQuote({ quoteId, reason: "Too dear" });

    expect(calls.map((c) => c.url)).toEqual([
      "http://api.test/v1/commands/send-quote-to-client",
      "http://api.test/v1/commands/accept-quote",
      "http://api.test/v1/commands/decline-quote",
    ]);
  });

  test("the queries send their parameters in the query string", async () => {
    const { calls, fetch } = recording([]);
    const api = client(fetch);

    await api.getQuote(quoteId);
    await api.listQuotes("Draft");
    await api.listQuotes();
    await api.listAdvisers();

    expect(calls.map((c) => c.url)).toEqual([
      `http://api.test/v1/queries/get-quote?QuoteId=${quoteId}`,
      "http://api.test/v1/queries/list-quotes?Status=Draft",
      "http://api.test/v1/queries/list-quotes",
      "http://api.test/v1/queries/list-advisers",
    ]);
    expect(calls.every((c) => c.init.method === "GET" && c.init.body === undefined)).toBe(true);
  });
});
