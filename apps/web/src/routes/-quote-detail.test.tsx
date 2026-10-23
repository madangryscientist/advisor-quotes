// @vitest-environment jsdom
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { routeTree } from "../routeTree.gen";
import type { ApiResult } from "../server/api-error";
import type { QuoteDetails } from "../server/quotes-api";

type ChangeArgs = { data: { quoteId: string; reason: string | null } };
const fake = vi.hoisted(() => ({
  getQuote: vi.fn<(args: { data: string }) => Promise<ApiResult<QuoteDetails>>>(),
  sendQuoteToClient: vi.fn<(args: ChangeArgs) => Promise<ApiResult<unknown>>>(),
  acceptQuote: vi.fn<(args: ChangeArgs) => Promise<ApiResult<unknown>>>(),
  declineQuote: vi.fn<(args: ChangeArgs) => Promise<ApiResult<unknown>>>(),
}));

// The server functions are the seam: no BFF, no API, no network.
vi.mock("../server/fns", () => ({
  getTheme: () => Promise.resolve("light"),
  getDemoLinks: () =>
    Promise.resolve({
      videoUrl: null,
      tracesUrl: "https://traces.example/login?t=x",
      resetsNightly: false,
    }),
  setTheme: () => Promise.resolve(),
  getAdviserId: () => Promise.resolve("ADV-001"),
  setAdviserId: () => Promise.resolve(),
  listAdvisers: () =>
    Promise.resolve({ ok: true, value: [{ adviserId: "ADV-001", name: "Ayanda" }] }),
  listQuotes: () => Promise.resolve({ ok: true, value: [] }),
  getQuote: (args: { data: string }) => fake.getQuote(args),
  sendQuoteToClient: (args: ChangeArgs) => fake.sendQuoteToClient(args),
  acceptQuote: (args: ChangeArgs) => fake.acceptQuote(args),
  declineQuote: (args: ChangeArgs) => fake.declineQuote(args),
}));

const QUOTE_ID = "6b1f0c5e-1111-4222-8333-444455556666";
const stored = (status: string, history: QuoteDetails["history"] = []): QuoteDetails => ({
  quoteId: QUOTE_ID,
  externalReference: "Q-100",
  clientName: "Thandi Mokoena",
  productCode: "LIFE",
  coverAmount: 750000,
  termYears: 15,
  premium: 123.45,
  currency: "ZAR",
  status,
  history,
});
const changed = { ok: true, value: { quoteId: QUOTE_ID, status: "SentToClient" } } as const;

beforeEach(() => {
  vi.stubGlobal("scrollTo", vi.fn()); // jsdom does not implement it and the router calls it on navigation
  for (const f of Object.values(fake)) f.mockReset();
  fake.getQuote.mockResolvedValue({ ok: true, value: stored("Draft") });
  fake.sendQuoteToClient.mockResolvedValue(changed);
  fake.acceptQuote.mockResolvedValue(changed);
  fake.declineQuote.mockResolvedValue(changed);
});

afterEach(cleanup);

async function renderDetail(status: string) {
  fake.getQuote.mockResolvedValue({ ok: true, value: stored(status) });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [`/quotes/${QUOTE_ID}`] }),
  });
  render(<RouterProvider router={router} />, { container: document });
  await screen.findByRole("heading", { name: "Q-100" });
}

test("a Draft offers Send to client, Decline and Edit", async () => {
  await renderDetail("Draft");
  expect(screen.getByRole("button", { name: "Send to client" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Decline" })).toBeTruthy();
  expect(screen.getByRole("link", { name: "Edit" })).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Accept" })).toBeNull();
});

test("a quote sent to the client offers Accept and Decline, not Send or Edit", async () => {
  await renderDetail("SentToClient");
  expect(screen.getByRole("button", { name: "Accept" })).toBeTruthy();
  expect(screen.getByRole("button", { name: "Decline" })).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Send to client" })).toBeNull();
  expect(screen.queryByRole("link", { name: "Edit" })).toBeNull();
});

test.each(["Accepted", "Declined"])("%s is final: no actions", async (status) => {
  await renderDetail(status);
  expect(within(screen.getByRole("main")).queryAllByRole("button")).toEqual([]);
  expect(screen.queryByRole("link", { name: "Edit" })).toBeNull();
  expect(screen.queryByLabelText(/reason/i)).toBeNull();
});

test("Send to client posts the quote id and no reason, then reloads the detail", async () => {
  await renderDetail("Draft");
  fake.getQuote.mockResolvedValue({
    ok: true,
    value: stored("SentToClient", [
      {
        from: "Draft",
        to: "SentToClient",
        reason: "Sent after the call",
        changedBy: "ADV-001",
        changedAt: "2026-10-21T10:00:00Z",
      },
    ]),
  });

  await userEvent.click(screen.getByRole("button", { name: "Send to client" }));

  expect(fake.sendQuoteToClient).toHaveBeenCalledWith({
    data: { quoteId: QUOTE_ID, reason: null },
  });
  await screen.findByRole("button", { name: "Accept" });
  expect(fake.getQuote).toHaveBeenCalledTimes(2);
  expect(screen.getByText("Draft → Sent to client")).toBeTruthy();
  expect(screen.getByText("Sent after the call")).toBeTruthy();
});

test("Accept sends the optional reason", async () => {
  await renderDetail("SentToClient");
  await userEvent.type(screen.getByLabelText(/reason/i), " Client signed ");
  await userEvent.click(screen.getByRole("button", { name: "Accept" }));

  expect(fake.acceptQuote).toHaveBeenCalledWith({
    data: { quoteId: QUOTE_ID, reason: "Client signed" },
  });
});

test("Decline needs a reason and sends it", async () => {
  await renderDetail("Draft");
  await userEvent.click(screen.getByRole("button", { name: "Decline" }));
  expect(screen.getByText("A reason is required to decline.")).toBeTruthy();
  expect(fake.declineQuote).not.toHaveBeenCalled();

  await userEvent.type(screen.getByLabelText(/reason/i), "Too expensive");
  await userEvent.click(screen.getByRole("button", { name: "Decline" }));

  await waitFor(() =>
    expect(fake.declineQuote).toHaveBeenCalledWith({
      data: { quoteId: QUOTE_ID, reason: "Too expensive" },
    }),
  );
});

test("a 409 says the quote changed and offers a refresh", async () => {
  await renderDetail("Draft");
  fake.sendQuoteToClient.mockResolvedValue({
    ok: false,
    error: {
      kind: "conflict",
      message: "A quote cannot move from Accepted to SentToClient.",
      fieldErrors: {},
    },
  });

  await userEvent.click(screen.getByRole("button", { name: "Send to client" }));

  expect(await screen.findByText("This quote changed, refresh")).toBeTruthy();
  expect(screen.getByText(/cannot move from Accepted/)).toBeTruthy();
  await userEvent.click(screen.getByRole("button", { name: "Refresh" }));
  await waitFor(() => expect(fake.getQuote).toHaveBeenCalledTimes(2));
});

test("a move that never answers says so and leaves the buttons usable", async () => {
  await renderDetail("Draft");
  fake.sendQuoteToClient.mockRejectedValueOnce(new TypeError("Failed to fetch"));

  await userEvent.click(screen.getByRole("button", { name: "Send to client" }));

  expect(await screen.findByText("Could not change the status")).toBeTruthy();
  expect(screen.getByText(/may or may not have been saved/)).toBeTruthy();
  expect(screen.getByRole("button", { name: "Send to client" }).hasAttribute("disabled")).toBe(
    false,
  );
});
