import { draft, expect, open, quotePage, test } from "./fixtures";

test("a quote sent to the client and accepted is final, with every move in its history", async ({
  page,
  api,
  client,
}) => {
  const input = draft(await client("Aisha Patel"));
  const quoteId = await api.createQuote("ADV-001", input);
  await open(page, `/quotes/${quoteId}`);
  const quote = quotePage(page);

  await quote.reason.fill("Emailed after the call");
  await quote.button("Send to client").click();
  await expect(quote.status).toHaveText("Sent to client");
  // A sent quote can be accepted or declined, but not sent again or edited.
  await expect(quote.button("Send to client")).toHaveCount(0);
  await expect(quote.edit).toHaveCount(0);
  await expect(quote.reason).toHaveValue("");

  await quote.button("Accept").click();
  await expect(quote.status).toHaveText("Accepted");

  await expect(quote.history).toHaveCount(3);
  await expect(quote.history.nth(0)).toContainText("New → Draft");
  await expect(quote.history.nth(1)).toContainText("Draft → Sent to client");
  await expect(quote.history.nth(1)).toContainText("Emailed after the call");
  await expect(quote.history.nth(2)).toContainText("Sent to client → Accepted");
  await expect(quote.statusButtons).toHaveCount(0);
  await expect(quote.edit).toHaveCount(0);
  await expect(quote.reason).toHaveCount(0);

  // Typing the edit URL does not get round it.
  await open(page, `/quotes/${quoteId}/edit`);
  await expect(
    page.getByText("This quote is Accepted, so it can no longer be changed."),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: "Save changes" })).toHaveCount(0);
});

test("a decline needs a reason, and the reason is recorded", async ({ page, api, client }) => {
  const input = draft(await client("Sipho Mahlangu"));
  const quoteId = await api.createQuote("ADV-001", input);
  await open(page, `/quotes/${quoteId}`);
  const quote = quotePage(page);

  await quote.button("Decline").click();
  await expect(page.getByText("A reason is required to decline.")).toBeVisible();
  await expect(quote.status).toHaveText("Draft");

  // Whitespace is not a reason either.
  await quote.reason.fill("   ");
  await quote.button("Decline").click();
  await expect(page.getByText("A reason is required to decline.")).toBeVisible();

  await quote.reason.fill("Client chose another provider");
  await quote.button("Decline").click();
  await expect(quote.status).toHaveText("Declined");
  await expect(quote.history).toHaveCount(2);
  await expect(quote.history.nth(1)).toContainText("Draft → Declined");
  await expect(quote.history.nth(1)).toContainText("Client chose another provider");
  await expect(quote.statusButtons).toHaveCount(0);

  // The API stored the decline, not just the page.
  const [stored] = await api.quotesFor("ADV-001", input.clientName);
  expect(stored?.status).toBe("Declined");
});

test("a move from a stale page is refused, says why and offers a refresh", async ({
  page,
  api,
  client,
}) => {
  const input = draft(await client("Karen Meyer"));
  const quoteId = await api.createQuote("ADV-001", input);
  await open(page, `/quotes/${quoteId}`);
  const quote = quotePage(page);
  await expect(quote.status).toHaveText("Draft");

  // Someone else, in another tab, declines it while this page still shows a Draft.
  await api.changeStatus("ADV-001", "decline-quote", quoteId, "Declined in another tab");
  await quote.button("Send to client").click();

  await expect(page.getByText("This quote changed, refresh")).toBeVisible();
  await expect(page.getByText("A quote cannot move from Declined to SentToClient.")).toBeVisible();
  await expect(quote.status).toHaveText("Draft");

  await quote.button("Refresh").click();
  await expect(quote.status).toHaveText("Declined");
  await expect(quote.history.nth(1)).toContainText("Declined in another tab");
  await expect(quote.statusButtons).toHaveCount(0);
});
