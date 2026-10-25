import { expect, open, test } from "./fixtures";

test("the header tabs navigate client-side and the active tab follows the route", async ({
  page,
}) => {
  await open(page, "/quotes");
  // A window property survives only while the document is never reloaded.
  await page.evaluate(() => {
    (window as unknown as Record<string, unknown>)["navMarker"] = "kept";
  });
  const nav = page.getByRole("navigation", { name: "Main" });
  const quotes = nav.getByRole("link", { name: "Quotes" });
  const about = nav.getByRole("link", { name: "About this demo" });

  await about.click();
  await expect(page).toHaveURL(/\/about$/);
  await expect(about).toHaveClass(/font-medium/);
  await expect(quotes).not.toHaveClass(/font-medium/);

  await quotes.click();
  await expect(page).toHaveURL(/\/quotes$/);
  await expect(quotes).toHaveClass(/font-medium/);
  await expect(about).not.toHaveClass(/font-medium/);

  expect(
    await page.evaluate(() => (window as unknown as Record<string, unknown>)["navMarker"]),
  ).toBe("kept");
});

test("clicking the brand logo goes to the quotes list client-side", async ({ page }) => {
  await open(page, "/about");
  await page.evaluate(() => {
    (window as unknown as Record<string, unknown>)["navMarker"] = "kept";
  });
  // The logo and the label are one link, so a click on the logo alone is a click on the link.
  const brand = page.getByRole("banner").getByRole("link", { name: "Advisor Quotes" });
  await brand.getByText("AQ").click();
  await expect(page).toHaveURL(/\/quotes$/);
  expect(
    await page.evaluate(() => (window as unknown as Record<string, unknown>)["navMarker"]),
  ).toBe("kept");
});
