// @vitest-environment jsdom
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { routeTree } from "../routeTree.gen";
import type { QuoteStatus } from "../server/quotes-api";

type Row = {
  adviserId: string;
  status: QuoteStatus;
  externalReference: string;
  clientName: string;
};
const rows: Row[] = [
  {
    adviserId: "ADV-001",
    status: "Draft",
    externalReference: "Q-100",
    clientName: "Thandi Mokoena",
  },
  {
    adviserId: "ADV-001",
    status: "Accepted",
    externalReference: "Q-101",
    clientName: "Pieter Botha",
  },
  {
    adviserId: "ADV-002",
    status: "Accepted",
    externalReference: "Q-200",
    clientName: "Naledi Dlamini",
  },
];

// The server functions are the seam: this fake answers the way the BFF would, per adviser cookie and status.
type ListArgs = {
  data: { status: QuoteStatus | undefined };
};
const fake = vi.hoisted(() => ({
  adviserId: "ADV-001",
  listQuotes: vi.fn<(args: ListArgs) => Promise<unknown>>(),
}));

vi.mock("../server/fns", () => ({
  getTheme: () => Promise.resolve("light"),
  getDemoLinks: () =>
    Promise.resolve({
      videoUrl: null,
      tracesUrl: "https://traces.example/login?t=x",
      resetsNightly: false,
    }),
  setTheme: () => Promise.resolve(),
  getAdviserId: () => Promise.resolve(fake.adviserId),
  setAdviserId: ({ data }: { data: string }) => {
    fake.adviserId = data;
    return Promise.resolve();
  },
  listAdvisers: () =>
    Promise.resolve({
      ok: true,
      value: [
        { adviserId: "ADV-001", name: "Ayanda Nkosi" },
        { adviserId: "ADV-002", name: "Sipho Khumalo" },
      ],
    }),
  listQuotes: (args: ListArgs) => fake.listQuotes(args),
}));

beforeEach(() => {
  vi.stubGlobal("scrollTo", vi.fn()); // jsdom does not implement it and the router calls it on navigation
  fake.adviserId = "ADV-001";
  fake.listQuotes.mockReset();
  fake.listQuotes.mockImplementation((args: ListArgs) =>
    Promise.resolve({
      ok: true,
      value: rows
        .filter((r) => r.adviserId === fake.adviserId)
        .filter((r) => args.data.status === undefined || r.status === args.data.status)
        .map((r) => ({
          quoteId: r.externalReference,
          ...r,
          productCode: "TERM-LIFE",
          coverAmount: 1_000_000,
          termYears: 20,
          premium: 450,
          currency: "ZAR",
          updatedAt: "2026-10-21T10:00:00Z",
        })),
    }),
  );
});

afterEach(cleanup);

/** The real route tree on a memory history; rendered into `document` because the root route owns `<html>`. */
async function renderAt(url: string) {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [url] }),
  });
  render(<RouterProvider router={router} />, { container: document });
  await screen.findByRole("table");
  return router;
}

test("lists the current adviser's quotes, and only theirs", async () => {
  await renderAt("/quotes");
  expect(screen.getByText("Q-100")).toBeTruthy();
  expect(screen.getByText("Q-101")).toBeTruthy();
  expect(screen.queryByText("Q-200")).toBeNull();
  expect(fake.listQuotes).toHaveBeenCalledWith({ data: { status: undefined } });
});

test("the root path lands on the list", async () => {
  await renderAt("/");
  expect(screen.getByText("Q-100")).toBeTruthy();
});

test("changing the status filter changes the request and the URL", async () => {
  const router = await renderAt("/quotes");
  await userEvent.click(screen.getByRole("button", { name: "Accepted" }));

  await waitFor(() => expect(screen.queryByText("Q-100")).toBeNull());
  expect(screen.getByText("Q-101")).toBeTruthy();
  expect(fake.listQuotes).toHaveBeenLastCalledWith({ data: { status: "Accepted" } });
  expect(router.state.location.search).toEqual({ status: "Accepted" });
});

test("a status in the URL is the initial filter", async () => {
  await renderAt("/quotes?status=Draft");
  expect(fake.listQuotes).toHaveBeenLastCalledWith({ data: { status: "Draft" } });
  expect(screen.queryByText("Q-101")).toBeNull();
});

test("an unknown status in the URL is ignored", async () => {
  await renderAt("/quotes?status=Nonsense");
  expect(fake.listQuotes).toHaveBeenLastCalledWith({ data: { status: undefined } });
});

test("switching adviser reloads the list for the new adviser", async () => {
  await renderAt("/quotes");
  const picker = screen.getByRole("combobox", { name: "Acting as" });
  expect(picker).toHaveProperty("value", "ADV-001");

  await userEvent.selectOptions(picker, "ADV-002");

  await waitFor(() => expect(screen.getByText("Q-200")).toBeTruthy());
  expect(screen.queryByText("Q-100")).toBeNull();
  expect(fake.listQuotes).toHaveBeenCalledTimes(2);
  expect(screen.getByRole("combobox", { name: "Acting as" })).toHaveProperty("value", "ADV-002");
});

test("says that authentication is out of scope", async () => {
  await renderAt("/quotes");
  expect(screen.getByText(/authentication is out of scope/i)).toBeTruthy();
});

test("the header links the trace viewer in a new tab", async () => {
  await renderAt("/quotes");
  const traces = screen.getByRole("link", { name: "Traces ↗" });
  expect(traces.getAttribute("href")).toBe("https://traces.example/login?t=x");
  expect(traces.getAttribute("target")).toBe("_blank");
});
