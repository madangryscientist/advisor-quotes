import { test as base, expect } from "@playwright/test";
import type { APIRequestContext, APIResponse, Locator, Page } from "@playwright/test";
import postgres from "postgres";
import type { Sql } from "postgres";
import { apiUrl, connectionString, webUrl } from "../stack";

/** The three advisers the migration seeds. */
export type AdviserId = "ADV-001" | "ADV-002" | "ADV-003";

export type QuoteInput = {
  clientName: string;
  clientDateOfBirth: string;
  clientIsSmoker: boolean;
  productCode: "LIFE" | "DISABILITY";
  coverAmount: number;
  termYears: number;
  notes?: string;
};

type Summary = { quoteId: string; clientName: string; status: string; premium: number };

/**
 * A date of birth that makes the client 45 today, give or take a month: the 40–49 band (R1.90 per R1,000)
 * whatever day the suite runs, so a premium is a fixed number. The month keeps clear of a birthday and of
 * the API's UTC date.
 */
export function aged45(): string {
  const date = new Date();
  date.setFullYear(date.getFullYear() - 45, date.getMonth() - 1);
  return date.toISOString().slice(0, 10);
}

/** A rand amount as the UI shows it in en-ZA (`R 1 045,00`), lenient about which space the ICU puts in. */
export function rand(amount: number): RegExp {
  const [whole = "", cents = ""] = amount.toFixed(2).split(".");
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, "\\s?");
  return new RegExp(`^R\\s?${grouped},${cents}$`);
}

/**
 * The API itself, bypassing the browser: tests use it to arrange state fast (a quote already sent, say) and to
 * check what the UI stored. What a test is about always goes through the UI.
 */
export class QuotesApi {
  constructor(private readonly http: APIRequestContext) {}

  private static async json<T>(response: APIResponse): Promise<T> {
    expect(response.ok(), `${response.url()} answered ${response.status()}`).toBe(true);
    return (await response.json()) as T;
  }

  async createQuote(adviser: AdviserId, input: QuoteInput): Promise<string> {
    const response = await this.http.post("/v1/commands/save-quote", {
      headers: { "X-Adviser-Id": adviser, "Idempotency-Key": crypto.randomUUID() },
      data: input,
    });
    return (await QuotesApi.json<{ quoteId: string }>(response)).quoteId;
  }

  async changeStatus(
    adviser: AdviserId,
    command: "send-quote-to-client" | "accept-quote" | "decline-quote",
    quoteId: string,
    reason?: string,
  ): Promise<void> {
    const response = await this.http.post(`/v1/commands/${command}`, {
      headers: { "X-Adviser-Id": adviser },
      data: { quoteId, reason: reason ?? null },
    });
    await QuotesApi.json(response);
  }

  /** The adviser's quotes for one client name: tests own their client names, so this is "my quotes". */
  async quotesFor(adviser: AdviserId, clientName: string): Promise<Summary[]> {
    const response = await this.http.get("/v1/queries/list-quotes", {
      headers: { "X-Adviser-Id": adviser },
    });
    return (await QuotesApi.json<Summary[]>(response)).filter((q) => q.clientName === clientName);
  }
}

/** A server-rendered page ignores input until React hydrates it; the root route marks <html> when it has. */
export async function open(page: Page, url: string): Promise<void> {
  await page.goto(url);
  await expect(page.locator("html[data-hydrated]")).toBeAttached();
}

/** Types an ISO date (yyyy-mm-dd) into the date picker's day, month and year segments, which take DDMMYYYY. */
export async function fillDate(page: Page, label: string, iso: string): Promise<void> {
  const [year, month, day] = iso.split("-");
  await page.getByRole("group", { name: label }).getByRole("spinbutton", { name: /day/ }).click();
  await page.keyboard.type(`${day}${month}${year}`);
}

/** The ISO value the date picker will submit, from its hidden input. */
export function dateValue(page: Page, name: string): Locator {
  return page.locator(`input[name="${name}"]`);
}

/** The detail page, by what a user reads on it. */
export function quotePage(page: Page) {
  const main = page.getByRole("main");
  const value = (label: string) =>
    main
      .locator("dl > div")
      .filter({ has: page.getByText(label, { exact: true }) })
      .getByRole("definition");
  return {
    reference: main.getByRole("heading", { level: 1 }),
    clientName: value("Name"),
    premium: value("Premium"),
    cover: value("Cover"),
    productAndTerm: value("Product and term"),
    status: value("Status"),
    history: main.getByRole("list", { name: "Status history" }).getByRole("listitem"),
    reason: page.getByLabel("Reason (required to decline)"),
    button: (name: string) => main.getByRole("button", { name, exact: true }),
    statusButtons: main.getByRole("button", { name: /^(Send to client|Accept|Decline)$/ }),
    edit: main.getByRole("link", { name: "Edit" }),
  };
}

/** A valid create for `clientName`, priced at R1,045.00: 500 × R1.90 (age 45) × 1.10 (term 20). */
export function draft(clientName: string): QuoteInput {
  return {
    clientName,
    clientDateOfBirth: aged45(),
    clientIsSmoker: false,
    productCode: "LIFE",
    coverAmount: 500_000,
    termYears: 20,
  };
}

/** `Host=…;Port=…;Database=…;Username=…;Password=…`, the API's own format, as a URL for postgres.js. */
function connectionUrl(adoNet: string): string {
  const parts = new Map(
    adoNet.split(";").map((pair) => {
      const at = pair.indexOf("=");
      return [
        pair.slice(0, at).trim().toLowerCase(),
        encodeURIComponent(pair.slice(at + 1).trim()),
      ];
    }),
  );
  const get = (key: string) => parts.get(key) ?? "";
  return `postgres://${get("username")}:${get("password")}@${get("host")}:${parts.get("port") ?? 5432}/${get("database")}`;
}

type Fixtures = {
  api: QuotesApi;
  /**
   * Gives the test `name` to use as its client, with any quotes an earlier run stored under it deleted first.
   * Names are fixed, so pages look the same on every run; each test uses names no other test does.
   */
  client: (name: string) => Promise<string>;
  /** Acts as `adviser` from the next page load, as the "Acting as" picker would. */
  actAs: (adviser: AdviserId) => Promise<void>;
};

export const test = base.extend<Fixtures, { db: Sql }>({
  db: [
    async ({}, use) => {
      const sql = postgres(connectionUrl(connectionString), { max: 1 });
      await use(sql);
      await sql.end();
    },
    { scope: "worker" },
  ],
  api: async ({ playwright }, use) => {
    const http = await playwright.request.newContext({ baseURL: apiUrl });
    await use(new QuotesApi(http));
    await http.dispose();
  },
  client: async ({ db }, use) => {
    await use(async (name) => {
      // The history goes with its quote (ON DELETE CASCADE).
      await db`DELETE FROM quotes WHERE client_name = ${name}`;
      return name;
    });
  },
  actAs: async ({ context }, use) => {
    await use((adviser) =>
      context.addCookies([{ name: "adviser-id", value: adviser, url: webUrl }]),
    );
  },
});

export { expect };
