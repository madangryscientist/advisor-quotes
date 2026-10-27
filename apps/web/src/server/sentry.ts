import "@tanstack/react-start/server-only";
import * as Sentry from "@sentry/node";
import type { ErrorEvent, NodeOptions } from "@sentry/node";

/**
 * Error reporting for the BFF: server-side errors only, and only when `SENTRY_DSN` is set. No tracing, profiling,
 * replay, logs or breadcrumbs, and no OpenTelemetry setup, so the OTLP tracing in `telemetry.ts` is left alone.
 */

type SentryEnv = Record<string, string | undefined>;

/** The reporting config, or null when no DSN is set: the one gate for the whole feature. */
export function sentryConfigOf(env: SentryEnv) {
  const dsn = env["SENTRY_DSN"]?.trim();
  if (!dsn) return null;
  return {
    dsn,
    release: env["SENTRY_RELEASE"]?.trim() || "unknown",
    environment: env["SENTRY_ENVIRONMENT"]?.trim() || "dev",
  };
}

/** Nothing about a request leaves with an error: no cookies, headers, body, query string or user. */
export function scrubEvent(event: ErrorEvent): ErrorEvent {
  if (event.request) {
    delete event.request.cookies;
    delete event.request.headers;
    delete event.request.data;
    delete event.request.query_string;
    if (event.request.url) [event.request.url = ""] = event.request.url.split(/[?#]/);
  }
  delete event.user;
  delete event.breadcrumbs;
  return event;
}

/** The SDK's options: errors only, no OpenTelemetry setup, and `scrubEvent` on every event. */
export function sentryOptions(config: NonNullable<ReturnType<typeof sentryConfigOf>>): NodeOptions {
  return {
    ...config,
    // Uncaught exceptions and rejections, plus the usual error enrichment. Nothing that reads requests or makes spans.
    integrations: [
      Sentry.eventFiltersIntegration(),
      Sentry.functionToStringIntegration(),
      Sentry.linkedErrorsIntegration(),
      Sentry.dedupeIntegration(),
      Sentry.onUncaughtExceptionIntegration(),
      Sentry.onUnhandledRejectionIntegration(),
    ],
    enableOpenTelemetrySetup: false,
    maxBreadcrumbs: 0,
    beforeSend: scrubEvent,
  };
}

let started = false;

/** Starts Sentry once, if `SENTRY_DSN` is set. Without it the SDK is never initialised and nothing is sent. */
export function startSentry(env: SentryEnv = process.env) {
  if (started) return;
  started = true;
  const config = sentryConfigOf(env);
  if (!config) return;
  Sentry.initWithoutDefaultIntegrations(sentryOptions(config));
}

/** Reports an error a server function or request let out; a no-op without a DSN, and it never throws. */
export function reportError(error: unknown) {
  try {
    // A thrown Response or redirect is control flow, not a bug.
    if (error instanceof Response) return;
    Sentry.captureException(error);
  } catch {
    // A failed report must not fail the request.
  }
}
