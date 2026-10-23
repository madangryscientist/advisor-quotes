import {
  aged45,
  dateValue,
  draft,
  expect,
  fillDate,
  open,
  quotePage,
  rand,
  test,
} from "./fixtures";

test("a new quote is priced on the server, opens as a Draft and is in the adviser's list", async ({
  page,
  api,
  client,
}) => {
  const clientName = await client("Thandi Nkosi");
  await open(page, "/quotes/new");
  await page.getByLabel("Client name").fill(clientName);
  await fillDate(page, "Date of birth", aged45());
  await page.getByLabel("Cover amount (ZAR)").fill("500000");
  await page.getByLabel("Term (years)").fill("20");
  await page.getByRole("button", { name: "Create quote" }).click();

  const quote = quotePage(page);
  await expect(page).toHaveURL(/\/quotes\/[0-9a-f-]{36}$/);
  await expect(quote.reference).toHaveText(/^Q-\d{8}-[0-9A-F]{4}$/);
  await expect(quote.clientName).toHaveText(clientName);
  // 500 × R1.90 (age 45) × 1.10 (term 20).
  await expect(quote.premium).toHaveText(rand(1045));
  await expect(quote.cover).toHaveText(rand(500_000));
  await expect(quote.productAndTerm).toHaveText("Life, 20 years");
  await expect(quote.status).toHaveText("Draft");
  await expect(quote.history).toHaveCount(1);
  await expect(quote.history.first()).toContainText("New → Draft");
  await expect(quote.history.first()).toContainText("ADV-001");

  await open(page, "/quotes");
  const row = page.getByRole("row", { name: clientName });
  await expect(row).toBeVisible();
  await expect(row).toContainText("Draft");
  await expect(row.getByRole("cell").nth(4)).toHaveText(rand(1045));

  // Exactly one quote was stored, as the UI showed it.
  const stored = await api.quotesFor("ADV-001", clientName);
  expect(stored).toHaveLength(1);
  expect(stored[0]?.premium).toBe(1045);
});

test("editing a Draft reprices it and adds no history", async ({ page, api, client }) => {
  const input = draft(await client("Pieter van Wyk"));
  const quoteId = await api.createQuote("ADV-001", input);

  await open(page, `/quotes/${quoteId}`);
  await quotePage(page).edit.click();
  // The form opens on what was stored.
  await expect(page.getByLabel("Client name")).toHaveValue(input.clientName);
  await expect(dateValue(page, "clientDateOfBirth")).toHaveValue(input.clientDateOfBirth);
  await expect(page.getByLabel("Term (years)")).toHaveValue("20");

  await page.getByLabel("Product").selectOption("DISABILITY");
  await page.getByLabel("The client smokes").check();
  await page.getByLabel("Term (years)").fill("25");
  await page.getByRole("button", { name: "Save changes" }).click();

  const quote = quotePage(page);
  await expect(page).toHaveURL(new RegExp(`/quotes/${quoteId}$`));
  // 500 × R1.90 × 1.50 (smoker) × 1.25 (term 25) × 1.40 (disability).
  await expect(quote.premium).toHaveText(rand(2493.75));
  await expect(quote.productAndTerm).toHaveText("Disability, 25 years");
  // An edit is not a status change: the history still has only the creation.
  await expect(quote.history).toHaveCount(1);
});

test("the server's validation shows beside each field and nothing is saved", async ({
  page,
  api,
  client,
}) => {
  const clientName = await client("Lindiwe Dube");
  await open(page, "/quotes/new");
  await page.getByRole("button", { name: "Create quote" }).click();

  const form = page.locator("form");
  await expect(page.getByText("Could not save the quote")).toBeVisible();
  await expect(form).toContainText("Client name is required.");
  await expect(form).toContainText("Client date of birth is required.");
  await expect(form).toContainText("Cover amount is required.");
  await expect(form).toContainText("Term is required.");

  // Every field is there now, but Pricing rejects the cover.
  await page.getByLabel("Client name").fill(clientName);
  await fillDate(page, "Date of birth", aged45());
  await page.getByLabel("Cover amount (ZAR)").fill("5000");
  await page.getByLabel("Term (years)").fill("20");
  await page.getByRole("button", { name: "Create quote" }).click();

  await expect(form).toContainText("Cover must be between R10,000 and R5,000,000.");
  await expect(form).not.toContainText("Client name is required.");
  await expect(page).toHaveURL(/\/quotes\/new$/);
  expect(await api.quotesFor("ADV-001", clientName)).toHaveLength(0);
});

test("a create whose response is lost is retried with the same key and stores one quote", async ({
  page,
  api,
  client,
}) => {
  const input = draft(await client("Johan Botha"));
  // The first save reaches the server and is stored, then the connection drops before the browser hears back.
  let dropped = false;
  await page.route("**/_serverFn/**", async (route) => {
    const request = route.request();
    if (dropped || request.method() !== "POST" || !request.postData()?.includes(input.clientName)) {
      await route.fallback();
      return;
    }
    dropped = true;
    await route.fetch();
    await route.abort("connectionreset");
  });

  await open(page, "/quotes/new");
  await page.getByLabel("Client name").fill(input.clientName);
  await fillDate(page, "Date of birth", input.clientDateOfBirth);
  await page.getByLabel("Cover amount (ZAR)").fill("500000");
  await page.getByLabel("Term (years)").fill("20");
  const create = page.getByRole("button", { name: "Create quote" });
  await create.click();

  await expect(page.getByText("Could not save the quote")).toBeVisible();
  expect(dropped).toBe(true);
  expect(await api.quotesFor("ADV-001", input.clientName)).toHaveLength(1);

  // The adviser clicks again: the form still holds the key, so the API replays the stored quote.
  await create.click();
  await expect(quotePage(page).premium).toHaveText(rand(1045));
  expect(await api.quotesFor("ADV-001", input.clientName)).toHaveLength(1);
});
