import "@tanstack/react-start/server-only";
import {
  SpanKind,
  SpanStatusCode,
  context,
  createContextKey,
  propagation,
  trace,
} from "@opentelemetry/api";
import type { Span, SpanOptions } from "@opentelemetry/api";
import { AsyncLocalStorageContextManager } from "@opentelemetry/context-async-hooks";
import { W3CTraceContextPropagator } from "@opentelemetry/core";
import { OTLPTraceExporter } from "@opentelemetry/exporter-trace-otlp-grpc";
import { resourceFromAttributes } from "@opentelemetry/resources";
import { BatchSpanProcessor, TracerProvider } from "@opentelemetry/sdk-trace";
import { ATTR_SERVICE_NAME } from "@opentelemetry/semantic-conventions";
import type { Fetch } from "./quotes-api";

/**
 * Tracing for the BFF, the same signal the API sends. A page load or server-function call is a server span, each
 * server function a span under it, and each API call a client span that carries `traceparent`, so the API's spans
 * join the same trace: one trace from the web request down to the SQL.
 *
 * Spans carry routes, function names, ids and status codes, never a client's name or date of birth.
 */

const tracer = trace.getTracer("advisor-quotes-web");

/**
 * Set while the browser is calling a server function. Its URL is a hash, so the request span is renamed after the
 * function once the function middleware knows the name. A server function a loader calls during a page render does
 * not set it, so the page's span keeps its route.
 */
const SERVER_FN_REQUEST = createContextKey("advisor-quotes-web server fn request");

/** Runs `work` marked as a browser's server-function request, whose span is the active one. */
export function asServerFnRequest<T>(work: () => T): T {
  return context.with(context.active().setValue(SERVER_FN_REQUEST, true), work);
}

/**
 * Names the active span after the server function, if this call is a browser's server-function request. Called by
 * the function middleware before it opens the function's own span, so the active span is still the request's.
 */
export function nameServerFnRequest(method: string, functionName: string) {
  if (context.active().getValue(SERVER_FN_REQUEST) !== true) return;
  trace.getActiveSpan()?.updateName(`${method} server fn ${functionName}`);
}

let started = false;

/**
 * Sets the context manager and the W3C propagator, and exports over OTLP/gRPC when `OTEL_EXPORTER_OTLP_ENDPOINT`
 * is set (the API reads the same variable). Without it no provider is registered and every span is a no-op.
 */
export function startTelemetry(env: Record<string, string | undefined> = process.env) {
  if (started) return;
  started = true;
  context.setGlobalContextManager(new AsyncLocalStorageContextManager().enable());
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  if (!env["OTEL_EXPORTER_OTLP_ENDPOINT"]) return;
  trace.setGlobalTracerProvider(
    new TracerProvider({
      resource: resourceFromAttributes({
        [ATTR_SERVICE_NAME]: env["OTEL_WEB_SERVICE_NAME"] ?? "advisor-quotes-web",
      }),
      spanProcessors: [
        new BatchSpanProcessor({
          exporter: new OTLPTraceExporter(),
          scheduledDelayMillis: scheduleDelay(env),
        }),
      ],
    }),
  );
}

/**
 * How long finished spans wait to be sent, in ms: `OTEL_BSP_SCHEDULE_DELAY`, the variable the .NET SDK reads for the
 * API, so one setting serves both. The JS SDK no longer reads it itself. Unset, 5000, the SDK's default.
 */
export function scheduleDelay(env: Record<string, string | undefined>): number {
  const delay = Number(env["OTEL_BSP_SCHEDULE_DELAY"]);
  return Number.isFinite(delay) && delay > 0 ? delay : 5000;
}

/** Runs `work` in an active span, marks a throw as an error and always ends the span. */
export async function inSpan<T>(
  name: string,
  options: SpanOptions,
  work: (span: Span) => Promise<T>,
): Promise<T> {
  return tracer.startActiveSpan(name, options, async (span) => {
    try {
      return await work(span);
    } catch (error) {
      span.setStatus({ code: SpanStatusCode.ERROR });
      throw error;
    } finally {
      span.end();
    }
  });
}

/** `/quotes/<uuid>/edit` as `/quotes/{id}/edit`, and a server function's hashed URL as `/_serverFn`, so span names group. */
export function routeOf(pathname: string): string {
  return pathname
    .replace(/^\/_serverFn\/.*/, "/_serverFn")
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, "{id}");
}

/** Wraps the API client's fetch: a client span per call, and `traceparent` on the request so the API continues it. */
export function tracedFetch(inner: Fetch): Fetch {
  return (input, init) => {
    const url = new URL(input);
    const method = init.method ?? "GET";
    return inSpan(
      `${method} ${url.pathname}`,
      {
        kind: SpanKind.CLIENT,
        attributes: {
          "http.request.method": method,
          "url.path": url.pathname,
          "server.address": url.hostname,
        },
      },
      async (span) => {
        const headers = new Headers(init.headers);
        propagation.inject(context.active(), headers, {
          set: (carrier, key, value) => carrier.set(key, value),
        });
        const response = await inner(input, { ...init, headers });
        span.setAttribute("http.response.status_code", response.status);
        if (response.status >= 500) span.setStatus({ code: SpanStatusCode.ERROR });
        return response;
      },
    );
  };
}
