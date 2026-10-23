import { draft, expect, open, quotePage, test } from "./fixtures";

test("the Acting as picker switches the adviser, and the list is only theirs", async ({
  page,
  api,
  client,
}) => {
  const input = draft(await client("Nomvula Zulu"));
  await api.createQuote("ADV-002", input);
  const actingAs = page.getByLabel("Acting as");
  const row = page.getByRole("row", { name: input.clientName });

  await open(page, "/quotes");
  await expect(actingAs).toHaveValue("ADV-001");
  await expect(row).toHaveCount(0);

  await actingAs.selectOption("ADV-002");
  await expect(row).toBeVisible();

  // The choice is a cookie, so it outlives a reload.
  await open(page, "/quotes");
  await expect(actingAs).toHaveValue("ADV-002");
  await expect(row).toBeVisible();

  await actingAs.selectOption("ADV-003");
  await expect(row).toHaveCount(0);
});

test("another adviser's quote cannot be opened or edited by its URL", async ({
  page,
  api,
  client,
  actAs,
}) => {
  const input = draft(await client("Ruan Steyn"));
  const quoteId = await api.createQuote("ADV-002", input);
  await actAs("ADV-003");

  for (const url of [`/quotes/${quoteId}`, `/quotes/${quoteId}/edit`]) {
    await open(page, url);
    await expect(page.getByText("Could not load the quote")).toBeVisible();
    // A 404, not a 403: the page does not even admit the quote exists.
    await expect(page.getByText("No such quote.")).toBeVisible();
    await expect(page.getByText(input.clientName)).toHaveCount(0);
  }
});

test("the status filter narrows the list, lives in the URL and survives a reload", async ({
  page,
  api,
  client,
  actAs,
}) => {
  const draftName = await client("Busi Ndlovu");
  const sentName = await client("Craig Adams");
  await api.createQuote("ADV-003", draft(draftName));
  const sentId = await api.createQuote("ADV-003", draft(sentName));
  await api.changeStatus("ADV-003", "send-quote-to-client", sentId);
  await actAs("ADV-003");

  // The root redirects to the list.
  await open(page, "/");
  await expect(page).toHaveURL(/\/quotes$/);
  const draftRow = page.getByRole("row", { name: draftName });
  const sentRow = page.getByRole("row", { name: sentName });
  await expect(draftRow).toBeVisible();
  await expect(sentRow).toBeVisible();

  await page.getByRole("button", { name: "Sent to client" }).click();
  await expect(page).toHaveURL(/\?status=SentToClient$/);
  await expect(sentRow).toBeVisible();
  await expect(draftRow).toHaveCount(0);

  await page.reload();
  await expect(page.getByRole("button", { name: "Sent to client" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  await expect(sentRow).toBeVisible();
  await expect(draftRow).toHaveCount(0);

  await page.getByRole("button", { name: "All" }).click();
  await expect(page).toHaveURL(/\/quotes$/);
  await expect(draftRow).toBeVisible();

  // A row opens its quote.
  await sentRow.getByRole("link").click();
  await expect(page).toHaveURL(new RegExp(`/quotes/${sentId}$`));
  await expect(quotePage(page).clientName).toHaveText(sentName);
});
