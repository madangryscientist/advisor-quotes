import { SpanKind } from "@opentelemetry/api";
import { createMiddleware, createStart } from "@tanstack/react-start";
import {
  asServerFnRequest,
  inSpan,
  nameServerFnRequest,
  routeOf,
  startTelemetry,
} from "./server/telemetry";
import { reportError, startSentry } from "./server/sentry";

/** Reports what a request lets out to Sentry (when a DSN is set), then rethrows so the response is unchanged. */
const reportRequestErrors = createMiddleware().server(async ({ request, pathname, next }) => {
  startSentry();
  try {
    // The verification affordance: off unless SENTRY_SMOKE=1, otherwise an ordinary 404.
    if (
      process.env["SENTRY_SMOKE"] === "1" &&
      request.method === "GET" &&
      pathname === "/smoke/sentry"
    ) {
      throw new Error("Sentry smoke test (web)");
    }
    return await next();
  } catch (error) {
    reportError(error);
    throw error;
  }
});

/** A server span per request: a page load (`GET /quotes/{id}`) or a server-function call from the browser. */
const traceRequest = createMiddleware().server(({ request, pathname, handlerType, next }) => {
  startTelemetry();
  return inSpan(
    `${request.method} ${routeOf(pathname)}`,
    {
      kind: SpanKind.SERVER,
      attributes: { "http.request.method": request.method, "url.path": routeOf(pathname) },
    },
    async (span) => {
      const result = await (handlerType === "serverFn" ? asServerFnRequest(next) : next());
      span.setAttribute("http.response.status_code", result.response.status);
      return result;
    },
  );
});

/** A span per server function, also when a loader calls one during the server render. */
const traceServerFn = createMiddleware({ type: "function" }).server(
  ({ method, serverFnMeta, next }) => {
    startTelemetry();
    nameServerFnRequest(method, serverFnMeta.name);
    return inSpan(`fn ${serverFnMeta.name}`, {}, () => next());
  },
);

/** The same for a server function, which is also what a loader calls during the server render. */
const reportFunctionErrors = createMiddleware({ type: "function" }).server(async ({ next }) => {
  startSentry();
  try {
    return await next();
  } catch (error) {
    reportError(error);
    throw error;
  }
});

export const startInstance = createStart(() => ({
  requestMiddleware: [reportRequestErrors, traceRequest],
  functionMiddleware: [reportFunctionErrors, traceServerFn],
}));
