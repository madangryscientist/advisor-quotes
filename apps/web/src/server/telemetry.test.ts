import { SpanKind, context, propagation, trace } from "@opentelemetry/api";
import { AsyncLocalStorageContextManager } from "@opentelemetry/context-async-hooks";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import {
  InMemorySpanExporter,
  SimpleSpanProcessor,
  TracerProvider,
} from "@opentelemetry/sdk-trace";
import { afterAll, beforeAll, beforeEach, expect, test } from "vitest";
import {
  asServerFnRequest,
  inSpan,
  nameServerFnRequest,
  routeOf,
  scheduleDelay,
  tracedFetch,
} from "./telemetry";

const exporter = new InMemorySpanExporter();

beforeAll(() => {
  context.setGlobalContextManager(new AsyncLocalStorageContextManager().enable());
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  trace.setGlobalTracerProvider(
    new TracerProvider({ spanProcessors: [new SimpleSpanProcessor({ exporter })] }),
  );
});

afterAll(() => {
  trace.disable();
  context.disable();
  propagation.disable();
});

beforeEach(() => exporter.reset());

type Finished = ReturnType<typeof exporter.getFinishedSpans>[number];

/** The one finished span that matches; fails the test when there is none. */
function finished(match: (span: Finished) => boolean): Finished {
  const span = exporter.getFinishedSpans().find(match);
  if (!span) throw new Error("no such span");
  return span;
}

test("an API call is a client span under the current one, and carries its trace to the API", async () => {
  const sent: Headers[] = [];
  const fetch = tracedFetch((_input, init) => {
    sent.push(new Headers(init.headers));
    return Promise.resolve(new Response("{}", { status: 200 }));
  });

  const parentTraceId = await inSpan("fn listQuotes", {}, async (span) => {
    await fetch("http://api.local/v1/queries/list-quotes?status=Draft", {
      method: "GET",
      headers: { "X-Adviser-Id": "ADV-001" },
    });
    return span.spanContext().traceId;
  });

  const client = finished((s) => s.kind === SpanKind.CLIENT);
  const parent = finished((s) => s.name === "fn listQuotes");
  expect(client.name).toBe("GET /v1/queries/list-quotes");
  expect(client.parentSpanContext?.spanId).toBe(parent.spanContext().spanId);
  expect(client.attributes["http.response.status_code"]).toBe(200);
  // traceparent is version-traceid-spanid-flags; the API continues the trace under the client span.
  const [, traceId, spanId] = String(sent[0]?.get("traceparent")).split("-");
  expect(traceId).toBe(parentTraceId);
  expect(spanId).toBe(client.spanContext().spanId);
  expect(sent[0]?.get("X-Adviser-Id")).toBe("ADV-001");
});

test("a span that throws is marked as an error and still ends", async () => {
  await expect(
    inSpan("fn saveQuote", {}, () => Promise.reject(new Error("no answer"))),
  ).rejects.toThrow("no answer");
  const [span] = exporter.getFinishedSpans();
  expect(span?.name).toBe("fn saveQuote");
  expect(span?.status.code).toBe(2);
});

test("quote ids and server-function hashes in a path are folded, so spans group by page", () => {
  expect(routeOf("/quotes/01a1680c-938c-7362-80f0-0bb249a2f810/edit")).toBe("/quotes/{id}/edit");
  expect(routeOf("/_serverFn/b9eb86cc51d25aa1?createServerFn")).toBe("/_serverFn");
  expect(routeOf("/quotes")).toBe("/quotes");
});

test("a browser's server-function request is named after the function; a page render keeps its route", async () => {
  // As the middleware runs: the request span, then naming, then the function's own span.
  await inSpan("POST /_serverFn", {}, () =>
    asServerFnRequest(async () => {
      nameServerFnRequest("POST", "saveQuote");
      await inSpan("fn saveQuote", {}, async () => {});
    }),
  );
  await inSpan("GET /quotes", {}, async () => {
    nameServerFnRequest("GET", "listQuotes");
    await inSpan("fn listQuotes", {}, async () => {});
  });

  const names = exporter.getFinishedSpans().map((s) => s.name);
  expect(names).toContain("POST server fn saveQuote");
  expect(names).toContain("GET /quotes");
  expect(names).not.toContain("GET server fn listQuotes");
});

test("the batch delay is OTEL_BSP_SCHEDULE_DELAY, the API's variable, and 5 s when it is unset or not a number", () => {
  expect(scheduleDelay({ OTEL_BSP_SCHEDULE_DELAY: "500" })).toBe(500);
  expect(scheduleDelay({})).toBe(5000);
  expect(scheduleDelay({ OTEL_BSP_SCHEDULE_DELAY: "soon" })).toBe(5000);
});
