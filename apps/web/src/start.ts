import { SpanKind } from "@opentelemetry/api";
import { createMiddleware, createStart } from "@tanstack/react-start";
import {
  asServerFnRequest,
  inSpan,
  nameServerFnRequest,
  routeOf,
  startTelemetry,
} from "./server/telemetry";

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

export const startInstance = createStart(() => ({
  requestMiddleware: [traceRequest],
  functionMiddleware: [traceServerFn],
}));
