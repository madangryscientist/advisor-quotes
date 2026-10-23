import { defineConfig, devices } from "@playwright/test";
import { apiUrl, connectionString, webUrl } from "./stack";

export default defineConfig({
  testDir: "tests",
  // Every test makes its own quotes under its own client names, so tests share the database but never each other's rows.
  fullyParallel: true,
  workers: process.env["CI"] ? 2 : 4,
  forbidOnly: !!process.env["CI"],
  timeout: 30_000,
  reporter: "list",
  use: {
    baseURL: webUrl,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    {
      command: `dotnet run --project ../api/Api.csproj --no-launch-profile --urls ${apiUrl}`,
      url: `${apiUrl}/health/ready`,
      timeout: 180_000,
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ConnectionStrings__Quotes: connectionString,
        Database__MigrateOnStartup: "true",
        // Explicit: Development turns the demo seed on, and the tests want only their own quotes.
        Database__SeedDemoData: "false",
      },
    },
    {
      // The production build, not the dev server: closer to the real stack.
      command: "bun run build && bun run preview --port 3001 --strictPort",
      cwd: "../web",
      url: webUrl,
      timeout: 120_000,
      reuseExistingServer: false,
      env: { QUOTES_API_URL: apiUrl },
    },
  ],
});
