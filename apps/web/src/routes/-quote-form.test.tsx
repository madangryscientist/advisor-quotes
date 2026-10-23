// @vitest-environment jsdom
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { routeTree } from "../routeTree.gen";
import type { ApiResult } from "../server/api-error";
import type { QuoteDetails } from "../server/quotes-api";

type SaveArgs = {
  data: {
    quote: Record<string, unknown>;
    idempotencyKey: string | undefined;
  };
};
const fake = vi.hoisted(() => ({
  saveQuote: vi.fn<(args: SaveArgs) => Promise<ApiResult<unknown>>>(),
  getQuote: vi.fn<(args: { data: string }) => Promise<ApiResult<QuoteDetails>>>(),
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
  saveQuote: (args: SaveArgs) => fake.saveQuote(args),
  getQuote: (args: { data: string }) => fake.getQuote(args),
}));

const QUOTE_ID = "6b1f0c5e-1111-4222-8333-444455556666";
const saved = { quoteId: QUOTE_ID, premium: 123.45, currency: "ZAR", status: "Draft" };
const stored = (status: string): QuoteDetails => ({
  quoteId: QUOTE_ID,
  externalReference: "Q-100",
  clientName: "Thandi Mokoena",
  clientDateOfBirth: "1990-05-17",
  clientIsSmoker: true,
  productCode: "DISABILITY",
  coverAmount: 750000,
  termYears: 15,
  premium: 123.45,
  currency: "ZAR",
  notes: "Called back",
  status,
});

beforeEach(() => {
  vi.stubGlobal("scrollTo", vi.fn()); // jsdom does not implement it and the router calls it on navigation
  fake.saveQuote.mockReset();
  fake.getQuote.mockReset();
  fake.getQuote.mockResolvedValue({ ok: true, value: stored("Draft") });
});

afterEach(cleanup);

async function renderAt(url: string, ready: string) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [url] }),
  });
  render(<RouterProvider router={router} />, { container: document });
  await screen.findByRole("heading", { name: ready });
  return router;
}

async function fillNewQuote() {
  await userEvent.type(screen.getByLabelText("Client name"), "Thandi Mokoena");
  // The date is typed as DDMMYYYY into the segments; each one moves on to the next when it is full.
  const dateOfBirth = screen.getByRole("group", { name: "Date of birth" });
  await userEvent.click(within(dateOfBirth).getByRole("spinbutton", { name: /day/ }));
  await userEvent.keyboard("17051990");
  await userEvent.click(screen.getByLabelText("The client smokes"));
  await userEvent.selectOptions(screen.getByLabelText("Product"), "DISABILITY");
  await userEvent.type(screen.getByLabelText("Cover amount (ZAR)"), "750000");
  await userEvent.type(screen.getByLabelText("Term (years)"), "15");
  await userEvent.type(screen.getByLabelText("Notes"), "Called back");
}

