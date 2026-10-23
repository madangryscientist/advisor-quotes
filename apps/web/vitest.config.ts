import { defineConfig } from "vitest/config";

// Separate from vite.config.ts so tests do not boot the Start plugin.
export default defineConfig({
  test: { environment: "node", include: ["src/**/*.test.{ts,tsx}"] },
});
