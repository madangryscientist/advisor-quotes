import * as Sentry from "@sentry/node";
import { expect, it } from "vitest";
import { sentryConfigOf, sentryOptions, startSentry } from "./sentry";

it("never initialises the SDK without a DSN", () => {
  expect(sentryConfigOf({})).toBeNull();
  expect(sentryConfigOf({ SENTRY_DSN: "  " })).toBeNull();
  startSentry({});
  expect(Sentry.getClient()).toBeUndefined();
});

/** The real SDK with the real options and a transport that keeps the envelopes: what would have left the machine. */
it("sends an error with the release and environment, and nothing from the request", async () => {
  const sent: unknown[] = [];
  const config = sentryConfigOf({
    SENTRY_DSN: "https://key@o1.ingest.us.sentry.io/2",
    SENTRY_RELEASE: "abc1234",
    SENTRY_ENVIRONMENT: "demo",
  });
  expect(config).not.toBeNull();
  Sentry.initWithoutDefaultIntegrations({
    ...sentryOptions(config!),
    transport: () => ({
      send: async (envelope) => {
        sent.push(envelope);
        return {};
      },
      flush: async () => true,
    }),
  });

  Sentry.captureEvent({
    message: "boom",
    request: {
      url: "http://quotes.hack-cove.com/quotes?token=s3cret",
      cookies: { session: "s3cret" },
      headers: { authorization: "Bearer s3cret", cookie: "session=s3cret" },
      data: { clientName: "Jane Doe" },
      query_string: "token=s3cret",
    },
    user: { id: "ADV-001" },
  });
  await Sentry.flush(2000);

  const wire = JSON.stringify(sent);
  expect(wire).toContain("boom");
  expect(wire).toContain("abc1234");
  expect(wire).toContain("demo");
  expect(wire).not.toMatch(/s3cret|Jane Doe|authorization|cookie|ADV-001|token=/i);
});