test("a create sends the typed payload with an Idempotency-Key, then shows the premium", async () => {
  fake.saveQuote.mockResolvedValue({ ok: true, value: saved });
  const router = await renderAt("/quotes/new", "New quote");
  await fillNewQuote();
  await userEvent.click(screen.getByRole("button", { name: "Create quote" }));

  await waitFor(() => expect(router.state.location.pathname).toBe(`/quotes/${QUOTE_ID}`));
  expect(fake.saveQuote).toHaveBeenCalledOnce();
  const { data } = fake.saveQuote.mock.calls[0]![0];
  expect(data.quote).toEqual({
    quoteId: null,
    clientName: "Thandi Mokoena",
    clientDateOfBirth: "1990-05-17",
    clientIsSmoker: true,
    productCode: "DISABILITY",
    coverAmount: 750000,
    termYears: 15,
    notes: "Called back",
  });
  expect(data.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
  expect(await screen.findByText(/R\s?123,45/)).toBeTruthy();
});

test("a double click while the first is in flight sends one request, and a retry reuses its key", async () => {
  // Slow enough that the second click lands while the first call is still in flight.
  fake.saveQuote.mockImplementationOnce(
    () =>
      new Promise((resolve) =>
        setTimeout(
          () =>
            resolve({
              ok: false,
              error: { kind: "unavailable", message: "API unreachable", fieldErrors: {} },
            }),
          100,
        ),
      ),
  );
  await renderAt("/quotes/new", "New quote");
  await fillNewQuote();
  await userEvent.dblClick(screen.getByRole("button", { name: "Create quote" }));
  expect(fake.saveQuote).toHaveBeenCalledOnce();

  await screen.findByText("API unreachable");

  fake.saveQuote.mockResolvedValue({ ok: true, value: saved });
  await userEvent.click(screen.getByRole("button", { name: "Create quote" }));
  await waitFor(() => expect(fake.saveQuote).toHaveBeenCalledTimes(2));
  const [first, second] = fake.saveQuote.mock.calls.map(([args]) => args.data.idempotencyKey);
  expect(second).toBe(first);
});

test("a save that never answers says so, and the retry reuses its key", async () => {
  fake.saveQuote.mockRejectedValueOnce(new TypeError("Failed to fetch"));
  await renderAt("/quotes/new", "New quote");
  await fillNewQuote();
  await userEvent.click(screen.getByRole("button", { name: "Create quote" }));

  expect(await screen.findByText(/may or may not have been saved/)).toBeTruthy();

  fake.saveQuote.mockResolvedValue({ ok: true, value: saved });
  await userEvent.click(screen.getByRole("button", { name: "Create quote" }));
  await waitFor(() => expect(fake.saveQuote).toHaveBeenCalledTimes(2));
  const [first, second] = fake.saveQuote.mock.calls.map(([args]) => args.data.idempotencyKey);
  expect(second).toBe(first);
});

test("field errors render next to their fields", async () => {
  fake.saveQuote.mockResolvedValue({
    ok: false,
    error: {
      kind: "validation",
      message: "The quote is not valid.",
      fieldErrors: {
        clientName: ["Client name is required."],
        coverAmount: ["Cover must be between R10,000 and R5,000,000."],
      },
    },
  });
  await renderAt("/quotes/new", "New quote");
  await userEvent.click(screen.getByRole("button", { name: "Create quote" }));

  for (const [label, message] of [
    ["Client name", "Client name is required."],
    ["Cover amount (ZAR)", "Cover must be between R10,000 and R5,000,000."],
  ] as const) {
    const alert = await screen.findByText(message);
    expect(within(alert.parentElement!).getByLabelText(label)).toBeTruthy();
  }
  expect(fake.saveQuote.mock.calls[0]![0].data.quote).toMatchObject({
    clientName: null,
    coverAmount: null,
  });
});

test("editing a Draft sends its quoteId and no Idempotency-Key", async () => {
  fake.saveQuote.mockResolvedValue({ ok: true, value: saved });
  await renderAt(`/quotes/${QUOTE_ID}/edit`, "Edit quote");
  const cover = screen.getByLabelText("Cover amount (ZAR)");
  expect(cover).toHaveProperty("value", "750000");
  await userEvent.clear(cover);
  await userEvent.type(cover, "900000");
  await userEvent.click(screen.getByRole("button", { name: "Save changes" }));

  await waitFor(() => expect(fake.saveQuote).toHaveBeenCalledOnce());
  const { data } = fake.saveQuote.mock.calls[0]![0];
  expect(data.quote).toMatchObject({ quoteId: QUOTE_ID, coverAmount: 900000 });
  expect(data.idempotencyKey).toBeUndefined();
});

test("only a Draft shows the edit link", async () => {
  await renderAt(`/quotes/${QUOTE_ID}`, "Q-100");
  expect(screen.getByRole("link", { name: "Edit" })).toBeTruthy();
  cleanup();

  fake.getQuote.mockResolvedValue({ ok: true, value: stored("Accepted") });
  await renderAt(`/quotes/${QUOTE_ID}`, "Q-100");
  expect(screen.queryByRole("link", { name: "Edit" })).toBeNull();
});
